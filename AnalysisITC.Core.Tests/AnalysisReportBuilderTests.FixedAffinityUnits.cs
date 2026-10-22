using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class AnalysisReportBuilderTests
{
    public static IEnumerable<object[]> FixedAffinityUnitCases()
    {
        foreach (var reverse in new[] { false, true })
        foreach (var style in Enum.GetValues<UncertaintyDisplayStyle>())
        foreach (var purpose in new[] { ResultOutputPurpose.Standard, ResultOutputPurpose.Diagnostic })
        foreach (var preference in new[] { ConcentrationUnit.nM, ConcentrationUnit.µM, ConcentrationUnit.mM })
            yield return new object[] { reverse, style, purpose, preference };
    }

    [Theory]
    [MemberData(nameof(FixedAffinityUnitCases))]
    public void FixedAffinityOverviewUsesHeaderUnit(
        bool reverse, UncertaintyDisplayStyle style, ResultOutputPurpose purpose, ConcentrationUnit preference)
    {
        var originalUnit = AppSettings.DefaultConcentrationUnit;
        var originalPrecision = AppSettings.NumberPrecision;
        try
        {
            AppSettings.DefaultConcentrationUnit = preference;
            AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
            var result = CreateResult(reverse ? 3 : 2);
            var members = result.Solution.Solutions;
            SetOverviewAffinity(members[0], ParameterType.Affinity1, reverse ? 2e-6 : 50e-9, isFixed: true);
            foreach (var member in members.Skip(1))
                SetOverviewAffinity(member, ParameterType.Affinity1, reverse ? 50e-9 : 5e-6, isFixed: false);

            var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
            {
                OutputPurpose = purpose,
                UncertaintyDisplayStyle = style,
            });
            var overview = OverviewTable(document);
            var column = OverviewAffinityColumn(overview, ParameterType.Affinity1);
            Assert.EndsWith(reverse ? "(nM)" : "(µM)", overview.Columns[column].Title);
            AssertFixedAffinity(overview.Rows[0].Cells[column], reverse ? 2000 : 0.05);

            var freeCell = overview.Rows[1].Cells[column];
            Assert.DoesNotContain("(fixed)", freeCell);
            Assert.Equal(style is UncertaintyDisplayStyle.Automatic or UncertaintyDisplayStyle.StandardDeviation
                or UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, freeCell.Contains("±"));
            Assert.Equal(style is UncertaintyDisplayStyle.ConfidenceInterval
                or UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, freeCell.Contains("["));

            // Standalone tables include explicit units and retain magnitude-based scaling.
            var parameters = document.Sections.First(section => section.Kind == AnalysisReportSectionKind.Experiment)
                .Blocks.OfType<AnalysisReportTableBlock>().Single(block => block.Title == "Fitted and derived parameters");
            var affinity = parameters.Rows.Single(row => row.Cells[0] == "Affinity");
            Assert.Equal(reverse ? "µM" : "nM", affinity.Cells[3]);
            Assert.Equal(reverse ? 2 : 50, double.Parse(affinity.Cells[2], CultureInfo.InvariantCulture), 8);
        }
        finally
        {
            AppSettings.DefaultConcentrationUnit = originalUnit;
            AppSettings.NumberPrecision = originalPrecision;
        }
    }

    [Theory]
    [InlineData(ResultOutputPurpose.Standard)]
    [InlineData(ResultOutputPurpose.Diagnostic)]
    public void FixedAffinityOverviewUsesEachSequentialColumnUnit(ResultOutputPurpose purpose)
    {
        var (_, result) = SequentialPersistenceTests.CreateGlobalResult(2, members =>
        {
            SetOverviewAffinity(members[0], ParameterType.Affinity1, 50e-9, isFixed: true);
            SetOverviewAffinity(members[1], ParameterType.Affinity1, 5e-6, isFixed: false);
            SetOverviewAffinity(members[0], ParameterType.Affinity2, 1.5e-6, isFixed: true);
            SetOverviewAffinity(members[1], ParameterType.Affinity2, 50e-9, isFixed: false);
        });
        var overview = OverviewTable(AnalysisReportBuilder.Build(result,
            new AnalysisReportOptions { OutputPurpose = purpose }));
        var first = OverviewAffinityColumn(overview, ParameterType.Affinity1);
        var second = OverviewAffinityColumn(overview, ParameterType.Affinity2);
        Assert.EndsWith("(µM)", overview.Columns[first].Title);
        Assert.EndsWith("(nM)", overview.Columns[second].Title);
        AssertFixedAffinity(overview.Rows[0].Cells[first], 0.05);
        AssertFixedAffinity(overview.Rows[0].Cells[second], 1500);
    }

    [Theory]
    [InlineData(ResultOutputPurpose.Standard, "")]
    [InlineData(ResultOutputPurpose.Diagnostic, "0.05")]
    public void FixedAffinityOverviewPreservesSuppressionAndNonFiniteCells(ResultOutputPurpose purpose, string expected)
    {
        var result = CreateResult(3);
        var members = result.Solution.Solutions;
        SetOverviewAffinity(members[0], ParameterType.Affinity1, 50e-9, isFixed: true);
        SetOverviewAffinity(members[1], ParameterType.Affinity1, 5e-6, isFixed: false);
        SetOverviewAffinity(members[2], ParameterType.Affinity1, double.NaN, isFixed: true);
        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
        result.SetMemberBindingAssessmentOverride(members[2].Guid, BindingAssessmentOutcome.NoBindingDetected);

        var overview = OverviewTable(AnalysisReportBuilder.Build(result,
            new AnalysisReportOptions { OutputPurpose = purpose }));
        var column = OverviewAffinityColumn(overview, ParameterType.Affinity1);
        Assert.EndsWith("(µM)", overview.Columns[column].Title);
        if (expected.Length == 0) Assert.Equal("", overview.Rows[0].Cells[column]);
        else AssertFixedAffinity(overview.Rows[0].Cells[column], double.Parse(expected, CultureInfo.InvariantCulture));
        Assert.Equal(purpose == ResultOutputPurpose.Standard ? "" : "—", overview.Rows[2].Cells[column]);
    }

    static void SetOverviewAffinity(SolutionInterface member, ParameterType parameter, double kd, bool isFixed)
    {
        var logKa = -Math.Log10(kd);
        member.Model.Parameters.Table[parameter].Update(logKa, isFixed);
        member.Parameters[parameter] = new FloatWithError(logKa, 0.02, logKa - 0.05, logKa + 0.05);
    }

    static int OverviewAffinityColumn(AnalysisReportTableBlock overview, ParameterType parameter) =>
        overview.Columns.ToList().FindIndex(column => column.Id == AnalysisResultOverviewTable.ParameterColumnId(parameter));

    static void AssertFixedAffinity(string cell, double expected)
    {
        const string suffix = " (fixed)";
        Assert.EndsWith(suffix, cell);
        Assert.Equal(expected, double.Parse(cell.Substring(0, cell.Length - suffix.Length), CultureInfo.InvariantCulture), 8);
    }
}
