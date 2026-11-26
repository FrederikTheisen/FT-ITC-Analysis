using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class MainWindowPreferencesRefreshTests
{
    [AvaloniaFact]
    public async Task OpeningPreferencesTwiceReusesTheDialogUntilItCloses()
    {
        var mainWindow = new MainWindow(false);
        try
        {
            mainWindow.Show();
            var firstOpen = mainWindow.OpenPreferencesAsync();
            var preferences = Assert.IsType<PreferencesWindow>(Assert.Single(mainWindow.OwnedWindows));

            var secondOpen = mainWindow.OpenPreferencesAsync();
            Assert.Same(preferences, Assert.Single(mainWindow.OwnedWindows));
            preferences.Close();
            await Task.WhenAll(firstOpen, secondOpen);

            var reopen = mainWindow.OpenPreferencesAsync();
            var reopened = Assert.IsType<PreferencesWindow>(Assert.Single(mainWindow.OwnedWindows));
            Assert.NotSame(preferences, reopened);
            reopened.Close();
            await reopen;
        }
        finally
        {
            mainWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task ApplyingUnrelatedPreferencePreservesManualFinalFigureChoices()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        MainWindow? mainWindow = null;
        PreferencesWindow? preferences = null;
        try
        {
            var initial = PreferencesState.Defaults();
            initial.FinalFigureWidthCentimeters = 9;
            initial.FitLineSmoothness = LineSmoothness.Smooth;
            initial.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation;
            initial.Apply();

            mainWindow = new MainWindow(false);
            mainWindow.Show();
            var figure = mainWindow.FindControl<FinalFigureWorkspaceControl>("FinalFigureWorkspace")!;
            var width = Field<NumericUpDown>(figure, "widthStepper");
            var smoothness = Field<ComboBox>(figure, "fitLineSmoothnessCombo");
            var uncertainty = Field<ComboBox>(figure, "uncertaintyCombo");

            preferences = new PreferencesWindow();
            preferences.UserNameBox.Text = "Updated operator";
            width.Value = 7;
            smoothness.SelectedIndex = 2;
            uncertainty.SelectedIndex = 3;

            await preferences.ApplyAsync();
            Assert.True(preferences.Applied);
            Assert.Contains(nameof(PreferencesState.UserName), preferences.AppliedChanges.ChangedProperties);

            mainWindow.RefreshAfterPreferencesApplied(preferences.AppliedChanges);

            Assert.Equal(7, width.Value);
            Assert.Equal(2, smoothness.SelectedIndex);
            Assert.Equal(3, uncertainty.SelectedIndex);
        }
        finally
        {
            preferences?.Close();
            mainWindow?.Close();
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [AvaloniaFact]
    public void SwitchingSummaryUncertaintyRefreshesTheOpenResultInBothDirections()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        DataManager.Clear(DataClearMode.ResetSession);
        MainWindow? window = null;
        try
        {
            const UncertaintyDisplayStyle style = UncertaintyDisplayStyle.StandardDeviation;
            var initial = PreferencesState.Defaults();
            initial.UncertaintyDisplayStyle = style;
            initial.Apply();
            var (first, second, result) = AnalysisReportEnhancementTests.CreateReplicateSummaryFixture();
            DataManager.AddData(new ITCDataContainer[] { first, second, result });
            window = new MainWindow(false);
            window.Show();
            var items = window.FindControl<ListBox>("ItemsList")!;
            items.SelectedIndex = -1;
            items.SelectedIndex = DataManager.IndexOfSourceItem(result);
            Dispatcher.UIThread.RunJobs();
            var workspace = window.FindControl<AnalysisResultWorkspaceControl>("ResultWorkspace")!;
            window.UpdateLayout();
            var inspector = Assert.Single(workspace.GetVisualDescendants().OfType<TabControl>());
            inspector.SelectedIndex = 1;
            window.UpdateLayout();
            var rows = Field<StackPanel>(workspace, "evaluationRowsPanel");

            Verify("SEM", Math.Sqrt(3.5));
            ApplyMode(SummaryUncertaintyMode.ReplicateSpread);
            Verify("Replicate SD", Math.Sqrt(7));
            ApplyMode(SummaryUncertaintyMode.Mean);
            Verify("SEM", Math.Sqrt(3.5));

            void ApplyMode(SummaryUncertaintyMode mode)
            {
                var preferences = PreferencesState.FromSettings();
                preferences.SummaryUncertaintyMode = mode;
                var changes = PreferencesChangeSet.Between(PreferencesState.FromSettings(), preferences);
                preferences.Apply();
                window.RefreshAfterPreferencesApplied(changes);
                Dispatcher.UIThread.RunJobs();
            }

            void Verify(string label, double uncertainty)
            {
                window.UpdateLayout();
                Assert.Same(result, DataManager.SelectedResult);
                Assert.Same(result, workspace.Result);
                Assert.True(workspace.IsVisible);
                var row = Assert.Single(rows.Children, child => child.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text?.Contains("∆H", StringComparison.Ordinal) == true));
                var texts = row.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Equal("Enthalpy (∆H)", texts[0]);
                var modeLine = Assert.Single(rows.Children.OfType<TextBlock>());
                Assert.Equal("Summary uncertainty: " + label, modeLine.Text);
                Assert.Contains("fitting uncertainty", Assert.IsType<string>(ToolTip.GetTip(modeLine)));
                var expected = new FloatWithError(11, uncertainty).Energy
                    .ToFormattedString(EnergyUnit.KiloJoule, permole: true, style: style);
                Assert.Equal(expected, texts[1]);
                var lines = Assert.IsType<string>(ToolTip.GetTip(row)).Split(Environment.NewLine);
                var mean = AppSettings.SummaryUncertaintyMode == SummaryUncertaintyMode.Mean;
                Assert.Equal(2, lines.Length);
                Assert.EndsWith(mean ? "kJ/mol (SEM)" : "kJ/mol (SD)", lines[0]);
                Assert.StartsWith(mean ? "95% limits:" : "95% range:", lines[1]);
            }
        }
        finally
        {
            DocumentDirtyTracker.MarkClean();
            window?.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
