using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
public sealed class DataAvailabilityReportRenderingTests
{
    public DataAvailabilityReportRenderingTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Theory]
    [InlineData(ResultOutputPurpose.Standard, true)]
    [InlineData(ResultOutputPurpose.Standard, false)]
    [InlineData(ResultOutputPurpose.Diagnostic, true)]
    [InlineData(ResultOutputPurpose.Diagnostic, false)]
    public void MixedThermogramAndHeatOnlyReportRendersAvailabilityAndSupportingData(
        ResultOutputPurpose purpose, bool includeInjectionTables)
    {
        var result = CreateMixedResult(out var heatOnly);
        var supporting = CreateExperiment("Heat-only supporting experiment", hasThermogram: false,
            ITCDataFormat.Unknown);
        supporting.Injections[1].Include = false;
        var report = new AnalysisReport { Name = "Data availability rendering QA" };
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { supporting.UniqueID });
        var document = AnalysisReportBuilder.Build(report,
            id => id == result.UniqueID ? result : null,
            id => id == supporting.UniqueID ? supporting : null,
            new AnalysisReportOptions
            {
                OutputPurpose = purpose,
                IncludeInjectionTables = includeInjectionTables,
                GeneratedAtUtc = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            });

        Assert.True(document.IsValid);
        var rawSection = Assert.Single(document.Sections, section => section.Id == "result-1-experiment-1");
        var rawProcessing = Assert.Single(rawSection.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Processing and integration");
        Assert.Contains(rawProcessing.Items, item => item.Label == "Baseline method");

        var heatSection = Assert.Single(document.Sections, section => section.Id == "result-1-experiment-2");
        AssertAvailabilityBlock(heatSection, expectIncompleteHeats: true);
        Assert.Empty(heatSection.Blocks.OfType<AnalysisReportNoticeBlock>());
        Assert.Equal(includeInjectionTables,
            heatSection.Blocks.OfType<AnalysisReportTableBlock>().Any(block => block.Title == "Injection table"));
        Assert.True(heatOnly.Injections.Count == 3);

        var supportSection = Assert.Single(document.Sections, section => section.Id == "supporting-experiments");
        Assert.Contains(supportSection.Blocks.OfType<AnalysisReportHeadingBlock>(),
            block => block.Text == "S1. Heat-only supporting experiment");
        AssertAvailabilityBlock(supportSection, expectIncompleteHeats: false);
        Assert.DoesNotContain(supportSection.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Processing and integration");
        AssertAvailabilityFollowsDetails(heatSection);
        AssertAvailabilityFollowsDetails(supportSection);

        RenderAndSave(document, purpose, includeInjectionTables);
    }

    static void AssertAvailabilityBlock(AnalysisReportSection section, bool expectIncompleteHeats)
    {
        var availability = Assert.Single(section.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Data availability");
        Assert.Contains(availability.Items, item => item.Label == "Input heats"
            && item.Value == "Imported integrated heats");
        Assert.Contains(availability.Items, item => item.Label == "Injection use"
            && item.Value.Contains("excluded: 2", StringComparison.Ordinal));
        Assert.Equal(expectIncompleteHeats,
            availability.Items.Any(item => item.Label == "Available heats" && item.Value == "2 of 3 injections"));
        Assert.DoesNotContain(availability.Items, item => item.Label is "Baseline method" or "Baseline status"
            or "Integrated injections" or "Integration regions" or "Start after injection" or "End after injection");
    }

    static void RenderAndSave(AnalysisReportDocument document, ResultOutputPurpose purpose, bool includeInjectionTables)
    {
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);
        Assert.NotEmpty(plan.Pages);
        var heatBlock = document.Sections.Single(section => section.Id == "result-1-experiment-2")
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Data availability");
        var supportBlock = document.Sections.Single(section => section.Id == "supporting-experiments")
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Data availability");
        var heatPage = PageContaining(plan, heatBlock);
        var supportPage = PageContaining(plan, supportBlock);
        using var heatPreview = renderer.RenderPageBitmap(document, plan, heatPage, 1200);
        using var supportPreview = renderer.RenderPageBitmap(document, plan, supportPage, 1200);
        Assert.True(heatPreview.Width > 0 && heatPreview.Height > 0);
        Assert.True(supportPreview.Width > 0 && supportPreview.Height > 0);

        using var pdfStream = new MemoryStream();
        renderer.WritePdf(document, plan, pdfStream);
        var pdf = pdfStream.ToArray();
        Assert.True(pdf.Length > 2_000);
        Assert.Equal("%PDF", System.Text.Encoding.Latin1.GetString(pdf, 0, 4));

        var outputDirectory = Environment.GetEnvironmentVariable("FTITC_DATA_AVAILABILITY_QA_DIRECTORY");
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;
        Directory.CreateDirectory(outputDirectory);
        var stem = "avalonia-" + purpose.ToString().ToLowerInvariant()
            + (includeInjectionTables ? "-tables" : "-no-tables");
        File.WriteAllBytes(Path.Combine(outputDirectory, stem + ".pdf"), pdf);
        SavePng(heatPreview, Path.Combine(outputDirectory, stem + "-heat-only.png"));
        if (supportPage != heatPage)
            SavePng(supportPreview, Path.Combine(outputDirectory, stem + "-supporting.png"));
    }

    static int PageContaining(AnalysisReportLayoutPlan plan, AnalysisReportKeyValueBlock block)
    {
        var fragments = plan.Pages.SelectMany((page, pageIndex) => page.Fragments
            .Where(fragment => ReferenceEquals(fragment.Block, block))
            .Select(fragment => (PageIndex: pageIndex, fragment.ItemCount))).ToList();
        Assert.NotEmpty(fragments);
        Assert.Equal(block.Items.Count, fragments.Sum(item => item.ItemCount));
        return fragments[0].PageIndex;
    }

    static void AssertAvailabilityFollowsDetails(AnalysisReportSection section)
    {
        var blocks = section.Blocks.ToList();
        var detailsIndex = blocks.FindIndex(block => block is AnalysisReportKeyValueBlock keyValue
            && keyValue.Title.StartsWith("Experiment details", StringComparison.Ordinal));
        var availabilityIndex = blocks.FindIndex(block => block is AnalysisReportKeyValueBlock keyValue
            && keyValue.Title == "Data availability");
        Assert.True(detailsIndex >= 0 && availabilityIndex == detailsIndex + 1,
            "Data availability must immediately follow Experiment details.");
    }

    static void SavePng(SkiaSharp.SKBitmap bitmap, string path)
    {
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        encoded.SaveTo(output);
    }

    static AnalysisResult CreateMixedResult(out ExperimentData heatOnly)
    {
        var raw = CreateExperiment("Raw thermogram experiment", hasThermogram: true, ITCDataFormat.ITC200);
        heatOnly = CreateExperiment("Heat-only fitted experiment", hasThermogram: false, ITCDataFormat.OriginProject);
        var members = new List<SolutionInterface> { CreateMember(raw), CreateMember(heatOnly) };
        heatOnly.Injections[1].Include = false;
        heatOnly.Injections[2].IsIntegrated = false;
        var convergence = members[0].Convergence;
        var globalModel = new GlobalModel(members.Select(member => member.Model).ToList())
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        foreach (var member in members) globalModel.Parameters.AddIndivdualParameter(member.Model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, members, convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        return new AnalysisResult(global) { Name = "Mixed thermogram availability" };
    }

    static SolutionInterface CreateMember(ExperimentData data)
    {
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.HeatMethod = InjectionHeatMethod.MicroCal;
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
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
        return model.Solution;
    }

    static ExperimentData CreateExperiment(string name, bool hasThermogram, ITCDataFormat sourceFormat)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name,
            CellConcentration = new FloatWithError(20e-6),
            SyringeConcentration = new FloatWithError(400e-6),
            CellVolume = 200e-6,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
            DataSourceFormat = sourceFormat,
            AppliedDilutionMethod = DilutionMethod.MicroCal,
        };
        if (hasThermogram)
        {
            data.DataPoints.Add(new DataPoint(0, 0, 25));
            data.DataPoints.Add(new DataPoint(60, -1e-6f, 25));
            data.DataPoints.Add(new DataPoint(120, 0, 25));
            data.BaseLineCorrectedDataPoints = data.DataPoints.ToList();
        }
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8e-10, include: true)
            {
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = index + 1,
                Time = 30 + index * 30,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        return data;
    }
}
