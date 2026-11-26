using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class TemperatureDependenceGraphTests
{
    [AvaloniaFact]
    public async Task TemperaturePlotAndPrintUseFitLimitsUntilUncertaintyIsHidden()
    {
        var previousPreferences = PreferencesState.FromSettings();
        var previousStore = PlatformServices.SettingsStore;
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
        var (_, _, result) = AnalysisReportEnhancementTests.CreateReplicateSummaryFixture(secondTemperature: 35);
        var first = result.Solution.Solutions[0];
        first.Parameters[ParameterType.Enthalpy1] = new FloatWithError(10, 1, -200_000, 320_000);
        var fixedMember = result.Solution.Solutions[1];
        fixedMember.Model.Parameters.Table[ParameterType.Enthalpy1].Update(12, lockpar: true);
        fixedMember.Parameters[ParameterType.Enthalpy1] = new FloatWithError(12, 3, -110_000, 130_000);
        result.UpdateSolution(result.Solution);
        var workspace = new AnalysisResultWorkspaceControl { Result = result };
        var window = new Window { Width = 900, Height = 700, Content = workspace };
        try
        {
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
            AppSettings.SummaryUncertaintyMode = SummaryUncertaintyMode.Mean;
            workspace.SetResultViewMode(ResultAnalysisViewMode.Temperature);
            workspace.SetUncertaintyDisplay(UncertaintyDisplayStyle.StandardDeviation);
            window.Show();
            window.UpdateLayout();
            var graph = Assert.IsType<ResultDependenceGraphControl>(workspace.GraphHostContentForTesting);

            Assert.Contains("the 95% limits for each fit", graph.UncertaintyCaptionForTesting);
            Assert.Contains("95% intervals for the fitted trend", graph.UncertaintyCaptionForTesting);
            Assert.True(graph.UncertaintyBandCountForTesting > 0);
            var pointLimits = graph.PointIntervalsForTesting;
            Assert.Contains(pointLimits, interval => Math.Abs(interval.Lower + 200) < 1e-9
                && Math.Abs(interval.Upper - 320) < 1e-9);
            Assert.DoesNotContain(pointLimits, interval => Math.Abs(interval.Lower + 110) < 1e-9
                && Math.Abs(interval.Upper - 130) < 1e-9);
            var range = graph.YRangeForTesting;
            var centers = graph.FitPointsForTesting;
            Assert.NotEmpty(centers);
            await VerifyPrintable("mean");

            workspace.SetUncertaintyDisplay(UncertaintyDisplayStyle.ConfidenceInterval);
            Assert.Equal(pointLimits, graph.PointIntervalsForTesting);
            Assert.Equal(range, graph.YRangeForTesting);

            AppSettings.SummaryUncertaintyMode = SummaryUncertaintyMode.ReplicateSpread;
            workspace.Refresh();
            Assert.Contains("95% replicate ranges", graph.UncertaintyCaptionForTesting);
            Assert.Equal(pointLimits, graph.PointIntervalsForTesting);
            Assert.Equal(centers, graph.FitPointsForTesting);
            await VerifyPrintable("spread");

            workspace.SetUncertaintyDisplay(UncertaintyDisplayStyle.None);
            Assert.Empty(graph.UncertaintyCaptionForTesting);
            Assert.Empty(graph.PointIntervalsForTesting);
            Assert.Equal(0, graph.UncertaintyBandCountForTesting);
            Assert.Equal(centers, graph.FitPointsForTesting);
            Assert.True(graph.YRangeForTesting.Min > range.Min);
            Assert.True(graph.YRangeForTesting.Max < range.Max);
            await VerifyPrintable("none");

            async Task VerifyPrintable(string mode)
            {
                Assert.True(workspace.TryGetPrintTarget(out var target));
                using var payload = await target!.CaptureAsync();
                Assert.NotEmpty(payload.Pdf);
                Assert.DoesNotContain("/Subtype /Image", Encoding.ASCII.GetString(payload.Pdf));

                var directory = Environment.GetEnvironmentVariable("FTITC_TEMPERATURE_QA_DIRECTORY");
                if (string.IsNullOrWhiteSpace(directory)) return;
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, $"temperature-{mode}.pdf"), payload.Pdf);
            }
        }
        finally
        {
            workspace.Result = null;
            window.Close();
            AnalysisResultWorkspaceControl.ResetSessionViewForTesting();
            previousPreferences.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(previousStore);
        }
    }
}
