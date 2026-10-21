using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Avalonia.Controls;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class MainWindowPreferencesRefreshTests
{
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

    static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
