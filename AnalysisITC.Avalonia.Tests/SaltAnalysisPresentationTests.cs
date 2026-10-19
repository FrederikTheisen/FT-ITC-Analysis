using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SaltAnalysisPresentationTests
{
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
        CheckRow("Counter ion", counter, result.ElectrostaticsAnalysis.CounterIonUnavailableReason);
        void CheckRow(string label, bool available, string missingReason)
        {
            var heading = text.Single(value => value.Text == label);
            var grid = (Grid)heading.Parent!;
            var value = grid.Children.OfType<TextBlock>().Last();
            Assert.Equal(!available, value.Text == "Unavailable");
            if (!available) Assert.Equal(missingReason, ToolTip.GetTip(value));
            else if (label == "Kd0") Assert.Equal(ElectrostaticsAnalysisAvailability.Kd0ToolTip, ToolTip.GetTip(value));
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
