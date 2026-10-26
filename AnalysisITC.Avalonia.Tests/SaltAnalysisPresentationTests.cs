using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SaltAnalysisPresentationTests
{
    [AvaloniaFact]
    public async Task DetailsApplyAndRunButtonReplaceInvalidatedSaltOutputs()
    {
        var span = AppSettings.MinimumIonSpanForFitting;
        var iterations = ResultAnalysisController.CalculationIterations;
        Window? host = null;
        try
        {
            AppSettings.MinimumIonSpanForFitting = .03;
            ResultAnalysisController.CalculationIterations = 8;
            var result = SaltDependenceGraphTests.CreateSaltResult(new[] { .025, .05, .1, .2 });
            for (var i = 0; i < 4; i++)
                result.Solution.Solutions[i].Parameters[ParameterType.Affinity1] = new(-Math.Log10(1e-7 * Math.Pow(16, i)));
            result.UpdateSolution(result.Solution);
            Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
            Assert.Equal(4, result.ElectrostaticsAnalysis.CounterIonRelease.Value, 10);

            for (var i = 0; i < 4; i++)
            {
                var details = new ExperimentDetailsWindow(result.Solution.Solutions[i].Data);
                var row = details.GetLogicalDescendants().OfType<ExperimentAttributeEditorControl>().Single();
                ((TextBox)row.GetType().GetField("valueBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(row)!).Text
                    = (25 * Math.Pow(4, i)).ToString("F0");
                details.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(details.Applied);
            }

            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            host = new Window { Content = workspace };
            host.Show();
            workspace.SetResultViewMode(ResultAnalysisViewMode.Salt);
            var panel = (StackPanel)typeof(AnalysisResultWorkspaceControl).GetField("analysisPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
            Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == ElectrostaticsAnalysisAvailability.InputsChangedReason);
            var graph = new ResultDependenceGraphControl { Result = result, Mode = ResultAnalysisViewMode.Salt,
                SaltMode = ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease };
            Assert.Empty(graph.FitPointsForTesting);
            panel.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Run Analysis"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((ProcessingActivity.IsBusy || !result.ElectrostaticsAnalysis.HasCounterIonResult) && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.False(ProcessingActivity.IsBusy);
            Assert.Equal(2, result.ElectrostaticsAnalysis.CounterIonRelease.Value, 10);
            graph.Rebuild();
            Assert.NotEmpty(graph.FitPointsForTesting);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == ElectrostaticsAnalysisAvailability.InputsChangedReason);
        }
        finally
        {
            host?.Close();
            AppSettings.MinimumIonSpanForFitting = span;
            ResultAnalysisController.CalculationIterations = iterations;
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void OutputUsesSharedAvailabilityAndTooltips(bool ionic, bool counter, bool missingFits)
    {
        var result = SaltDependenceGraphTests.CreateSaltResult();
        result.ElectrostaticsAnalysis.RestoreResult(
            ionic ? new(new(2e-6), new(2), FloatWithError.NaN, false) : missingFits ? null : new(new(double.PositiveInfinity), new(2), new(0)),
            counter ? new(new(0), new(-12), 0) : missingFits ? null : new(new(double.NaN), new(-12), 0),
            8, 7, DateTime.UtcNow, ErrorEstimationMethod.None, false);
        var workspace = new AnalysisResultWorkspaceControl { Result = result };
        workspace.SetResultViewMode(ResultAnalysisViewMode.Salt);
        var panel = (StackPanel)typeof(AnalysisResultWorkspaceControl).GetField("analysisPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        var text = panel.GetLogicalDescendants().OfType<TextBlock>().ToArray();
        CheckRow("Kd0", ionic, result.ElectrostaticsAnalysis.IonicStrengthUnavailableReason);
        CheckRow("Salt-dependence slope", counter, result.ElectrostaticsAnalysis.CounterIonUnavailableReason);
        void CheckRow(string label, bool available, string missingReason)
        {
            var heading = text.Single(value => value.Text == label);
            var grid = (Grid)heading.Parent!;
            var value = grid.Children.OfType<TextBlock>().Last();
            Assert.Equal(!available, value.Text == "Unavailable");
            if (!available) Assert.Equal(missingReason, ToolTip.GetTip(value));
            else Assert.Equal(label == "Kd0" ? ElectrostaticsAnalysisAvailability.Kd0ToolTip : ElectrostaticsAnalysisAvailability.SaltDependenceToolTip, ToolTip.GetTip(value));
        }
    }

    [AvaloniaFact]
    public void PreferenceLabelAndTooltipExplainSpacing()
    {
        var window = new PreferencesWindow();
        Assert.Single(window.GetLogicalDescendants().OfType<TextBlock>(), value => value.Text == "Minimum ionic-strength span (mM)");
        var field = (TextBox)typeof(PreferencesWindow).GetField("minimumIonSpanBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Equal(ElectrostaticsAnalysisAvailability.MinimumSpanToolTip, ToolTip.GetTip(field));
    }

    [AvaloniaFact]
    public void IneligibleRestoredOutputKeepsSaltHiddenAndExplainsSpacing()
    {
        var span = AppSettings.MinimumIonSpanForFitting;
        try
        {
            AppSettings.MinimumIonSpanForFitting = .03;
            var result = SaltDependenceGraphTests.CreateSaltResult(new[] { .04, .16 });
            Assert.True(result.EnsureElectrostaticsAnalysisForRestore());
            result.ElectrostaticsAnalysis.RestoreResult(new(new(2e-6), new(2), new(0)), null, 8, 0, DateTime.UtcNow, ErrorEstimationMethod.None, false);
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            Assert.DoesNotContain(ResultAnalysisViewMode.Salt, workspace.AvailableViewModes);
            var section = (Control)typeof(AnalysisResultWorkspaceControl).GetMethod("BuildAvailabilitySection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(workspace, null)!;
            Assert.Single(section.GetLogicalDescendants().OfType<TextBlock>(), value => value.Text == result.ElectrostaticsAnalysisUnavailableReason);
        }
        finally { AppSettings.MinimumIonSpanForFitting = span; }
    }
}
