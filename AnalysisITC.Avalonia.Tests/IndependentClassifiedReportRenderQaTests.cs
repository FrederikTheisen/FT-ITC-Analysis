using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SkiaSharp;
using Xunit;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class IndependentClassifiedReportRenderQaTests
{
    public IndependentClassifiedReportRenderQaTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void MixedIndependentStandardAndDiagnosticReportsRenderAllEstimatesWithAssessments()
    {
        var result = CreateIndependentResult();
        var members = result.Solution.Solutions;
        members[0].Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25000, 0, -27000, -24000);
        members[1].Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25000, 1500, -29000, -23000);
        members[2].Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25000, 0, -1e11, 1e11);
        result.RestoreMemberAssessment(members[0].Guid, BindingAssessmentState.Restore(
            BindingAssessmentOutcome.BindingDetected, BindingAssessmentState.CurrentRuleId, null));
        result.RestoreMemberAssessment(members[1].Guid, BindingAssessmentState.Restore(
            BindingAssessmentOutcome.Inconclusive, BindingAssessmentState.CurrentRuleId, null));
        result.RestoreMemberAssessment(members[2].Guid, BindingAssessmentState.Restore(
            BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentState.CurrentRuleId, null));

        var standard = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
            { ExtraTraceability = true });
        var summary = standard.Sections.Single(section => section.Id == "result-1-analysis-summary");
        var modelAndFit = Assert.Single(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Model and fit details");
        Assert.Contains(modelAndFit.Items, item => item.Label == "Model");
        Assert.Contains(modelAndFit.Items, item => item.Label.StartsWith("RMSD", StringComparison.Ordinal));
        var overview = summary.Blocks.OfType<AnalysisReportTableBlock>().Single(table => table.Title == "Experiment parameter overview");
        Assert.Equal(3, overview.Rows.Count);
        var assessmentText = string.Join(" ", overview.Rows.SelectMany(row => row.Cells));
        Assert.Contains("Inconclusive", assessmentText);
        Assert.Contains("No binding detected", assessmentText);
        Assert.DoesNotContain("attempted-model", string.Join(" ", summary.Blocks.OfType<AnalysisReportNoticeBlock>().Select(block => block.Message)));
        Assert.Single(summary.Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>());
        Assert.Equal(2, summary.Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>().Single().Series.Count);
        Assert.DoesNotContain(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(), block => block.Title == "Combined parameters");
        Assert.DoesNotContain(standard.Sections, section => section.Blocks.Count == 0);
        AssertMemberParameterVisibility(standard, members, visible: new[] { true, true, true });
        var details = standard.Sections.Single(section => section.Id == "result-1-experiment-1")
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.DoesNotContain(details.Items, item => item.Label == "Not recorded"
            && item.Value.Contains("experiment ID", StringComparison.OrdinalIgnoreCase));
        var externalIdIndex = details.Items.ToList().FindIndex(item => item.Label == "External experiment ID");
        var experimentSettingsIndex = details.Items.ToList().FindIndex(item => item.Label == "Experiment settings");
        Assert.True(externalIdIndex >= 0 && externalIdIndex < experimentSettingsIndex);
        Assert.Equal(0, details.Items[externalIdIndex].IndentLevel);
        Assert.Equal(1, details.Items.Single(item => item.Label == "Instrument").IndentLevel);
        var processing = standard.Sections.Single(section => section.Id == "result-1-experiment-1")
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Processing and integration");
        Assert.DoesNotContain(processing.Items, item => item.Label == "Bookkeeping convention");
        var bookkeeping = Assert.Single(standard.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportNoticeBlock>(), block => block.Title == "Bookkeeping conventions");
        Assert.Contains("MicroCal: 1A, 1B, 1C", bookkeeping.Message);
        Assert.DoesNotContain(standard.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportHeadingBlock>(), heading => heading.Text == "Pooled Comparison Diagnostics");

        var diagnostic = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
            { OutputPurpose = ResultOutputPurpose.Diagnostic, ExtraTraceability = true });
        AssertMemberParameterVisibility(diagnostic, members, visible: new[] { true, true, true });
        var diagnosticSummary = diagnostic.Sections.Single(section => section.Id == "result-1-analysis-summary");
        Assert.Contains(diagnosticSummary.Blocks.OfType<AnalysisReportHeadingBlock>(), heading =>
            heading.Text == "Pooled Comparison Diagnostics");
        Assert.Contains(diagnosticSummary.Blocks.OfType<AnalysisReportTextBlock>(), block =>
            block.Text.Contains("does not determine member assessments", StringComparison.Ordinal));

        RenderQaPages(standard, new[] { "result-1-analysis-summary", "result-1-experiment-1", "result-1-experiment-2" }, "standard");
        RenderQaPages(diagnostic, new[] { "result-1-analysis-summary", "result-1-experiment-2", "appendix" }, "diagnostic");
        RenderQaBlockPage(standard, "result-1-experiment-1", "Processing and integration", "standard-processing");
        RenderQaBlockPage(standard, "result-1-analysis-summary", "Model and fit details", "standard-model-fit");
        RenderQaBlockPage(diagnostic, "result-1-analysis-summary", "Combined parameters", "diagnostic-combined");
    }

    static void AssertMemberParameterVisibility(AnalysisReportDocument document,
        IReadOnlyList<SolutionInterface> members, IReadOnlyList<bool> visible)
    {
        for (var index = 0; index < members.Count; index++)
        {
            var section = document.Sections.Single(item => item.Id == "result-1-experiment-" + (index + 1));
            var parameterTables = section.Blocks.OfType<AnalysisReportTableBlock>()
                .Where(table => table.Title == "Fitted and derived parameters").ToList();
            Assert.Equal(visible[index], parameterTables.Count == 1);
            var sentinel = SentinelFor(index).ToString("0.####E+00", System.Globalization.CultureInfo.InvariantCulture);
            var tableText = string.Join(" ", parameterTables.SelectMany(table => table.Rows)
                .SelectMany(row => row.Cells));
            if (visible[index]) Assert.Contains(sentinel, tableText, StringComparison.Ordinal);
            else Assert.DoesNotContain(sentinel, tableText, StringComparison.Ordinal);
        }
    }

    static void RenderQaPages(AnalysisReportDocument document, IReadOnlyList<string> sectionIds, string label)
    {
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);
        Assert.NotEmpty(plan.Pages);
        var outputDirectory = Environment.GetEnvironmentVariable("FTITC_INDEPENDENT_REPORT_QA_DIR");
        if (!string.IsNullOrWhiteSpace(outputDirectory)) Directory.CreateDirectory(outputDirectory);
        foreach (var sectionId in sectionIds)
        {
            var pageIndex = -1;
            for (var index = 0; index < plan.Pages.Count; index++)
            {
                if (plan.Pages[index].Fragments.Any(fragment => fragment.Section?.Id == sectionId))
                { pageIndex = index; break; }
            }
            Assert.True(pageIndex >= 0, $"No rendered page contains section {sectionId}.");
            using var bitmap = renderer.RenderPageBitmap(document, plan, pageIndex, 1200);
            Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
            if (string.IsNullOrWhiteSpace(outputDirectory)) continue;
            var fileName = $"independent-{label}-{sectionId}.png";
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(Path.Combine(outputDirectory, fileName));
            encoded.SaveTo(output);
        }
    }

    static void RenderQaBlockPage(AnalysisReportDocument document, string sectionId, string blockTitle, string label)
    {
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);
        var targetBlock = document.Sections.Single(section => section.Id == sectionId).Blocks
            .OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == blockTitle);
        var pageIndex = plan.Pages.ToList().FindIndex(page => page.Fragments.Any(fragment =>
            ReferenceEquals(fragment.Block, targetBlock)));
        Assert.True(pageIndex >= 0, $"No rendered page contains {blockTitle} in {sectionId}.");
        using var bitmap = renderer.RenderPageBitmap(document, plan, pageIndex, 1200);
        Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
        var outputDirectory = Environment.GetEnvironmentVariable("FTITC_INDEPENDENT_REPORT_QA_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;
        Directory.CreateDirectory(outputDirectory);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(Path.Combine(outputDirectory, $"independent-{label}.png"));
        encoded.SaveTo(output);
    }

    static AnalysisResult CreateIndependentResult()
    {
        var parts = new[] { "Eligible binder", "Inconclusive member", "No-binding member" }
            .Select((name, index) => CreateSingleMember(name, SentinelFor(index))).ToList();
        var solutions = parts.Select(part => part.Solution.Solutions.Single()).ToList();
        var models = solutions.Select(solution => solution.Model).ToList();
        var convergence = solutions[0].Convergence;
        var globalModel = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        foreach (var solution in solutions) globalModel.Parameters.AddIndivdualParameter(solution.Model.Parameters);
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel }, solutions, convergence,
            reconstructBootstrap: false);
        globalModel.Solution = globalSolution;
        var result = new AnalysisResult(globalSolution) { Name = "Independent classified report QA" };
        result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(globalSolution));
        return result;
    }

    static AnalysisResult CreateSingleMember(string name, double sentinel)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name,
            CellConcentration = new FloatWithError(20e-6),
            SyringeConcentration = new FloatWithError(400e-6),
            CellVolume = 200e-6,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
            Date = new DateTime(2026, 8, 1),
            AppliedDilutionMethod = DilutionMethod.MicroCal,
            ExternalExperimentId = name == "Eligible binder" ? "instrument-run-001" : null,
        };
        data.DataPoints.Add(new DataPoint(0, 0, 25));
        data.DataPoints.Add(new DataPoint(60, -1e-6f, 25));
        data.DataPoints.Add(new DataPoint(120, 0, 25));
        data.BaseLineCorrectedDataPoints = data.DataPoints.ToList();
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8e-10, include: true)
            {
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = index + 1,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.HeatMethod = InjectionHeatMethod.MicroCal;
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, sentinel);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 1,
            Iterations = 1,
        });
        model.Solution = SolutionInterface.FromModel(model, convergence);
        data.UpdateSolution(model);
        var globalModel = new GlobalModel(new List<Model> { model })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            new List<SolutionInterface> { model.Solution }, convergence, reconstructBootstrap: false);
        globalModel.Solution = globalSolution;
        return new AnalysisResult(globalSolution) { Name = name };
    }

    static double SentinelFor(int index) => index switch
    {
        0 => 111111,
        1 => 222222,
        _ => 333333,
    };
}
