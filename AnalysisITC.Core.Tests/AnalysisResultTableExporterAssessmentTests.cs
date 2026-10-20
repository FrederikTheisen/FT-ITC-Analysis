using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class AnalysisResultTableExporterAssessmentTests
{
    [Fact]
    public void IndependentSummaryShowsCollectionCountsAndLeavesOrdinaryComparisonBlank()
    {
        var result = IndependentResult(out var members, includePooledComparison: true);
        members[0].NullComparison = Comparison(members[0].Data.UniqueID, 0);
        members[1].NullComparison = Comparison(members[1].Data.UniqueID, 15);
        foreach (var member in members)
            result.RestoreMemberComparison(member.Guid, member.NullComparison);

        var table = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.Summary,
            FileFormat = AnalysisResultExportFileFormat.TSV,
        });
        var lines = Lines(table);
        var headers = lines[0];
        var row = lines[1];

        Assert.Contains("Mixed assessments; 1 of 2 binding detected, 1 of 2 no binding detected",
            row[Array.IndexOf(headers, "Binding assessment")]);
        Assert.Equal("Derived from member assessments", row[Array.IndexOf(headers, "Assessment mode")]);
        foreach (var label in new[] { "Null model", "Null fit", "Comparison scope", "Null model parameters",
                     "Binding AICc", "Null AICc", "ΔAICc", "Assessment reason" })
            Assert.Equal("", row[Array.IndexOf(headers, label)]);
        var rmsdColumn = headers.Select((header, index) => (header, index))
            .Single(item => item.header.StartsWith("Null RMSD (", StringComparison.Ordinal)).index;
        Assert.Equal("", row[rmsdColumn]);
    }

    [Theory]
    [InlineData(0, "No binding detected", false)]
    [InlineData(3, "Inconclusive", true)]
    [InlineData(12, "Inconclusive", true)]
    [InlineData(15, "Binding detected", true)]
    public void IndependentRowsUseEachMemberAssessmentAndOnlySuppressIneligibleMember(
        double firstDelta, string firstAssessment, bool firstAllowed)
    {
        var result = IndependentResult(out var members, includePooledComparison: false);
        members[0].NullComparison = Comparison(members[0].Data.UniqueID, 0);
        members[1].NullComparison = Comparison(members[1].Data.UniqueID, 15);
        foreach (var member in members)
        {
            member.NullComparison = Comparison(member.Data.UniqueID,
                ReferenceEquals(member, members[0]) ? firstDelta : 15);
            member.Parameters[ParameterType.Nvalue1] = new FloatWithError(ReferenceEquals(member, members[0]) ? 111 : 222, 0);
            result.RestoreMemberComparison(member.Guid, member.NullComparison);
        }

        var table = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.AllRows,
            FileFormat = AnalysisResultExportFileFormat.TSV,
        });
        var lines = Lines(table);
        var headers = lines[0];
        var rows = lines.Skip(1).ToList();
        var assessment = Array.IndexOf(headers, "Binding assessment");
        var value = Array.IndexOf(headers, "N");
        var scope = Array.IndexOf(headers, "Comparison scope");

        Assert.Equal(firstAssessment, rows[0][assessment]);
        Assert.Equal("Binding detected", rows[1][assessment]);
        Assert.Equal(firstAllowed, !string.IsNullOrWhiteSpace(rows[0][value]));
        Assert.NotEqual("", rows[1][value]);
        Assert.Equal("Independent member comparison", rows[0][scope]);
        Assert.Equal("Independent member comparison", rows[1][scope]);
        Assert.All(rows, row => Assert.Equal("Offset", row[Array.IndexOf(headers, "Null model")]));
    }

    [Fact]
    public void DiagnosticSummaryAddsPooledColumnsOnlyForIndependentRows()
    {
        var independent = IndependentResult(out _, includePooledComparison: true);
        var singleModel = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var single = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = singleModel }));
        var table = AnalysisResultTableExporter.Build(new[] { independent, single }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.Summary,
            OutputPurpose = ResultOutputPurpose.Diagnostic,
            FileFormat = AnalysisResultExportFileFormat.TSV,
        });
        var lines = Lines(table);
        var headers = lines[0];
        var pooledIndex = Array.IndexOf(headers, "Pooled diagnostic binding AICc");
        Assert.True(pooledIndex >= 0);
        var independentRow = lines[1];
        var singleRow = lines[2];
        Assert.NotEqual("", independentRow[pooledIndex]);
        Assert.Equal("", singleRow[pooledIndex]);
        Assert.Contains("pooled variance convention", independentRow[Array.IndexOf(headers, "Pooled diagnostic scope")]);
        Assert.Equal("", independentRow[Array.IndexOf(headers, "Binding AICc")]);
    }

    public static IEnumerable<object[]> ExportLayouts()
    {
        foreach (var format in Enum.GetValues<AnalysisResultExportFileFormat>())
        foreach (var rows in Enum.GetValues<AnalysisResultExportRowMode>())
        foreach (var errors in Enum.GetValues<AnalysisResultExportErrorStyle>())
        foreach (var purpose in Enum.GetValues<ResultOutputPurpose>())
            yield return new object[] { format, rows, errors, purpose };
    }

    [Theory]
    [MemberData(nameof(ExportLayouts))]
    public void EntireTableUsesDecimalPointsAcrossCultures(
        AnalysisResultExportFileFormat format, AnalysisResultExportRowMode rows,
        AnalysisResultExportErrorStyle errors, ResultOutputPurpose purpose)
    {
        var single = FractionalResult();
        var independent = IndependentResult(out var members, includePooledComparison: false);
        independent.RestoreNullComparison(Comparison(members[0].Data.UniqueID, 12.5, 123.25, 4.184));
        foreach (var member in members)
            independent.RestoreMemberComparison(member.Guid,
                Comparison(member.Data.UniqueID, 12.5, 123.25, 4.184));
        var options = new AnalysisResultExportOptions
        {
            FileFormat = format, RowMode = rows, ErrorStyle = errors, OutputPurpose = purpose,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            EnergyUnitOverride = EnergyUnit.KiloJoule,
        };
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var expected = AnalysisResultTableExporter.Build(new[] { single, independent }, options);
            Assert.Contains("\"Run, \"\"A\"\"\"", expected);

            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                Assert.Equal(expected, AnalysisResultTableExporter.Build(new[] { single, independent }, options));
                Assert.Same(culture, CultureInfo.CurrentCulture);
                Assert.Same(originalUiCulture, CultureInfo.CurrentUICulture);
                Assert.Same(originalDefaultCulture, CultureInfo.DefaultThreadCurrentCulture);
                Assert.Same(originalDefaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture);

                // TSV makes individual numeric cells easy to inspect independently of CSV quoting.
                options.FileFormat = AnalysisResultExportFileFormat.TSV;
                var table = Lines(AnalysisResultTableExporter.Build(new[] { single, independent }, options));
                options.FileFormat = format;
                var header = table[0];
                var row = table[1];
                Assert.Equal("123.25", row[Array.IndexOf(header, "Binding AICc")]);
                Assert.Equal("135.75", row[Array.IndexOf(header, "Null AICc")]);
                Assert.Equal("+12.5", row[Array.IndexOf(header, "ΔAICc")]);
                Assert.Equal("4.184", row[Array.IndexOf(header, "Null RMSD (µJ)")]);
                Assert.Equal("25.4", row[Array.IndexOf(header,
                    rows == AnalysisResultExportRowMode.Summary ? "Evaluation temperature (°C)" : "Temperature (°C)")]);
                if (rows == AnalysisResultExportRowMode.AllRows)
                {
                    if (errors == AnalysisResultExportErrorStyle.SeparateColumns)
                    {
                        Assert.Equal("1.25", row[Array.IndexOf(header, "N_value")]);
                        Assert.Equal("0.125", row[Array.IndexOf(header, "N_sd")]);
                        Assert.Equal("1.05", row[Array.IndexOf(header, "N_ci_lower")]);
                        Assert.Equal("1.45", row[Array.IndexOf(header, "N_ci_upper")]);
                        Assert.Equal("-25.25", row[Array.IndexOf(header, "∆H (kJ/mol)_value")]);
                    }
                    else
                        Assert.Equal("1.25 ± 0.125 [1.05, 1.45]", row[Array.IndexOf(header, "N")]);
                }
                if (rows == AnalysisResultExportRowMode.Summary && purpose == ResultOutputPurpose.Diagnostic)
                {
                    var pooled = table[2];
                    Assert.Equal("123.25", pooled[Array.IndexOf(header, "Pooled diagnostic binding AICc")]);
                    Assert.Equal("135.75", pooled[Array.IndexOf(header, "Pooled diagnostic null AICc")]);
                    Assert.Equal("+12.5", pooled[Array.IndexOf(header, "Pooled diagnostic ΔAICc")]);
                    Assert.Equal("4.184", pooled[Array.IndexOf(header, "Pooled diagnostic null RMSD (µJ)")]);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void EmptyAndThrowingInputsRestoreCallingCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var culture = CultureInfo.GetCultureInfo("da-DK");
        try
        {
            CultureInfo.CurrentCulture = culture;
            Assert.Equal("", AnalysisResultTableExporter.Build(Array.Empty<AnalysisResult>(), null));
            Assert.Same(culture, CultureInfo.CurrentCulture);
            var exception = new InvalidOperationException("Input enumeration failed");
            IEnumerable<AnalysisResult> ThrowingResults()
            {
                yield return null;
                throw exception;
            }
            Assert.Same(exception, Assert.Throws<InvalidOperationException>(() =>
                AnalysisResultTableExporter.Build(ThrowingResults(), null)));
            Assert.Same(culture, CultureInfo.CurrentCulture);
            Assert.Same(uiCulture, CultureInfo.CurrentUICulture);
            Assert.Same(defaultCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Same(defaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    static AnalysisResult FractionalResult()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        model.Data.Name = "Run, \"A\"";
        model.Data.TargetTemperature = model.Data.MeasuredTemperature = 25.4;
        model.Solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(1.25, 0.125, 1.05, 1.45);
        model.Solution.Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25250, 125, -25500, -25000);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        result.Name = model.Data.Name;
        result.RestoreNullComparison(Comparison(model.Data.UniqueID, 12.5, 123.25, 4.184));
        return result;
    }

    static AnalysisResult IndependentResult(out List<SolutionInterface> members, bool includePooledComparison)
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        first.Data.Name = "Duplicate";
        second.Data.Name = "Duplicate";
        members = new List<SolutionInterface> { first.Solution, second.Solution };
        var model = new GlobalModel(new List<Model> { first, second })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        model.Parameters.AddIndivdualParameter(first.Parameters);
        model.Parameters.AddIndivdualParameter(second.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = model }, members,
            first.Solution.Convergence, reconstructBootstrap: false);
        model.Solution = solution;
        if (includePooledComparison)
            solution.NullComparison = Comparison(first.Data.UniqueID, 15);
        return new AnalysisResult(solution);
    }

    static NullModelComparison Comparison(string id, double delta, double bindingAicc = 100, double rmsd = 2)
    {
        FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(
            observationCount: 20, fittedParameterCount: 2, likelihoodParameterCount: 3,
            likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance,
            minusTwoLogLikelihood: aicc - 10, aic: aicc - 4, aicc: aicc,
            isAicAvailable: true, isAiccAvailable: true, aicUnavailableReason: "",
            aiccUnavailableReason: "", rawResidualSumOfSquares: 1,
            residualRmsdMicrojoules: rmsd, standardizedResidualSumOfSquares: 1, logSigmaSquaredSum: 1);
        return new NullModelComparison
        {
            NullModelId = "offset",
            BindingFitSucceeded = true,
            NullFitSucceeded = true,
            BindingInformationCriteria = Criteria(bindingAicc),
            NullInformationCriteria = Criteria(bindingAicc + delta),
            DeltaAicc = delta,
            Members = new List<NullModelComparisonMember>
            {
                new() { ExperimentId = id, Offset = 1234, Scope = "local" },
            },
        };
    }

    static List<string[]> Lines(string table)
        => table.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t')).ToList();
}
