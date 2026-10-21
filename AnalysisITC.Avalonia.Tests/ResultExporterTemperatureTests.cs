using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ResultExporterTemperatureTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ControlsDriveClipboardAndFileUnitsWithoutChangingEvaluation(bool allRows, bool tsv)
    {
        var preferences = PreferencesState.FromSettings();
        var savedClipboard = PlatformServices.ClipboardService;
        var clipboard = new RecordingClipboard();
        var directory = Directory.CreateTempSubdirectory("itc154-avalonia-");
        DataManager.Clear(DataClearMode.ResetSession);
        PreferencesState.Defaults().ApplyToSettings();
        AppSettings.ReferenceTemperature = 27;
        var result = CreateResult();
        DataManager.AddData(result.Solution.Solutions.Select(s => (ITCDataContainer)s.Data).Append(result));
        PlatformServices.RegisterClipboardService(clipboard);
        var window = new AnalysisResultExporterWindow();
        try
        {
            var temperature = Field<ComboBox>(window, "temperatureCombo");
            Assert.Equal(new[] { "Celsius", "Kelvin" }, temperature.Items.Cast<object>().Select(x => x.ToString()));
            Assert.Equal(0, temperature.SelectedIndex);
            Assert.Equal(AnalysisResultExportPresentation.TemperatureToolTip, ToolTip.GetTip(temperature));
            Field<ComboBox>(window, "rowModeCombo").SelectedIndex = allRows ? 1 : 0;
            Field<ComboBox>(window, "formatCombo").SelectedIndex = tsv ? 1 : 0;
            Field<ComboBox>(window, "errorStyleCombo").SelectedIndex = 1;
            var copy = window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Copy"));
            var originalSolution = result.Solution;
            var originalValues = result.Solution.Solutions.Select(s => s.Parameters[ParameterType.Affinity1].Value).ToArray();
            string? celsius = null;
            foreach (var index in new[] { 0, 1, 0 })
            {
                temperature.SelectedIndex = index;
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var text = clipboard.Value;
                var rows = Table(text, tsv);
                var label = (allRows ? "Temperature" : "Evaluation temperature") + (index == 1 ? " (K)" : " (°C)");
                var column = Array.IndexOf(rows[0], label);
                Assert.True(column >= 0, text);
                Assert.Equal(allRows ? new[] { index == 1 ? "293.2" : "20.0", index == 1 ? "303.2" : "30.0" }
                    : new[] { index == 1 ? "300.2" : "27.0" }, rows.Skip(1).Select(row => row[column]));
                if (celsius == null) celsius = text;
                AssertOnlyTemperatureChanges(Table(celsius, tsv), rows, column);

                var path = Path.Combine(directory.FullName, "results." + (tsv ? "tsv" : "csv"));
                File.WriteAllText(path, "Previous export");
                var lookup = window.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
                Assert.True(lookup.IsCompleted);
#pragma warning disable xUnit1031
                using var file = lookup.GetAwaiter().GetResult();
                Assert.NotNull(file);
                window.SaveFilePickerForTesting = options =>
                {
                    Assert.Equal("analysis_results." + (tsv ? "tsv" : "csv"), options.SuggestedFileName);
                    return Task.FromResult<IStorageFile?>(file);
                };
                var operation = window.ExportToFileForTesting();
                var timer = Stopwatch.StartNew();
                while (!operation.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Yield();
                }
                Assert.True(operation.IsCompleted);
                operation.GetAwaiter().GetResult();
#pragma warning restore xUnit1031
                Assert.Equal(text, File.ReadAllText(path));
                Assert.Equal("Analysis result table exported.", window.StatusTextForTesting);
                Assert.Same(originalSolution, result.Solution);
                Assert.Equal(new[] { 20.0, 30.0 }, result.Solution.Solutions.Select(s => s.Temp));
                Assert.Equal(originalValues, result.Solution.Solutions.Select(s => s.Parameters[ParameterType.Affinity1].Value));
                Assert.Equal(27, AppSettings.ReferenceTemperature);
            }
        }
        finally
        {
            window.Close();
            PlatformServices.RegisterClipboardService(savedClipboard);
            DataManager.Clear(DataClearMode.ResetSession);
            preferences.ApplyToSettings();
            directory.Delete(true);
        }
    }

    static string[][] Table(string value, bool tsv) => value.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
        .Select(row => row.Split(tsv ? '\t' : ',')).ToArray();

    static void AssertOnlyTemperatureChanges(string[][] before, string[][] after, int column)
    {
        Assert.Equal(before.Length, after.Length);
        for (var row = 0; row < before.Length; row++)
            Assert.Equal(before[row].Where((_, i) => i != column), after[row].Where((_, i) => i != column));
    }

    internal static AnalysisResult CreateResult()
    {
        var models = new List<Model>();
        foreach (var temperature in new[] { 20.0, 30.0 })
        {
            var data = new ExperimentData("temperature-export-" + temperature)
            {
                CellConcentration = new FloatWithError(20e-6), SyringeConcentration = new FloatWithError(400e-6),
                CellVolume = 200e-6, MeasuredTemperature = temperature, TargetTemperature = temperature,
            };
            for (var i = 0; i < 3; i++)
            {
                var injection = new InjectionData(data, i, 2e-6, 8e-10, include: true)
                {
                    Ratio = i + 1, ActualCellConcentration = 20e-6, ActualTitrantConcentration = (i + 1) * 5e-6,
                };
                injection.SetPeakArea(new FloatWithError(-2e-6 + i * 1e-7));
                data.Injections.Add(injection);
            }
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            models.Add(model);
        }
        var global = new GlobalModel(models);
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(m => m.Solution).ToList(),
            models[0].Solution.Convergence, reconstructBootstrap: false);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }

    static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    sealed class RecordingClipboard : IClipboardService
    {
        public string Value { get; private set; } = "";
        public void SetString(string value) => Value = value;
    }
}
