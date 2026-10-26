using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Viewer;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed partial class SaltAnalysisTests : IDisposable
{
    readonly double oldSpan = AppSettings.MinimumIonSpanForFitting;
    readonly bool oldBuffer = AppSettings.IncludeBufferInIonicStrengthCalc;
    readonly int oldIterations = ResultAnalysisController.CalculationIterations;

    public SaltAnalysisTests()
    {
        AppSettings.MinimumIonSpanForFitting = .03;
        AppSettings.IncludeBufferInIonicStrengthCalc = false;
        ResultAnalysisController.CalculationIterations = 8;
    }

    public void Dispose()
    {
        AppSettings.MinimumIonSpanForFitting = oldSpan;
        AppSettings.IncludeBufferInIonicStrengthCalc = oldBuffer;
        ResultAnalysisController.CalculationIterations = oldIterations;
    }

    [Theory]
    [InlineData(new double[] { 0, .01, .02, .04 }, true)]
    [InlineData(new double[] { .04, .01, 0, .02 }, true)]
    [InlineData(new double[] { .05, .05, .15, .15 }, false)]
    [InlineData(new double[] { 0, .01, .04 }, false)]
    [InlineData(new double[] { .05, .1, .15 }, true)]
    public void GreedyScanFindsSpacedSubsequence(double[] strengths, bool expected)
    {
        var result = Create(strengths);
        Assert.Equal(expected, result.IsElectrostaticsAnalysisDependenceEnabled);
        Assert.Equal(expected, string.IsNullOrEmpty(result.ElectrostaticsAnalysisUnavailableReason));
    }

    [Theory]
    [InlineData(.015625 - .001953125, false)]
    [InlineData(.015625, false)]
    [InlineData(.015625 + .001953125, true)]
    public void ExactDyadicBoundaryUsesStrictComparisonWithoutTolerance(double gap, bool expected)
    {
        AppSettings.MinimumIonSpanForFitting = .03125;
        var result = Create(new[] { 0, gap, gap * 2 }, explicitStrength: true);
        Assert.Equal(expected, result.IsElectrostaticsAnalysisDependenceEnabled);
    }

    [Fact]
    public void ZeroSpanStillRequiresThreeDistinctStrengthsAndUsableAffinities()
    {
        AppSettings.MinimumIonSpanForFitting = 0;
        Assert.False(Create(new[] { .05, .05, .15 }).IsElectrostaticsAnalysisDependenceEnabled);
        Assert.True(Create(new[] { .05, .1, .15 }).IsElectrostaticsAnalysisDependenceEnabled);
        var result = Create(new[] { .05, .1, .15 });
        result.Solution.Solutions[1].Parameters[ParameterType.Affinity1] = new(double.NaN);
        result.UpdateSolution(result.Solution);
        Assert.False(result.IsElectrostaticsAnalysisDependenceEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothPreferencesAreCapturedUntilUpdate(bool initiallyEligible)
    {
        AppSettings.MinimumIonSpanForFitting = initiallyEligible ? .03 : 1;
        var result = Create(new[] { .05, .1, .15 }, buffer: true);
        var before = result.Solution.Solutions.Select(member => result.GetSaltAnalysisIonicStrength(member.Data)).ToArray();
        AppSettings.MinimumIonSpanForFitting = initiallyEligible ? 1 : .03;
        AppSettings.IncludeBufferInIonicStrengthCalc = true;
        Assert.Equal(initiallyEligible, result.IsElectrostaticsAnalysisDependenceEnabled);
        Assert.Equal(before, result.Solution.Solutions.Select(member => result.GetSaltAnalysisIonicStrength(member.Data)));
        var analysis = result.ElectrostaticsAnalysis ?? new ElectrostaticsAnalysis(result);
        Assert.Equal(initiallyEligible, await analysis.PerformAnalysisAsync());
        Assert.Equal(initiallyEligible, analysis.Calculated);
        if (!initiallyEligible) Assert.Contains("500 mM", result.ElectrostaticsAnalysisUnavailableReason);
        else Assert.Contains("15 mM", ElectrostaticsAnalysisAvailability.IonicStrengthRequirement(result.SaltAnalysisMinimumSpan));
        Assert.True(BufferAttribute.GetIonicStrength(result.Solution.Solutions[0].Data) > before[0]);
        result.UpdateSolution(result.Solution);
        Assert.Equal(!initiallyEligible, result.IsElectrostaticsAnalysisDependenceEnabled);
        Assert.True(result.GetSaltAnalysisIonicStrength(result.Solution.Solutions[0].Data) > before[0]);
    }

    [Theory]
    [InlineData(new double[] { 0, .05, .15 }, false)]
    [InlineData(new double[] { 0, .05, .05, .15 }, false)]
    [InlineData(new double[] { .05, .1, .15 }, true)]
    public async Task AsyncRunCommitsPartialOrCompleteResults(double[] strengths, bool counterAvailable)
    {
        var result = Create(strengths);
        var analysis = result.ElectrostaticsAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.True(analysis.Calculated);
        Assert.True(analysis.HasIonicStrengthResult);
        Assert.Equal(counterAvailable, analysis.HasCounterIonResult);
        Assert.NotNull(analysis.CompletedAtUtc);
        Assert.Equal(8, analysis.CompletedIterations);
        // Independent linear relation in sqrt(I): ln(Kd) = ln(1e-6) + 2 sqrt(I).
        Assert.Equal(1e-6, analysis.Kd0.Value, 15);
        Assert.Equal(2, analysis.IonicStrengthDependenceFit.SaltSensitivity.Value, 10);
        if (!counterAvailable)
        {
            Assert.Null(analysis.CounterIonReleaseFit);
            Assert.Contains("three distinct positive", analysis.CounterIonUnavailableReason);
            Assert.DoesNotContain("spacing", analysis.CounterIonUnavailableReason);
        }
    }

    [Fact]
    public async Task CounterIonUsesReplicatesAndOriginalDataCenter()
    {
        var result = Create(new[] { .05, .05, .1, .15 });
        double[] logKd = { -12, -11, -10, -8 };
        for (var i = 0; i < logKd.Length; i++)
            result.Solution.Solutions[i].Parameters[ParameterType.Affinity1] = new(-logKd[i] / Math.Log(10));
        Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
        var x = new[] { .05, .05, .1, .15 }.Select(c => Math.Log(c)).ToArray();
        var meanX = x.Average();
        var meanY = logKd.Average();
        var expected = x.Select((value, i) => (value - meanX) * (logKd[i] - meanY)).Sum()
            / x.Sum(value => (value - meanX) * (value - meanX));
        Assert.Equal(expected, result.ElectrostaticsAnalysis.CounterIonRelease.Value, 11);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AllConsumersUseComponentAvailability(bool badIonic, bool badCounter)
    {
        var result = Create(new[] { .05, .1, .15 }, buffer: true);
        var analysis = result.ElectrostaticsAnalysis;
        analysis.RestoreResult(
            new(new(badIonic ? double.PositiveInfinity : 2e-6), new(2), FloatWithError.NaN, false),
            new(new(badCounter ? double.NaN : 0), new(-12), 0), 8, 7, DateTime.UtcNow, ErrorEstimationMethod.None, true);
        Assert.Equal(.05 + (badIonic ? 0 : BufferContribution), analysis.GetPlotIonicStrength(result.Solution.Solutions[0].Data), 12);
        Assert.Equal(!badIonic, analysis.HasIonicStrengthResult);
        Assert.Equal(!badCounter, analysis.HasCounterIonResult);
        var available = AnalysisReportBuilder.GetAvailableAdvancedSections(result).Select(item => item.Request.Kind).ToArray();
        Assert.Equal(!badIonic, available.Contains(AnalysisReportAdvancedSectionKind.DebyeHuckel));
        Assert.Equal(!badCounter, available.Contains(AnalysisReportAdvancedSectionKind.CounterIonRelease));
        var report = new AnalysisReport(); report.SetResultIds(new[] { result.UniqueID });
        var evidence = Assert.Single(AnalysisInterpretationPackageBuilder.Build(report, result).Result.AdvancedAnalyses,
            item => item.Type == "electrostatics");
        Assert.Equal(!badIonic, evidence.IonicStrengthAvailable);
        Assert.Equal(!badCounter, evidence.CounterIonReleaseAvailable);
        Assert.Equal(badIonic ? (int?)null : 8, evidence.IonicStrengthCompletedIterations);
        Assert.Equal(badCounter ? (int?)null : 7, evidence.CounterIonReleaseCompletedIterations);
        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new(AnalysisReportAdvancedSectionKind.DebyeHuckel));
        options.AdvancedSections.Add(new(AnalysisReportAdvancedSectionKind.CounterIonRelease));
        var items = AnalysisReportBuilder.Build(result, options).Sections
            .Where(section => section.Kind == AnalysisReportSectionKind.AdvancedAnalysis)
            .SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .Where(block => block.Title == "Saved result").SelectMany(block => block.Items).ToArray();
        Assert.Equal(!badIonic, items.Any(item => item.Label == "Kd at zero ionic strength"));
        Assert.Equal(!badCounter, items.Any(item => item.Label == "Salt-dependence slope"));
        Assert.Equal(!badIonic, evidence.Values.Any(value => value.Id == "kd-zero-ionic-strength"));
        Assert.Equal(!badCounter, evidence.Values.Any(value => value.Id == "salt-dependence-slope"));
        var dto = (ViewerElectrostaticsDto)typeof(ViewerDocumentReader).GetMethod("BuildElectrostatics", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { analysis });
        Assert.Equal(badIonic, dto.Kd0Micromolar == null);
        Assert.Equal(badCounter, dto.CounterIonRelease == null);
        Assert.Equal(!badIonic, dto.Plots.Single(plot => plot.Key == "debye-huckel").Series.Any(series => series.Kind == "line"));
        Assert.Equal(!badCounter, dto.Plots.Single(plot => plot.Key == "counter-ion-release").Series.Any(series => series.Kind == "line"));
        if (badIonic) Assert.Contains("No usable ionic-strength fit", analysis.IonicStrengthUnavailableReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopenPreservesSavedComponentsWhenNewEligibilityFails(bool partial)
    {
        var result = Create(new[] { .05, .1, .15 });
        result.ElectrostaticsAnalysis.RestoreResult(new(new(2e-6), new(2), new(0)),
            partial ? null : new(new(0), new(-12), 0), 8, partial ? 0 : 7, DateTime.UtcNow, ErrorEstimationMethod.None, false);
        using var saved = await Save(result);
        AppSettings.MinimumIonSpanForFitting = 1;
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(saved, FtxtcReadPolicy.Strict)).Containers.OfType<AnalysisResult>());
        Assert.False(restored.IsElectrostaticsAnalysisDependenceEnabled);
        Assert.True(restored.ElectrostaticsAnalysis.HasIonicStrengthResult);
        Assert.Equal(!partial, restored.ElectrostaticsAnalysis.HasCounterIonResult);
        Assert.False(await restored.ElectrostaticsAnalysis.PerformAnalysisAsync());
        var available = AnalysisReportBuilder.GetAvailableAdvancedSections(restored).Select(item => item.Request.Kind);
        Assert.DoesNotContain(AnalysisReportAdvancedSectionKind.AffinityVersusSalt, available);
        Assert.Contains(AnalysisReportAdvancedSectionKind.DebyeHuckel, available);
        using var resaved = await Save(restored);
        var reopened = Assert.Single((await FTXTCReader.ReadStream(resaved)).OfType<AnalysisResult>());
        Assert.Equal(2e-6, reopened.ElectrostaticsAnalysis.Kd0.Value);
        resaved.Position = 0;
        var viewer = await new ViewerDocumentReader().ReadAsync(resaved, "salt.ftxtc", ViewerFileFormat.Ftxtc);
        Assert.Equal(2, Assert.Single(viewer.AnalysisResults).AdvancedAnalyses.Electrostatics.Kd0Micromolar.Value);
        reopened.UpdateSolution(reopened.Solution);
        Assert.Null(reopened.ElectrostaticsAnalysis);
    }

    [Fact]
    public void TwoConditionsLoseCoupledColumnsButDoNotBypassMissingMetadata()
    {
        var result = Create(new[] { .04, .16 });
        Assert.DoesNotContain("IS (mM)", AnalysisResultTableExporter.Build(new[] { result }, new() { RowMode = AnalysisResultExportRowMode.AllRows }));
        Assert.DoesNotContain(AnalysisResultOverviewTable.Build(result, EnergyUnitFamily.Joules, false).Columns, column => column.Id == "IS");
        Assert.True(result.EnsureElectrostaticsAnalysisForRestore());
        result.Solution.Solutions[0].Data.Attributes.Clear();
        Assert.False(result.EnsureElectrostaticsAnalysisForRestore());
        result.UpdateSolution(result.Solution);
        Assert.Contains("salt-0", result.ElectrostaticsAnalysisUnavailableReason);
    }

    [Theory]
    [InlineData(AnalysisResultExportFileFormat.CSV)]
    [InlineData(AnalysisResultExportFileFormat.TSV)]
    public void EligibilityControlsExportsClipboardOverviewAndAttributeDisplay(AnalysisResultExportFileFormat format)
    {
        var clipboard = new TestClipboard();
        var previousClipboard = PlatformServices.ClipboardService;
        PlatformServices.RegisterClipboardService(clipboard);
        try
        {
            foreach (var eligible in new[] { false, true })
            {
                var result = Create(eligible ? new[] { .04, .16, .36 } : new[] { .04, .16 });
                var table = AnalysisResultTableExporter.Build(new[] { result }, new() { FileFormat = format, RowMode = AnalysisResultExportRowMode.AllRows });
                Assert.Equal(eligible, table.Contains("IS (mM)"));
                Exporter.CopyToClipboard(result, EnergyUnit.Joule, false);
                Assert.Equal(eligible, ReadCopiedIonicStrength(clipboard.Value).Column >= 0);
                Assert.Equal(eligible, AnalysisResultOverviewTable.Build(result, EnergyUnitFamily.Joules, false).Columns.Any(column => column.Id == "IS"));
                DataManager.AddData(result);
                try
                {
                    var attributes = result.Solution.Solutions[0].UIExperimentModelAttributes(DisplayAttributeOptions.UsedInAnalysis);
                    Assert.Equal(eligible, attributes.Any(item => item.Item1.Contains("NaCl")));
                }
                finally { DataManager.RemoveSourceItemAt(DataManager.SourceItems.ToList().IndexOf(result)); }
            }
        }
        finally { PlatformServices.RegisterClipboardService(previousClipboard); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipboardUsesCapturedRunPolicyUntilUpdateIndependentlyOfCompletedPlotPolicy(bool includeBuffer)
    {
        AppSettings.IncludeBufferInIonicStrengthCalc = includeBuffer;
        var strengths = new[] { .05, .1, .15 };
        var result = Create(strengths, buffer: true);
        result.ElectrostaticsAnalysis.RestoreResult(new(new(2e-6), new(2), new(0)), null,
            8, 0, DateTime.UtcNow, ErrorEstimationMethod.None, !includeBuffer);
        AppSettings.IncludeBufferInIonicStrengthCalc = !includeBuffer;
        Assert.Equal(.05 + (!includeBuffer ? BufferContribution : 0),
            result.ElectrostaticsAnalysis.GetPlotIonicStrength(result.Solution.Solutions[0].Data), 12);

        var clipboard = new TestClipboard();
        var previousClipboard = PlatformServices.ClipboardService;
        PlatformServices.RegisterClipboardService(clipboard);
        try
        {
            CheckCopiedValues(includeBuffer);
            result.UpdateSolution(result.Solution);
            CheckCopiedValues(!includeBuffer);
        }
        finally { PlatformServices.RegisterClipboardService(previousClipboard); }

        void CheckCopiedValues(bool expectedBuffer)
        {
            var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                Exporter.CopyToClipboard(result, EnergyUnit.Joule, false);
                var copied = ReadCopiedIonicStrength(clipboard.Value);
                Assert.True(copied.Column >= 0);
                Assert.True(copied.Rows.Length >= strengths.Length);
                for (var i = 0; i < strengths.Length; i++)
                {
                    var expected = Math.Round(1000 * (strengths[i] + (expectedBuffer ? BufferContribution : 0)), 2);
                    var actual = double.Parse(copied.Rows[i][copied.Column], System.Globalization.CultureInfo.InvariantCulture);
                    Assert.Equal(expected, actual);
                }
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
        }
    }

    static (int Column, string[][] Rows) ReadCopiedIonicStrength(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var delimiter = lines[0].Contains('\t') ? '\t' : ',';
        var header = lines[0].Split(delimiter);
        var column = Array.FindIndex(header, value => value is "IS(mM)" or "IS (mM)");
        return (column, lines.Skip(1).Select(line => line.Split(delimiter)).ToArray());
    }

    sealed class TestClipboard : IClipboardService
    {
        public string Value { get; private set; }
        public void SetString(string value) => Value = value;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictRestoreStillRejectsMissingSaltOrUnsupportedModel(bool unsupportedModel)
    {
        var result = Create(new[] { .05, .1, .15 }, twoSite: unsupportedModel);
        var analysis = new ElectrostaticsAnalysis(result);
        // Inject an inconsistent saved state to exercise the reader's structural guard.
        // A normal post-calculation edit now invalidates the component before writing.
        if (!unsupportedModel) result.Solution.Solutions[0].Data.Attributes.Clear();
        analysis.RestoreResult(new(new(2e-6), new(2), new(0)), null, 8, 0, DateTime.UtcNow, ErrorEstimationMethod.None, false);
        typeof(AnalysisResult).GetProperty(nameof(AnalysisResult.ElectrostaticsAnalysis)).SetValue(result, analysis);
        Assert.False(result.EnsureElectrostaticsAnalysisForRestore());
        using var stream = await Save(result);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict));
        Assert.Contains("electrostatics", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // Independent numerical root for 10 mM HEPES, pH 7.4, 25 C:
    // I = .01 / (1 + 10^(7.66 - A*sqrt(I)/(1+sqrt(I)) + .1*I - 7.4)),
    // A = .4918 + .0006614*25 + .000004975*25^2.
    const double BufferContribution = .0037005197012182495;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedPlotPolicySurvivesOppositeReopenPreferences(bool savedPolicy)
    {
        AppSettings.IncludeBufferInIonicStrengthCalc = savedPolicy;
        var result = Create(new[] { .05, .1, .15 }, buffer: true);
        foreach (var member in result.Solution.Solutions)
        {
            var salt = member.Data.Attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue.Value;
            var ionic = salt + (savedPolicy ? BufferContribution : 0);
            member.Parameters[ParameterType.Affinity1] = new(-Math.Log10(1e-6 * Math.Exp(2 * Math.Sqrt(ionic))));
        }
        var original = result.ElectrostaticsAnalysis;
        Assert.True(await original.PerformAnalysisAsync());
        Assert.Equal(savedPolicy, original.CompletedIncludeBufferInIonicStrengthCalc);
        Assert.Equal(1e-6, original.Kd0.Value, 15);
        Assert.Equal(2, original.IonicStrengthDependenceFit.SaltSensitivity.Value, 10);
        using var saved = await Save(result);
        AppSettings.IncludeBufferInIonicStrengthCalc = !savedPolicy;
        AppSettings.MinimumIonSpanForFitting = 1;
        var reopened = Assert.Single((await FTXTCReader.ReadWithRecovery(saved, FtxtcReadPolicy.Strict)).Containers.OfType<AnalysisResult>());
        var analysis = reopened.ElectrostaticsAnalysis;
        Assert.False(reopened.IsElectrostaticsAnalysisDependenceEnabled);
        Assert.False(await analysis.PerformAnalysisAsync());
        Assert.Equal(!savedPolicy, reopened.SaltAnalysisIncludesBuffer);
        Assert.Equal(savedPolicy, analysis.CompletedIncludeBufferInIonicStrengthCalc);
        SameEstimate(original.Kd0, analysis.Kd0);
        SameEstimate(original.IonicStrengthDependenceFit.SaltSensitivity, analysis.IonicStrengthDependenceFit.SaltSensitivity);
        SameEstimate(original.CounterIonRelease, analysis.CounterIonRelease);
        SameEstimate(original.IonicStrengthDependenceFit.Curvature, analysis.IonicStrengthDependenceFit.Curvature);
        SameEstimate(original.CounterIonReleaseFit.Intercept, analysis.CounterIonReleaseFit.Intercept);
        Assert.Equal(original.CompletedAtUtc, analysis.CompletedAtUtc);
        Assert.Equal(original.CompletedIterations, analysis.CompletedIterations);
        Assert.Equal(original.CounterIonReleaseIterations, analysis.CounterIonReleaseIterations);
        var expected = new[] { .05, .1, .15 }.Select(i => i + (savedPolicy ? BufferContribution : 0)).ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            var data = reopened.Solution.Solutions[i].Data;
            Assert.Equal(expected[i], analysis.GetPlotIonicStrength(data), 12);
            Assert.Equal(new[] { .05, .1, .15 }[i] + (!savedPolicy ? BufferContribution : 0), reopened.GetSaltAnalysisIonicStrength(data), 12);
        }
        Assert.Equal(expected, analysis.GetDataPoints(ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel).Select(p => p.Item1), new DoublePrecisionComparer(12));
        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new(AnalysisReportAdvancedSectionKind.DebyeHuckel));
        var plot = AnalysisReportBuilder.Build(reopened, options).Sections.SelectMany(s => s.Blocks).OfType<AnalysisReportPlotBlock>()
            .Single(p => p.Title == "Debye-Huckel dependence");
        Assert.Equal(expected.Select(Math.Sqrt), plot.Series.Single(s => s.Kind == AnalysisReportPlotSeriesKind.Points).Points.Select(p => p.X), new DoublePrecisionComparer(12));
        Assert.All(plot.Series.Single(s => s.Kind == AnalysisReportPlotSeriesKind.Line).Points,
            p => Assert.Equal(-6 + 2 * p.X / Math.Log(10), p.Y, 10));
        using var resaved = await Save(reopened);
        var viewer = await new ViewerDocumentReader().ReadAsync(resaved, "saved-salt.ftxtc", ViewerFileFormat.Ftxtc);
        var dto = Assert.Single(viewer.AnalysisResults).AdvancedAnalyses.Electrostatics;
        Assert.Equal(1, dto.Kd0Micromolar.Value, 10);
        // Viewer coordinates are sqrt(I), in the same saved context as its fitted curve.
        Assert.Equal(expected.Select(Math.Sqrt), dto.Plots.Single(p => p.Key == "debye-huckel").Series.Single(s => s.Kind == "points").X, new DoublePrecisionComparer(12));
        var curve = dto.Plots.Single(p => p.Key == "debye-huckel").Series.Single(s => s.Kind == "line");
        for (var i = 0; i < curve.X.Length; i++) Assert.Equal(-6 + 2 * curve.X[i] / Math.Log(10), curve.Y[i], 10);
        var report = new AnalysisReport(); report.SetResultIds(new[] { reopened.UniqueID });
        var evidence = Assert.Single(AnalysisInterpretationPackageBuilder.Build(report, reopened).Result.AdvancedAnalyses, a => a.Type == "electrostatics");
        Assert.Equal(1e-6, evidence.Values.Single(v => v.Id == "kd-zero-ionic-strength").Value.Value, 15);
        resaved.Position = 0;
        var again = Assert.Single((await FTXTCReader.ReadStream(resaved)).OfType<AnalysisResult>());
        Assert.Equal(savedPolicy, again.ElectrostaticsAnalysis.CompletedIncludeBufferInIonicStrengthCalc);
        SameEstimate(original.Kd0, again.ElectrostaticsAnalysis.Kd0);
        Assert.Equal(original.CompletedAtUtc, again.ElectrostaticsAnalysis.CompletedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NextRunUsesReopenedCaptureAndReplacesFitAndPolicy(bool savedPolicy)
    {
        AppSettings.IncludeBufferInIonicStrengthCalc = savedPolicy;
        var result = Create(new[] { .05, .1, .15 }, buffer: true);
        Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
        using var saved = await Save(result);
        AppSettings.IncludeBufferInIonicStrengthCalc = !savedPolicy;
        var reopened = Assert.Single((await FTXTCReader.ReadStream(saved)).OfType<AnalysisResult>());
        var analysis = reopened.ElectrostaticsAnalysis;
        var previous = analysis.IonicStrengthDependenceFit;
        // Further live changes cannot alter the policy captured on reopen.
        AppSettings.IncludeBufferInIonicStrengthCalc = savedPolicy;
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.Equal(!savedPolicy, analysis.CompletedIncludeBufferInIonicStrengthCalc);
        Assert.NotSame(previous, analysis.IonicStrengthDependenceFit);
        Assert.True(Math.Abs(previous.Kd0.Value - analysis.Kd0.Value) > 1e-10);
        var data = reopened.Solution.Solutions[0].Data;
        Assert.Equal(reopened.GetSaltAnalysisIonicStrength(data), analysis.GetPlotIonicStrength(data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledRunRetainsCompletedFitPolicyAndMetadata(bool cancel)
    {
        AppSettings.IncludeBufferInIonicStrengthCalc = true;
        var result = Create(new[] { .05, .1, .15 }, buffer: true);
        var analysis = new InterruptedSaltAnalysis(result, cancel);
        var fit = new IonicStrengthDependenceFit(new(2e-6, 1e-7), new(2), new(0));
        var completed = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        analysis.RestoreResult(fit, null, 12, 0, completed, ErrorEstimationMethod.None, false);
        try
        {
            Assert.False(await analysis.PerformAnalysisAsync());
            Assert.True(analysis.Calculated);
            Assert.Same(fit, analysis.IonicStrengthDependenceFit);
            Assert.False(analysis.CompletedIncludeBufferInIonicStrengthCalc);
            Assert.Equal(completed, analysis.CompletedAtUtc);
            Assert.Equal(12, analysis.CompletedIterations);
            Assert.Equal(.05, analysis.GetPlotIonicStrength(result.Solution.Solutions[0].Data), 12);
        }
        finally { ResultAnalysisController.TerminateAnalysisFlag.Lower(); }
    }

    sealed class InterruptedSaltAnalysis(AnalysisResult result, bool cancel) : ElectrostaticsAnalysis(result)
    {
        protected override void Calculate()
        {
            base.Calculate();
            if (cancel) ResultAnalysisController.TerminateAnalysisFlag.Raise();
            else throw new InvalidOperationException("Intentional failure after calculating replacement state.");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NullComponentsAreCompleteSavedAttempts(bool ionic, bool counter)
    {
        var result = Create(new[] { .05, .1, .15 });
        result.ElectrostaticsAnalysis.RestoreResult(ionic ? new(new(2e-6), new(2), new(0)) : null,
            counter ? new(new(0), new(-12), 0) : null, ionic ? 8 : 0, counter ? 7 : 0, DateTime.UtcNow, ErrorEstimationMethod.None, false);
        using var saved = await Save(result);
        var read = await FTXTCReader.ReadWithRecovery(saved, FtxtcReadPolicy.Strict);
        Assert.Empty(read.Issues);
        var reopened = Assert.Single(read.Containers.OfType<AnalysisResult>()).ElectrostaticsAnalysis;
        Assert.True(reopened.Calculated);
        Assert.False(reopened.CompletedIncludeBufferInIonicStrengthCalc);
        Assert.Equal(ionic, reopened.HasIonicStrengthResult);
        Assert.Equal(counter, reopened.HasCounterIonResult);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("null-policy")]
    [InlineData("kd0")]
    [InlineData("sensitivity")]
    [InlineData("curvature")]
    [InlineData("usesCurvature")]
    [InlineData("referenceX")]
    [InlineData("slope")]
    [InlineData("intercept")]
    public async Task IncompleteOptionalObjectIsQuietlyOmittedInBothReadModes(string missing)
    {
        var result = Create(new[] { .05, .1, .15 }, buffer: true);
        // Distinct buffers make a separate saved protonation analysis available.
        result.Solution.Solutions[1].Data.Attributes.Single(a => a.Key == AttributeKey.Buffer).IntValue = (int)AnalysisITC.Core.Data.Buffer.Tris;
        result.Solution.Solutions[2].Data.Attributes.Single(a => a.Key == AttributeKey.Buffer).IntValue = (int)AnalysisITC.Core.Data.Buffer.SodiumPhosphate;
        for (var i = 0; i < result.Solution.Solutions.Count; i++)
            result.Solution.Solutions[i].Data.MeasuredTemperature = 20 + 5 * i;
        result.UpdateSolution(result.Solution);
        result.SpolarRecordAnalysis.RestoreResult(FTSRMethod.SRFoldedMode.Glob, FTSRMethod.SRTempMode.MeanTemperature,
            new FTSRMethod.SROutput(new(-.11), new(-.22), new(42), new(25)), 20, DateTime.UtcNow);
        result.ProtonationAnalysis.RestoreResult(new(-25000), new(.8), 18, DateTime.UtcNow, ErrorEstimationMethod.None);
        Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
        using var saved = await Save(result);
        using var incomplete = FTXTCFormatTests.RewriteAuthenticatedPackage(saved, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var root = System.Text.Json.Nodes.JsonNode.Parse(bytes);
            var salt = root["advancedAnalyses"]["electrostatics"];
            if (missing == "policy") salt.AsObject().Remove("includeBufferInIonicStrengthCalc");
            else if (missing == "null-policy") salt["includeBufferInIonicStrengthCalc"] = null;
            else salt[missing is "slope" or "intercept" or "referenceX" ? "counterIonReleaseFit" : "ionicStrengthFit"].AsObject().Remove(missing);
            return System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: 6);
        foreach (var mode in new[] { FtxtcReadPolicy.Strict, FtxtcReadPolicy.RecoverUsableContent })
        foreach (var eligible in new[] { true, false })
        {
            AppSettings.MinimumIonSpanForFitting = eligible ? .03 : 1;
            incomplete.Position = 0;
            var read = await FTXTCReader.ReadWithRecovery(incomplete, mode);
            Assert.Empty(read.Issues);
            Assert.False(read.IsPartial);
            var reopened = Assert.Single(read.Containers.OfType<AnalysisResult>());
            Assert.Equal(result.UniqueID, reopened.UniqueID);
            Assert.Equal(3, reopened.Solution.Solutions.Count);
            Assert.Equal(.8, reopened.ProtonationAnalysis.ProtonationChange.Value);
            Assert.Equal(18, reopened.ProtonationAnalysis.CompletedIterations);
            Assert.Equal(42, reopened.SpolarRecordAnalysis.Result.Item3.Value);
            Assert.Equal(20, reopened.SpolarRecordAnalysis.CompletedIterations);
            Assert.Equal(eligible, reopened.ElectrostaticsAnalysis != null);
            Assert.False(reopened.ElectrostaticsAnalysis?.Calculated == true);
            using var resaved = await Save(reopened);
            var state = ReadElectrostaticsPayload(resaved);
            Assert.Null(state);
            // Refit/update captures current preferences and makes the normal next run available.
            AppSettings.MinimumIonSpanForFitting = .03;
            reopened.UpdateSolution(reopened.Solution);
            Assert.True(await reopened.ElectrostaticsAnalysis.PerformAnalysisAsync());
            using var recalculated = await Save(reopened);
            Assert.False(ReadElectrostaticsPayload(recalculated)["includeBufferInIonicStrengthCalc"].GetValue<bool>());
        }
    }

    static System.Text.Json.Nodes.JsonNode ReadElectrostaticsPayload(Stream stream)
    {
        stream.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, true);
        using var entry = zip.Entries.Single(e => e.FullName.EndsWith("/result.json", StringComparison.Ordinal)).Open();
        return System.Text.Json.Nodes.JsonNode.Parse(entry)["advancedAnalyses"]?["electrostatics"];
    }

    static void SameEstimate(FloatWithError expected, FloatWithError actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.SD, actual.SD);
        Assert.Equal(expected.Lower, actual.Lower);
        Assert.Equal(expected.Upper, actual.Upper);
    }

    sealed class DoublePrecisionComparer(int precision) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Round(x, precision) == Math.Round(y, precision);
        public int GetHashCode(double value) => Math.Round(value, precision).GetHashCode();
    }

    static async Task<MemoryStream> Save(AnalysisResult result)
    {
        var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, result.Solution.Solutions.Select(member => member.Data), new[] { result });
        stream.Position = 0;
        return stream;
    }

    internal static AnalysisResult Create(double[] strengths, bool explicitStrength = false, bool buffer = false, bool twoSite = false)
    {
        var models = new List<Model>(); var members = new List<SolutionInterface>();
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Algorithm = SolverAlgorithm.LevenbergMarquardt, Termination = SolverTermination.Converged, Loss = 1 });
        for (var index = 0; index < strengths.Length; index++)
        {
            var strength = strengths[index];
            var data = new ExperimentData("salt-" + index + ".itc")
                { CellConcentration = new(30e-6), SyringeConcentration = new(400e-6), CellVolume = 1.4e-3, MeasuredTemperature = 25 };
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
            salt.IntValue = (int)Salt.NaCl; salt.ParameterValue = new(strength); data.Attributes.Add(salt);
            if (explicitStrength)
            {
                var ionic = ExperimentAttribute.FromKey(AttributeKey.IonicStrength);
                ionic.ParameterValue = new(strength); data.Attributes.Add(ionic);
            }
            if (buffer)
            {
                var attribute = ExperimentAttribute.FromKey(AttributeKey.Buffer);
                attribute.IntValue = (int)AnalysisITC.Core.Data.Buffer.Hepes;
                attribute.DoubleValue = 7.4; attribute.ParameterValue = new(.01); data.Attributes.Add(attribute);
            }
            var injection = new InjectionData(data, 0, 2e-6, 2e-10, true)
                { ActualCellConcentration = data.CellConcentration, ActualTitrantConcentration = 2e-6, Ratio = 1 };
            injection.SetPeakArea(new FloatWithError(-1e-6, 1e-8));
            data.Injections.Add(injection);
            Model model = twoSite ? new TwoSetsOfSites(data) : new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, -Math.Log10(1e-6 * Math.Exp(2 * Math.Sqrt(strength))));
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence);
            data.UpdateSolution(model);
            models.Add(model); members.Add(model.Solution);
        }
        var global = new GlobalModel(models) { Parameters = new GlobalModelParameters(), ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        global.Solution = new GlobalSolution(new GlobalSolver { Model = global, ErrorEstimationMethod = ErrorEstimationMethod.None }, members, convergence);
        return new AnalysisResult(global.Solution);
    }
}
