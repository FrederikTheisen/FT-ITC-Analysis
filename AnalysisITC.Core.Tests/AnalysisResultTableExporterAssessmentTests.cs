using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
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
        members[1].NullComparison = Comparison(members[1].Data.UniqueID, 12);
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
        foreach (var label in new[] { "Null model", "Null fit", "Comparison scope", "Null offsets",
                     "Binding AICc", "Null AICc", "ΔAICc", "Assessment reason" })
            Assert.Equal("", row[Array.IndexOf(headers, label)]);
        var rmsdColumn = headers.Select((header, index) => (header, index))
            .Single(item => item.header.StartsWith("Null RMSD (", StringComparison.Ordinal)).index;
        Assert.Equal("", row[rmsdColumn]);
    }

    [Theory]
    [InlineData(0, "No binding detected", false)]
    [InlineData(3, "Inconclusive", true)]
    public void IndependentRowsUseEachMemberAssessmentAndOnlySuppressIneligibleMember(
        double firstDelta, string firstAssessment, bool firstAllowed)
    {
        var result = IndependentResult(out var members, includePooledComparison: false);
        members[0].NullComparison = Comparison(members[0].Data.UniqueID, 0);
        members[1].NullComparison = Comparison(members[1].Data.UniqueID, 12);
        foreach (var member in members)
        {
            member.NullComparison = Comparison(member.Data.UniqueID,
                ReferenceEquals(member, members[0]) ? firstDelta : 12);
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

    static NullModelComparison Comparison(string id, double delta)
    {
        static FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(
            observationCount: 20, fittedParameterCount: 2, likelihoodParameterCount: 3,
            likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance,
            minusTwoLogLikelihood: aicc - 10, aic: aicc - 4, aicc: aicc,
            isAicAvailable: true, isAiccAvailable: true, aicUnavailableReason: "",
            aiccUnavailableReason: "", rawResidualSumOfSquares: 1,
            residualRmsdMicrojoules: 2, standardizedResidualSumOfSquares: 1, logSigmaSquaredSum: 1);
        return new NullModelComparison
        {
            BindingFitSucceeded = true,
            NullFitSucceeded = true,
            BindingInformationCriteria = Criteria(100),
            NullInformationCriteria = Criteria(100 + delta),
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
