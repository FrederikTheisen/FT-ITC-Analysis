using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
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
    public void LongSourceFilenameAndFormatWrapCompletelyInPreview()
    {
        var fixture = CreateResult("Source metadata QA");
        var fileName = string.Concat(Enumerable.Repeat("long-source-name-", 12)) + ".itc";
        fixture.Data.SetFileName(fileName);
        fixture.Data.DataSourceFormat = AnalysisITC.Core.DataReaders.ITCDataFormat.ITC200;
        var document = AnalysisReportBuilder.Build(fixture.Result);
        var metadata = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        var source = Assert.Single(metadata.Items, item => item.Label == "Source file");
        Assert.Equal(fileName + " (MicroCal ITC Data File)", source.Value);
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);
        var page = plan.Pages.Select((value, index) => (value, index)).Single(entry =>
            entry.value.Fragments.Any(fragment => ReferenceEquals(fragment.Block, metadata)
                && metadata.Items.Skip(fragment.FirstItem).Take(fragment.ItemCount).Contains(source)));
        var fragment = page.value.Fragments.Single(item => ReferenceEquals(item.Block, metadata)
            && metadata.Items.Skip(item.FirstItem).Take(item.ItemCount).Contains(source));
        var lines = (List<string>)typeof(SkiaAnalysisReportRenderer)
            .GetMethod("Wrap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer,
                new object[] { source.Value, (float)(fragment.Bounds.Width * .70 - 6), 9f, false })!;
        Assert.True(lines.Count > 1);
        Assert.Equal(source.Value.Replace(" ", ""), string.Concat(lines).Replace(" ", ""));
        Assert.True(fragment.Bounds.Height >= lines.Count * 12 + 6);
        using var bitmap = renderer.RenderPageBitmap(document, plan, page.index, 1600);
        Assert.True(bitmap.Height > 0);
        var output = Environment.GetEnvironmentVariable("FTITC_REPORT_SOURCE_QA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(output, "avalonia-source.png"));
            png.SaveTo(stream);
        }
    }

    [Fact]
    public void ClassifiedReportRenderQaWritesNegativeMixedAndDiagnosticPagesWhenRequested()
    {
        var prefix = Environment.GetEnvironmentVariable("FTITC_CLASSIFIED_REPORT_QA_PREFIX");

        var negative = CreateResult("QA no binding").Result;
        negative.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        SetNullComparison(negative, new NullModelComparison
        {
            BindingFitSucceeded = true,
            NullFitSucceeded = true,
            DeltaAicc = 1,
            BindingInformationCriteria = QaCriteria(10),
            NullInformationCriteria = QaCriteria(11),
            Members = negative.Solution.Solutions.Select(solution => new NullModelComparisonMember
            {
                ExperimentId = solution.Data.UniqueID,
                Offset = 1234,
                Points = solution.Data.Injections.Select(injection => new NullModelComparisonPoint
                {
                    InjectionId = injection.ID,
                    Ratio = injection.Ratio,
                    InjectionMass = injection.InjectionMass,
                    ObservedHeatJoules = injection.PeakArea.Value,
                    PredictedHeatJoules = 1234 * injection.InjectionMass,
                    Included = injection.Include,
                }).ToList(),
            }).ToList(),
        });
        var missingNull = CreateResult("QA no saved null").Result;
        missingNull.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        var positive = CreateResult("QA binding detected").Result;
        positive.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        var renderer = new SkiaAnalysisReportRenderer();
        var cases = new[]
        {
            ("negative", AnalysisReportBuilder.Build(negative, new AnalysisReportOptions())),
            ("mixed", AnalysisReportBuilder.Build(new[] { missingNull, positive }, new AnalysisReportOptions())),
            ("diagnostic", AnalysisReportBuilder.Build(missingNull, new AnalysisReportOptions
                { OutputPurpose = ResultOutputPurpose.Diagnostic })),
        };

        foreach (var (name, document) in cases)
        {
            var plan = renderer.CreatePlan(document);
            Assert.NotEmpty(plan.Pages);
            var comparisonBlock = document.Sections.SelectMany(section => section.Blocks)
                .OfType<AnalysisReportKeyValueBlock>()
                .FirstOrDefault(block => block.Items.Any(item => item.Label == "ΔAICc"));
            Assert.NotNull(comparisonBlock);
            if (name == "negative")
            {
                string F1(double value) => value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Contains(comparisonBlock!.Items, item => item.Label == "ΔAICc"
                    && item.Value == "+" + F1(1) + " (binding " + F1(10) + ", null " + F1(11) + ")");
                Assert.DoesNotContain(comparisonBlock.Items, item => item.Label is "Binding AICc" or "Null AICc");
            }
            using var bitmap = renderer.RenderPageBitmap(document, plan, 0, 1200);
            Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
            if (string.IsNullOrWhiteSpace(prefix)) continue;
            using (var pdf = File.Create(prefix + "-" + name + ".pdf"))
                renderer.WritePdf(document, plan, pdf);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(prefix + "-" + name + ".png");
            png.SaveTo(output);
        }
    }

    static void SetNullComparison(AnalysisResult result, NullModelComparison comparison) =>
        typeof(AnalysisResult).GetProperty(nameof(AnalysisResult.NullComparison), BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(true)!.Invoke(result, new object[] { comparison });

    static FitInformationCriteria QaCriteria(double aicc) => FitInformationCriteria.Restore(
        observationCount: 3, fittedParameterCount: 1, likelihoodParameterCount: 2,
        likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance,
        minusTwoLogLikelihood: aicc - 3, aic: aicc - 1, aicc: aicc,
        isAicAvailable: true, isAiccAvailable: true,
        aicUnavailableReason: "", aiccUnavailableReason: "",
        rawResidualSumOfSquares: 1e-12, residualRmsdMicrojoules: 1,
        standardizedResidualSumOfSquares: 1, logSigmaSquaredSum: 0);

    [Fact]
    public void ExportPreparationRefreshesCachedPreviewWhenPreparerChanges()
    {
        var fixture = CreateResult("Export preview");
        var previousOperator = AppSettings.UserName;
        var originalContext = SynchronizationContext.Current;
        Task<bool>? preparation = null;
        AnalysisReportDocument? preview = null;
        string? displayedGenerationTime = null;
        AnalysisReportWindow? window = null;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            AppSettings.UserName = "Operator A";
            Dispatcher.UIThread.Invoke(() =>
            {
                ResetData();
                DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
                DocumentDirtyTracker.MarkClean();
                window = new AnalysisReportWindow(fixture.Result);
                preview = AnalysisReportBuilder.Build(fixture.Result, new AnalysisReportOptions
                {
                    Author = AppSettings.UserName,
                    GeneratedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                });
                displayedGenerationTime = preview.ExportDateText;
                var renderer = new SkiaAnalysisReportRenderer();
                var previewPlan = renderer.CreatePlan(preview);
                typeof(AnalysisReportWindow).GetField("currentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, preview);
                typeof(AnalysisReportWindow).GetField("currentPlan", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, previewPlan);
                typeof(AnalysisReportWindow).GetField("previewStale", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, false);

                AppSettings.UserName = "Operator B";
                var ensure = typeof(AnalysisReportWindow).GetMethod("EnsureExportDocumentAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                preparation = (Task<bool>)ensure.Invoke(window, null)!;
            });
            PumpUntilCompleted(preparation!);
            Dispatcher.UIThread.Invoke(() =>
            {
                Assert.True(preparation!.GetAwaiter().GetResult());
                var rebuilt = FieldValue<AnalysisReportDocument>(window!, "currentDocument");
                Assert.NotSame(preview, rebuilt);
                Assert.NotEqual(displayedGenerationTime, rebuilt.ExportDateText);
                Assert.Equal("Operator B", rebuilt.Author);
                Assert.Equal("Operator A", preview!.Author);
                window!.Close();
            });
        }
        finally
        {
            if (window != null) Dispatcher.UIThread.Invoke(window.Close);
            AppSettings.UserName = previousOperator;
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void TraceabilityModeShowsReportIdAndPreservesItWhenDisabledAndReopened()
    {
        var fixture = CreateResult("Traceability report ID");
        Dispatcher.UIThread.Invoke(() =>
        {
            ResetData();
            DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
            DocumentDirtyTracker.MarkClean();
            var window = new AnalysisReportWindow(fixture.Result);
            var id = Field<TextBox>(window, "reportIdBox");
            var idRow = Field<Control>(window, "reportIdRow");
            var applyState = typeof(AnalysisReportWindow).GetMethod("ApplyTraceabilityCheckboxState",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var currentOptions = typeof(AnalysisReportWindow).GetMethod("CurrentOptions",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var priorMode = AppSettings.TraceabilityModeEnabled;
            try
            {
                AppSettings.TraceabilityModeEnabled = true;
                applyState.Invoke(window, null);
                Assert.True(idRow.IsVisible);
                Assert.True(id.IsEnabled);
                id.Text = "  QA-2026-α  ";
                var options = (AnalysisReportOptions)currentOptions.Invoke(window, null)!;
                Assert.Equal("QA-2026-α", options.ReportId);
                var savedReport = Assert.Single(DataManager.Reports);
                Assert.Equal("QA-2026-α", savedReport.PresentationSettings.ReportId);

                AppSettings.TraceabilityModeEnabled = false;
                applyState.Invoke(window, null);
                Assert.False(idRow.IsVisible);
                Assert.Equal("QA-2026-α", savedReport.PresentationSettings.ReportId);

                var reopened = new AnalysisReportWindow(fixture.Result);
                try { Assert.Equal("QA-2026-α", Field<TextBox>(reopened, "reportIdBox").Text); }
                finally { reopened.Close(); }
            }
            finally
            {
                AppSettings.TraceabilityModeEnabled = priorMode;
                window.Close();
            }
        });
    }

    [Fact]
    public void RepeatedEditAfterPreviewBuildsOneNewSnapshotThenReusesIt()
    {
        var fixture = CreateResult("Snapshot rebuild");
        var previousOperator = AppSettings.UserName;
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            AppSettings.UserName = "Operator A";
            var window = Dispatcher.UIThread.Invoke(() => new AnalysisReportWindow());
            Task<bool>? preparation = null;
            AnalysisReportDocument? preview = null;
            Dispatcher.UIThread.Invoke(() =>
            {
                FieldValue<List<AnalysisResult>>(window, "selectedResults").Add(fixture.Result);
                typeof(AnalysisReportWindow).GetMethod("ObserveSourceChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
                fixture.Result.Name = "Edited before preview";
                preview = AnalysisReportBuilder.Build(fixture.Result, new AnalysisReportOptions
                {
                    Author = AppSettings.UserName,
                    GeneratedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                });
                var renderer = new SkiaAnalysisReportRenderer();
                typeof(AnalysisReportWindow).GetField("currentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, preview);
                typeof(AnalysisReportWindow).GetField("currentPlan", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, renderer.CreatePlan(preview));
                typeof(AnalysisReportWindow).GetField("previewStale", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, false);

                fixture.Result.Name = "Edited after preview";
                Assert.True(FieldValue<bool>(window, "previewStale"));
                AppSettings.UserName = "Operator B";
                var ensure = typeof(AnalysisReportWindow).GetMethod("EnsureExportDocumentAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                preparation = (Task<bool>)ensure.Invoke(window, null)!;
            });
            PumpUntilCompleted(preparation!);
            Dispatcher.UIThread.Invoke(() =>
            {
                Assert.True(preparation!.GetAwaiter().GetResult());
                var rebuilt = FieldValue<AnalysisReportDocument>(window, "currentDocument");
                Assert.NotSame(preview, rebuilt);
                Assert.Equal("Operator B", rebuilt.Author);

                var ensure = typeof(AnalysisReportWindow).GetMethod("EnsureExportDocumentAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var secondPreparation = (Task<bool>)ensure.Invoke(window, null)!;
                Assert.True(secondPreparation.IsCompleted);
                Assert.True(secondPreparation.GetAwaiter().GetResult());
                Assert.Same(rebuilt, FieldValue<AnalysisReportDocument>(window, "currentDocument"));
            });
        }
        finally
        {
            AppSettings.UserName = previousOperator;
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    static void PumpUntilCompleted(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }
        Assert.True(task.IsCompleted, "Avalonia UI work should complete within 10 seconds.");
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
            ReportId = "QA-2026-43",
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
        Assert.Equal("QA-2026-43", document.ReportId);
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
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        AppSettings.UserName = "Analysis operator Zoë";
        var first = CreateResult("A very long analysis result name with Unicode λ and β");
        var second = CreateResult("Second result name with an extended scientific description");
        first.Data.ExternalExperimentId = "Study-β-" + new string('0', 120);
        first.Data.CellSampleId = "Cell-α/batch-0007-" + new string('A', 90);
        first.Data.SyringeSampleId = "Syringe-β/batch-0009-" + new string('B', 90);
        second.Data.CellSampleId = first.Data.CellSampleId;
        second.Data.SyringeSampleId = first.Data.SyringeSampleId;
        var report = new AnalysisReport { Name = "Cover visual QA" };
        var renderer = new SkiaAnalysisReportRenderer();
        foreach (var resultCount in new[] { 1, 2 })
        foreach (var traceability in new[] { false, true })
        {
            report.SetResultIds(new[] { first.Result.UniqueID, second.Result.UniqueID }.Take(resultCount));
            var options = new AnalysisReportOptions
            {
                Title = "A long custom report title for cover layout inspection",
                DocumentLabel = "Subtitle for visual inspection — multi-result presentation, Unicode λ β, and wrapped content",
                Author = "Research collaborator Zoë with a deliberately long display name",
                ReportId = "Study-β-" + new string('0', 120),
                ExtraTraceability = traceability,
            };
            var document = AnalysisReportBuilder.Build(report,
                id => id == first.Result.UniqueID ? first.Result : id == second.Result.UniqueID ? second.Result : null,
                _ => null, options);
            var suffix = (resultCount == 1 ? "single-" : "") + (traceability ? "traceability-on" : "traceability-off");
            var plan = renderer.CreatePlan(document);
            var signOffs = plan.Pages.SelectMany(page => page.Fragments)
                .Where(fragment => fragment.Kind == AnalysisReportFragmentKind.SignOff).ToList();
            Assert.Equal(traceability ? 1 : 0, signOffs.Count);
            if (traceability)
            {
                var signOff = Assert.Single(signOffs);
                Assert.Contains(signOff, plan.Pages[0].Fragments);
                Assert.Equal(plan.PageHeight - plan.MarginBottom - 18, signOff.Bounds.Bottom, 6);
                Assert.All(plan.Pages[0].Fragments.Where(fragment => fragment != signOff),
                    fragment => Assert.True(fragment.Bounds.Bottom <= signOff.Bounds.Y - 9 + 1e-6));
            }
            using (var bitmap = renderer.RenderPageBitmap(document, plan, 0, 1200))
                Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
            using (var pdf = new MemoryStream())
            {
                renderer.WritePdf(document, plan, pdf);
                Assert.True(pdf.Length > 0);
            }
            if (string.IsNullOrWhiteSpace(output)) continue;
            renderer.WritePdf(document, plan, Path.Combine(output, suffix + ".pdf"));
            var detailsPage = plan.Pages.ToList().FindIndex(page => page.Fragments.Any(fragment =>
                fragment.Block is AnalysisReportKeyValueBlock block
                && block.Items.Any(item => item.Label == "External experiment ID")));
            Assert.True(detailsPage >= 0);
            foreach (var pageIndex in new[] { 0, detailsPage }.Distinct())
            {
                using var bitmap = renderer.RenderPageBitmap(document, plan, pageIndex, 1200);
                using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                var fileName = pageIndex == 0 ? suffix + ".png" : suffix + "-identifiers.png";
                using var file = File.Create(Path.Combine(output, fileName));
                image.SaveTo(file);
            }
        }
    }

    static void ResetData()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
        DocumentDirtyTracker.MarkClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TandemSourcePreselectionAndPickerRolesStayOptional(bool automatic)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            ResetData();
            AppSettings.AutoSelectReportReferenceExperiments = automatic;
            var tandem = CreateResult("Tandem");
            var first = CreateResult("First source").Data;
            var second = CreateResult("Second source").Data;
            tandem.Data.SetTandemSourceExperimentIds(new[] { first.UniqueID, second.UniqueID });
            tandem.Data.SetBufferSubtraction(first, BufferSubtractionMethod.MatchedInjection, notify: false);
            DataManager.AddData(new ITCDataContainer[] { second, first, tandem.Data, tandem.Result });
            var window = new AnalysisReportWindow(tandem.Result);
            try
            {
                window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
                window.Show();
                window.UpdateLayout();
                Assert.Equal(automatic ? new[] { second.UniqueID, first.UniqueID } : Array.Empty<string>(),
                    Field<List<ExperimentData>>(window, "selectedSupportingExperiments").Select(data => data.UniqueID));
                typeof(AnalysisReportWindow).GetMethod("OpenResultPicker", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
                var checks = Field<List<CheckBox>>(window, "resultPickerChecks");
                var firstCheck = checks.Single(check => ReferenceEquals(check.Tag, first));
                var tandemCheck = checks.Single(check => ReferenceEquals(check.Tag, tandem.Data));
                Assert.Equal("Buffer and tandem source for selected results", ToolTip.GetTip(firstCheck));
                Assert.Equal("Already reported with a selected result", ToolTip.GetTip(tandemCheck));
                Assert.Contains("Included through Result 1", ((StackPanel)tandemCheck.Content!).Children.OfType<TextBlock>().Last().Text);
                Assert.False(tandemCheck.IsEnabled);
                Assert.Contains("Buffer reference; Tandem source", ((StackPanel)firstCheck.Content!).Children.OfType<TextBlock>().Last().Text);
                // Checking a result also triggers preselection when enabled.
                var resultCheck = checks.Single(check => ReferenceEquals(check.Tag, tandem.Result));
                Assert.Equal("Reports this fit and its experiments", ToolTip.GetTip(resultCheck));
                resultCheck.IsChecked = false;
                Assert.Equal("Adds saved data as a supporting experiment", ToolTip.GetTip(firstCheck));
                firstCheck.IsChecked = false;
                checks.Single(check => ReferenceEquals(check.Tag, second)).IsChecked = false;
                resultCheck.IsChecked = true;
                Assert.Equal(automatic, firstCheck.IsChecked == true);
                firstCheck.IsChecked = false;
                var flyout = Field<Flyout>(window, "resultPickerFlyout");
                var footer = ((Grid)flyout.Content!).Children.OfType<Grid>().Single();
                footer.Children.OfType<Button>().Single(button => Equals(button.Content, "Apply"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.DoesNotContain(first, Field<List<ExperimentData>>(window, "selectedSupportingExperiments"));
                var report = Field<AnalysisReport>(window, "report");
                var document = AnalysisReportBuilder.Build(report, _ => tandem.Result,
                    id => DataManager.Data.FirstOrDefault(data => data.UniqueID == id));
                Assert.DoesNotContain(document.SupportingExperiments, item => item.Id == first.UniqueID);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TandemSourcesAndMissingSourceDetailsRenderInReportPdf()
    {
        var tandem = CreateResult("Tandem result");
        var source = CreateResult("Source experiment").Data;
        tandem.Data.TandemMergeDescription = "2 experiments merged; fixed back-mixing";
        tandem.Data.SetTandemSourceExperimentIds(new[] { source.UniqueID, "unavailable-source" });
        tandem.Data.SetBufferSubtraction(source, BufferSubtractionMethod.MatchedInjection, notify: false);
        tandem.Result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(tandem.Result.Solution));
        var report = new AnalysisReport();
        report.SetResultIds(new[] { tandem.Result.UniqueID });
        report.SetSupportingExperimentIds(new[] { source.UniqueID });
        var document = AnalysisReportBuilder.Build(report, _ => tandem.Result,
            id => id == source.UniqueID ? source : null);
        Assert.True(document.IsValid);
        var renderer = new SkiaAnalysisReportRenderer();
        var plan = renderer.CreatePlan(document);
        Assert.NotEmpty(plan.Pages);
        using var pdf = new MemoryStream();
        renderer.WritePdf(document, plan, pdf);
        Assert.True(pdf.Length > 0);
        var output = Environment.GetEnvironmentVariable("FTITC_TANDEM_REPORT_QA_DIRECTORY");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "avalonia.pdf"), pdf.ToArray());
        var relevantPages = plan.Pages.Select((page, index) => (page, index)).Where(item => item.page.Fragments.Any(fragment =>
            fragment.Block?.Title == "Experiment sources" || fragment.Block is AnalysisReportKeyValueBlock metadata
                && metadata.Items.Any(value => value.Label == "Tandem sources")));
        foreach (var item in relevantPages)
        {
            using var bitmap = renderer.RenderPageBitmap(document, plan, item.index, 1200);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(output, "avalonia-page-" + item.index + ".png"));
            png.SaveTo(file);
        }
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
