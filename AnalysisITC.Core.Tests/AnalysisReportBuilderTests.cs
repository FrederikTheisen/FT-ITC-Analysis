using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisReportBuilderTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(12, 5)]
    public void CoverChoosesThreeToFiveColumnsAndCentersThroughSharedCanvasLayout(int count, int expectedColumns)
    {
        var canvas = AnalysisReportBuilder.Build(CreateResult(count)).Sections[0]
            .Blocks.OfType<AnalysisReportFigureCanvasBlock>().Single().Canvas;

        Assert.Equal(expectedColumns, canvas.Options.Columns);
        Assert.Equal((int)Math.Ceiling(count / (double)expectedColumns), canvas.Options.Rows);
        Assert.True(canvas.Options.PlotWidthCentimeters <= 5);
        Assert.True(canvas.Options.PlotHeightCentimeters <= 7.7);
        Assert.Equal(10, canvas.Options.FontSize);
    }

    [Fact]
    public void ReportDefaultsToSdAndConfidenceInterval()
    {
        Assert.Equal(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            new AnalysisReportOptions().UncertaintyDisplayStyle);
    }

    [Theory]
    [InlineData(7, 7.5)]
    [InlineData(8, 5.5)]
    [InlineData(12, 5.5)]
    public void SummaryTableReducesTypeWhenMoreThanSevenParameters(
        int parameterCount,
        double expectedFontSize)
    {
        Assert.Equal(expectedFontSize, AnalysisReportBuilder.SummaryTableFontSize(parameterCount));
    }

    [Fact]
    public void OverviewAppendsOffsetBeforeLossAndUsesMemberFittedValues()
    {
        var result = CreateResult(2);
        var members = result.Solution.Solutions;
        members[0].Parameters[ParameterType.Offset] = new FloatWithError(125, 5);
        members[1].Parameters[ParameterType.Offset] = new FloatWithError(-375, 10);

        var table = AnalysisResultOverviewTable.Build(
            result,
            EnergyUnitFamily.Joules,
            EnergyUnit.Joule,
            useKelvin: false,
            UncertaintyDisplayStyle.StandardDeviation);
        var offset = Assert.Single(table.Columns, column => column.Parameter == ParameterType.Offset);
        var offsetIndex = table.Columns.ToList().IndexOf(offset);
        var lossIndex = table.Columns.ToList().FindIndex(column => column.Id == "Loss");

        Assert.Equal(lossIndex - 1, offsetIndex);
        Assert.Equal("Offset (J/mol)", offset.Title);
        Assert.DoesNotContain(ParameterType.Offset, members[0].ReportParameters.Keys);
        Assert.Equal("125 ± 5", table.Rows[0][offset.Id]);
        Assert.Equal("-375 ± 10", table.Rows[1][offset.Id]);
    }

    [Fact]
    public void OverviewUsesBootstrapOffsetUncertaintyAndKeepsOriginalBestFit()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions.Single();
        const double bestFit = 125;
        member.Parameters[ParameterType.Offset] = new FloatWithError(bestFit);

        var bootstraps = new List<SolutionInterface>();
        foreach (var offset in new[] { 100d, 110d, 140d, 160d })
        {
            var bootstrapModel = CreateModel(member.Data, affinity: 6, enthalpy: -25_000);
            bootstrapModel.Parameters.Table[ParameterType.Offset].Update(offset);
            var bootstrap = SolutionInterface.FromModel(bootstrapModel, Convergence());
            bootstrapModel.Solution = bootstrap;
            bootstraps.Add(bootstrap);
        }
        member.SetBootstrapSolutions(bootstraps);

        var estimate = member.Parameters[ParameterType.Offset];
        var table = AnalysisResultOverviewTable.Build(
            result,
            EnergyUnitFamily.Joules,
            EnergyUnit.Joule,
            useKelvin: false,
            UncertaintyDisplayStyle.StandardDeviation);
        var offsetColumn = Assert.Single(table.Columns, column => column.Parameter == ParameterType.Offset);

        Assert.Equal(bestFit, estimate.Value);
        Assert.Equal(Math.Sqrt(575), estimate.SD, 12);
        Assert.Equal(
            estimate.Energy.ToFormattedString(
                EnergyUnit.Joule,
                withunit: false,
                style: UncertaintyDisplayStyle.StandardDeviation),
            table.Rows.Single()[offsetColumn.Id]);
        Assert.Contains(" ± ", table.Rows.Single()[offsetColumn.Id]);
    }

    [Fact]
    public void OverviewPreservesOffsetProfileConfidenceInterval()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions.Single();
        member.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        member.Parameters[ParameterType.Offset] = new FloatWithError(125, 12, 90, 170);

        var table = AnalysisResultOverviewTable.Build(
            result,
            EnergyUnitFamily.Joules,
            EnergyUnit.Joule,
            useKelvin: false,
            UncertaintyDisplayStyle.ConfidenceInterval);
        var offset = Assert.Single(table.Columns, column => column.Parameter == ParameterType.Offset);

        Assert.Equal(125, member.Parameters[ParameterType.Offset].Value);
        Assert.Equal(
            member.Parameters[ParameterType.Offset].Energy.ToFormattedString(
                EnergyUnit.Joule,
                withunit: false,
                style: UncertaintyDisplayStyle.ConfidenceInterval),
            table.Rows.Single()[offset.Id]);
        Assert.Contains("[90, 170]", table.Rows.Single()[offset.Id]);
    }

    [Fact]
    public void OverviewLeavesMissingMemberOffsetBlankAndOmitsColumnWhenAllAreMissing()
    {
        var result = CreateResult(2);
        var members = result.Solution.Solutions;
        members[1].Parameters.Remove(ParameterType.Offset);

        var mixed = AnalysisResultOverviewTable.Build(
            result, EnergyUnit.Joule, useKelvin: false);
        var offset = Assert.Single(mixed.Columns, column => column.Parameter == ParameterType.Offset);
        Assert.NotEmpty(mixed.Rows[0][offset.Id]);
        Assert.Empty(mixed.Rows[1][offset.Id]);

        members[0].Parameters.Remove(ParameterType.Offset);
        var missing = AnalysisResultOverviewTable.Build(
            result, EnergyUnit.Joule, useKelvin: false);
        Assert.DoesNotContain(missing.Columns, column => column.Parameter == ParameterType.Offset);
    }

    [Fact]
    public void OverviewAutomaticEnergyUnitIncludesOffsetMagnitude()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions.Single();
        member.Parameters[ParameterType.Affinity1] = new FloatWithError(0);
        member.Parameters[ParameterType.Enthalpy1] = new FloatWithError(10);
        member.Parameters[ParameterType.Offset] = new FloatWithError(500);

        var table = AnalysisResultOverviewTable.Build(
            result, EnergyUnitFamily.Joules, useKelvin: false);
        var offset = Assert.Single(table.Columns, column => column.Parameter == ParameterType.Offset);

        Assert.Equal(EnergyUnit.KiloJoule, table.ResolvedEnergyUnit);
        Assert.Equal("Offset (kJ/mol)", offset.Title);
    }

    [Fact]
    public void ReportOverviewIncludesOffsetColumn()
    {
        var result = CreateResult(2);
        foreach (var member in result.Solution.Solutions)
            member.Parameters[ParameterType.Offset] = new FloatWithError(-250, 15);

        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        {
            EnergyUnitOverride = EnergyUnit.Joule,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation,
        });
        var overview = document.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportTableBlock>().Single();
        var offsetIndex = overview.Columns.ToList().FindIndex(column => column.Title == "Offset (J/mol)");

        Assert.True(offsetIndex >= 0);
        Assert.All(overview.Rows, row => Assert.Equal("-250 ± 15", row.Cells[offsetIndex]));
    }

    [Fact]
    public async System.Threading.Tasks.Task JorsSummaryUsesAllSavedMembersAndOriginalBestFitValues()
    {
        using var source = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "jors.ftxtc"));
        var result = (await FTXTCReader.ReadStream(source)).OfType<AnalysisResult>()
            .Single(item => item.Name == "OneSetOfSites");
        var before = result.Solution.Solutions
            .Select(solution => solution.ReportParameters[ParameterType.Enthalpy1].Value).ToArray();

        var document = AnalysisReportBuilder.Build(result);
        var summary = document.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>().Single();

        Assert.Equal(3, summary.Series.Count);
        Assert.Equal(new[] { "ΔH", "−TΔS", "ΔG" }, summary.Categories);
        Assert.Equal(new[] { "1A", "1B", "1C" },
            summary.Series.Select(series => series.Label.Split('.')[0]));
        Assert.Equal(UncertaintyDisplayStyle.ConfidenceInterval, summary.UncertaintyStyle);
        Assert.DoesNotContain("symmetric approximation", summary.UncertaintyNote);
        Assert.Contains("95% CI", summary.UncertaintyNote);
        Assert.Contains("solution distribution", summary.UncertaintyNote);
        Assert.Equal(before, result.Solution.Solutions
            .Select(solution => solution.ReportParameters[ParameterType.Enthalpy1].Value).ToArray());
        for (var index = 0; index < summary.Series.Count; index++)
        {
            var bar = summary.Series[index].Bars.Single(item => item.Category == "ΔH");
            Assert.Equal(before[index] * Energy.ScaleFactor(EnergyUnit.KiloJoule), bar.Value, 8);
            Assert.NotNull(bar.StandardDeviationLower);
            Assert.NotNull(bar.ConfidenceLower);
        }

        var processingFigure = document.Sections
            .First(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportFigurePairBlock>().Single().LeftFigure;
        var baseline = processingFigure.ThermogramPanel.Series
            .Single(series => series.Role == PublicationSeriesRole.Baseline);
        Assert.All(baseline.Points, point => Assert.InRange(
            point.Y,
            processingFigure.ThermogramPanel.YAxis.Minimum,
            processingFigure.ThermogramPanel.YAxis.Maximum));
        var raw = processingFigure.ThermogramPanel.Series
            .Single(series => series.Role == PublicationSeriesRole.Thermogram);
        Assert.Contains(raw.Points, point =>
            point.Y < processingFigure.ThermogramPanel.YAxis.Minimum
            || point.Y > processingFigure.ThermogramPanel.YAxis.Maximum);
    }

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
        Assert.Equal("Exported 3 Sep 2026 UTC", document.ExportDateText);
        Assert.Equal("ANALYSIS VALID", document.StatusBadgeText);
        var subtitle = Assert.Single(document.Sections[0].Blocks.OfType<AnalysisReportTextBlock>(),
            block => block.Text == "Supporting Document 1B");
        Assert.Equal("", subtitle.Title);
        var coverAnalysis = document.Sections[0].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Analysis");
        Assert.DoesNotContain(coverAnalysis.Items, item => item.Label == "Status");
        var overview = document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportTableBlock>()
            .Single(block => block.Title == "Experiment parameter overview");
        var parameterWidth = overview.Columns.First(column => column.Id.StartsWith("Parameter:", StringComparison.Ordinal)).WidthWeight;
        Assert.True(overview.Columns.Single(column => column.Id == "Loss").WidthWeight < parameterWidth);
        Assert.True(overview.Columns.Single(column => column.Id == "InformationCriteria").WidthWeight < parameterWidth);

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
    public void CoverUsesPublicationCanvasWithStableLabelsTitlesAndReportPreset()
    {
        var result = CreateResult(27);
        var document = AnalysisReportBuilder.Build(result);
        var cover = Assert.IsType<AnalysisReportFigureCanvasBlock>(
            document.Sections[0].Blocks.Single(block => block is AnalysisReportFigureCanvasBlock));
        var canvas = cover.Canvas;

        Assert.Equal(27, canvas.Cells.Count);
        Assert.Equal("1A", canvas.Cells[0].PanelLabel);
        Assert.Equal("1Z", canvas.Cells[25].PanelLabel);
        Assert.Equal("1AA", canvas.Cells[26].PanelLabel);
        Assert.Equal("Experiment 1", canvas.Cells[0].PanelTitle);
        Assert.True(cover.Layout.HasFlag(AnalysisReportLayoutPolicy.KeepTogether));
        Assert.True(cover.Layout.HasFlag(AnalysisReportLayoutPolicy.ShrinkToSinglePage));
        Assert.InRange(canvas.Options.Columns, 3, 5);
        Assert.True(canvas.Options.Rows * canvas.Options.Columns >= canvas.Cells.Count);
        Assert.True(canvas.Options.ShowPanelTitles);
        Assert.False(canvas.Options.GroupResultFigures);
        Assert.False(canvas.Options.ShowInformationBoxes);

        Assert.False(canvas.FigureOptions.ShowExperimentDetails);
        Assert.False(canvas.FigureOptions.ShowFitParameters);
        Assert.True(canvas.FigureOptions.ShowErrorBars);
        Assert.True(canvas.FigureOptions.ShowConfidenceBand);
        Assert.True(canvas.FigureOptions.ShowZeroLine);

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
    public void ExpandedExperimentLabelsMatchCoverAndContainDetailsAndInjectionTables()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(3));
        var canvas = document.Sections[0].Blocks.OfType<AnalysisReportFigureCanvasBlock>().Single().Canvas;
        var experiments = document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment).ToList();

        Assert.Equal(canvas.Cells.Select(cell => cell.PanelLabel + ". " + cell.PanelTitle),
            experiments.Select(section => section.Title));
        var overview = document.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportTableBlock>().Single();
        Assert.Equal(new[] { "1A", "1B", "1C" },
            overview.Rows.Select(row => row.Cells[0].Split('.')[0]));
        var provenance = document.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.Appendix)
            .Blocks.OfType<AnalysisReportTableBlock>()
            .Single(table => table.Title == "Input provenance");
        Assert.Equal(new[] { "1A", "1B", "1C" },
            provenance.Rows.Select(row => row.Cells[0].Split('.')[0]));
        Assert.All(experiments, section =>
        {
            var figures = Assert.Single(section.Blocks.OfType<AnalysisReportFigurePairBlock>());
            Assert.Equal("Baseline and integration windows", figures.LeftTitle);
            Assert.Null(figures.LeftFigure.FitPanel);
            Assert.NotNull(figures.LeftFigure.ThermogramPanel);
            Assert.True(figures.LeftFigure.Options.ShowBaseline);
            Assert.True(figures.LeftFigure.Options.ShowIntegrationRegions);
            Assert.True(figures.LeftFigure.Options.FocusThermogramOnBaseline);
            Assert.Equal(PublicationIntegrationRegionStyle.Line, figures.LeftFigure.Options.IntegrationRegionStyle);
            Assert.Equal(8.5, figures.LeftFigure.Options.PlotWidthCentimeters);
            Assert.Equal(figures.RightFigure.Options.PlotHeightCentimeters,
                figures.LeftFigure.Options.PlotHeightCentimeters);
            Assert.True(figures.LeftFigure.Options.PlotWidthCentimeters
                > figures.RightFigure.Options.PlotWidthCentimeters);
            Assert.Equal(6, figures.RightFigure.Options.PlotWidthCentimeters);
            Assert.Equal(10, figures.RightFigure.Options.PlotHeightCentimeters);
            var metadata = section.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Single(block => block.Title == "Experiment details");
            Assert.Contains(metadata.Items, item => item.Label == "Source file" && item.Value.EndsWith(".itc"));
            Assert.Contains(metadata.Items, item => item.Label == "Temperature"
                && item.Value.Contains("Measured") && item.Value.Contains("target"));
            Assert.DoesNotContain(metadata.Items, item => item.Label == "Measured temperature"
                || item.Label == "Target temperature");
            Assert.Contains(metadata.Items, item => item.Label == "Experiment settings" && item.IndentLevel == 0);
            Assert.Contains(metadata.Items, item => item.Label == "Cell volume"
                && item.Value.Contains("µL") && item.IndentLevel == 1);
            var processing = section.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Single(block => block.Title == "Processing and integration");
            Assert.Contains(processing.Items, item => item.Label == "Injection use" && item.Value.Contains("3 included"));
            Assert.Contains(processing.Items, item => item.Label == "Integration regions" && item.IndentLevel == 0);
            Assert.Contains(processing.Items, item => item.Label == "Start after injection" && item.IndentLevel == 1);
            Assert.Contains(processing.Items, item => item.Label == "End after injection" && item.IndentLevel == 1);
            Assert.Contains(section.Blocks.OfType<AnalysisReportTableBlock>(), block => block.Title == "Fitted and derived parameters");
            var injectionTable = section.Blocks.OfType<AnalysisReportTableBlock>()
                .Single(block => block.Title == "Injection data");
            Assert.True(injectionTable.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage));
            Assert.True(injectionTable.Layout.HasFlag(AnalysisReportLayoutPolicy.AllowContinuation));
            Assert.Equal(1.5, injectionTable.VerticalCellPadding);
            Assert.Equal(10, injectionTable.Columns.Count);
            Assert.Equal(4, injectionTable.Rows.Count);
            Assert.Equal(new[] { "#", "Use", "Vol. (µL)" },
                injectionTable.Columns.Take(3).Select(column => column.Title));
            Assert.Contains(injectionTable.Columns, column => column.Title.StartsWith("Heat ("));
            Assert.Contains(injectionTable.Columns, column => column.Title.StartsWith("Fit ("));
            Assert.Contains(injectionTable.Columns, column => column.Title.StartsWith("Residual ("));
        });
        Assert.Contains(experiments[0].Blocks.OfType<AnalysisReportTextBlock>(), block =>
            block.Title == "Comments" && block.Text == "Experiment note.");
    }

    [Fact]
    public void InjectionTablesCanBeOmittedForEveryExperiment()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(3), new AnalysisReportOptions
        {
            IncludeInjectionTables = false,
        });

        Assert.All(document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment),
            section => Assert.DoesNotContain(section.Blocks.OfType<AnalysisReportTableBlock>(),
                block => block.Title == "Injection data"));
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
            .Blocks.OfType<AnalysisReportTableBlock>()
            .Single(block => block.Title == "Fitted and derived parameters");
        Assert.Equal(4, parameterTable.Columns.Count);
        Assert.All(parameterTable.Rows, row => Assert.DoesNotContain(" ± ", row.Cells[2]));

        var summaryPlot = Assert.Single(summary.Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>());
        Assert.Equal(result.Solution.Solutions.Count, summaryPlot.Series.Count);
        Assert.Contains("ΔH", summaryPlot.Categories);
        Assert.Contains("−TΔS", summaryPlot.Categories);
        Assert.Contains("ΔG", summaryPlot.Categories);
        Assert.Equal(UncertaintyDisplayStyle.ConfidenceInterval, summaryPlot.UncertaintyStyle);
        Assert.Contains("95% CI", summaryPlot.UncertaintyNote);
        Assert.DoesNotContain("±1 SD", summaryPlot.UncertaintyNote);
    }

    [Fact]
    public void ExperimentProcessingOmitsRoutineCompletionNoiseButKeepsExceptions()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(1));
        var processing = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Processing and integration");

        Assert.DoesNotContain(processing.Items, item => item.Label == "Baseline completed");
        Assert.DoesNotContain(processing.Items, item => item.Label == "Integration mode");
        Assert.Contains(processing.Items, item => item.Label == "Baseline status" && item.Value == "Incomplete");
        Assert.Contains(processing.Items, item => item.Label == "Injection use");
        Assert.DoesNotContain(processing.Items, item => item.Label == "Included injections" || item.Label == "Excluded injections");
    }

    [Fact]
    public void GlobalMemberDetailsDoNotContradictGlobalFitConfiguration()
    {
        var result = CreateResult(3, weighted: true);
        result.Model.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.SameForAll);
        var document = AnalysisReportBuilder.Build(result);
        foreach (var section in document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment))
        {
            var details = section.Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Fit details");
            Assert.DoesNotContain(details.Items, item => item.Label == "Fitting");
            Assert.DoesNotContain(details.Items, item => item.Label == "Status");
            Assert.DoesNotContain(details.Items, item => item.Label == "Uncertainty method");
            Assert.Contains(details.Items, item => item.Label == "RMSD");
        }

        var appendix = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Appendix);
        var configuration = appendix.Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Analysis configuration");
        Assert.DoesNotContain(configuration.Items, item => item.Label == "RMSD");
        Assert.DoesNotContain(configuration.Items, item => item.Label == "Algorithm");
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
            .Blocks.OfType<AnalysisReportTableBlock>()
            .Single(block => block.Title == "Fitted and derived parameters");
        var affinity = table.Rows.Single(row => row.Cells[0] == "Affinity");
        var expected = result.Solution.Solutions[0].ReportParameters[ParameterType.Affinity1]
            .AsFormattedConcentration(
                ConcentrationUnit.nM,
                withunit: false,
                style: UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval);

        Assert.Equal(expected, affinity.Cells[2]);
        Assert.DoesNotContain("\n", affinity.Cells[2]);
        Assert.Contains(" [", affinity.Cells[2]);
        Assert.Equal("nM", affinity.Cells[3]);
        Assert.Equal(1.5, table.VerticalCellPadding);

        var overview = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportTableBlock>().Single();
        Assert.Contains(overview.Rows.SelectMany(row => row.Cells), cell => cell.Contains("\n["));
        var summaryPlot = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>().Single();
        Assert.Equal(UncertaintyDisplayStyle.ConfidenceInterval, summaryPlot.UncertaintyStyle);
        Assert.Contains("95% CI", summaryPlot.UncertaintyNote);
        Assert.DoesNotContain("SD", summaryPlot.UncertaintyNote);
    }

    [Fact]
    public void FitDiagnosticsUseConciseRmsdLabelAndOnlyCallOutWeightingWhenEnabled()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(1, weighted: true));
        var labels = document.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items)
            .Select(item => item.Label)
            .ToList();

        Assert.Contains("RMSD", labels);
        Assert.DoesNotContain("Unweighted RMSD", labels);
        Assert.DoesNotContain(labels, label => label.Contains("objective", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(document.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items), item =>
                item.Label == "Fitting" && item.Value == "Weighted injection errors");

        var unweighted = AnalysisReportBuilder.Build(CreateResult(1, weighted: false));
        Assert.DoesNotContain(unweighted.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items), item => item.Label == "Fitting");
    }

    [Theory]
    [InlineData(false, "one estimated common residual variance", "one common estimated residual variance")]
    [InlineData(true, "injection errors as relative uncertainties", "one common estimated variance multiplier")]
    public void InformationCriteriaDiagnosticsIdentifyLikelihoodAndPooling(bool weighted, string likelihoodText, string poolingText)
    {
        var document = AnalysisReportBuilder.Build(CreateResult(2, weighted));
        var items = document.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items).ToArray();

        Assert.Contains(items, item => item.Label == "AIC likelihood" && item.Value.Contains(likelihoodText, StringComparison.Ordinal));
        Assert.Contains(items, item => item.Label == "AIC scope" && item.Value.Contains(poolingText, StringComparison.Ordinal));
        Assert.DoesNotContain(items, item => item.Value.Contains("known-sigma likelihood", StringComparison.Ordinal));
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
    public void DebyeHuckelReportPlotsLogKdAgainstSquareRootOfIonicStrength()
    {
        var result = CreateResult(2, includeSalt: true);
        result.ElectrostaticsAnalysis.RestoreResult(
            new IonicStrengthDependenceFit(new(1e-6), new(10), new(0)),
            null, 100, 0, DateTime.UtcNow, ErrorEstimationMethod.None);
        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(
            AnalysisReportAdvancedSectionKind.DebyeHuckel));

        var plot = AnalysisReportBuilder.Build(result, options).Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportPlotBlock>())
            .Single(block => block.Title == "Debye-Huckel dependence");
        var line = Assert.Single(plot.Series, series => series.Kind == AnalysisReportPlotSeriesKind.Line);
        Assert.True(line.Points.Count >= 2);
        Assert.All(line.Points, point =>
        {
            // Kd = 1 µM * exp(10 sqrt(I)); tolerate the evaluator's existing rounded ln(10).
            var expected = Math.Log10(1e-6 * Math.Exp(10 * point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.InRange(Math.Abs(point.Y - expected), 0, 0.002);
        });
    }

    [Fact]
    public void CorrelationDiscoveryUsesOneReportControlAndPreservesExplicitMemberRequests()
    {
        var result = CreateResult(2, includeSkewedBootstrap: true);
        var direct = new BootstrapCorrelationAnalyzer().Analyze(result, 1);
        Assert.True(direct.IsAvailable, direct.Availability.Reason);
        var available = AnalysisReportBuilder.GetAvailableAdvancedSections(result);
        var aggregate = Assert.Single(available, item =>
            item.Request.Kind == AnalysisReportAdvancedSectionKind.Correlation);
        Assert.Null(aggregate.Request.CorrelationMemberIndex);
        Assert.Equal("Parameter correlations", aggregate.Title);

        var allOptions = new AnalysisReportOptions();
        allOptions.AdvancedSections.Add(aggregate.Request);
        var allDocument = AnalysisReportBuilder.Build(result, allOptions);
        Assert.NotEmpty(allDocument.Sections
            .SelectMany(item => item.Blocks.OfType<AnalysisReportCorrelationMatrixBlock>()));
        Assert.Single(allDocument.Sections.Single(item =>
                item.Kind == AnalysisReportSectionKind.Experiment && item.Title.Contains("Experiment 2"))
            .Blocks.OfType<AnalysisReportCorrelationMatrixBlock>());

        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(
            AnalysisReportAdvancedSectionKind.Correlation, 1));

        var document = AnalysisReportBuilder.Build(result, options);
        var section = document.Sections.Single(item =>
            item.Kind == AnalysisReportSectionKind.Experiment && item.Title.Contains("Experiment 2"));
        var matrix = Assert.Single(section.Blocks.OfType<AnalysisReportCorrelationMatrixBlock>());

        Assert.NotEmpty(matrix.Labels);
        Assert.Equal(matrix.Labels.Count, matrix.ColumnLabels.Count);
        Assert.All(matrix.ColumnLabels, label => Assert.DoesNotContain("·", label));
        Assert.Equal(6.5, matrix.PreferredSizeCentimeters);
        Assert.Equal(matrix.Labels.Count, matrix.Matrix.GetLength(0));
        Assert.Equal(matrix.Labels.Count, matrix.Matrix.GetLength(1));
        Assert.Empty(document.Sections
            .Single(item => item.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportCorrelationMatrixBlock>());
        Assert.Empty(document.Sections.Single(item =>
                item.Kind == AnalysisReportSectionKind.Experiment && item.Title.Contains("Experiment 1"))
            .Blocks.OfType<AnalysisReportCorrelationMatrixBlock>());
    }

    [Fact]
    public void ExperimentDateRequiresDataFileOrUserProvenanceAndMissingThermogramGetsFallback()
    {
        var result = CreateResult(3);
        result.Solution.Solutions[0].Data.Date = new DateTime(2023, 9, 8);
        result.Solution.Solutions[0].Data.DateSource = ExperimentDateSource.DataFile;
        result.Solution.Solutions[1].Data.DateSource = ExperimentDateSource.FileSystem;
        result.Solution.Solutions[2].Data.DateSource = ExperimentDateSource.UserModified;
        result.Solution.Solutions[1].Data.DataPoints.Clear();
        result.Solution.Solutions[1].Data.BaseLineCorrectedDataPoints.Clear();

        var sections = AnalysisReportBuilder.Build(result).Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.Experiment).ToList();
        var firstMetadata = sections[0].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details");
        var secondMetadata = sections[1].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details");
        var thirdMetadata = sections[2].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details");

        Assert.Contains(firstMetadata.Items,
            item => item.Label == "Experiment date" && item.Value == "8 Sep 2023");
        Assert.DoesNotContain(secondMetadata.Items, item => item.Label == "Experiment date");
        Assert.Contains(thirdMetadata.Items, item => item.Label == "Experiment date");
        Assert.Contains(sections[1].Blocks.OfType<AnalysisReportNoticeBlock>(),
            block => block.Title == "Raw processing unavailable");
        Assert.Empty(sections[1].Blocks.OfType<AnalysisReportFigurePairBlock>());
        Assert.Single(sections[1].Blocks.OfType<AnalysisReportFigureBlock>());
    }

    [Fact]
    public void ExperimentSettingsIncludeAvailableInstrumentMetadataAsNestedRows()
    {
        var result = CreateResult(1);
        var data = result.Solution.Solutions[0].Data;
        data.StirringSpeed = 750;
        data.FeedBackMode = FeedbackMode.High;
        data.InitialDelay = 60;
        var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
        salt.IntValue = (int)Salt.NaCl;
        salt.ParameterValue = new FloatWithError(0.1);
        data.Attributes.Add(salt);

        var metadata = AnalysisReportBuilder.Build(result).Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details");

        Assert.Contains(metadata.Items, item => item.Label == "Stirring speed"
            && item.Value == "750 rpm" && item.IndentLevel == 1);
        Assert.Contains(metadata.Items, item => item.Label == "Feedback"
            && item.Value == "High" && item.IndentLevel == 1);
        Assert.Contains(metadata.Items, item => item.Label == "Initial delay"
            && item.Value == "60 s" && item.IndentLevel == 1);
        Assert.Contains(metadata.Items, item => item.Label == "Attributes"
            && item.Value == "" && item.IndentLevel == 0);
        Assert.Contains(metadata.Items, item => item.Label == salt.GetDisplayName()
            && item.Value == salt.GetDisplayValue(data) && item.IndentLevel == 1);
    }

    [Fact]
    public void StaleResultRemainsReportableWithProminentWarnings()
    {
        var result = CreateResult(2);
        result.Solution.Solutions[0].Data.CellConcentration = new FloatWithError(99e-6);

        var document = AnalysisReportBuilder.Build(result);

        Assert.True(document.IsValid);
        Assert.NotEqual(AnalysisResultHealth.Valid, result.Health);
        Assert.NotEqual("ANALYSIS VALID", document.StatusBadgeText);
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
        var missing = AnalysisReportBuilder.Build((AnalysisResult)null);

        Assert.False(missing.IsValid);
        Assert.Empty(missing.Sections);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "missing-result");
        Assert.Empty(AnalysisReportBuilder.GetAvailableAdvancedSections(null));
    }

    [Fact]
    public void LightweightValidationMatchesBuildBlockingErrors()
    {
        Assert.False(AnalysisReportBuilder.Validate((AnalysisResult)null).IsValid);
        Assert.Contains(AnalysisReportBuilder.Validate((AnalysisResult)null).Diagnostics,
            diagnostic => diagnostic.Code == "missing-result");

        var result = CreateResult(1);
        Assert.True(AnalysisReportBuilder.Validate(result).IsValid);
        Assert.Empty(AnalysisReportBuilder.Validate(result).Errors);
    }

    [Fact]
    public void PaginationUsesExactA4GeometryForcedBreaksAndSafeBounds()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(2));
        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());

        Assert.Equal(21 * AnalysisReportLayoutEngine.PointsPerCentimeter, plan.PageWidth, 6);
        Assert.Equal(29.7 * AnalysisReportLayoutEngine.PointsPerCentimeter, plan.PageHeight, 6);
        Assert.Equal(1.5 * AnalysisReportLayoutEngine.PointsPerCentimeter, plan.MarginLeft, 6);
        Assert.True(plan.Pages.Count >= document.Sections.Count);
        Assert.Equal("Report result", Assert.Single(plan.Pages[0].Fragments,
            fragment => fragment.Kind == AnalysisReportFragmentKind.SectionTitle).Lines.Single());

        var expectedStarts = new[] { "Analysis summary", "1A. Experiment 1", "1B. Experiment 2", "Appendix" };
        foreach (var title in expectedStarts)
            Assert.Contains(plan.Pages, page => page.Fragments.First().Lines.Contains(title));

        var permittedBottom = plan.PageHeight - plan.MarginBottom - 18;
        Assert.All(plan.Pages.SelectMany(page => page.Fragments), fragment =>
        {
            Assert.True(fragment.Bounds.X >= plan.MarginLeft - .001);
            Assert.True(fragment.Bounds.Right <= plan.PageWidth - plan.MarginRight + .001);
            Assert.True(fragment.Bounds.Y >= plan.MarginTop - .001);
            Assert.True(fragment.Bounds.Bottom <= permittedBottom + .001);
        });

        var injectionTables = document.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportTableBlock>())
            .Where(table => table.Title == "Injection data")
            .ToList();
        Assert.All(injectionTables, table =>
        {
            var firstFragment = plan.Pages.SelectMany(page => page.Fragments)
                .First(fragment => ReferenceEquals(fragment.Block, table));
            var page = plan.Pages.Single(item => item.Fragments.Contains(firstFragment));
            Assert.Same(table, page.Fragments.First().Block);
        });
    }

    [Fact]
    public void PaginationKeepsCoverAndShrinkTableOnSinglePages()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(27));
        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        var canvas = Assert.Single(plan.Pages[0].Fragments,
            fragment => fragment.Kind == AnalysisReportFragmentKind.FigureCanvas);
        Assert.Same(document.Sections[0].Blocks.OfType<AnalysisReportFigureCanvasBlock>().Single(), canvas.Block);

        var overview = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportTableBlock>().Single();
        var fragments = plan.Pages.SelectMany(page => page.Fragments)
            .Where(fragment => ReferenceEquals(fragment.Block, overview)).ToList();
        Assert.Single(fragments);
        Assert.Equal(overview.Rows.Count, fragments[0].ItemCount);
        Assert.InRange(fragments[0].Scale, .42, 1);
    }

    [Fact]
    public void PaginationContinuesLongTablesWithRepeatedHeaders()
    {
        var document = new AnalysisReportDocument { Title = "Continuation test" };
        var section = new AnalysisReportSection(AnalysisReportSectionKind.Appendix, "appendix", "Appendix",
            AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
        var table = new AnalysisReportTableBlock("Long table",
            new[]
            {
                new AnalysisReportTableColumn("a", "Experiment"),
                new AnalysisReportTableColumn("b", "Long provenance value"),
            },
            Enumerable.Range(1, 120).Select(index => new AnalysisReportTableRow(new[]
            {
                "Experiment " + index,
                string.Join(" ", Enumerable.Repeat("saved provenance", 8)),
            })), AnalysisReportLayoutPolicy.AllowContinuation);
        section.Add(table);
        document.AddSection(section);

        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        var fragments = plan.Pages.SelectMany(page => page.Fragments)
            .Where(fragment => ReferenceEquals(fragment.Block, table)).ToList();

        Assert.True(fragments.Count > 1);
        Assert.All(fragments, fragment => Assert.True(fragment.RepeatTableHeader));
        Assert.Equal(table.Rows.Count, fragments.Sum(fragment => fragment.ItemCount));
    }

    [Fact]
    public void MultiResultBuildPreservesOrderAndCreatesIsolatedChapters()
    {
        var first = CreateResult(2); first.Name = "First analysis";
        var second = CreateResult(1); second.Name = "Second analysis";
        second.Solution.Solutions[0].Data.SetID(first.Solution.Solutions[0].Data.UniqueID);
        var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
        salt.IntValue = (int)Salt.NaCl;
        salt.ParameterValue = new FloatWithError(0.15);
        second.Solution.Solutions[0].Data.Attributes.Add(salt);

        var document = AnalysisReportBuilder.Build(new[] { first, second });

        Assert.True(document.IsValid);
        Assert.True(document.IsMultiResult);
        Assert.Equal(new[] { first.UniqueID, second.UniqueID }, document.Results.Select(result => result.Id));
        Assert.Equal(new[] { "1", "2" }, document.Results.Select(result => result.Label));
        var openers = document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.ResultOverview).ToList();
        Assert.Contains(openers, section => section.Id == "result-1-overview" && section.ResultName == first.Name);
        Assert.Contains(openers, section => section.Id == "result-2-overview" && section.ResultName == second.Name);
        Assert.Equal(document.Sections.Count, document.Sections.Select(section => section.Id).Distinct().Count());
        Assert.Contains(document.Sections, section => section.Title == "1A. Experiment 1");
        Assert.Contains(document.Sections, section => section.Title == "2A. Experiment 1");
        var summaryTables = document.Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Select(section => section.Blocks.OfType<AnalysisReportTableBlock>().Single())
            .ToList();
        Assert.Equal("1A. Experiment 1", summaryTables[0].Rows[0].Cells[0]);
        Assert.Equal("2A. Experiment 1", summaryTables[1].Rows[0].Cells[0]);
        var summaryPlots = document.Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Select(section => section.Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>().Single())
            .ToList();
        Assert.StartsWith("1A. ", summaryPlots[0].Series[0].Label);
        Assert.StartsWith("2A. ", summaryPlots[1].Series[0].Label);
        var scope = document.Sections[0].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Report scope");
        Assert.Contains(scope.Items, item => item.Label == "Distinct result experiments" && item.Value == "2");
        Assert.Equal(2, AnalysisReportBuilder.CountDistinctExperiments(new[] { first, second }));
        Assert.True(AnalysisReportBuilder.HasRepeatedExperiments(new[] { first, second }));
        var repeatedExperiment = document.Sections.Single(section => section.Title == "2A. Experiment 1");
        Assert.Single(repeatedExperiment.Blocks.OfType<AnalysisReportFigurePairBlock>());
        var condensedDetails = repeatedExperiment.Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details — condensed");
        Assert.Contains(condensedDetails.Items,
            item => item.Label == "Previously reported as" && item.Value == "1A");
        Assert.Contains(condensedDetails.Items, item => item.Label == "Source file");
        Assert.Contains(condensedDetails.Items, item => item.Label == "Instrument");
        Assert.Contains(condensedDetails.Items, item => item.Label == "Temperature");
        Assert.Contains(condensedDetails.Items, item => item.Label == "Cell concentration");
        Assert.Contains(condensedDetails.Items, item => item.Label == "Syringe concentration");
        Assert.Contains(condensedDetails.Items,
            item => item.Label == "Attributes" && item.IndentLevel == 0);
        Assert.Contains(condensedDetails.Items,
            item => item.Label == salt.GetDisplayName() && item.IndentLevel == 1);
        Assert.DoesNotContain(repeatedExperiment.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Processing and integration");
        Assert.Contains(repeatedExperiment.Blocks.OfType<AnalysisReportTextBlock>(),
            block => block.Title == "Comments" && block.Text == "Experiment note.");
        Assert.All(document.Sections.Where(section => section.Id.StartsWith("result-1-", StringComparison.Ordinal)),
            section => Assert.Equal(first.Name, section.ResultName));
        Assert.All(document.Sections.Where(section => section.Id.StartsWith("result-2-", StringComparison.Ordinal)),
            section => Assert.Equal(second.Name, section.ResultName));
    }

    [Fact]
    public void RepeatedExperimentCondensingCanBeDisabled()
    {
        var first = CreateResult(1);
        var second = CreateResult(1);
        second.Solution.Solutions[0].Data.SetID(first.Solution.Solutions[0].Data.UniqueID);

        var document = AnalysisReportBuilder.Build(new[] { first, second }, new AnalysisReportOptions
        {
            CondenseRepeatedExperiments = false,
        });
        var repeatedExperiment = document.Sections.Single(section => section.Title == "2A. Experiment 1");

        Assert.Contains(repeatedExperiment.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Experiment details");
        Assert.Contains(repeatedExperiment.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Processing and integration");
    }

    [Fact]
    public void MultiResultContentsResolveToActualChapterPages()
    {
        var first = CreateResult(1); first.Name = "Alpha";
        var second = CreateResult(1); second.Name = "Beta";
        var document = AnalysisReportBuilder.Build(new[] { first, second });
        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        var contents = document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportTableOfContentsBlock>().Single();

        Assert.Contains(plan.Pages[0].Fragments, fragment =>
            fragment.Kind == AnalysisReportFragmentKind.TableOfContents);

        foreach (var entry in contents.Entries)
        {
            var targetPage = plan.Pages.Single(page => page.Fragments.Any(fragment =>
                fragment.Kind == AnalysisReportFragmentKind.SectionTitle
                && fragment.Section?.Id == entry.TargetSectionId));
            Assert.Equal(targetPage.PageNumber, entry.PageNumber);
        }
        Assert.Equal(first.Name, plan.Pages.First(page => page.ResultName == first.Name).ResultName);
        Assert.Equal(second.Name, plan.Pages.First(page => page.ResultName == second.Name).ResultName);
    }

    [Fact]
    public void PersistedMultiResultInterpretationAppearsOnceAfterContents()
    {
        var first = CreateResult(1); first.Name = "Alpha";
        var second = CreateResult(1); second.Name = "Beta";
        var report = new AnalysisReport();
        report.SetResultIds(new[] { first.UniqueID, second.UniqueID });
        report.SetManualInterpretation("## Overall interpretation\n### Combined conclusion\nA **strong** but *qualified* conclusion.");

        var document = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : id == second.UniqueID ? second : null);

        Assert.True(document.IsValid);
        Assert.Equal(1, document.Sections.Count(section => section.Kind == AnalysisReportSectionKind.Interpretation));
        Assert.Equal(AnalysisReportSectionKind.Cover, document.Sections[0].Kind);
        Assert.Contains(document.Sections[0].Blocks, block => block is AnalysisReportTableOfContentsBlock);
        Assert.Equal(AnalysisReportSectionKind.Interpretation, document.Sections[1].Kind);
        Assert.Equal(AnalysisReportSectionKind.ResultOverview, document.Sections[2].Kind);
        var contents = document.Sections[0].Blocks.OfType<AnalysisReportTableOfContentsBlock>().Single();
        Assert.Equal("Interpretation", contents.Entries[0].Title);
        var interpretation = document.Sections[1];
        Assert.Contains(interpretation.Blocks.OfType<AnalysisReportHeadingBlock>(), block =>
            block.Level == 3 && block.Text == "Combined conclusion");
        Assert.Contains(interpretation.Blocks.OfType<AnalysisReportTextBlock>(), block =>
            block.InlineMarkdown && block.Text.Contains("**strong**") && block.Text.Contains("*qualified*"));
    }

    [Fact]
    public void MultiResultValidationRejectsEmptyDuplicateAndInvalidSelections()
    {
        Assert.Contains(AnalysisReportBuilder.Validate(Array.Empty<AnalysisResult>()).Diagnostics,
            diagnostic => diagnostic.Code == "missing-results");
        var result = CreateResult(1);
        Assert.Contains(AnalysisReportBuilder.Validate(new[] { result, result }).Diagnostics,
            diagnostic => diagnostic.Code == "duplicate-result");
        Assert.False(AnalysisReportBuilder.Validate(new AnalysisResult[] { result, null }).IsValid);
    }

    [Fact]
    public void PersistedReportRendersOnlyEffectiveSupportingExperiments()
    {
        var result = CreateResult(1);
        var covered = result.Solution.Solutions[0].Data;
        var supporting = CreateExperiment(8, 25);
        supporting.Name = "Ligand into buffer";
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { covered.UniqueID, supporting.UniqueID });

        var document = AnalysisReportBuilder.Build(report,
            id => id == result.UniqueID ? result : null,
            id => id == covered.UniqueID ? covered : id == supporting.UniqueID ? supporting : null);

        Assert.True(document.IsValid);
        Assert.Equal(new[] { "S1" }, document.SupportingExperiments.Select(item => item.Label));
        var section = Assert.Single(document.Sections, item => item.Kind == AnalysisReportSectionKind.SupportingData);
        Assert.Contains(section.Blocks.OfType<AnalysisReportHeadingBlock>(), item =>
            item.Text == "S1. Ligand into buffer");
        Assert.Contains(section.Blocks.OfType<AnalysisReportFigurePairBlock>(), item =>
            item.RightTitle == "Integrated heats — no fit"
            && item.RightFigure.FitPanel.Series.All(series => series.Role != PublicationSeriesRole.Fit));
        var processingNotes = Assert.Single(section.Blocks.OfType<AnalysisReportKeyValueBlock>(), item =>
            item.Title == "Correction and exceptions");
        Assert.DoesNotContain(processingNotes.Items, item => item.Label == "Baseline method");
        Assert.DoesNotContain(processingNotes.Items, item => item.Label == "Injection use");
        Assert.DoesNotContain(processingNotes.Items, item => item.Label == "Integration regions");
        Assert.Equal(new[] { covered.UniqueID, supporting.UniqueID }, report.SupportingExperimentIds);
    }

    [Fact]
    public void SupportingExperimentValidationRejectsMissingReferences()
    {
        var result = CreateResult(1);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { "missing-experiment" });

        var validation = AnalysisReportBuilder.Validate(report,
            id => id == result.UniqueID ? result : null, _ => null);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, item => item.Code == "unresolved-supporting-experiment");
    }

    [Fact]
    public void MultiResultContentsIncludeSupportingChapterAfterResults()
    {
        var first = CreateResult(1);
        var second = CreateResult(1);
        var supporting = CreateExperiment(9, 25);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { first.UniqueID, second.UniqueID });
        report.SetSupportingExperimentIds(new[] { supporting.UniqueID });

        var document = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : id == second.UniqueID ? second : null,
            id => id == supporting.UniqueID ? supporting : null);

        Assert.Equal(AnalysisReportSectionKind.SupportingData, document.Sections[^1].Kind);
        var contents = document.Sections[0].Blocks.OfType<AnalysisReportTableOfContentsBlock>().Single();
        Assert.Equal("Supporting experiments", contents.Entries[^1].Title);
        Assert.Equal("supporting-experiments", contents.Entries[^1].TargetSectionId);
    }

    sealed class FakeTextMeasurer : IAnalysisReportTextMeasurer
    {
        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style) =>
            new AnalysisReportSize((text ?? "").Length * style.FontSize * .53, style.FontSize);
    }

    static AnalysisResult CreateResult(
        int count,
        bool weighted = false,
        double temperatureStep = 0,
        bool includeSkewedBootstrap = false,
        bool includeSalt = false)
    {
        var models = new List<Model>();
        var members = new List<SolutionInterface>();
        for (var index = 0; index < count; index++)
        {
            var data = CreateExperiment(index, 20 + index * temperatureStep);
            if (includeSalt)
            {
                var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
                salt.IntValue = (int)Salt.NaCl;
                salt.ParameterValue = new FloatWithError(index == 0 ? 0.04 : 0.16);
                data.Attributes.Add(salt);
            }
            var model = CreateModel(data, affinity: 6, enthalpy: -25_000 - index * 1_000);
            var member = SolutionInterface.FromModel(model, Convergence());
            member.UseWeightedFitting = weighted;
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
