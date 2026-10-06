using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Viewer;

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
        var canvas = ResultOverview(AnalysisReportBuilder.Build(CreateResult(count)))
            .Blocks.OfType<AnalysisReportFigureCanvasBlock>().Single().Canvas;

        Assert.Equal(expectedColumns, canvas.Options.Columns);
        Assert.Equal((int)Math.Ceiling(count / (double)expectedColumns), canvas.Options.Rows);
        Assert.True(canvas.Options.PlotWidthCentimeters <= 5);
        Assert.True(canvas.Options.PlotHeightCentimeters <= 7.7);
        Assert.Equal(9, canvas.Options.FontSize);
    }

    [Fact]
    public void ReportDefaultsToSdAndConfidenceInterval()
    {
        Assert.Equal(UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            new AnalysisReportOptions().UncertaintyDisplayStyle);
    }

    [Fact]
    public void ReportShowsSavedBookkeepingInProcessingAndBufferSubtractionSeparatelyFromCurrentSettings()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions[0];
        var experiment = member.Data;
        experiment.AppliedDilutionMethod = DilutionMethod.MicroCal;
        member.Model.HeatMethod = InjectionHeatMethod.MicroCal;
        var savedReference = new ExperimentData("Saved buffer blank");
        savedReference.SetID("saved-reference");
        var currentReference = new ExperimentData("Current buffer blank");
        currentReference.SetID("current-reference");
        experiment.SetBufferSubtraction(savedReference, BufferSubtractionMethod.MatchedInjection, notify: false);
        result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(result.Solution));
        experiment.SetBufferSubtraction(currentReference, BufferSubtractionMethod.Linear, notify: false);

        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        var document = AnalysisReportBuilder.Build(report,
            id => id == result.UniqueID ? result : null,
            id => id == savedReference.UniqueID ? savedReference
                : id == currentReference.UniqueID ? currentReference : null,
            new AnalysisReportOptions());
        var summary = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        Assert.DoesNotContain(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(), block =>
            block.Title == "Bookkeeping conventions");
        Assert.Contains(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(), block =>
            block.Title == "Buffer subtraction" && block.Items.Any(item =>
                item.Value.Contains("1A", StringComparison.Ordinal)
                && item.Value.Contains("Saved buffer blank", StringComparison.Ordinal)
                && item.Value.Contains("matched injections", StringComparison.OrdinalIgnoreCase)));

        var processing = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Processing and integration");
        Assert.DoesNotContain(processing.Items, item => item.Label == "Bookkeeping convention");
        Assert.Contains(document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportNoticeBlock>(), notice => notice.Title == "Bookkeeping conventions" && notice.Message.Contains("MicroCal: 1A"));

        var details = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        Assert.DoesNotContain(details.Items, item => item.IndentLevel == 0
            && item.Label.Equals("Buffer subtraction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(details.Items, item => item.IndentLevel == 1 && item.Label == "Buffer subtraction");
    }

    [Fact]
    public void MultiResultOverviewWarnsWhenSavedBookkeepingConventionsDiffer()
    {
        var microcal = CreateResult(1);
        var discrete = CreateResult(1);
        var microcalMember = microcal.Solution.Solutions[0];
        microcalMember.Data.AppliedDilutionMethod = DilutionMethod.MicroCal;
        microcalMember.Model.HeatMethod = InjectionHeatMethod.MicroCal;
        microcal.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(microcal.Solution));

        var discreteMember = discrete.Solution.Solutions[0];
        discreteMember.Data.AppliedDilutionMethod = DilutionMethod.DiscreteDisplacement;
        discreteMember.Model.HeatMethod = InjectionHeatMethod.DiscreteDisplacement;
        discrete.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(discrete.Solution));

        var originalMethod = AppSettings.DilutionCalculationMethod;
        try
        {
            AppSettings.DilutionCalculationMethod = DilutionMethod.Exponential;
            var document = AnalysisReportBuilder.Build(new[] { microcal, discrete });
            var notice = Assert.Single(document.Sections.SelectMany(section => section.Blocks)
                .OfType<AnalysisReportNoticeBlock>(), notice => notice.Title == "Bookkeeping conventions");
            Assert.Equal(AnalysisReportNoticeLevel.Information, notice.Level);
            Assert.Contains("MicroCal: 1A", notice.Message);
            Assert.Contains("Discrete displacement: 2A", notice.Message);
        }
        finally
        {
            AppSettings.DilutionCalculationMethod = originalMethod;
        }
    }

    [Fact]
    public void ResultSummaryListsBookkeepingOnlyWhenMembersDifferAndNamesEachMember()
    {
        var result = CreateResult(2);
        var first = result.Solution.Solutions[0];
        first.Data.AppliedDilutionMethod = DilutionMethod.MicroCal;
        first.Model.HeatMethod = InjectionHeatMethod.MicroCal;
        var second = result.Solution.Solutions[1];
        second.Data.AppliedDilutionMethod = DilutionMethod.DiscreteDisplacement;
        second.Model.HeatMethod = InjectionHeatMethod.DiscreteDisplacement;
        result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(result.Solution));

        var document = AnalysisReportBuilder.Build(result);
        var conventions = Assert.Single(document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportNoticeBlock>(), notice => notice.Title == "Bookkeeping conventions");
        Assert.Contains("MicroCal: 1A", conventions.Message);
        Assert.Contains("Discrete displacement: 1B", conventions.Message);
        Assert.Equal(AnalysisReportNoticeLevel.Information, conventions.Level);
    }

    [Fact]
    public void FixedParameterOverviewOmitsUncertaintyAndListsFixedZeroOffset()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions[0];
        member.Model.Parameters.Table[ParameterType.Enthalpy1].Update(
            member.Model.Parameters.Table[ParameterType.Enthalpy1].Value, true);
        member.Model.Parameters.Table[ParameterType.Offset].Update(
            member.Model.Parameters.Table[ParameterType.Offset].Value, true);
        member.Parameters[ParameterType.Offset] = new FloatWithError(0, 0);
        result.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, -25_000, islocked: true);
        result.Model.Parameters.AddorUpdateGlobalParameter(ParameterType.HeatCapacity1, 500, islocked: true);

        var document = AnalysisReportBuilder.Build(result);
        var summary = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        var overview = summary.Blocks.OfType<AnalysisReportTableBlock>().Single();
        var enthalpyColumn = overview.Columns.ToList().FindIndex(column =>
            column.Id == AnalysisResultOverviewTable.ParameterColumnId(ParameterType.Enthalpy1));
        Assert.Contains("(fixed)", overview.Rows[0].Cells[enthalpyColumn], StringComparison.Ordinal);
        Assert.DoesNotContain("±", overview.Rows[0].Cells[enthalpyColumn], StringComparison.Ordinal);
        var fixedParameters = Assert.Single(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Fixed parameters");
        Assert.Contains(fixedParameters.Items, item => item.Label.StartsWith("Enthalpy", StringComparison.Ordinal)
            && item.Value.Length > 0);
        Assert.Contains(fixedParameters.Items, item => item.Label == "Offset — Experiment 1"
            && item.Value.StartsWith("0", StringComparison.Ordinal));
        Assert.Contains(fixedParameters.Items, item => item.Label == "Shared Gibbs free energy"
            && item.Value.Contains("-25", StringComparison.Ordinal));
        Assert.Contains(fixedParameters.Items, item => item.Label == "Shared Heat capacity"
            && !string.IsNullOrWhiteSpace(item.Value));
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
    public void OverviewCanReuseCapturedPresentationValues()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions.Single();
        member.Parameters[ParameterType.Offset] = new FloatWithError(125, 5);
        var presentation = new AnalysisResultPresentationData(result);

        // A later edit belongs to a new presentation refresh.  The existing
        // table must continue to use the one captured report-value dictionary.
        member.Parameters[ParameterType.Offset] = new FloatWithError(875, 5);
        var table = AnalysisResultOverviewTable.Build(
            presentation,
            EnergyUnitFamily.Joules,
            EnergyUnit.Joule,
            useKelvin: false,
            UncertaintyDisplayStyle.StandardDeviation);

        var offset = Assert.Single(table.Columns, column => column.Parameter == ParameterType.Offset);
        Assert.Equal("125 ± 5", table.Rows.Single()[offset.Id]);
        Assert.Equal(125, presentation.Members.Single().Parameters[ParameterType.Offset].Value);
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
        result.UpdateSolution(result.Solution);
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
            summary.Series.Select(series => series.Label));
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
        var generatedLocal = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc).ToLocalTime();
        Assert.StartsWith(generatedLocal.ToString("d MMM yyyy, HH:mm ", CultureInfo.InvariantCulture), document.ExportDateText);
        Assert.DoesNotContain("Generated", document.ExportDateText, StringComparison.Ordinal);
        Assert.Equal("ANALYSIS VALID", document.StatusBadgeText);
        var subtitle = Assert.Single(document.Sections[0].Blocks.OfType<AnalysisReportTextBlock>(),
            block => block.Text == "Supporting Document 1B");
        Assert.Equal("", subtitle.Title);
        var coverAnalysis = ResultOverview(document).Blocks.OfType<AnalysisReportKeyValueBlock>()
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
            AnalysisReportSectionKind.ResultOverview,
            AnalysisReportSectionKind.AnalysisSummary,
            AnalysisReportSectionKind.Experiment,
            AnalysisReportSectionKind.Experiment,
            AnalysisReportSectionKind.Appendix,
        }, document.Sections.Select(section => section.Kind));
        Assert.False(document.Sections[0].Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage));
        Assert.True(document.Sections[0].Layout.HasFlag(AnalysisReportLayoutPolicy.ShrinkToSinglePage));
        Assert.All(document.Sections.Skip(1), section =>
            Assert.True(section.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage)));
        Assert.True(ResultOverview(document).Layout.HasFlag(AnalysisReportLayoutPolicy.ShrinkToSinglePage));
        Assert.All(document.Sections.Where(section => section.Kind != AnalysisReportSectionKind.Cover
                && section.Kind != AnalysisReportSectionKind.ResultOverview), section =>
            Assert.True(section.Layout.HasFlag(AnalysisReportLayoutPolicy.AllowContinuation)));
    }

    [Fact]
    public void CoverUsesPublicationCanvasWithStableLabelsTitlesAndReportPreset()
    {
        var result = CreateResult(27);
        var document = AnalysisReportBuilder.Build(result);
        var cover = Assert.IsType<AnalysisReportFigureCanvasBlock>(
            ResultOverview(document).Blocks.Single(block => block is AnalysisReportFigureCanvasBlock));
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
        Assert.DoesNotContain(reportOverview.Columns, column => column.Id == "Assessment");
        Assert.Equal(sourceOverview.Rows.Count, reportOverview.Rows.Count);
    }

    [Fact]
    public void ReportFiguresKeepFullExperimentNamesForRenderFitAndSummaryUsesReference()
    {
        var result = CreateResult(1);
        const string fullName = "20250126_Lysozyme_NAG_25C_run03";
        result.Solution.Solutions[0].Data.Name = fullName;

        var document = AnalysisReportBuilder.Build(result);
        var canvas = ResultOverview(document).Blocks
            .OfType<AnalysisReportFigureCanvasBlock>().Single().Canvas;
        var summary = document.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>().Single();
        var experiment = document.Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.Experiment);

        // Renderers shorten the title to the measured panel width.
        Assert.Equal(fullName, canvas.Cells.Single().PanelTitle);
        Assert.Equal("1A", summary.Series.Single().Label);
        Assert.Contains(fullName, experiment.Title);
    }

    [Fact]
    public void SummaryLegendWrapsShortReferencesAndLargeChartsUseAvailableWidth()
    {
        var summary = AnalysisReportBuilder.Build(CreateResult(12)).Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>().Single();
        Assert.Equal(Enumerable.Range(0, 12)
            .Select(index => AnalysisReportReferenceLabels.Experiment(0, index)),
            summary.Series.Select(series => series.Label));

        const double availableWidth = 100;
        var positions = AnalysisReportThermodynamicSummaryLayout.LegendPositions(
            summary.Series, availableWidth, _ => 10);
        Assert.Equal(0, positions[0].X);
        Assert.Equal(0, positions[0].Row);
        Assert.Equal(34, positions[1].X);
        Assert.Equal(0, positions[2].X);
        Assert.Equal(1, positions[2].Row);
        Assert.All(positions, position => Assert.True(position.X + 34 <= availableWidth));
        Assert.Equal(550, AnalysisReportThermodynamicSummaryLayout.ChartWidth(550, 3, 12));
        Assert.True(AnalysisReportThermodynamicSummaryLayout.ChartWidth(550, 3, 10) < 550);
    }

    [Theory]
    [InlineData(BindingAssessmentOutcome.NotAssessed, ResultOutputPurpose.Standard, null)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, ResultOutputPurpose.Standard, null)]
    [InlineData(BindingAssessmentOutcome.NotAssessed, ResultOutputPurpose.Diagnostic, null)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, ResultOutputPurpose.Diagnostic, null)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, ResultOutputPurpose.Standard, "No binding detected")]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, ResultOutputPurpose.Diagnostic, "No binding detected")]
    public void ResultOverviewShowsBindingAssessmentOnlyWhenItChangesTheOutput(
        BindingAssessmentOutcome outcome, ResultOutputPurpose purpose, string expected)
    {
        var result = CreateResult(1);
        if (outcome != BindingAssessmentOutcome.NotAssessed)
            result.SetBindingAssessmentOverride(outcome);
        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
        var analysis = ResultOverview(document)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Analysis");

        Assert.DoesNotContain(analysis.Items, item => item.Label == "Output purpose" || item.Label == "Assessment mode");
        Assert.Equal(expected, analysis.Items.SingleOrDefault(item => item.Label == "Binding assessment")?.Value);
    }

    [Theory]
    [InlineData(BindingAssessmentOutcome.NotAssessed, ResultOutputPurpose.Standard)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, ResultOutputPurpose.Standard)]
    [InlineData(BindingAssessmentOutcome.NotAssessed, ResultOutputPurpose.Diagnostic)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, ResultOutputPurpose.Diagnostic)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, ResultOutputPurpose.Diagnostic)]
    public void ReportFinalBindingFitFiguresDoNotAddAssessmentAnnotations(
        BindingAssessmentOutcome outcome, ResultOutputPurpose purpose)
    {
        var result = CreateResult(1);
        if (outcome != BindingAssessmentOutcome.NotAssessed)
            result.SetBindingAssessmentOverride(outcome);
        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
        var experiment = Assert.Single(document.Sections,
            section => section.Kind == AnalysisReportSectionKind.Experiment);
        var figure = Assert.Single(experiment.Blocks.OfType<AnalysisReportFigurePairBlock>()).RightFigure;
        var labels = figure.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines).ToList();

        Assert.Contains(figure.FitPanel.Series, series => series.Role == PublicationSeriesRole.Fit);
        Assert.Equal(purpose == ResultOutputPurpose.Diagnostic
            ? new[] { "Binding-fit diagnostics" } : Array.Empty<string>(), labels);
    }

    [Fact]
    public void IndependentStandardReportKeepsEligibleMemberAndDiagnosticSummaryShowsPooledComparison()
    {
        var result = CreateResult(2);
        Assert.True(result.IsIndependentAssessmentCollection);
        result.SetMemberBindingAssessmentOverride(result.Solution.Solutions[0].Guid,
            BindingAssessmentOutcome.BindingDetected);
        result.SetMemberBindingAssessmentOverride(result.Solution.Solutions[1].Guid,
            BindingAssessmentOutcome.NoBindingDetected);

        var standard = AnalysisReportBuilder.Build(result);
        var summary = standard.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        Assert.Contains(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(), block => block.Title == "Model and fit details");
        var overview = summary.Blocks.OfType<AnalysisReportTableBlock>().Single();
        Assert.Equal(2, overview.Rows.Count);
        // The assessment is a second line under each experiment name, not a separate column.
        Assert.DoesNotContain(overview.Columns, column => column.Id == "Assessment");
        Assert.EndsWith("\nBinding detected (manual)", overview.Rows[0].Cells[0]);
        Assert.EndsWith("\nNo binding detected (manual)", overview.Rows[1].Cells[0]);
        Assert.DoesNotContain(overview.Rows.SelectMany(row => row.Cells), cell => cell.Contains("attempted", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(summary.Blocks.OfType<AnalysisReportTableBlock>(), table =>
            table.Title == "Analysis result overview");
        var experiments = standard.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment).ToList();
        Assert.Equal(2, experiments.Count);
        Assert.Contains(experiments[0].Blocks.OfType<AnalysisReportTableBlock>(), table =>
            table.Title == "Fitted and derived parameters");
        var fitDetails = experiments[0].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Fit details").Items;
        Assert.Contains(fitDetails, item => item.Label == "Binding assessment" && item.Value == "Binding detected");
        Assert.Single(fitDetails, item => item.Label == "ΔAICc (null − binding)");
        Assert.DoesNotContain(standard.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Member binding assessment and null comparison");
        Assert.Contains(experiments[1].Blocks.OfType<AnalysisReportTableBlock>(), table => table.Title == "Fitted and derived parameters");
        Assert.DoesNotContain(experiments[1].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Fit details").Items, item => item.Label.StartsWith("c-value"));
        Assert.DoesNotContain(experiments[1].Blocks.OfType<AnalysisReportNoticeBlock>(), block =>
            block.Title.StartsWith("Binding parameters omitted", StringComparison.Ordinal));
        Assert.DoesNotContain(standard.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportHeadingBlock>(),
            heading => heading.Text == "Pooled Comparison Diagnostics");

        var diagnostic = AnalysisReportBuilder.Build(result,
            new AnalysisReportOptions { OutputPurpose = ResultOutputPurpose.Diagnostic });
        var diagnosticSummary = diagnostic.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        Assert.Contains(diagnosticSummary.Blocks.OfType<AnalysisReportHeadingBlock>(), heading =>
            heading.Text == "Pooled Comparison Diagnostics");
        Assert.DoesNotContain(diagnostic.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Appendix)
            .Blocks.OfType<AnalysisReportHeadingBlock>(), heading => heading.Text == "Pooled Comparison Diagnostics");
        Assert.Contains(diagnosticSummary.Blocks.OfType<AnalysisReportTextBlock>(), block =>
            block.Title == ""
            && block.Text.Contains("does not determine member assessments", StringComparison.Ordinal));
        var pooled = diagnosticSummary.Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "" && block.Items.Any(item => item.Label == "ΔAICc"));
        Assert.Equal("", pooled.Title);
        Assert.Equal(new[] { "ΔAICc", "Null fit" }, pooled.Items.Select(item => item.Label));
    }

    [Fact]
    public void IndependentStandardReportNeverShowsPooledComparisonWhenCollectionGateIsOpen()
    {
        var result = CreateResult(2);
        Assert.True(result.IsIndependentAssessmentCollection);
        result.RestoreNullComparison(new NullModelComparison
        {
            BindingFitSucceeded = true,
            NullFitSucceeded = true,
            DeltaAicc = 123.456,
        });

        var document = AnalysisReportBuilder.Build(result);
        var summary = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        Assert.DoesNotContain(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Binding assessment and null comparison");
        Assert.DoesNotContain(document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .Where(block => block.Title == "Fit details").SelectMany(block => block.Items),
            item => item.Label is "Binding assessment" or "ΔAICc");

        var diagnostic = AnalysisReportBuilder.Build(result,
            new AnalysisReportOptions { OutputPurpose = ResultOutputPurpose.Diagnostic });
        var items = diagnostic.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Binding assessment and null comparison")
            .Items;
        Assert.Contains(items, item => item.Label == "Conclusion" && item.Value == "Not assessed");
        Assert.Contains(items, item => item.Label == "Not assessed" && item.Value == "2");
        Assert.DoesNotContain(items, item => item.Label is "ΔAICc" or "Binding AICc" or "Null AICc"
            or "Assessment mode" or "Output purpose");
        Assert.DoesNotContain(summary.Blocks.OfType<AnalysisReportTextBlock>(), block =>
            block.Text.Contains("123.456", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Appendix)
            .Blocks.OfType<AnalysisReportHeadingBlock>(), heading => heading.Text == "Pooled Comparison Diagnostics");
    }

    [Fact]
    public void ExpandedExperimentLabelsMatchCoverAndContainDetailsAndInjectionTables()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(3));
        var canvas = ResultOverview(document).Blocks.OfType<AnalysisReportFigureCanvasBlock>().Single().Canvas;
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
            .Single(table => table.Title == "Experiment sources");
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
            Assert.Contains(metadata.Items, item => item.Label == "Source file" && item.Value.Contains(".itc ("));
            Assert.Contains(metadata.Items, item => item.Label == "Temperature"
                && item.Value.Contains("Measured") && item.Value.Contains("target"));
            Assert.DoesNotContain(metadata.Items, item => item.Label == "Measured temperature"
                || item.Label == "Target temperature");
            Assert.Contains(metadata.Items, item => item.Label == "Experiment settings" && item.IndentLevel == 0);
            Assert.Contains(metadata.Items, item => item.Label == "Cell volume"
                && item.Value.Contains("µL") && item.IndentLevel == 1);
            var processing = section.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Single(block => block.Title.StartsWith("Processing and integration", StringComparison.Ordinal));
            Assert.Contains(processing.Items, item => item.Label == "Injection use" && item.Value.Contains("3 included"));
            Assert.Contains(processing.Items, item => item.Label == "Integration regions" && item.IndentLevel == 0);
            Assert.Contains(processing.Items, item => item.Label == "Start after injection" && item.IndentLevel == 1);
            Assert.Contains(processing.Items, item => item.Label == "End after injection" && item.IndentLevel == 1);
            Assert.Contains(section.Blocks.OfType<AnalysisReportTableBlock>(), block => block.Title == "Fitted and derived parameters");
            var injectionTable = section.Blocks.OfType<AnalysisReportTableBlock>()
                .Single(block => block.Title.StartsWith("Injection table", StringComparison.Ordinal));
            Assert.False(injectionTable.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage));
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
                block => block.Title.StartsWith("Injection table", StringComparison.Ordinal)));
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
            .Single(block => block.Title.StartsWith("Processing and integration", StringComparison.Ordinal));

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
            Assert.DoesNotContain(details.Items, item => item.Label == "Uncertainty");
            Assert.Contains(details.Items, item => item.Label.StartsWith("RMSD", StringComparison.Ordinal));
        }

        var appendix = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Appendix);
        Assert.DoesNotContain(appendix.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title is "Analysis configuration" or "Optimizer details");
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
    public void ParameterEvaluationCombinesMemberUncertaintyWithReplicateSpread()
    {
        var result = CreateResult(3);
        var calculator = new AnalysisResultAggregateSummaryCalculator(result);

        var expectedEnthalpy = result.Solution.TemperatureDependence[ParameterType.Enthalpy1].Evaluate(20).Value;
        var enthalpy = calculator.Evaluate(ParameterType.Enthalpy1, 20);
        Assert.Equal(expectedEnthalpy, enthalpy.Value.Value);
        Assert.Equal(1000, enthalpy.Value.SD, 12);

        foreach (var member in result.Solution.Solutions)
            member.Parameters[ParameterType.Enthalpy1] = new FloatWithError(
                member.Parameters[ParameterType.Enthalpy1].Value, 1_000_000);

        // Publish direct fixture edits before requesting the cached presentation again.
        result.UpdateSolution(result.Solution);
        Assert.Equal(Math.Sqrt(1_000_000.0 + 1_000_000.0 * 1_000_000.0), calculator.Evaluate(ParameterType.Enthalpy1, 20).Value.SD, 12);

        var evaluation = AnalysisResultParameterEvaluator.Evaluate(
            result, 20, EnergyUnit.Joule, UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval);
        Assert.Contains("Combined SD (n = 3)", evaluation.Rows.Single(row => row.Label.Contains("∆H")).Tooltip);
        Assert.Contains("Combined SD (n = 3)", evaluation.Rows.Single(row => row.Label == "Affinity (Kd)").Tooltip);
    }

    [Fact]
    public void ParameterEvaluationUsesTrendResidualsAndSlopeStandardError()
    {
        var result = CreateResult(3, temperatureStep: 10);
        var calculator = new AnalysisResultAggregateSummaryCalculator(result);

        var enthalpy = calculator.Evaluate(ParameterType.Enthalpy1, 30);
        Assert.Equal(0, enthalpy.Value.SD, 12);
        Assert.Equal(AggregateUncertaintyKind.TemperatureTrendStandardDeviation, enthalpy.Kind);

        var heatCapacity = calculator.EvaluateHeatCapacity(ThermodynamicParameterSlots.ForStep(1));
        Assert.Equal(0, heatCapacity.Value.SD, 12);
        Assert.Equal(AggregateUncertaintyKind.RegressionSlopeStandardError, heatCapacity.Kind);

        var evaluation = AnalysisResultParameterEvaluator.Evaluate(
            result, 30, EnergyUnit.Joule, UncertaintyDisplayStyle.ConfidenceInterval);
        Assert.Contains("Combined SD around temperature trend", evaluation.Rows.Single(row => row.Label.Contains("∆H")).Tooltip);
        Assert.Contains("Propagated slope SD", evaluation.Rows.Single(row => row.Label.Contains("∆Cp")).Tooltip);
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

        Assert.Contains(labels, label => label.StartsWith("RMSD", StringComparison.Ordinal));
        Assert.DoesNotContain("Unweighted RMSD", labels);
        Assert.DoesNotContain("Fitting", labels);
        Assert.DoesNotContain(labels, label => label.Contains("objective", StringComparison.OrdinalIgnoreCase));
        var summary = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        var summaryFit = Assert.Single(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Model and fit details");
        Assert.Contains(summaryFit.Items, item => item.Label == "Model");
        Assert.Contains(summaryFit.Items, item => item.Label == "Analysis");
        Assert.Contains(summaryFit.Items, item => item.Label.StartsWith("RMSD", StringComparison.Ordinal));
        Assert.Contains(summaryFit.Items, item => item.Label == "Uncertainty");
        Assert.DoesNotContain(summary.Blocks, block => block.Title is "Model" or "Fit details");
        Assert.EndsWith("; weighted by injection SDs", summaryFit.Items.Single(item => item.Label == "Solver").Value);
        Assert.DoesNotContain(document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportNoticeBlock>(), block => block.Title == "Reading fit diagnostics");

        var expanded = AnalysisReportBuilder.Build(CreateResult(1, weighted: true),
            new AnalysisReportOptions { ExpandedExplanations = true });
        Assert.Contains(expanded.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportNoticeBlock>(), block => block.Title == "Reading fit diagnostics");

        var unweighted = AnalysisReportBuilder.Build(CreateResult(1, weighted: false));
        Assert.DoesNotContain(unweighted.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items), item => item.Label == "Fitting"
                || item.Value.Contains("weighted by injection SDs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "one estimated common residual variance", "one common estimated residual variance")]
    [InlineData(true, "injection errors as relative uncertainties", "one common estimated variance multiplier")]
    public void InformationCriteriaDiagnosticsIdentifyLikelihoodAndPooling(bool weighted, string likelihoodText, string poolingText)
    {
        var result = CreateResult(2, weighted);
        var document = AnalysisReportBuilder.Build(result);
        var items = document.Sections
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items).ToArray();

        if (result.IsIndependentAssessmentCollection)
        {
            Assert.DoesNotContain(items, item => item.Label == "AIC likelihood");
            Assert.DoesNotContain(items, item => item.Label == "AIC scope");
            var overview = document.Sections.SelectMany(section => section.Blocks)
                .OfType<AnalysisReportTableBlock>().First(block => block.Title.Contains("Overview", StringComparison.OrdinalIgnoreCase));
            var criteriaColumn = overview.Columns.ToList().FindIndex(column => column.Id == "InformationCriteria");
            Assert.True(criteriaColumn >= 0);
            Assert.All(overview.Rows, row => Assert.NotEmpty(row.Cells[criteriaColumn]));
        }
        else
        {
            Assert.Contains(items, item => item.Label == "AIC likelihood" && item.Value.Contains(likelihoodText, StringComparison.Ordinal));
            Assert.Contains(items, item => item.Label == "AIC scope" && item.Value.Contains(poolingText, StringComparison.Ordinal));
        }
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
            diagnostic.Code == "result-1-advanced-section-omitted"
            && diagnostic.Message.Contains("SpolarRecord"));
        Assert.Contains(requested.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Appendix)
            .Blocks.OfType<AnalysisReportNoticeBlock>(), block =>
                block.Title == "Report warnings" && block.Message.Contains("SpolarRecord"));
    }

    [Fact]
    public void SavedSpolarAndTemperatureSelectionsShareOneTemperaturePlot()
    {
        var result = CreateResult(2, temperatureStep: 15);
        var isoentropicTemperature = new FloatWithError(25.1234, 1.2345);
        result.SpolarRecordAnalysis.RestoreResult(
            FTSRMethod.SRFoldedMode.Glob,
            FTSRMethod.SRTempMode.IsoEntropicPoint,
            new FTSRMethod.SROutput(
                new FloatWithError(-10, 1),
                new FloatWithError(-20, 2),
                new FloatWithError(123.456789, 4.321),
                isoentropicTemperature),
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
        var advancedItems = document.Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.AdvancedAnalysis)
            .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
            .SelectMany(block => block.Items)
            .ToList();
        Assert.DoesNotContain(advancedItems,
            item => item.Label.Contains("iteration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(advancedItems, item => item.Label == "Uncertainty"
            && item.Value == "Repeated random sampling of saved input uncertainties.");
        var expectedResidue = InReportCulture(() => result.SpolarRecordAnalysis.Result.Rvalue.AsNumber(
            options.UncertaintyDisplayStyle));
        var expectedIsoentropic = InReportCulture(() => isoentropicTemperature.AsNumber(options.UncertaintyDisplayStyle));
        Assert.Contains(advancedItems, item => item.Label == "Residue estimate" && item.Value == expectedResidue);
        Assert.DoesNotContain(advancedItems, item => item.Label == "Residue estimate"
            && item.Value.Contains("123.46", StringComparison.Ordinal));
        Assert.Contains(advancedItems, item => item.Label == "Iso-entropic temperature"
            && item.Value == expectedIsoentropic + " °C");
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

        Assert.DoesNotContain(document.Sections.SelectMany(item => item.Blocks)
            .OfType<AnalysisReportNoticeBlock>(), block => block.Title == "Reading parameter correlations");
        options.ExpandedExplanations = true;
        var explained = AnalysisReportBuilder.Build(result, options);
        Assert.Contains(explained.Sections.SelectMany(item => item.Blocks)
            .OfType<AnalysisReportNoticeBlock>(), block => block.Title == "Reading parameter correlations");
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

        Assert.Equal(("Experiment date", "8 Sep 2023 (data file)"),
            (firstMetadata.Items[0].Label, firstMetadata.Items[0].Value));
        Assert.DoesNotContain(secondMetadata.Items,
            item => item.Label.StartsWith("Experiment date", StringComparison.Ordinal));
        Assert.Equal("Experiment date", thirdMetadata.Items[0].Label);
        Assert.EndsWith(" (user provided)", thirdMetadata.Items[0].Value);
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
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "result-1-result-health");
        Assert.Contains(ResultOverview(document).Blocks.OfType<AnalysisReportNoticeBlock>(), notice =>
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
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "result-1-non-finite-parameters");
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
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "result-1-missing-result");
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
            .Where(table => table.Title.StartsWith("Injection table", StringComparison.Ordinal))
            .ToList();
        Assert.All(injectionTables, table =>
        {
            var firstFragment = plan.Pages.SelectMany(page => page.Fragments)
                .First(fragment => ReferenceEquals(fragment.Block, table));
            var page = plan.Pages.Single(item => item.Fragments.Contains(firstFragment));
            Assert.NotSame(table, page.Fragments.First().Block);
        });
    }

    [Fact]
    public void PaginationKeepsResultOverviewAndShrinkTableOnSinglePages()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(27));
        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        var overviewPage = plan.Pages.Single(page => page.Fragments.Any(fragment =>
            fragment.Kind == AnalysisReportFragmentKind.SectionTitle
            && fragment.Section?.Kind == AnalysisReportSectionKind.ResultOverview));
        Assert.Equal(2, overviewPage.PageNumber);
        var canvas = Assert.Single(overviewPage.Fragments,
            fragment => fragment.Kind == AnalysisReportFragmentKind.FigureCanvas);
        Assert.Same(ResultOverview(document).Blocks.OfType<AnalysisReportFigureCanvasBlock>().Single(), canvas.Block);

        var overview = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportTableBlock>().Single();
        var fragments = plan.Pages.SelectMany(page => page.Fragments)
            .Where(fragment => ReferenceEquals(fragment.Block, overview)).ToList();
        Assert.Single(fragments);
        Assert.Equal(overview.Rows.Count, fragments[0].ItemCount);
        Assert.InRange(fragments[0].Scale, .42, 1);
    }

    [Fact]
    public void ExperimentDetailsStartBelowExperimentFigures()
    {
        var result = CreateResult(1);
        var data = result.Solution.Solutions[0].Data;
        for (var index = 0; index < 12; index++)
        {
            var attribute = ExperimentAttribute.FromKey(AttributeKey.Salt);
            attribute.IntValue = (int)Salt.NaCl;
            attribute.ParameterValue = new FloatWithError(0.01 * (index + 1));
            data.Attributes.Add(attribute);
        }

        var document = AnalysisReportBuilder.Build(result);
        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        var experiment = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment);
        var figures = experiment.Blocks.OfType<AnalysisReportFigurePairBlock>().Single();
        var details = experiment.Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Experiment details");
        int PageOf(AnalysisReportBlock block) => plan.Pages.First(page =>
            page.Fragments.Any(fragment => ReferenceEquals(fragment.Block, block))).PageNumber;
        var detailFragments = plan.Pages.SelectMany(page => page.Fragments)
            .Where(fragment => ReferenceEquals(fragment.Block, details)).ToList();

        Assert.Equal(PageOf(figures), PageOf(details));
        Assert.True(detailFragments.Count > 1);
        Assert.Equal(details.Items.Count, detailFragments.Sum(fragment => fragment.ItemCount));
    }

    [Fact]
    public void ContinuedBlockFragmentsMarkTheirTitles()
    {
        var document = new AnalysisReportDocument { Title = "Continuation titles" };
        var section = new AnalysisReportSection(AnalysisReportSectionKind.Appendix, "appendix", "Appendix",
            AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
        var table = new AnalysisReportTableBlock("Long table",
            new[] { new AnalysisReportTableColumn("a", "Experiment") },
            Enumerable.Range(1, 200).Select(index => new AnalysisReportTableRow(new[] { "Experiment " + index })),
            AnalysisReportLayoutPolicy.AllowContinuation);
        var values = new AnalysisReportKeyValueBlock("Long details",
            Enumerable.Range(1, 200).Select(index => new AnalysisReportKeyValueItem("Item " + index, "Value")),
            AnalysisReportLayoutPolicy.AllowContinuation);
        var text = new AnalysisReportTextBlock("Long text",
            string.Join("\n", Enumerable.Range(1, 200).Select(index => "Line " + index)),
            AnalysisReportLayoutPolicy.AllowContinuation);
        section.Add(table);
        section.Add(values);
        section.Add(text);
        document.AddSection(section);

        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        foreach (var block in new AnalysisReportBlock[] { table, values, text })
        {
            var fragments = plan.Pages.SelectMany(page => page.Fragments)
                .Where(fragment => ReferenceEquals(fragment.Block, block)).ToList();
            Assert.True(fragments.Count > 1, block.Title);
            Assert.Equal(block.Title, fragments[0].Title);
            Assert.All(fragments.Skip(1), fragment => Assert.Equal(block.Title + " – continued", fragment.Title));
        }
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
        Assert.All(fragments, fragment =>
        {
            var layout = Assert.IsType<AnalysisReportTableLayout>(fragment.TableLayout);
            Assert.Equal(fragment.Bounds.Width, layout.ColumnWidths.Sum(), 6);
            var allocated = (string.IsNullOrWhiteSpace(table.Title) ? 0 : 18 * fragment.Scale)
                + layout.HeaderHeight
                + layout.Rows.Skip(fragment.FirstItem).Take(fragment.ItemCount).Sum(row => row.Height);
            Assert.True(allocated <= fragment.Bounds.Height + .001);
            Assert.All(layout.Rows, row => Assert.True(row.Height > 0));
        });
    }

    [Fact]
    public void OversizedSingleTableRowShrinksToFitItsMeasuredFragment()
    {
        var document = new AnalysisReportDocument { Title = "Tall row" };
        var section = new AnalysisReportSection(AnalysisReportSectionKind.Appendix, "appendix", "Appendix",
            AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
        var table = new AnalysisReportTableBlock("Dense table",
            new[] { new AnalysisReportTableColumn("a", "Weighted column"), new AnalysisReportTableColumn("b", "Details") },
            new[] { new AnalysisReportTableRow(new[] { "narrow", string.Join("\n", Enumerable.Repeat("Observation detail", 300)) }) },
            AnalysisReportLayoutPolicy.AllowContinuation, 7.5);
        section.Add(table);
        document.AddSection(section);

        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        var fragment = Assert.Single(plan.Pages.SelectMany(page => page.Fragments),
            candidate => ReferenceEquals(candidate.Block, table));
        var layout = Assert.IsType<AnalysisReportTableLayout>(fragment.TableLayout);
        var allocated = 18 * fragment.Scale + layout.HeaderHeight + layout.Rows.Single().Height;
        Assert.Equal(1, fragment.ItemCount);
        Assert.True(fragment.Scale < 1);
        Assert.True(allocated <= fragment.Bounds.Height + .001);
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
        Assert.Equal("1A", summaryPlots[0].Series[0].Label);
        Assert.Equal("2A", summaryPlots[1].Series[0].Label);
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
            block => block.Title.StartsWith("Processing and integration", StringComparison.Ordinal));
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
            block => block.Title.StartsWith("Processing and integration", StringComparison.Ordinal));
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
            item.Title == "Notes");
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

        Assert.Equal(AnalysisReportSectionKind.SupportingData, document.Sections[^2].Kind);
        Assert.Equal(AnalysisReportSectionKind.Appendix, document.Sections[^1].Kind);
        var contents = document.Sections[0].Blocks.OfType<AnalysisReportTableOfContentsBlock>().Single();
        Assert.Equal(("Supporting experiments", "supporting-experiments"),
            (contents.Entries[^2].Title, contents.Entries[^2].TargetSectionId));
        Assert.Equal(("Appendix", "appendix"), (contents.Entries[^1].Title, contents.Entries[^1].TargetSectionId));
    }

    [Fact]
    public void SingleResultUsesFrontPageAndResultChapterLayout()
    {
        var result = CreateResult(2); result.Name = "Only analysis";
        var first = CreateResult(2); first.Name = "First analysis";
        var second = CreateResult(1); second.Name = "Second analysis";

        var single = AnalysisReportBuilder.Build(result);
        var multi = AnalysisReportBuilder.Build(new[] { first, second });

        Assert.True(single.IsValid);
        Assert.Equal("Only analysis", single.Title);
        Assert.Equal(new[] { result.UniqueID }, single.Results.Select(item => item.Id));
        Assert.Equal(AnalysisReportSectionKind.Cover, single.Sections[0].Kind);
        Assert.Equal("cover", single.Sections[0].Id);
        Assert.DoesNotContain(single.Sections[0].Blocks, block => block is AnalysisReportFigureCanvasBlock);
        var scope = single.Sections[0].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Report scope");
        Assert.Contains(scope.Items, item => item.Label == "Analysis results" && item.Value == "1");
        var included = single.Sections[0].Blocks.OfType<AnalysisReportTableBlock>()
            .Single(block => block.Title == "Included results");
        Assert.Equal("1. Only analysis", Assert.Single(included.Rows).Cells[0]);
        var contents = single.Sections[0].Blocks.OfType<AnalysisReportTableOfContentsBlock>().Single();
        Assert.Equal(new[] { ("Result 1. Only analysis", "result-1-overview"), ("Appendix", "appendix") },
            contents.Entries.Select(entry => (entry.Title, entry.TargetSectionId)));

        var overview = single.Sections[1];
        Assert.Equal(AnalysisReportSectionKind.ResultOverview, overview.Kind);
        Assert.Equal("result-1-overview", overview.Id);
        Assert.Equal("Result 1. Only analysis", overview.Title);
        Assert.Equal(result.Health, overview.StatusBadgeHealth);
        Assert.True(overview.Layout.HasFlag(AnalysisReportLayoutPolicy.StartOnNewPage));
        var singleChapter = single.Sections.Skip(1).Take(single.Sections.Count - 2).ToList();
        Assert.All(singleChapter, section =>
        {
            Assert.StartsWith("result-1-", section.Id, StringComparison.Ordinal);
            Assert.Equal(result.Name, section.ResultName);
        });
        Assert.Equal((AnalysisReportSectionKind.Appendix, "appendix"), (single.Sections[^1].Kind, single.Sections[^1].Id));
        Assert.Single(multi.Sections, section => section.Kind == AnalysisReportSectionKind.Appendix);

        var multiFirstChapter = multi.Sections
            .Where(section => section.Id.StartsWith("result-1-", StringComparison.Ordinal))
            .Select(section => (section.Kind, section.Id));
        Assert.Equal(multiFirstChapter, singleChapter.Select(section => (section.Kind, section.Id)));
        Assert.Equal(AnalysisReportBuilder.Build(new[] { result }).Sections.Select(section => section.Id),
            single.Sections.Select(section => section.Id));
    }

    [Fact]
    public void SingleResultInterpretationAndSupportingExperimentsFollowFrontPageContents()
    {
        var result = CreateResult(1); result.Name = "Alpha";
        var supporting = CreateExperiment(9, 25);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { supporting.UniqueID });
        report.SetManualInterpretation("## Overall interpretation\nA conclusion.");

        var document = AnalysisReportBuilder.Build(report,
            id => id == result.UniqueID ? result : null,
            id => id == supporting.UniqueID ? supporting : null);

        Assert.True(document.IsValid);
        Assert.Equal(AnalysisReportSectionKind.Cover, document.Sections[0].Kind);
        Assert.Equal(AnalysisReportSectionKind.Interpretation, document.Sections[1].Kind);
        Assert.Equal(AnalysisReportSectionKind.ResultOverview, document.Sections[2].Kind);
        Assert.Equal(AnalysisReportSectionKind.SupportingData, document.Sections[^2].Kind);
        Assert.Equal(AnalysisReportSectionKind.Appendix, document.Sections[^1].Kind);
        var contents = document.Sections[0].Blocks.OfType<AnalysisReportTableOfContentsBlock>().Single();
        Assert.Equal(new[] { "interpretation", "result-1-overview", "supporting-experiments", "appendix" },
            contents.Entries.Select(entry => entry.TargetSectionId));
    }

    [Theory]
    [InlineData(ResultOutputPurpose.Standard, false)]
    [InlineData(ResultOutputPurpose.Diagnostic, true)]
    public void MultiResultChaptersUseReportOutputPurpose(ResultOutputPurpose purpose, bool expectComparison)
    {
        var first = CreateResult(1);
        var second = CreateResult(1);

        var document = AnalysisReportBuilder.Build(new[] { first, second },
            new AnalysisReportOptions { OutputPurpose = purpose });

        var summaries = document.Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary).ToList();
        Assert.Equal(2, summaries.Count);
        Assert.All(summaries, summary => Assert.Equal(expectComparison,
            summary.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Any(block => block.Title == "Binding assessment and null comparison")));
    }

    [Fact]
    public void ReportAppendixListsEveryResultIdentifierOnce()
    {
        var first = CreateResult(1);
        var second = CreateResult(1);

        var document = AnalysisReportBuilder.Build(new[] { first, second },
            new AnalysisReportOptions { ExtraTraceability = true });

        var appendix = Assert.Single(document.Sections, section => section.Kind == AnalysisReportSectionKind.Appendix);
        Assert.Equal("appendix", appendix.Id);
        var identifiers = appendix.Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Report details")
            .Items.Single(item => item.Label == "Result identifiers").Value;
        Assert.Equal("1: " + first.UniqueID + "; 2: " + second.UniqueID, identifiers);
    }

    [Fact]
    public void MultiResultWarnsOnlyForAdvancedSectionsNoResultProvides()
    {
        var first = CreateResult(1);
        var second = CreateResult(1);
        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(
            AnalysisReportAdvancedSectionKind.SpolarRecord));

        var document = AnalysisReportBuilder.Build(new[] { first, second }, options);

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "result-1-advanced-section-omitted");
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "result-2-advanced-section-omitted");
    }

    sealed class FakeTextMeasurer : IAnalysisReportTextMeasurer
    {
        public AnalysisReportSize Measure(string text, AnalysisReportTextStyle style) =>
            new AnalysisReportSize((text ?? "").Length * style.FontSize * .53, style.FontSize);
    }

    [Fact]
    public void SummaryExportsAndEvaluationAgreeInSameCoordinateAndRetainTheirCentralValues()
    {
        var result = CreateResult(2);
        result.Solution.Solutions[0].Parameters[ParameterType.Enthalpy1] = new FloatWithError(10, 1, 8, 14);
        result.Solution.Solutions[1].Parameters[ParameterType.Enthalpy1] = new FloatWithError(12, 3, 11, 15);
        result.Solution.TemperatureDependence[ParameterType.Enthalpy1] = new LinearFitWithError(0, 11, 20);
        var evaluated = new AnalysisResultAggregateSummaryCalculator(result).Evaluate(ParameterType.Enthalpy1, 20).Value;
        var exported = AnalysisResultTableExporter.SummaryValue(result, ParameterType.Enthalpy1);
        Assert.Equal(evaluated, exported);
        Assert.Equal(11, exported.Value);
        Assert.Equal(Math.Sqrt(7), exported.SD, 12);
        Assert.Equal(11 - Math.Sqrt(5.0916), exported.Lower, 12);
        Assert.Equal(11 + Math.Sqrt(10.0916), exported.Upper, 12);
        foreach (var format in new[] { AnalysisResultExportFileFormat.CSV, AnalysisResultExportFileFormat.TSV })
        {
            var options = new AnalysisResultExportOptions
            {
                RowMode = AnalysisResultExportRowMode.Summary, FileFormat = format,
                ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
                UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
                EnergyUnitOverride = EnergyUnit.Joule,
            };
            var table = AnalysisResultTableExporter.Build(new[] { result }, options);
            Assert.Equal(table, AnalysisResultTableExporter.Build(new[] { result }, options));
            Assert.Contains("Approximate propagated interval", table);
            Assert.Contains("_interval_lower", table);
            Assert.Contains("_interval_upper", table);
            options.RowMode = AnalysisResultExportRowMode.AllRows;
            var members = AnalysisResultTableExporter.Build(new[] { result }, options);
            Assert.Contains("_ci_lower", members);
            Assert.DoesNotContain("Approximate propagated interval", members);
        }
        // A stored central estimate is not replaced when recalculating uncertainty.
        result.Solution.TemperatureDependence[ParameterType.Enthalpy1] = new LinearFitWithError(0, 42, 20);
        result.UpdateSolution(result.Solution);
        var shifted = new AnalysisResultAggregateSummaryCalculator(result).Evaluate(ParameterType.Enthalpy1, 20).Value;
        Assert.Equal(42, shifted.Value);
        Assert.Equal(exported.SD, shifted.SD);
        Assert.Equal(42, AnalysisResultTableExporter.SummaryValue(result, ParameterType.Enthalpy1).Value);
    }

    [Fact]
    public void DuplicateMemberIdentitiesLeaveNewSummaryErrorsUnavailable()
    {
        var result = CreateResult(2);
        var before = new AnalysisResultAggregateSummaryCalculator(result).Evaluate(ParameterType.Enthalpy1, 20).Value.Value;
        result.Solution.Solutions[1].Data.SetID(result.Solution.Solutions[0].Data.UniqueID);
        result.UpdateSolution(result.Solution);
        var evaluated = new AnalysisResultAggregateSummaryCalculator(result).Evaluate(ParameterType.Enthalpy1, 20).Value;
        var exported = AnalysisResultTableExporter.SummaryValue(result, ParameterType.Enthalpy1);
        Assert.Equal(before, evaluated.Value);
        Assert.True(double.IsNaN(evaluated.SD));
        Assert.True(double.IsNaN(evaluated.Lower));
        Assert.True(double.IsNaN(exported.SD));
        Assert.Equal(-25500, exported.Value);
    }

    [Fact]
    public void SummaryExportDerivesKdFromTheEvaluatedGibbsEnergy()
    {
        var result = CreateResult(2);
        result.Model.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.SameForAll);
        var temperature = AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);
        var thermalEnergy = Energy.R * (temperature + 273.15);
        // Kd = 1 and 9 have a geometric (Gibbs-energy) summary of 3.
        var summarizedGibbs = (thermalEnergy * Math.Log(1) + thermalEnergy * Math.Log(9)) / 2;
        result.Solution.TemperatureDependence[ParameterType.Gibbs1] = new LinearFitWithError(
            0, new FloatWithError(summarizedGibbs, thermalEnergy * .2), temperature);

        var kd = AnalysisResultTableExporter.SummaryValue(result, ParameterType.Affinity1);

        Assert.Equal(3, kd.Value, 12);
    }

    [Fact]
    public void TemperatureSummaryExportsLeaveHeatCapacityBlankForStaticResults()
    {
        var temperatureResult = CreateResult(2, temperatureStep: 10);
        var staticResult = CreateResult(2);
        var options = new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.Summary,
            ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
            FileFormat = AnalysisResultExportFileFormat.TSV,
            EnergyUnitOverride = EnergyUnit.Joule,
        };
        var summary = AnalysisResultTableExporter.Build(new[] { temperatureResult, staticResult }, options);
        var members = AnalysisResultTableExporter.Build(new[] { temperatureResult }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.AllRows,
            ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
            EnergyUnitOverride = EnergyUnit.Joule,
        });

        Assert.Contains("Evaluation temperature (°C)", summary);
        Assert.Contains("∆Cp", summary);
        Assert.DoesNotContain("∆Cp", members);
        Assert.False(staticResult.IsTemperatureDependenceEnabled);

        var rows = summary.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
            .Select(line => line.Split('\t')).ToArray();
        var heatCapacityColumn = Array.FindIndex(rows[0], column => column.StartsWith("∆Cp", StringComparison.Ordinal));
        Assert.True(heatCapacityColumn >= 0);
        Assert.Equal("", rows[2][heatCapacityColumn]);
        Assert.Equal("", rows[2][heatCapacityColumn + 1]);
    }

    [Fact]
    public void SummaryAutomaticEnergyUnitsUseEvaluatedValues()
    {
        var result = CreateResult(2, temperatureStep: 10);
        var evaluationTemperature = AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);
        foreach (var parameter in new[]
                 {
                     ParameterType.Enthalpy1,
                     ParameterType.EntropyContribution1,
                     ParameterType.Gibbs1,
                 })
        {
            result.Solution.TemperatureDependence[parameter] =
                new LinearFitWithError(0, 50, evaluationTemperature);
        }

        var options = new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.Summary,
            ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
            EnergyUnitFamily = EnergyUnitFamily.Joules,
        };

        AnalysisResultTableExporter.Build(new[] { result }, options);

        Assert.Equal(EnergyUnit.Joule, options.ResolvedEnergyUnit);
    }

    [Fact]
    public void SharedSummaryUsesModelUncertaintyAndOriginalIntervalLabel()
    {
        var result = CreateResult(2);
        result.Model.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, VariableConstraint.SameForAll);
        var fit = new LinearFitWithError(new FloatWithError(0), new FloatWithError(-10, 2, -14, -9), 20);
        result.Solution.TemperatureDependence[ParameterType.Enthalpy1] = fit;
        var actual = new AnalysisResultAggregateSummaryCalculator(result).Evaluate(ParameterType.Enthalpy1, 20);
        Assert.Equal(fit.Evaluate(20), actual.Value);
        Assert.Equal(AggregateUncertaintyKind.ModelEstimated, actual.Kind);
        var tooltip = AnalysisResultParameterEvaluator.Evaluate(result, 20, EnergyUnit.Joule,
            UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval).Rows.Single(row => row.Label.Contains("∆H")).Tooltip;
        Assert.Contains("95% confidence interval", tooltip);
        Assert.DoesNotContain("Approximate propagated interval", tooltip);
    }

    [Fact]
    public void NonFiniteCentralValuesAreExcludedFromSummaryExports()
    {
        var result = CreateResult(2);
        result.Solution.Solutions[0].Parameters[ParameterType.Enthalpy1] = FloatWithError.NaN;
        result.Solution.Solutions[1].Parameters[ParameterType.Enthalpy1] = new FloatWithError(12, 3, 11, 15);
        var exported = AnalysisResultTableExporter.SummaryValue(result, ParameterType.Enthalpy1);
        Assert.Equal(-25500, exported.Value);
        Assert.Equal(3, exported.SD);
        Assert.Equal(-25501, exported.Lower);
        Assert.Equal(-25497, exported.Upper);
    }

    [Fact]
    public async Task SummaryRoundTripAndViewerPayloadRetainSameUncertainty()
    {
        var result = CreateResult(3, temperatureStep: 10);
        for (var i = 0; i < 3; i++)
        {
            var member = result.Solution.Solutions[i];
            var central = member.Parameters[ParameterType.Enthalpy1].Value;
            member.Parameters[ParameterType.Enthalpy1] = new FloatWithError(central, 100 * (i + 1), central - 100 * (i + 1), central + 400 * (i + 1));
        }
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, result.Model.Models.Select(model => model.Data), new[] { result });
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>());
        stream.Position = 0;
        var viewer = await new ViewerDocumentReader().ReadAsync(stream, "summary.ftxtc", ViewerFileFormat.Ftxtc);
        var dto = Assert.Single(viewer.AnalysisResults).TemperatureParameterEvaluation.Dependences
            .Single(item => item.Key == ParameterType.Enthalpy1.ToString());
        foreach (var temperature in new[] { 10.0, 30, 50 })
        {
            var original = new AnalysisResultAggregateSummaryCalculator(result).Evaluate(ParameterType.Enthalpy1, temperature).Value;
            var actual = new AnalysisResultAggregateSummaryCalculator(restored).Evaluate(ParameterType.Enthalpy1, temperature).Value;
            Assert.Equal(original.Value, actual.Value, 10);
            Assert.Equal(original.SD, actual.SD, 10);
            Assert.Equal(original.Lower, actual.Lower, 10);
            Assert.Equal(original.Upper, actual.Upper, 10);
            var delta = temperature - dto.ReferenceTemperatureCelsius;
            var central = dto.Intercept + delta * dto.Slope;
            double variance = 0, lowerVariance = 0, upperVariance = 0;
            foreach (var term in dto.Contributions)
            {
                var weight = term.Weight + delta * term.WeightSlope;
                variance += Math.Pow(weight * term.Sd.Value, 2);
                lowerVariance += Math.Pow(weight * (weight < 0 ? term.UpperWidth.Value : term.LowerWidth.Value), 2);
                upperVariance += Math.Pow(weight * (weight < 0 ? term.LowerWidth.Value : term.UpperWidth.Value), 2);
            }
            Assert.Equal(actual.Value / 1000, central, 10);
            Assert.Equal(actual.SD / 1000, Math.Sqrt(variance), 10);
            Assert.Equal(actual.Lower / 1000, central + dto.LowerOffset.Value - Math.Sqrt(lowerVariance), 10);
            Assert.Equal(actual.Upper / 1000, central + dto.UpperOffset.Value + Math.Sqrt(upperVariance), 10);
        }
        var slope = new AnalysisResultAggregateSummaryCalculator(restored).EvaluateHeatCapacity(ThermodynamicParameterSlots.ForStep(1)).Value;
        Assert.Equal(slope.SD / 1000, dto.HeatCapacity.Sd.Value, 10);
        Assert.Equal(slope.Lower / 1000, dto.HeatCapacity.ConfidenceLower.Value, 10);
        var report = AnalysisReportBuilder.Build(restored);
        Assert.DoesNotContain(report.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportNoticeBlock>(),
            notice => notice.Title == "Summary uncertainty");
        Assert.DoesNotContain(report.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportTextBlock>(),
            block => block.Text.Contains("95% coverage is not established"));
        var expandedReport = AnalysisReportBuilder.Build(restored,
            new AnalysisReportOptions { ExpandedExplanations = true });
        Assert.Contains(expandedReport.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportNoticeBlock>(),
            notice => notice.Title == "Summary uncertainty");
        var withoutIntervals = AnalysisReportBuilder.Build(restored, new AnalysisReportOptions
        {
            ExpandedExplanations = true,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation,
        });
        Assert.DoesNotContain(withoutIntervals.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportNoticeBlock>(),
            notice => notice.Title == "Summary uncertainty");
    }

    [Fact]
    public void ExperimentFitDetailsContainCValueButSummaryDoesNot()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(1));
        var experiment = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment);
        var details = experiment.Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Single(block => block.Title == "Fit details");

        Assert.Contains(details.Items, item => item.Label == "Wiseman c-value");
        Assert.DoesNotContain(document.Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .SelectMany(section => section.Blocks)
            .OfType<AnalysisReportTableBlock>()
            .SelectMany(block => block.Columns), column => column.Title.Contains("c-value"));
    }

    [Fact]
    public void MultiResultReportsAddCValuesToEveryExperiment()
    {
        var document = AnalysisReportBuilder.Build(new[] { CreateResult(1), CreateResult(2) });
        var details = document.Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Select(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Single(block => block.Title == "Fit details"))
            .ToList();

        Assert.Equal(3, details.Count);
        Assert.All(details, block => Assert.Contains(block.Items,
            item => item.Label == "Wiseman c-value"));
    }

    [Fact]
    public void CValueKeepsOriginalBestFitWhenBootstrapDistributionIsSkewed()
    {
        var result = CreateResult(1, includeSkewedBootstrap: true);
        var member = result.Solution.Solutions.Single();
        var expected = member.Parameters[ParameterType.Nvalue1].Value
            * member.Data.CellConcentration.Value
            / member.ReportParameters[ParameterType.Affinity1].Value;

        var actual = Assert.Single(AnalysisCValueCalculator.Calculate(member));

        Assert.Equal(expected, actual.Estimate.Value.Value, 12);
    }

    static AnalysisReportSection ResultOverview(AnalysisReportDocument document) =>
        document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.ResultOverview);

    // Report text always uses invariant number formatting, independent of the test machine's culture.
    static string InReportCulture(Func<string> format)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { return format(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void NoBindingNonFiniteAttemptedParametersRetainFullSummary()
    {
        var result = CreateResult(2);
        foreach (var member in result.Solution.Solutions)
            result.SetMemberBindingAssessmentOverride(member.Guid, BindingAssessmentOutcome.NoBindingDetected);
        result.Solution.Solutions[0].Parameters[ParameterType.Enthalpy1] = FloatWithError.NaN;
        var document = AnalysisReportBuilder.Build(result);
        Assert.True(document.IsValid);
        var summary = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        var modelAndFit = Assert.Single(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Model and fit details");
        Assert.Contains(modelAndFit.Items, item => item.Label == "Model");
        Assert.Contains(modelAndFit.Items, item => item.Label.StartsWith("RMSD", StringComparison.Ordinal));
        Assert.DoesNotContain(summary.Blocks, block => block is AnalysisReportThermodynamicSummaryBlock);
        Assert.Contains(summary.Blocks.OfType<AnalysisReportTableBlock>().Single().Rows[0].Cells, cell => cell == "—");
        Assert.Contains(summary.Blocks.OfType<AnalysisReportNoticeBlock>(), block => block.Message.Contains("Experiment 1") && block.Message.Contains("Experiment 2"));
    }

    [Theory]
    [InlineData(0, false, "20.25 °C")]
    [InlineData(0, true, "293.40 K")]
    [InlineData(2, false, "21.25 °C")]
    [InlineData(2, true, "294.40 K")]
    [InlineData(3, false, "21.75 °C")]
    [InlineData(3, true, "294.90 K")]
    public void StaticResultUsesCombinedEvaluatorAtMeanTargetTemperature(
        double temperatureStep, bool useKelvin, string expectedTemperature)
    {
        var previousReference = AppSettings.ReferenceTemperature;
        var previousSpan = AppSettings.MinimumTemperatureSpanForFitting;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            AppSettings.ReferenceTemperature = 40;
            AppSettings.MinimumTemperatureSpanForFitting = 3;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var result = CreateResult(2, temperatureStep: temperatureStep);
            foreach (var model in result.Model.Models) model.Data.TargetTemperature += 0.25;
            Assert.False(result.Model.TemperatureDependenceExposed);
            var temperature = 20.25 + temperatureStep / 2;
            Assert.Equal(temperature, AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result));
            var options = new AnalysisReportOptions { UseKelvin = useKelvin };
            var expected = AnalysisResultParameterEvaluator.Evaluate(result, temperature,
                options.EnergyUnitFamily, options.EnergyUnitOverride, options.UncertaintyDisplayStyle);
            var document = AnalysisReportBuilder.Build(result, options);
            var combined = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
                .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Combined parameters");
            Assert.Equal("Evaluation temperature", combined.Items[0].Label);
            Assert.Equal(expectedTemperature, combined.Items[0].Value);
            Assert.Equal(expected.Rows.Select(row => (row.Label, row.Value)),
                combined.Items.Skip(1).Select(item => (item.Label, item.Value)));
        }
        finally
        {
            AppSettings.ReferenceTemperature = previousReference;
            AppSettings.MinimumTemperatureSpanForFitting = previousSpan;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(false, "40.00 °C")]
    [InlineData(true, "313.15 K")]
    public void TemperatureSummaryUsesSharedDefaultEvaluationTemperature(bool useKelvin, string expectedTemperature)
    {
        var previousReference = AppSettings.ReferenceTemperature;
        var previousSpan = AppSettings.MinimumTemperatureSpanForFitting;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            AppSettings.ReferenceTemperature = 40;
            AppSettings.MinimumTemperatureSpanForFitting = 3;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var result = CreateResult(3, temperatureStep: 10);
            Assert.True(result.Model.TemperatureDependenceExposed);
            Assert.True(result.Model.ShouldFitIndividually);
            Assert.Equal(30, result.Model.MeanTemperature);
            Assert.Equal(30, result.Solution.ReferenceTemperatureCelsius);
            // The report must show the same values as the Analysis Result views.
            var temperature = AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);
            Assert.Equal(40, temperature);
            var options = new AnalysisReportOptions { UseKelvin = useKelvin };
            var expected = AnalysisResultParameterEvaluator.Evaluate(result, temperature,
                options.EnergyUnitFamily, options.EnergyUnitOverride, options.UncertaintyDisplayStyle);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var document = AnalysisReportBuilder.Build(result, options);
            var combined = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
                .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Combined parameters");
            Assert.Equal("Evaluation temperature", combined.Items[0].Label);
            Assert.Equal(expectedTemperature, combined.Items[0].Value);
            Assert.Equal(expected.Rows.Select(row => (row.Label, row.Value)),
                combined.Items.Skip(1).Select(item => (item.Label, item.Value)));
        }
        finally
        {
            AppSettings.ReferenceTemperature = previousReference;
            AppSettings.MinimumTemperatureSpanForFitting = previousSpan;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void SingleExperimentResultHasNoCombinedValues()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(1));
        var summary = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        Assert.DoesNotContain(summary.Blocks.OfType<AnalysisReportKeyValueBlock>(), block =>
            block.Title == "Combined parameters");
    }

    [Fact]
    public void DiagnosticNoBindingReportKeepsCombinedOutput()
    {
        var result = CreateResult(2);
        foreach (var member in result.Solution.Solutions)
            result.SetMemberBindingAssessmentOverride(member.Guid, BindingAssessmentOutcome.NoBindingDetected);
        var standard = AnalysisReportBuilder.Build(result).Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);
        var diagnostic = AnalysisReportBuilder.Build(result,
            new AnalysisReportOptions { OutputPurpose = ResultOutputPurpose.Diagnostic }).Sections
            .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary);

        Assert.DoesNotContain(standard.Blocks, block => block is AnalysisReportThermodynamicSummaryBlock);
        Assert.DoesNotContain(standard.Blocks.OfType<AnalysisReportKeyValueBlock>(), block =>
            block.Title == "Combined parameters");
        Assert.Contains(diagnostic.Blocks, block => block is AnalysisReportThermodynamicSummaryBlock);
        Assert.Contains(diagnostic.Blocks.OfType<AnalysisReportKeyValueBlock>(), block =>
            block.Title == "Combined parameters");
        Assert.DoesNotContain(diagnostic.Blocks.OfType<AnalysisReportNoticeBlock>(), block => block.Title == "No binding detected");
    }

    [Fact]
    public void EmptyBlocksAndSectionsArePruned()
    {
        var document = AnalysisReportBuilder.Build(CreateResult(1));
        var before = document.Sections.Count;
        var section = new AnalysisReportSection(AnalysisReportSectionKind.AdvancedAnalysis, "empty", "Empty", AnalysisReportLayoutPolicy.StartOnNewPage);
        section.Add(new AnalysisReportKeyValueBlock("Empty values", Array.Empty<AnalysisReportKeyValueItem>()));
        section.Add(new AnalysisReportTextBlock("Empty text", " ", AnalysisReportLayoutPolicy.None));
        section.Add(new AnalysisReportTableBlock("Empty table", new[] { new AnalysisReportTableColumn("value", "Value") },
            Array.Empty<AnalysisReportTableRow>(), AnalysisReportLayoutPolicy.AllowContinuation));
        document.AddSection(section);
        Assert.Empty(section.Blocks);
        Assert.Equal(before, document.Sections.Count);
        var contents = document.Sections.SelectMany(item => item.Blocks).OfType<AnalysisReportTableOfContentsBlock>().Single();
        contents.AddEntry(new AnalysisReportTableOfContentsEntry("Empty advanced analysis", "empty"));
        var layout = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        Assert.DoesNotContain(contents.Entries, entry => entry.TargetSectionId == "empty");
        Assert.DoesNotContain(layout.Pages.SelectMany(page => page.Fragments), fragment => fragment.Section?.Id == "empty");
    }

    [Fact]
    public void ThermodynamicSummaryKeepsSdFreeAsymmetricIntervalsAndSuppressesFixedUncertainty()
    {
        var result = CreateResult(1);
        var member = result.Solution.Solutions[0];
        member.Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25000, 0, -27000, -24000);
        var document = AnalysisReportBuilder.Build(result);
        var bar = document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportThermodynamicSummaryBlock>().Single().Series.Single().Bars.Single(bar => bar.Category == "ΔH");
        var scale = bar.Value / -25000;
        Assert.Null(bar.StandardDeviationLower);
        Assert.Equal(-27000 * scale, bar.ConfidenceLower);
        Assert.Equal(-24000 * scale, bar.ConfidenceUpper);
        member.Model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000, islocked: true);
        document = AnalysisReportBuilder.Build(result);
        bar = document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportThermodynamicSummaryBlock>().Single().Series.Single().Bars.Single(bar => bar.Category == "ΔH");
        Assert.Null(bar.StandardDeviationLower);
        Assert.Null(bar.ConfidenceLower);
        Assert.Null(bar.ConfidenceUpper);
    }

    [Fact]
    public void ExperimentHeadersFollowEachChapterAcrossContinuationPages()
    {
        var first = CreateResult(2); first.Name = "Alpha";
        var second = CreateResult(2); second.Name = "Beta";
        var repeated = second.Solution.Solutions[0].Data;
        repeated.SetID(first.Solution.Solutions[0].Data.UniqueID);
        foreach (var result in new[] { first, second })
            foreach (var member in result.Solution.Solutions)
                member.Data.Comments = string.Join("\n", Enumerable.Repeat("Continuation context", 180));

        var document = AnalysisReportBuilder.Build(new[] { first, second },
            new AnalysisReportOptions { CondenseRepeatedExperiments = true });
        var sections = document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment).ToList();
        Assert.Equal(new[] { "1A", "1B", "2A", "2B" }, sections.Select(section => section.ExperimentLabel));
        Assert.Contains(sections[2].Blocks.OfType<AnalysisReportKeyValueBlock>(),
            block => block.Title == "Experiment details — condensed");
        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        foreach (var section in sections)
        {
            Assert.Equal(section.ExperimentLabel + ". " + section.ExperimentName, section.Title);
            var pages = plan.Pages.Where(page => page.Fragments.Any(fragment =>
                ReferenceEquals(fragment.Section, section) || section.Blocks.Contains(fragment.Block))).ToList();
            Assert.True(pages.Count > 1);
            Assert.All(pages, page =>
            {
                Assert.Equal(section.ResultName, page.ResultName);
                Assert.Equal(section.ExperimentLabel, page.ExperimentLabel);
                Assert.Equal(section.ExperimentName, page.ExperimentName);
            });
            Assert.Single(pages.SelectMany(page => page.Fragments),
                fragment => fragment.Kind == AnalysisReportFragmentKind.SectionTitle);
        }
        Assert.All(plan.Pages.Where(page => page.ExperimentLabel.Length == 0), page =>
            Assert.Equal("", page.ExperimentName));
        Assert.Equal("", plan.Pages.Last().ExperimentLabel);
        Assert.All(document.Sections.Where(section => section.Kind != AnalysisReportSectionKind.Experiment), section =>
        {
            Assert.Equal("", section.ExperimentLabel);
            Assert.Equal("", section.ExperimentName);
        });
    }

    [Fact]
    public void SupportingAndOtherSectionsDoNotInheritExperimentHeaders()
    {
        var document = new AnalysisReportDocument { Title = "Context reset" };
        var kinds = new[] { AnalysisReportSectionKind.Cover, AnalysisReportSectionKind.Experiment,
            AnalysisReportSectionKind.AnalysisSummary, AnalysisReportSectionKind.AdvancedAnalysis,
            AnalysisReportSectionKind.SupportingData, AnalysisReportSectionKind.Appendix };
        foreach (var kind in kinds)
        {
            var experiment = kind == AnalysisReportSectionKind.Experiment;
            var section = new AnalysisReportSection(kind, kind.ToString(), kind.ToString(),
                AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation,
                experimentLabel: experiment ? "1A" : "", experimentName: experiment ? "Experiment" : "");
            section.Add(new AnalysisReportTextBlock("Details", "Body", AnalysisReportLayoutPolicy.None));
            document.AddSection(section);
        }
        var plan = AnalysisReportLayoutEngine.Paginate(document, new FakeTextMeasurer());
        Assert.Equal(kinds.Length, plan.Pages.Count);
        Assert.Equal("1A", plan.Pages[1].ExperimentLabel);
        Assert.All(plan.Pages.Where((page, index) => index != 1), page =>
        {
            Assert.Equal("", page.ExperimentLabel);
            Assert.Equal("", page.ExperimentName);
        });
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
