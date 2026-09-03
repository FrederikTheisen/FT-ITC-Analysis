using System;
using System.Collections.Generic;
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

public sealed class AnalysisReportBuilderTests
{
    [Fact]
    public void BuildProducesA4PortraitDocumentWithOrderedForcedSections()
    {
        var result = CreateResult(2);

        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        {
            DocumentLabel = "Supporting Document 1B",
            Title = "Printable analysis",
            GeneratedAtUtc = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc),
        });

        Assert.True(document.IsValid);
        Assert.Equal(21.0, document.PageSettings.WidthCentimeters);
        Assert.Equal(29.7, document.PageSettings.HeightCentimeters);
        Assert.Equal(1.5, document.PageSettings.MarginTopCentimeters);
        Assert.False(document.PageSettings.IsLandscape);
        Assert.Equal("neutral-scientific", document.Appearance.StyleId);
        Assert.True(document.Appearance.MonochromeFriendly);
        Assert.False(document.Appearance.ProminentBranding);
        Assert.Equal("Supporting Document 1B", document.DocumentLabel);
        Assert.Equal("Printable analysis", document.Title);

        Assert.Equal(new[]
        {
            AnalysisReportSectionKind.Cover,
            AnalysisReportSectionKind.AnalysisSummary,
            AnalysisReportSectionKind.Experiment,
            AnalysisReportSectionKind.Experiment,
            AnalysisReportSectionKind.Appendix,
        }, document.Sections.Select(section => section.Kind));
        Assert.False(document.Sections[0].Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage));
        Assert.True(document.Sections[0].Layout.HasFlag(AnalysisReportLayoutPolicy.ShrinkToSinglePage));
        Assert.All(document.Sections.Skip(1), section =>
            Assert.True(section.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage)));
        Assert.All(document.Sections.Where(section => section.Kind != AnalysisReportSectionKind.Cover), section =>
            Assert.True(section.Layout.HasFlag(AnalysisReportLayoutPolicy.AllowContinuation)));
    }

    [Fact]
    public void CoverContainsAllMembersWithStableExcelStyleLabelsAndReportPreset()
    {
        var result = CreateResult(27);
        var document = AnalysisReportBuilder.Build(result);
        var contactSheet = Assert.IsType<AnalysisReportContactSheetBlock>(
            document.Sections[0].Blocks.Single(block => block is AnalysisReportContactSheetBlock));

        Assert.Equal(27, contactSheet.Cells.Count);
        Assert.Equal("A", contactSheet.Cells[0].PanelLabel);
        Assert.Equal("Z", contactSheet.Cells[25].PanelLabel);
        Assert.Equal("AA", contactSheet.Cells[26].PanelLabel);
        Assert.True(contactSheet.Layout.HasFlag(AnalysisReportLayoutPolicy.KeepTogether));
        Assert.True(contactSheet.Layout.HasFlag(AnalysisReportLayoutPolicy.ShrinkToSinglePage));
        Assert.True(contactSheet.Rows * contactSheet.Columns >= contactSheet.Cells.Count);

        foreach (var cell in contactSheet.Cells)
        {
            Assert.NotNull(cell.Figure.ThermogramPanel);
            Assert.NotNull(cell.Figure.FitPanel);
            Assert.NotNull(cell.Figure.ResidualPanel);
            Assert.False(cell.Figure.Options.ShowExperimentDetails);
            Assert.False(cell.Figure.Options.ShowFitParameters);
            Assert.True(cell.Figure.Options.ShowErrorBars);
            Assert.True(cell.Figure.Options.ShowConfidenceBand);
            Assert.True(cell.Figure.Options.ShowZeroLine);
            Assert.Empty(cell.Figure.Panels.SelectMany(panel => panel.AnnotationBoxes));
        }

        var sourceOverview = AnalysisResultOverviewTable.Build(
            result,
            new AnalysisReportOptions().EnergyUnitFamily,
            energyUnitOverride: null,
            useKelvin: false);
        var reportOverview = document.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportTableBlock>().Single();
        Assert.Equal(sourceOverview.Columns.Count, reportOverview.Columns.Count);
        Assert.Equal(sourceOverview.Rows.Count, reportOverview.Rows.Count);
    }

    [Fact]
    public void ExpandedExperimentLabelsMatchCoverAndContainDetailsWithoutRawTables()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(3));
        var contactSheet = document.Sections[0].Blocks.OfType<AnalysisReportContactSheetBlock>().Single();
        var experiments = document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment).ToList();

        Assert.Equal(contactSheet.Cells.Select(cell => cell.PanelLabel),
            experiments.Select(section => section.Blocks.OfType<AnalysisReportFigureBlock>().Single().PanelLabel));
        Assert.All(experiments, section =>
        {
            var metadata = section.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Single(block => block.Title == "Experiment details");
            Assert.Contains(metadata.Items, item => item.Label == "Source file" && item.Value.EndsWith(".itc"));
            Assert.Contains(metadata.Items, item => item.Label == "Cell volume" && item.Value.Contains("µL"));
            var processing = section.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Single(block => block.Title == "Processing and integration");
            Assert.Contains(processing.Items, item => item.Label == "Included injections" && item.Value == "3");
            Assert.Contains(processing.Items, item => item.Label == "Excluded injections" && item.Value == "2");
            Assert.Contains(section.Blocks.OfType<AnalysisReportTableBlock>(), block => block.Title == "Fitted and derived parameters");
            Assert.DoesNotContain(section.Blocks.OfType<AnalysisReportTableBlock>(), block =>
                block.Title.Contains("injection", StringComparison.OrdinalIgnoreCase)
                || block.Title.Contains("raw", StringComparison.OrdinalIgnoreCase));
        });
        Assert.Contains(experiments[0].Blocks.OfType<AnalysisReportTextBlock>(), block =>
            block.Title == "Comments" && block.Text == "Experiment note.");
    }

    [Fact]
    public void SummaryUsesOverviewTableAndHonorsUnitsTemperatureAndUncertaintyOptions()
    {
        var result = CreateResult(2, temperatureStep: 15);
        var options = new AnalysisReportOptions
        {
            EnergyUnitFamily = EnergyUnitFamily.Calories,
            EnergyUnitOverride = EnergyUnit.Cal,
            UseKelvin = true,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.ConfidenceInterval,
        };

        var document = AnalysisReportBuilder.Build(result, options);
        var summary = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        var overview = summary.Blocks.OfType<AnalysisReportTableBlock>().Single();

        Assert.Equal(result.Solution.Solutions.Count, overview.Rows.Count);
        Assert.True(overview.Layout.HasFlag(AnalysisReportLayoutPolicy.ShrinkToSinglePage));
        Assert.Contains(overview.Columns, column => column.Title.Contains("K"));
        Assert.Contains(overview.Columns, column => column.Title.Contains("cal"));
        var parameterTable = document.Sections
            .First(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportTableBlock>().Single();
        Assert.All(parameterTable.Rows, row => Assert.True(string.IsNullOrEmpty(row.Cells[3])));
    }

    [Fact]
    public void ParameterTableKeepsOriginalBestFitWhenBootstrapDistributionIsSkewed()
    {
        var result = CreateResult(1, includeSkewedBootstrap: true);
        var expectedKd = result.Solution.Solutions[0].ReportParameters[ParameterType.Affinity1].Value;
        Assert.Equal(1e-6, expectedKd, 12);

        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        {
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
        });
        var table = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportTableBlock>().Single();
        var affinity = table.Rows.Single(row => row.Cells[0] == "Affinity");

        Assert.Equal("1000", affinity.Cells[2]);
        Assert.Equal("nM", affinity.Cells[5]);
        Assert.NotEmpty(affinity.Cells[3]);
    }

    [Fact]
    public void WeightedFitRetainsUnweightedRmsdLabelAndDoesNotInventObjective()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(1, weighted: true));
        var labels = document.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items)
            .Select(item => item.Label)
            .ToList();

        Assert.Contains("Unweighted RMSD", labels);
        Assert.DoesNotContain(labels, label => label.Contains("objective", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(document.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items), item =>
                item.Label == "Fitting" && item.Value == "Weighted injection errors");
    }

    [Fact]
    public void AdvancedSectionsAreOptInAndUnavailableRequestsBecomeWarnings()
    {
        var result = CreateResult(1);
        Assert.Empty(AnalysisReportBuilder.GetAvailableAdvancedSections(result));
        Assert.DoesNotContain(AnalysisReportBuilder.Build(result).Sections,
            section => section.Kind == AnalysisReportSectionKind.AdvancedAnalysis);

        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(
            AnalysisReportAdvancedSectionKind.SpolarRecord));
        var requested = AnalysisReportBuilder.Build(result, options);

        Assert.DoesNotContain(requested.Sections,
            section => section.Kind == AnalysisReportSectionKind.AdvancedAnalysis);
        Assert.Contains(requested.Diagnostics, diagnostic =>
            diagnostic.Code == "advanced-section-omitted"
            && diagnostic.Message.Contains("SpolarRecord"));
        Assert.Contains(requested.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Appendix)
            .Blocks.OfType<AnalysisReportNoticeBlock>(), block =>
                block.Title == "Report warnings" && block.Message.Contains("SpolarRecord"));
    }

    [Fact]
    public void SavedSpolarAndTemperatureSelectionsShareOneTemperaturePlot()
    {
        var result = CreateResult(2, temperatureStep: 15);
        result.SpolarRecordAnalysis.RestoreResult(
            FTSRMethod.SRFoldedMode.Glob,
            FTSRMethod.SRTempMode.ReferenceTemperature,
            new FTSRMethod.SROutput(
                new FloatWithError(-10, 1),
                new FloatWithError(-20, 2),
                new FloatWithError(100, 5),
                new FloatWithError(25)),
            500,
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(
            AnalysisReportAdvancedSectionKind.SpolarRecord));
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(
            AnalysisReportAdvancedSectionKind.TemperatureDependence));

        var document = AnalysisReportBuilder.Build(result, options);
        var plots = document.Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.AdvancedAnalysis)
            .SelectMany(section => section.Blocks.OfType<AnalysisReportPlotBlock>())
            .Where(plot => plot.Title == "Temperature dependence")
            .ToList();

        Assert.Single(plots);
        Assert.NotEmpty(plots[0].Series);
        Assert.All(document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.AdvancedAnalysis),
            section => Assert.True(section.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage)));
    }

    [Fact]
    public void CorrelationDiscoveryAndRequestPreserveSelectedMemberScope()
    {
        var result = CreateResult(2, includeSkewedBootstrap: true);
        var direct = new BootstrapCorrelationAnalyzer().Analyze(result, 1);
        Assert.True(direct.IsAvailable, direct.Availability.Reason);
        var available = AnalysisReportBuilder.GetAvailableAdvancedSections(result);
        var member = available.Single(item =>
            item.Request.Kind == AnalysisReportAdvancedSectionKind.Correlation
            && item.Request.CorrelationMemberIndex == 1);
        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(member.Request);

        var document = AnalysisReportBuilder.Build(result, options);
        var section = document.Sections.Single(item =>
            item.Kind == AnalysisReportSectionKind.AdvancedAnalysis);
        var matrix = Assert.Single(section.Blocks.OfType<AnalysisReportCorrelationMatrixBlock>());

        Assert.Contains("Experiment 2", section.Title);
        Assert.NotEmpty(matrix.Labels);
        Assert.Equal(matrix.Labels.Count, matrix.Matrix.GetLength(0));
        Assert.Equal(matrix.Labels.Count, matrix.Matrix.GetLength(1));
    }

    [Fact]
    public void StaleResultRemainsReportableWithProminentWarnings()
    {
        var result = CreateResult(2);
        result.Solution.Solutions[0].Data.CellConcentration = new FloatWithError(99e-6);

        var document = AnalysisReportBuilder.Build(result);

        Assert.True(document.IsValid);
        Assert.NotEqual(AnalysisResultHealth.Valid, result.Health);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "result-health");
        Assert.Contains(document.Sections[0].Blocks.OfType<AnalysisReportNoticeBlock>(), notice =>
            notice.Level == AnalysisReportNoticeLevel.Warning
            || notice.Level == AnalysisReportNoticeLevel.Error);
    }

    [Fact]
    public void NonFiniteReportedParametersFailValidationWithoutRendering()
    {
        var result = CreateResult(1);
        result.Solution.Solutions[0].Parameters[ParameterType.Enthalpy1] = FloatWithError.NaN;

        var document = AnalysisReportBuilder.Build(result);

        Assert.False(document.IsValid);
        Assert.Empty(document.Sections);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "non-finite-parameters");
    }

    [Fact]
    public void BuildDoesNotMutateSelectionParametersOrSavedBootstrapData()
    {
        var result = CreateResult(1, includeSkewedBootstrap: true);
        var member = result.Solution.Solutions[0];
        var include = member.Data.Injections[1].Include;
        var enthalpy = member.Parameters[ParameterType.Enthalpy1].Value;
        var bootstrapCount = member.BootstrapSolutions.Count;
        var bootstrapAffinity = member.BootstrapSolutions[0].Parameters[ParameterType.Affinity1].Value;

        _ = AnalysisReportBuilder.Build(result);

        Assert.Equal(include, member.Data.Injections[1].Include);
        Assert.Equal(enthalpy, member.Parameters[ParameterType.Enthalpy1].Value);
        Assert.Equal(bootstrapCount, member.BootstrapSolutions.Count);
        Assert.Equal(bootstrapAffinity, member.BootstrapSolutions[0].Parameters[ParameterType.Affinity1].Value);
    }

    [Fact]
    public void NullAndStructurallyUnusableResultsReturnValidationErrors()
    {
        var missing = AnalysisReportBuilder.Build(null);

        Assert.False(missing.IsValid);
        Assert.Empty(missing.Sections);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "missing-result");
        Assert.Empty(AnalysisReportBuilder.GetAvailableAdvancedSections(null));
    }

    static AnalysisResult CreateResult(
        int count,
        bool weighted = false,
        double temperatureStep = 0,
        bool includeSkewedBootstrap = false)
    {
        var models = new List<Model>();
        var members = new List<SolutionInterface>();
        for (var index = 0; index < count; index++)
        {
            var data = CreateExperiment(index, 20 + index * temperatureStep);
            var model = CreateModel(data, affinity: 6, enthalpy: -25_000 - index * 1_000);
            var member = SolutionInterface.FromModel(model, Convergence());
            member.ErrorMethod = includeSkewedBootstrap
                ? ErrorEstimationMethod.BootstrapResiduals
                : ErrorEstimationMethod.None;
            model.Solution = member;
            data.UpdateSolution(model);

            if (includeSkewedBootstrap)
            {
                var bootstraps = new List<SolutionInterface>();
                foreach (var affinity in Enumerable.Range(0, 30).Select(value => 2.0 + value * 0.05))
                {
                    var bootstrapModel = CreateModel(data, affinity, -10_000 - affinity * 1_000);
                    var bootstrap = SolutionInterface.FromModel(bootstrapModel, Convergence());
                    bootstrapModel.Solution = bootstrap;
                    bootstraps.Add(bootstrap);
                }
                member.SetBootstrapSolutions(bootstraps);
            }

            models.Add(model);
            members.Add(member);
        }

        var globalModel = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = includeSkewedBootstrap
                    ? ErrorEstimationMethod.BootstrapResiduals
                    : ErrorEstimationMethod.None,
            },
        };
        foreach (var member in members)
            globalModel.Parameters.AddIndivdualParameter(member.Model.Parameters);
        var global = new GlobalSolution(
            new GlobalSolver
            {
                Model = globalModel,
                UseErrorWeightedFitting = weighted,
                ErrorEstimationMethod = includeSkewedBootstrap
                    ? ErrorEstimationMethod.BootstrapResiduals
                    : ErrorEstimationMethod.None,
            },
            members,
            Convergence());
        globalModel.Solution = global;
        return new AnalysisResult(global)
        {
            Name = "Report result",
            Comments = "Saved analysis comments.",
            Date = new DateTime(2026, 8, 30),
        };
    }

    static ExperimentData CreateExperiment(int index, double temperature)
    {
        var data = new ExperimentData("experiment-" + (index + 1) + ".itc")
        {
            Name = "Experiment " + (index + 1),
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = temperature,
            TargetTemperature = temperature,
            Date = new DateTime(2026, 8, 1).AddDays(index),
            Comments = index == 0 ? "Experiment note." : "",
        };
        data.DataPoints.Add(new DataPoint(0, 0, (float)temperature));
        data.DataPoints.Add(new DataPoint(60, -1e-6f, (float)temperature));
        data.DataPoints.Add(new DataPoint(120, 0, (float)temperature));
        data.BaseLineCorrectedDataPoints = data.DataPoints.ToList();
        for (var injectionIndex = 0; injectionIndex < 4; injectionIndex++)
        {
            var injection = new InjectionData(
                data,
                injectionIndex,
                2e-6,
                data.SyringeConcentration * 2e-6,
                include: injectionIndex != 1)
            {
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = (injectionIndex + 1) * 5e-6,
                Ratio = injectionIndex + 1,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + injectionIndex * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        return data;
    }

    static OneSetOfSites CreateModel(ExperimentData data, double affinity, double enthalpy)
    {
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, affinity);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, enthalpy);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.ModelCloneOptions = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.None,
        };
        return model;
    }

    static SolverConvergence Convergence()
    {
        return SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 1.25,
            Iterations = 12,
            TimeSeconds = 0.25,
        });
    }
}
