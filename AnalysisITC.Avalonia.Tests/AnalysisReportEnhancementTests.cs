using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Avalonia.Controls;
using Avalonia.Threading;

using SkiaSharp;

using Xunit;

using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisReportEnhancementTests : IDisposable
{
    readonly PreferencesState originalPreferences = PreferencesState.FromSettings();

    public AnalysisReportEnhancementTests() => AvaloniaTestBootstrap.EnsureInitialized();

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        originalPreferences.ApplyToSettings();
    }

    [Fact]
    public void UserNamePreferenceIsLoadedAndStagedBeforeApplying()
    {
        var state = PreferencesState.Defaults();
        state.UserName = "Zoë Sørensen";
        var originalName = AppSettings.UserName;
        var window = new PreferencesWindow();
        window.LoadState(state);
        var field = (TextBox)typeof(PreferencesWindow).GetField("userNameBox",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Equal("Zoë Sørensen", field.Text);
        field.Text = "Report generator β";
        Assert.True(window.TryBuildState(out var edited));
        Assert.Equal("Report generator β", edited.UserName);
        Assert.Equal(originalName, AppSettings.UserName);
    }

    [Fact]
    public void OpeningReportForAResultDoesNotRegisterAnUntouchedDraft()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            ResetData();
            var fixture = CreateResult("No-op result");
            DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
            DocumentDirtyTracker.MarkClean();

            _ = new AnalysisReportWindow(fixture.Result);

            Assert.Empty(DataManager.Reports);
            Assert.False(DocumentDirtyTracker.IsDirty);
        });
    }

    [Fact]
    public void ReportSettingsStayWithTheirResultAcrossAtoBtoASelectionChanges()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            ResetData();
            var a = CreateResult("Result A");
            var b = CreateResult("Result B");
            DataManager.AddData(new ITCDataContainer[] { a.Data, a.Result, b.Data, b.Result });
            var window = new AnalysisReportWindow(a.Result);
            var label = Field<TextBox>(window, "labelBox");
            var title = Field<TextBox>(window, "titleBox");
            var energy = Field<ComboBox>(window, "energyCombo");
            var temperature = Field<ComboBox>(window, "temperatureCombo");
            var uncertainty = Field<ComboBox>(window, "uncertaintyCombo");
            var injections = Field<CheckBox>(window, "injectionTablesCheck");
            var condense = Field<CheckBox>(window, "condenseRepeatedCheck");
            var traceability = Field<CheckBox>(window, "extraTraceabilityCheck");
            var advanced = Field<List<CheckBox>>(window, "advancedChecks");
            var expectedAAdvanced = advanced.Where(check => check.IsChecked == true)
                .Select(check => ((AnalysisReportAdvancedSectionDescriptor)check.Tag!).Request.Key).ToArray();
            var appEnergyIndex = AppSettings.EnergyUnitFamily == EnergyUnitFamily.Joules ? 0 : 1;
            var alternateEnergyIndex = appEnergyIndex == 0 ? 1 : 0;

            label.Text = "A subtitle — λ-binding study";
            title.Text = "A custom title";
            energy.SelectedIndex = alternateEnergyIndex;
            temperature.SelectedIndex = 1;
            uncertainty.SelectedIndex = 2;
            injections.IsChecked = false;
            condense.IsChecked = false;
            traceability.IsChecked = true;
            DocumentDirtyTracker.MarkClean();

            SelectResult(window, b.Result);
            Assert.Equal("", label.Text);
            Assert.Equal("Result B", title.Text);
            Assert.True(FieldValue<bool>(window, "automaticTitle"));
            Assert.Equal(appEnergyIndex, energy.SelectedIndex);
            Assert.Equal(0, temperature.SelectedIndex);
            Assert.Equal(3, uncertainty.SelectedIndex);
            Assert.True(injections.IsChecked);
            Assert.True(condense.IsChecked);
            Assert.False(traceability.IsChecked);
            Assert.Equal(AnalysisReportBuilder.GetAvailableAdvancedSections(b.Result).Select(item => item.Request.Key),
                advanced.Where(check => check.IsChecked == true)
                    .Select(check => ((AnalysisReportAdvancedSectionDescriptor)check.Tag!).Request.Key));

            SelectResult(window, a.Result);
            Assert.Equal("A subtitle — λ-binding study", label.Text);
            Assert.Equal("A custom title", title.Text);
            Assert.False(FieldValue<bool>(window, "automaticTitle"));
            Assert.Equal(alternateEnergyIndex, energy.SelectedIndex);
            Assert.Equal(1, temperature.SelectedIndex);
            Assert.Equal(2, uncertainty.SelectedIndex);
            Assert.False(injections.IsChecked);
            Assert.False(condense.IsChecked);
            Assert.True(traceability.IsChecked);
            Assert.Equal(expectedAAdvanced, advanced.Where(check => check.IsChecked == true)
                .Select(check => ((AnalysisReportAdvancedSectionDescriptor)check.Tag!).Request.Key));

            // Editing B cannot overwrite A's custom title or subtitle.
            SelectResult(window, b.Result);
            label.Text = "B subtitle";
            title.Text = "B custom title";
            SelectResult(window, a.Result);
            Assert.Equal("A subtitle — λ-binding study", label.Text);
            Assert.Equal("A custom title", title.Text);
        });
    }

    [Fact]
    public void SavedSettingsBuildWithCurrentAuthorTimestampAndReportIdentity()
    {
        var fixture = CreateResult("Metadata result");
        var report = new AnalysisReport { Name = "Metadata report" };
        report.SetResultIds(new[] { fixture.Result.UniqueID });
        report.UpdatePresentationSettings(new AnalysisReportOptions
        {
            AutomaticTitle = false,
            Title = "Persistent report title",
            DocumentLabel = "Saved subtitle",
        });
        AppSettings.UserName = "Researcher Ω";
        var before = DateTime.UtcNow;

        var document = AnalysisReportBuilder.Build(report,
            id => id == fixture.Result.UniqueID ? fixture.Result : null,
            _ => null);

        var after = DateTime.UtcNow;
        Assert.Equal("Persistent report title", document.Title);
        Assert.Equal("Saved subtitle", document.DocumentLabel);
        Assert.Equal("Researcher Ω", document.Author);
        Assert.Equal(report.UniqueID, document.ReportId);
        Assert.InRange(document.GeneratedAtUtc, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.False(string.IsNullOrWhiteSpace(document.ApplicationVersion));
    }

    [Fact]
    public void PdfMetadataKeepsAuthorSeparateFromApplicationCreator()
    {
        var fixture = CreateResult("Metadata result");
        var document = AnalysisReportBuilder.Build(fixture.Result, new AnalysisReportOptions
        {
            Title = "PDF metadata check",
            Author = "Researcher A",
            ApplicationVersion = "test-version",
            GeneratedAtUtc = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc),
        });
        var renderer = new SkiaAnalysisReportRenderer();
        using var pdf = new MemoryStream();
        renderer.WritePdf(document, renderer.CreatePlan(document), pdf);
        var content = Encoding.Latin1.GetString(pdf.ToArray());

        Assert.Contains("/Author", content, StringComparison.Ordinal);
        Assert.Contains("Researcher A", content, StringComparison.Ordinal);
        Assert.Contains("/Creator", content, StringComparison.Ordinal);
        Assert.Contains("FT-ITC Analysis test-version", content, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalCoverQaArtifactsRenderLongMultiResultTitlesAndTraceabilityModes()
    {
        var output = Environment.GetEnvironmentVariable("FTITC_REPORT_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;

        Directory.CreateDirectory(output);
        var first = CreateResult("A very long analysis result name with Unicode λ and β");
        var second = CreateResult("Second result name with an extended scientific description");
        var report = new AnalysisReport { Name = "Cover visual QA" };
        report.SetResultIds(new[] { first.Result.UniqueID, second.Result.UniqueID });
        var renderer = new SkiaAnalysisReportRenderer();
        foreach (var traceability in new[] { false, true })
        {
            var options = new AnalysisReportOptions
            {
                Title = "A long custom report title for cover layout inspection",
                DocumentLabel = "Subtitle for visual inspection — multi-result presentation, Unicode λ β, and wrapped content",
                Author = "Research collaborator with a deliberately long display name",
                ExtraTraceability = traceability,
            };
            var document = AnalysisReportBuilder.Build(report,
                id => id == first.Result.UniqueID ? first.Result : id == second.Result.UniqueID ? second.Result : null,
                _ => null, options);
            var suffix = traceability ? "traceability-on" : "traceability-off";
            var plan = renderer.CreatePlan(document);
            renderer.WritePdf(document, plan, Path.Combine(output, suffix + ".pdf"));
            using var bitmap = renderer.RenderPageBitmap(document, plan, 0, 1200);
            using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(output, suffix + ".png"));
            image.SaveTo(file);
        }
    }

    static void ResetData()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
        DocumentDirtyTracker.MarkClean();
    }

    static void SelectResult(AnalysisReportWindow window, AnalysisResult result)
    {
        typeof(AnalysisReportWindow).GetMethod("ApplyResultSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, new object[] { new[] { result }, true, Array.Empty<ExperimentData>() });
    }

    static T Field<T>(AnalysisReportWindow window, string name) where T : class =>
        (T)typeof(AnalysisReportWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    static T FieldValue<T>(AnalysisReportWindow window, string name) =>
        (T)typeof(AnalysisReportWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    static (ExperimentData Data, AnalysisResult Result) CreateResult(string name)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name + " experiment",
            CellConcentration = new FloatWithError(20e-6),
            SyringeConcentration = new FloatWithError(400e-6),
            CellVolume = 200e-6,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
            Date = new DateTime(2026, 8, 1),
        };
        data.DataPoints.Add(new DataPoint(0, 0, 25));
        data.DataPoints.Add(new DataPoint(60, -1e-6f, 25));
        data.DataPoints.Add(new DataPoint(120, 0, 25));
        data.BaseLineCorrectedDataPoints = data.DataPoints.ToList();
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8e-10, include: true)
            {
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = index + 1,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 1,
            Iterations = 1,
        });
        model.Solution = SolutionInterface.FromModel(model, convergence);
        data.UpdateSolution(model);

        var globalModel = new GlobalModel(new List<Model> { model })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            new List<SolutionInterface> { model.Solution }, convergence, reconstructBootstrap: false);
        globalModel.Solution = globalSolution;
        var result = new AnalysisResult(globalSolution) { Name = name, Date = new DateTime(2026, 8, 30) };
        return (data, result);
    }
}
