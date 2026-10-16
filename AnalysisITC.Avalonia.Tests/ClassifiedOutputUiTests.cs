using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ClassifiedOutputUiTests
{
    [AvaloniaFact]
    public void AnalysisResultExportPickerFailureIsHandledOnce()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var result = CreateResult();
        DataManager.AddData(new ITCDataContainer[] { result.Solution.Solutions[0].Data, result });
        var exporter = new AnalysisResultExporterWindow
        {
            SaveFilePickerForTesting = _ => Task.FromException<IStorageFile?>(new IOException("result picker unavailable"))
        };
        var notifications = new List<HandledException>();
        EventHandler<HandledException> handler = (_, message) => notifications.Add(message);
        AppEventHandler.ShowAppMessage += handler;
        try
        {
            exporter.ExportToFileForTesting().GetAwaiter().GetResult();

            var message = Assert.Single(notifications);
            Assert.Contains("result picker unavailable", message.Message);
            Assert.Contains("result picker unavailable", exporter.StatusTextForTesting);
            Assert.DoesNotContain("exported", exporter.StatusTextForTesting, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppEventHandler.ShowAppMessage -= handler;
            exporter.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    [AvaloniaFact]
    public void BuiltInPurposeSelectorsDefaultToStandardAndRemainIndependentOfRowMode()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var report = new AnalysisReportWindow();
            var exporter = new AnalysisResultExporterWindow();
            try
            {
                var reportPurpose = Field<ComboBox>(report, "outputPurposeCombo");
                var exportPurpose = Field<ComboBox>(exporter, "outputPurposeCombo");
                Assert.Equal(ResultOutputPurpose.Standard, Options<AnalysisReportOptions>(report).OutputPurpose);
                Assert.Equal(ResultOutputPurpose.Standard, Options<AnalysisResultExportOptions>(exporter).OutputPurpose);
                Assert.Equal(2, reportPurpose.ItemCount);
                Assert.Equal(2, exportPurpose.ItemCount);

                reportPurpose.SelectedIndex = 1;
                exportPurpose.SelectedIndex = 1;
                Field<ComboBox>(exporter, "rowModeCombo").SelectedIndex = 1;
                Assert.Equal(ResultOutputPurpose.Diagnostic, Options<AnalysisReportOptions>(report).OutputPurpose);
                Assert.Equal(ResultOutputPurpose.Diagnostic, Options<AnalysisResultExportOptions>(exporter).OutputPurpose);
                Assert.Equal(AnalysisResultExportRowMode.AllRows, Options<AnalysisResultExportOptions>(exporter).RowMode);

                exportPurpose.SelectedIndex = 0;
                Assert.Equal(ResultOutputPurpose.Standard, Options<AnalysisResultExportOptions>(exporter).OutputPurpose);
                Assert.Equal(AnalysisResultExportRowMode.AllRows, Options<AnalysisResultExportOptions>(exporter).RowMode);
            }
            finally
            {
                report.Close();
                exporter.Close();
            }
        });
    }

    [AvaloniaFact]
    public void AssessmentChangeInvalidatesCachedReportWithoutReplacingOriginalFit()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var result = CreateResult();
            DataManager.AddData(new ITCDataContainer[] { result.Solution.Solutions[0].Data, result });
            var window = new AnalysisReportWindow(result);
            try
            {
                var originalSolution = result.Solution;
                var originalDate = result.Date;
                var document = AnalysisReportBuilder.Build(result);
                SetField(window, "currentDocument", document);
                SetField(window, "currentPlan", new SkiaAnalysisReportRenderer().CreatePlan(document));
                SetField(window, "previewStale", false);

                result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);

                Assert.True(Field<bool>(window, "previewStale"));
                Assert.Null(Field<object?>(window, "currentDocument"));
                Assert.Same(originalSolution, result.Solution);
                Assert.Equal(originalDate, result.Date);
                var refreshed = AnalysisReportBuilder.Build(result);
                Assert.Contains(refreshed.Sections.SelectMany(section => section.Blocks)
                    .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
                    item => item.Label == "Binding assessment" && item.Value == "No binding detected (manual)");
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaTheory]
    [InlineData(false, "Partially invalid", "PARTIAL / STALE")]
    [InlineData(true, "Invalid", "INVALID / STALE")]
    public void TemperatureEditUpdatesResultStatusAndReportBadge(bool temperatureDependent, string expectedTitle, string expectedBadge)
    {
        var originalPreferences = PreferencesState.FromSettings();
        AppSettings.MinimumTemperatureSpanForFitting = 3;
        DataManager.Clear(DataClearMode.ResetSession);
        var parts = new[] { CreateResult(25), CreateResult(temperatureDependent ? 35 : 25) };
        var models = parts.Select(part => part.Solution.Solutions[0].Model).ToList();
        var globalModel = new GlobalModel(models);
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel },
            models.Select(model => model.Solution).ToList(), models[0].Solution.Convergence, reconstructBootstrap: false);
        globalModel.Solution = global;
        var result = new AnalysisResult(global);
        DataManager.AddData(models.Select(model => (ITCDataContainer)model.Data).Append(result));
        var workspace = new AnalysisResultWorkspaceControl { Result = result };
        var window = new Window { Content = workspace };
        try
        {
            window.Show();
            models[0].Data.MeasuredTemperature = 30;
            workspace.SetResultViewMode(ResultAnalysisViewMode.Summary);
            workspace.Refresh();
            var labels = workspace.GetLogicalDescendants().OfType<TextBlock>().Select(label => label.Text).ToList();
            Assert.Contains(expectedTitle, labels);
            Assert.Contains(labels, text => text?.EndsWith(": temperature changed.") == true);
            var document = AnalysisReportBuilder.Build(result);
            Assert.True(document.IsValid);
            Assert.Equal(expectedBadge, document.StatusBadgeText);
            var renderer = new SkiaAnalysisReportRenderer();
            var plan = renderer.CreatePlan(document);
            using var bitmap = renderer.RenderPageBitmap(document, plan, 0, 600);
            Assert.True(bitmap.Height > 0);
        }
        finally
        {
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            originalPreferences.ApplyToSettings();
        }
    }

    static AnalysisResult CreateResult(double temperature = 25)
    {
        var data = new ExperimentData("Classified UI fixture")
        {
            CellConcentration = new FloatWithError(20e-6),
            SyringeConcentration = new FloatWithError(400e-6),
            CellVolume = 200e-6,
            MeasuredTemperature = temperature,
        };
        var model = new OneSetOfSites(data);
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8e-10, include: true)
            {
                Ratio = index + 1,
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 5e-6,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
        });
        model.Solution = SolutionInterface.FromModel(model, convergence);
        var globalModel = new GlobalModel(new List<Model> { model })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            new List<SolutionInterface> { model.Solution }, convergence, reconstructBootstrap: false);
        globalModel.Solution = globalSolution;
        return new AnalysisResult(globalSolution);
    }

    static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    static object? Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    static T Options<T>(object target) => (T)Invoke(target, "CurrentOptions")!;
}
