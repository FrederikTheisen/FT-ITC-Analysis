using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
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
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisReportEnhancementTests : IDisposable
{
    readonly PreferencesState originalPreferences = PreferencesState.FromSettings();

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        originalPreferences.ApplyToSettings();
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void ClassifiedReportRenderQaWritesNegativeMixedAndDiagnosticPagesWhenRequested()
    {
        var prefix = Environment.GetEnvironmentVariable("FTITC_CLASSIFIED_REPORT_QA_PREFIX");

        var negative = CreateResult("QA no binding").Result;
        negative.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        SetNullComparison(negative, new NullModelComparison
        {
            NullModelId = "offset",
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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
                Assert.Empty(DataManager.Reports);
                Assert.True(window.SaveReport(false));
                var savedReport = Assert.Single(DataManager.Reports);
                Assert.Equal("QA-2026-α", savedReport.PresentationSettings.ReportId);

                AppSettings.TraceabilityModeEnabled = false;
                applyState.Invoke(window, null);
                Assert.False(idRow.IsVisible);
                Assert.Equal("QA-2026-α", savedReport.PresentationSettings.ReportId);

                var reopened = new AnalysisReportWindow(savedReport: savedReport);
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

    [AvaloniaFact]
    public void RepeatedEditAfterPreviewBuildsOneNewSnapshotThenReusesIt()
    {
        var fixture = CreateResult("Snapshot rebuild");
        var previousOperator = AppSettings.UserName;
        var originalContext = SynchronizationContext.Current;
        AnalysisReportWindow? window = null;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            AppSettings.UserName = "Operator A";
            window = Dispatcher.UIThread.Invoke(() =>
            {
                ResetData();
                DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
                return new AnalysisReportWindow(fixture.Result);
            });
            Task<bool>? preparation = null;
            AnalysisReportDocument? preview = null;
            Dispatcher.UIThread.Invoke(() =>
            {
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
            if (window != null) Dispatcher.UIThread.Invoke(window.Close);
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void SelectionEditsTheCurrentDraftAndSavedReportsOpenExplicitly()
    {
        ResetData();
        var a = CreateResult("Result A"); var b = CreateResult("Result B");
        DataManager.AddData(new ITCDataContainer[] { a.Data, a.Result, b.Data, b.Result });
        var window = new AnalysisReportWindow(a.Result);
        window.LeavePromptOverride = () => Task.FromResult(1);
        var title = Field<TextBox>(window, "titleBox");
        title.Text = "My report";
        Field<TextBox>(window, "labelBox").Text = "Retain study context";
        Field<TextBox>(window, "interpretationBox").Text = "Retain interpretation";
        SelectResult(window, b.Result);
        Assert.Equal("My report", title.Text);
        Assert.Equal("Retain study context", Field<TextBox>(window, "labelBox").Text);
        Assert.Contains("Retain interpretation", Field<TextBox>(window, "interpretationBox").Text);
        Assert.Empty(DataManager.Reports);
        Assert.True(window.SaveReport(false));
        var saved = Assert.Single(DataManager.Reports);
        Assert.Equal("My report", saved.Name);
        Assert.Equal(new[] { b.Result.UniqueID }, saved.ResultIds);
        title.Text = "Separate copy";
        Assert.True(window.SaveReport(true));
        Assert.Equal("My report", saved.Name);
        Assert.Equal(2, DataManager.Reports.Count);
        var ordinary = new AnalysisReportWindow(b.Result);
        Assert.Empty(Field<TextBox>(ordinary, "interpretationBox").Text!);
        var exact = new AnalysisReportWindow(savedReport: saved);
        Assert.NotSame(saved, Field<AnalysisReport>(exact, "report"));
        Assert.Equal(saved.UniqueID, Field<AnalysisReport>(exact, "report").UniqueID);
        Assert.Equal("My report", Field<TextBox>(exact, "titleBox").Text);
        window.Close(); ordinary.Close(); exact.Close();
    }

    [AvaloniaFact]
    public async Task LeavingAnEditedDraftHonorsCancelDiscardAndSave()
    {
        ResetData();
        var fixture = CreateResult("Draft transitions");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        DocumentDirtyTracker.MarkClean();
        var window = new AnalysisReportWindow(fixture.Result);
        Field<TextBox>(window, "titleBox").Text = "Edited draft";
        window.LeavePromptOverride = () => Task.FromResult(2);
        Assert.False(await window.ResolveDraftBeforeLeavingAsync());
        Assert.Empty(DataManager.Reports);
        Assert.False(DocumentDirtyTracker.IsDirty);
        window.LeavePromptOverride = () => Task.FromResult(1);
        Assert.True(await window.ResolveDraftBeforeLeavingAsync());
        Assert.Empty(DataManager.Reports);
        window.LeavePromptOverride = () => Task.FromResult(0);
        Assert.True(await window.ResolveDraftBeforeLeavingAsync());
        Assert.Equal("Edited draft", Assert.Single(DataManager.Reports).Name);
        Assert.True(DocumentDirtyTracker.IsDirty);
        Assert.False(Field<Button>(window, "saveReportButton").IsEnabled);
        Field<TextBox>(window, "titleBox").Text = "Failed save";
        Field<AnalysisReport>(window, "report").SetSupportingExperimentIds(new[] { "unresolved" });
        Assert.False(await window.ResolveDraftBeforeLeavingAsync());
        Assert.Equal("Edited draft", Assert.Single(DataManager.Reports).Name);
        window.LeavePromptOverride = () => Task.FromResult(1);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveInterpretationEditsReplaceAiAndRemainDirtyAfterUndoWithoutLeavingEditor(bool withHeading)
    {
        ResetData();
        var fixture = CreateResult("Live interpretation");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var saved = new AnalysisReport(); saved.SetResultIds(new[] { fixture.Result.UniqueID });
        saved.UpdatePresentationSettings(new AnalysisReportOptions());
        saved.ApproveInterpretation(new AnalysisInterpretationRecord
        {
            InterpretationMarkdown = withHeading ? "## Overall interpretation\nGenerated text" : "Generated text",
            InputFingerprint = "original fingerprint", Provider = "original provider",
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme,
        });
        DataManager.AddReport(saved); DocumentDirtyTracker.MarkClean();
        var window = new AnalysisReportWindow(savedReport: saved);
        var session = Field<AnalysisReportEditSession>(window, "editSession");
        var editor = Field<TextBox>(window, "interpretationBox");
        Assert.False(session.HasChanges);
        Assert.Equal(AnalysisInterpretationOrigin.AiGenerated, session.Draft.ApprovedInterpretation.Origin);
        Assert.False(Field<Button>(window, "saveReportButton").IsEnabled);
        var original = editor.Text;
        editor.Text += " added word";
        Assert.Equal(AnalysisInterpretationOrigin.Manual, session.Draft.ApprovedInterpretation.Origin);
        Assert.Contains("added word", session.Draft.ApprovedInterpretation.InterpretationMarkdown);
        Assert.Empty(session.Draft.ApprovedInterpretation.InputFingerprint);
        Assert.False(DocumentDirtyTracker.IsDirty);
        editor.Text = original;
        Assert.True(session.HasChanges);
        Assert.True(Field<Button>(window, "saveReportButton").IsEnabled);
        Assert.Equal(AnalysisInterpretationOrigin.AiGenerated, saved.ApprovedInterpretation.Origin);
        window.LeavePromptOverride = () => Task.FromResult(2);
        Assert.False(await window.RequestCloseAsync());
        window.LeavePromptOverride = () => Task.FromResult(0);
        Assert.True(await window.RequestCloseAsync());
        Assert.Equal(AnalysisInterpretationOrigin.Manual, saved.ApprovedInterpretation.Origin);
        Assert.Equal(AnalysisInterpretationResponseParser.ParseManual(original!), saved.ApprovedInterpretation.InterpretationMarkdown);
        Assert.False(session.HasChanges);
    }

    [AvaloniaFact]
    public async Task ClearingAndRetypingImmediatelyAffectsDraftAndReviewWithoutCommittingFocus()
    {
        ResetData();
        var fixture = CreateResult("Named in the text");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var window = new AnalysisReportWindow(fixture.Result);
        window.LeavePromptOverride = () => Task.FromResult(1);
        var session = Field<AnalysisReportEditSession>(window, "editSession");
        var editor = Field<TextBox>(window, "interpretationBox");
        editor.Text = "Named in the text has this result";
        Assert.True(window.SaveReport(false));
        fixture.Result.Name = "Renamed result";
        Assert.True(Field<Button>(window, "confirmInterpretationButton").IsVisible);
        editor.Text += " "; // Even a user edit normalized away confirms the current result.
        Assert.False(Field<Button>(window, "confirmInterpretationButton").IsVisible);
        fixture.Result.Comments = "Further change";
        Assert.True(Field<Button>(window, "confirmInterpretationButton").IsVisible);
        editor.Text = "";
        Assert.Null(session.Draft.ApprovedInterpretation);
        Assert.True(session.HasChanges);
        editor.Text = "Retyped interpretation";
        Assert.Contains("Retyped interpretation", session.Draft.ApprovedInterpretation.InterpretationMarkdown);
        Assert.False(Field<Button>(window, "confirmInterpretationButton").IsVisible);
        await window.RequestCloseAsync();
        Assert.DoesNotContain("Retyped", session.SavedReport!.ApprovedInterpretation.InterpretationMarkdown);
    }

    [AvaloniaFact]
    public async Task GenerationDialogDisplaysReadOnlyTextAndAcceptsTheGeneratedRecordUnchanged()
    {
        ResetData();
        var fixture = CreateResult("Generated response");
        var report = new AnalysisReport(); report.SetResultIds(new[] { fixture.Result.UniqueID });
        using var client = new System.Net.Http.HttpClient(new NoNetworkHandler());
        var dialog = new AnalysisInterpretationDialog(report, fixture.Result, client, () => { });
        var text = AnyField<TextBox>(dialog, "draftBox");
        Assert.True(text.IsReadOnly);
        var generated = new AnalysisInterpretationRecord
        {
            InterpretationMarkdown = "## Overall interpretation\nExact AI response",
            InputFingerprint = "fingerprint", Provider = "provider", ServiceRequestId = "request",
        };
        typeof(AnalysisInterpretationDialog).GetField("generatedRecord", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, generated);
        // The display is not the source of the accepted record.
        text.Text = "A programmatic display replacement";
        var owner = new Window(); owner.Show();
        var accepted = dialog.ShowDialog<AnalysisInterpretationRecord?>(owner);
        AnyField<Button>(dialog, "use").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var record = await accepted;
        Assert.NotNull(record);
        Assert.Equal(generated.InterpretationMarkdown, record.InterpretationMarkdown);
        Assert.Equal(generated.InputFingerprint, record.InputFingerprint);
        Assert.Equal(generated.ServiceRequestId, record.ServiceRequestId);
        Assert.Equal(AnalysisInterpretationOrigin.AiGenerated, record.Origin);
        Assert.Equal(generated.ApprovedAtUtc, record.ApprovedAtUtc);
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppQuitAndMainWindowCloseHonorCancelForAnActiveEditor(bool quit)
    {
        ResetData();
        var fixture = CreateResult("Quit with an active editor");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var owner = new MainWindow(); owner.SetStartupReady(); owner.Show();
        var flow = owner.OpenAnalysisReportAsync();
        var window = Assert.IsType<AnalysisReportWindow>(Assert.Single(owner.OwnedWindows));
        try
        {
            for (var attempt = 0; attempt < 1000 && AnyField<bool>(window, "busy"); attempt++) await Task.Yield();
            var editor = Field<TextBox>(window, "interpretationBox");
            editor.Text = "Unsaved text at exit";
            var prompts = 0;
            window.LeavePromptOverride = () => { prompts++; return Task.FromResult(2); };
            DocumentDirtyTracker.MarkClean();
            if (quit) await owner.QuitAsync();
            else
            {
                // Close() is ignored for a disabled modal owner; exercise its OS callback.
                var closing = (WindowClosingEventArgs)Activator.CreateInstance(typeof(WindowClosingEventArgs),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new object[] { WindowCloseReason.WindowClosing, false }, null)!;
                typeof(MainWindow).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(owner, new object[] { closing });
                Assert.True(closing.Cancel);
            }
            for (var attempt = 0; attempt < 1000 && AnyField<bool>(owner, "isHandlingDirtyClose"); attempt++) await Task.Yield();
            Assert.Equal(1, prompts);
            Assert.True(owner.IsVisible);
            Assert.True(window.IsVisible);
            Assert.False(flow.IsCompleted);
            Assert.Empty(DataManager.Reports);
            Assert.Contains("Unsaved text at exit", Field<AnalysisReport>(window, "report").ApprovedInterpretation.InterpretationMarkdown);
        }
        finally
        {
            window.LeavePromptOverride = () => Task.FromResult(1);
            await window.RequestCloseAsync(); await flow;
            DocumentDirtyTracker.MarkClean(); owner.Close();
        }
    }

    sealed class NoNetworkHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }

    static T AnyField<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    [AvaloniaFact]
    public async Task OpeningSavedReportHonorsCancelAndDiscardForLiveTextEdits()
    {
        ResetData();
        var fixture = CreateResult("Switch reports");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var saved = new AnalysisReport(); saved.SetResultIds(new[] { fixture.Result.UniqueID });
        saved.UpdatePresentationSettings(new AnalysisReportOptions());
        saved.SetManualInterpretation("Stored report text"); DataManager.AddReport(saved);
        var window = new AnalysisReportWindow(fixture.Result);
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        window.Show();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (AnyField<bool>(window, "busy") && DateTime.UtcNow < deadline) await Task.Delay(1);
            Assert.False(AnyField<bool>(window, "busy"));
            Field<TextBox>(window, "interpretationBox").Text = "Unsaved live text";
            var originalDraft = Field<AnalysisReport>(window, "report");
            foreach (var choice in new[] { 2, 1 })
            {
                window.LeavePromptOverride = () => Task.FromResult(choice);
                Field<Button>(window, "savedReportsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var items = Field<StackPanel>(window, "savedReportsItems");
                items.Children.OfType<Grid>().SelectMany(row => row.Children).OfType<Button>()
                    .Single(button => Equals(button.Content, "Open")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var attempt = 0; attempt < 1000 && AnyField<Task<bool>?>(window, "pendingLeave") != null; attempt++) await Task.Yield();
                if (choice == 2)
                {
                    Assert.Same(originalDraft, Field<AnalysisReport>(window, "report"));
                    Assert.Contains("Unsaved live text", originalDraft.ApprovedInterpretation.InterpretationMarkdown);
                }
            }
            Assert.Equal(saved.UniqueID, Field<AnalysisReport>(window, "report").UniqueID);
            Assert.Contains("Stored report text", Field<TextBox>(window, "interpretationBox").Text);
            Assert.False(Field<AnalysisReportEditSession>(window, "editSession").HasChanges);
            Assert.False(Field<Button>(window, "saveReportButton").IsEnabled);
            Assert.True(Field<Button>(window, "saveReportAsButton").IsEnabled);
        }
        finally { window.LeavePromptOverride = () => Task.FromResult(1); await window.RequestCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task PreviewAndProjectSnapshotDoNotCommitDraft()
    {
        ResetData();
        var fixture = CreateResult("Preview isolation");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        DocumentDirtyTracker.MarkClean();
        var window = new AnalysisReportWindow(fixture.Result);
        window.LeavePromptOverride = () => Task.FromResult(1);
        Field<TextBox>(window, "interpretationBox").Text = "Written text";
        var ensure = typeof(AnalysisReportWindow).GetMethod("EnsureExportDocumentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(await (Task<bool>)ensure.Invoke(window, null)!);
        Assert.Empty(DataManager.Reports);
        Assert.False(DocumentDirtyTracker.IsDirty);
        Assert.True(window.SaveReport(false));
        var saved = Assert.Single(DataManager.Reports);
        Field<TextBox>(window, "titleBox").Text = "Unsaved title";
        using var stream = new MemoryStream();
        await AnalysisITC.Core.Export.FTXTCWriter.WriteStream(stream, DataManager.Data, DataManager.Results, reports: DataManager.Reports);
        stream.Position = 0;
        var reopened = await AnalysisITC.Core.DataReaders.FTXTCReader.ReadWithRecovery(stream, AnalysisITC.Core.DataReaders.FtxtcReadPolicy.Strict);
        Assert.Equal(saved.PresentationSettings.Title, Assert.Single(reopened.Reports).PresentationSettings.Title);
        Assert.NotEqual("Unsaved title", saved.PresentationSettings.Title);
        window.Close();
    }

    [AvaloniaFact]
    public void ConfirmDismissesReviewWarningAndIsSavedOnlyWithReport()
    {
        ResetData();
        var fixture = CreateResult("Updated result");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var window = new AnalysisReportWindow(fixture.Result);
        window.LeavePromptOverride = () => Task.FromResult(1);
        Field<TextBox>(window, "interpretationBox").Text = "Check retained text";
        Assert.True(window.SaveReport(false));
        var saved = Assert.Single(DataManager.Reports);
        fixture.Result.Date = DateTime.Now;
        typeof(AnalysisReportWindow).GetMethod("MarkStale", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        var confirm = Field<Button>(window, "confirmInterpretationButton");
        Assert.True(confirm.IsVisible);
        var approved = saved.ApprovedInterpretation.ApprovedAtUtc;
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(confirm.IsVisible);
        Assert.Null(saved.ApprovedInterpretation.ConfirmedAtUtc);
        Assert.True(window.SaveReport(false));
        Assert.NotNull(saved.ApprovedInterpretation.ConfirmedAtUtc);
        Assert.Equal(approved, saved.ApprovedInterpretation.ApprovedAtUtc);
        window.Close();
    }

    [AvaloniaFact]
    public void SaveShortcutsTargetTheProjectWhileReportDraftStaysUnsaved()
    {
        ResetData();
        var fixture = CreateResult("Project save shortcuts");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var window = new AnalysisReportWindow(fixture.Result);
        window.LeavePromptOverride = () => Task.FromResult(1);
        Field<TextBox>(window, "titleBox").Text = "Unsaved report draft";
        var saves = 0; var savesAs = 0;
        window.SaveProjectCommand = () => { saves++; return Task.CompletedTask; };
        window.SaveProjectAsCommand = () => { savesAs++; return Task.CompletedTask; };
        var primary = OperatingSystem.IsMacOS() ? global::Avalonia.Input.KeyModifiers.Meta : global::Avalonia.Input.KeyModifiers.Control;
        window.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
            Key = global::Avalonia.Input.Key.S, KeyModifiers = primary });
        window.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent,
            Key = global::Avalonia.Input.Key.S, KeyModifiers = primary | global::Avalonia.Input.KeyModifiers.Shift });
        Assert.Equal(1, saves); Assert.Equal(1, savesAs);
        Assert.Empty(DataManager.Reports);
        Assert.True(Field<Button>(window, "saveReportButton").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ReportBuilderControlsRemainReachableAtMinimumSize()
    {
        var output = Environment.GetEnvironmentVariable("FTITC_PROJECT_INFORMATION_QA_DIRECTORY");
        if (string.IsNullOrWhiteSpace(output)) return;
        ResetData();
        var fixture = CreateResult("A report with a long descriptive result name λ β Æ 中文");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var app = Application.Current!;
        var colors = new global::Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://FT-ITC Analysis/"))
            { Source = new Uri("avares://FT-ITC Analysis/Styling/AppTheme.axaml") };
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        app.Resources.MergedDictionaries.Add(colors); app.Styles.Add(theme);
        var window = new AnalysisReportWindow(fixture.Result) { Width = 960, Height = 620 };
        try
        {
            window.LeavePromptOverride = () => Task.FromResult(1);
            Field<TextBox>(window, "interpretationBox").Text = "Check the interpretation after updating the result.";
            Assert.True(window.SaveReport(false));
            fixture.Result.Date = DateTime.Now;
            window.Show();
            // Opening builds the initial preview; let its continuation return before layout inspection.
            for (var attempt = 0; attempt < 100 && (bool)typeof(AnalysisReportWindow).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; attempt++)
                await Task.Delay(20);
            window.UpdateLayout();
            Directory.CreateDirectory(output);
            using (var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height)))
            {
                bitmap.Render(window); bitmap.Save(Path.Combine(output, "avalonia-report-builder-minimum-top.png"));
            }
            var save = Field<Button>(window, "saveReportAsButton");
            save.BringIntoView();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            await Task.Delay(30); window.UpdateLayout();
            var position = save.TranslatePoint(new global::Avalonia.Point(), window)!.Value;
            Assert.True(position.Y >= 0 && position.Y + save.Bounds.Height <= window.ClientSize.Height);
            using (var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height)))
            {
                bitmap.Render(window); bitmap.Save(Path.Combine(output, "avalonia-report-builder-minimum.png"));
            }
            typeof(AnalysisReportWindow).GetMethod("OpenSavedReportsPicker", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            window.UpdateLayout();
            var items = Field<StackPanel>(window, "savedReportsItems");
            Assert.Contains(items.Children.OfType<Grid>().SelectMany(grid => grid.Children).OfType<Button>(), button => Equals(button.Content, "Open"));
            Field<Flyout>(window, "savedReportsFlyout").IsOpen = false;
            window.Close();
        }
        finally
        {
            window.LeavePromptOverride = () => Task.FromResult(1); window.Close();
            app.Styles.Remove(theme); app.Resources.MergedDictionaries.Remove(colors);
        }
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void OptionalCoverQaArtifactsRenderLongMultiResultTitlesAndTraceabilityModes()
    {
        var output = Environment.GetEnvironmentVariable("FTITC_REPORT_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);
        AppSettings.UserName = "Analysis operator Zoë";
        var first = CreateResult("A very long analysis result name with Unicode λ, β and 李明");
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
                Author = "Research collaborator Zoë 李明 with a deliberately long display name",
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TandemSourcePreselectionAndPickerRolesStayOptional(bool automatic)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            ResetData();
            AppSettings.AutoSelectReportReferenceExperiments = automatic;
            AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
            var first = CreateResult("First source").Data;
            var second = CreateResult("Second source").Data;
            var tandem = TandemConcatenation.ConcatTandem(new List<ExperimentData> { first, second });
            var corrected = CreateResult("Corrected").Data;
            corrected.SetBufferSubtraction(first, BufferSubtractionMethod.MatchedInjection, notify: false);
            var result = CreateResult("Tandem", tandem, corrected);
            Assert.True(tandem.IsTandemExperiment);
            Assert.Equal(2, tandem.Segments.Count);
            Assert.Null(tandem.BufferSubtractionSettings);
            DataManager.AddData(new ITCDataContainer[] { second, first, tandem, corrected, result });
            var window = new AnalysisReportWindow(result);
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
                var tandemCheck = checks.Single(check => ReferenceEquals(check.Tag, tandem));
                Assert.Equal("Buffer and tandem source for selected results", ToolTip.GetTip(firstCheck));
                Assert.Equal("Already reported with a selected result", ToolTip.GetTip(tandemCheck));
                Assert.Contains("Included through Result 1", ((StackPanel)tandemCheck.Content!).Children.OfType<TextBlock>().Last().Text);
                Assert.False(tandemCheck.IsEnabled);
                Assert.Contains("Buffer reference; Tandem source", ((StackPanel)firstCheck.Content!).Children.OfType<TextBlock>().Last().Text);
                // Supporting edits remain transient until Apply; references
                // are selected automatically only for a new report.
                var resultCheck = checks.Single(check => ReferenceEquals(check.Tag, result));
                Assert.Equal("Reports this fit and its experiments", ToolTip.GetTip(resultCheck));
                resultCheck.IsChecked = false;
                Assert.Equal("Adds saved data as a supporting experiment", ToolTip.GetTip(firstCheck));
                firstCheck.IsChecked = false;
                checks.Single(check => ReferenceEquals(check.Tag, second)).IsChecked = false;
                resultCheck.IsChecked = true;
                Assert.False(firstCheck.IsChecked == true);
                firstCheck.IsChecked = false;
                var flyout = Field<Flyout>(window, "resultPickerFlyout");
                var footer = ((Grid)flyout.Content!).Children.OfType<Grid>().Single();
                footer.Children.OfType<Button>().Single(button => Equals(button.Content, "Apply"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.DoesNotContain(first, Field<List<ExperimentData>>(window, "selectedSupportingExperiments"));
                var report = Field<AnalysisReport>(window, "report");
                var document = AnalysisReportBuilder.Build(report, _ => result,
                    id => DataManager.Data.FirstOrDefault(data => data.UniqueID == id));
                Assert.True(document.IsValid);
                Assert.DoesNotContain(document.SupportingExperiments, item => item.Id == first.UniqueID);
            }
            finally { window.Close(); }
        });
    }

    [AvaloniaFact]
    public void TandemSourcesAndMissingSourceDetailsRenderInReportPdf()
    {
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        var source = CreateResult("Source experiment").Data;
        var missingSource = CreateResult("Unavailable source").Data;
        var tandem = TandemConcatenation.ConcatTandem(new List<ExperimentData> { source, missingSource });
        var corrected = CreateResult("Corrected").Data;
        corrected.SetBufferSubtraction(source, BufferSubtractionMethod.MatchedInjection, notify: false);
        var result = CreateResult("Tandem result", tandem, corrected);
        Assert.True(tandem.IsTandemExperiment);
        Assert.Equal(2, tandem.Segments.Count);
        Assert.Null(tandem.BufferSubtractionSettings);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { source.UniqueID });
        var document = AnalysisReportBuilder.Build(report, _ => result,
            id => id == source.UniqueID ? source : null);
        Assert.True(document.IsValid);
        var sources = document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportTableBlock>()
            .Single(table => table.Title == "Experiment sources");
        Assert.Equal("Buffer reference for 1B; Tandem source for 1A",
            sources.Rows.Single(row => row.Cells[1] == source.Name).Cells[7]);
        Assert.Contains(document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items), item => item.Label == "Tandem sources"
                && item.Value == "1 of 2 recorded source experiments is not in this project.");
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
        return (data, CreateResult(name, data));
    }

    static AnalysisResult CreateResult(string name, params ExperimentData[] experiments)
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 1,
            Iterations = 1,
        });
        var models = experiments.Select(data =>
        {
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.Solution = SolutionInterface.FromModel(model, convergence);
            data.UpdateSolution(model);
            return (Model)model;
        }).ToList();
        var globalModel = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            models.Select(model => model.Solution).ToList(), convergence, reconstructBootstrap: false);
        globalModel.Solution = globalSolution;
        return new AnalysisResult(globalSolution) { Name = name, Date = new DateTime(2026, 8, 30) };
    }
}
