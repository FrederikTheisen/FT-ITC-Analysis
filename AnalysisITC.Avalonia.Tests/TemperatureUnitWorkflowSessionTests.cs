using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia.Controls;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class TemperatureUnitWorkflowSessionTests
{
    [AvaloniaFact]
    public void ResultsAndExporterChoicesSurviveReconstructionAndRemainIndependent()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        Window? resultsHost = null;
        AnalysisResultExporterWindow? firstExporter = null;
        AnalysisResultExporterWindow? reopenedExporter = null;
        try
        {
            var defaults = PreferencesState.Defaults();
            defaults.DefaultTemperatureUnit = TemperatureUnit.Celsius;
            defaults.Apply();
            TemperatureUnitSession.Reset(TemperatureUnit.Celsius);

            var results = new AnalysisResultWorkspaceControl();
            resultsHost = new Window { Content = results };
            resultsHost.Show();
            results.SetTemperatureDisplay(kelvin: true);

            firstExporter = new AnalysisResultExporterWindow();
            Assert.Equal(0, Field<ComboBox>(firstExporter, "temperatureCombo").SelectedIndex);
            Field<ComboBox>(firstExporter, "temperatureCombo").SelectedIndex = 1;
            reopenedExporter = new AnalysisResultExporterWindow();

            var reopenedResults = new AnalysisResultWorkspaceControl();
            Assert.Equal(1, Field<ComboBox>(reopenedResults, "temperatureUnitCombo").SelectedIndex);
            Assert.Equal(1, Field<ComboBox>(reopenedExporter, "temperatureCombo").SelectedIndex);

            firstExporter.Close();
            reopenedExporter.Close();
            firstExporter = null;
            reopenedExporter = null;
            resultsHost.Close();
            resultsHost = null;

            var afterReopen = new AnalysisResultWorkspaceControl();
            Assert.Equal(1, Field<ComboBox>(afterReopen, "temperatureUnitCombo").SelectedIndex);
        }
        finally
        {
            firstExporter?.Close();
            reopenedExporter?.Close();
            resultsHost?.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            TemperatureUnitSession.Reset(original.DefaultTemperatureUnit);
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [AvaloniaFact]
    public void ChangingSavedDefaultResetsBothOpenWorkflowsAndPreservesEvaluationTemperature()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        Window? resultsHost = null;
        AnalysisResultExporterWindow? exporter = null;
        try
        {
            var defaults = PreferencesState.Defaults();
            defaults.DefaultTemperatureUnit = TemperatureUnit.Celsius;
            defaults.Apply();
            AppSettings.DefaultTemperatureUnit = TemperatureUnit.Celsius;
            AppSettings.Save();
            TemperatureUnitSession.Reset(TemperatureUnit.Celsius);

            var workspace = new AnalysisResultWorkspaceControl
            {
                Result = ResultExporterTemperatureTests.CreateResult()
            };
            resultsHost = new Window { Content = workspace };
            resultsHost.Show();
            exporter = new AnalysisResultExporterWindow();
            exporter.Show();

            var evaluationTemperature = Field<TextBox>(workspace, "evaluationTemperatureBox");
            evaluationTemperature.Text = "31.123456789";
            AppSettings.DefaultTemperatureUnit = TemperatureUnit.Kelvin;
            AppSettings.Save();

            Assert.Equal(1, Field<ComboBox>(workspace, "temperatureUnitCombo").SelectedIndex);
            Assert.Equal(1, Field<ComboBox>(exporter, "temperatureCombo").SelectedIndex);
            Assert.Equal(304.273456789, ReadDisplayed(evaluationTemperature), 9);

            AppSettings.DefaultTemperatureUnit = TemperatureUnit.Celsius;
            AppSettings.Save();

            Assert.Equal(0, Field<ComboBox>(workspace, "temperatureUnitCombo").SelectedIndex);
            Assert.Equal(0, Field<ComboBox>(exporter, "temperatureCombo").SelectedIndex);
            Assert.Equal(31.123456789, ReadDisplayed(evaluationTemperature), 9);
        }
        finally
        {
            exporter?.Close();
            resultsHost?.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            TemperatureUnitSession.Reset(original.DefaultTemperatureUnit);
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [AvaloniaFact]
    public async Task UnrelatedPreferenceAndNoOpSavePreserveSessionChoicesAndManualFigureOverrides()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        Window? resultsHost = null;
        Window? figureHost = null;
        AnalysisResultExporterWindow? exporter = null;
        PreferencesWindow? preferences = null;
        try
        {
            var defaults = PreferencesState.Defaults();
            defaults.DefaultTemperatureUnit = TemperatureUnit.Celsius;
            defaults.Apply();
            AppSettings.DefaultTemperatureUnit = TemperatureUnit.Celsius;
            AppSettings.Save();

            TemperatureUnitSession.Results = TemperatureUnit.Kelvin;
            TemperatureUnitSession.ResultExporter = TemperatureUnit.Celsius;
            var workspace = new AnalysisResultWorkspaceControl();
            var resultTemperature = Field<ComboBox>(workspace, "temperatureUnitCombo");
            resultsHost = new Window { Content = workspace };
            resultsHost.Show();
            exporter = new AnalysisResultExporterWindow();
            exporter.Show();

            var figure = new AnalysisITC.Avalonia.FinalFigure.FinalFigureWorkspaceControl();
            figureHost = new Window { Content = figure };
            figureHost.Show();
            var details = Field<CheckBox>(figure, "experimentDetailsCheck");
            details.IsChecked = !(details.IsChecked ?? false);
            var manualFigureValue = details.IsChecked;

            preferences = new PreferencesWindow();
            Field<TextBox>(preferences, "userNameBox").Text = "Unrelated preference";
            await preferences.ApplyAsync();
            preferences = null;

            Assert.Equal(1, resultTemperature.SelectedIndex);
            Assert.Equal(0, Field<ComboBox>(exporter, "temperatureCombo").SelectedIndex);
            Assert.Equal(manualFigureValue, details.IsChecked);

            var noOp = PreferencesState.FromSettings();
            noOp.Apply();
            Assert.Equal(TemperatureUnit.Kelvin, TemperatureUnitSession.Results);
            Assert.Equal(TemperatureUnit.Celsius, TemperatureUnitSession.ResultExporter);
            Assert.Equal(manualFigureValue, details.IsChecked);
        }
        finally
        {
            preferences?.Close();
            exporter?.Close();
            resultsHost?.Close();
            figureHost?.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            TemperatureUnitSession.Reset(original.DefaultTemperatureUnit);
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    static double ReadDisplayed(TextBox textBox)
        => double.Parse(textBox.Text!, NumberStyles.Float, CultureInfo.CurrentCulture);

    static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
