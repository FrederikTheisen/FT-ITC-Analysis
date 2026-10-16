using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProjectInformationTests : IDisposable
{
    readonly bool originalAutomaticReferences = AppSettings.AutoSelectReportReferenceExperiments;

    [AvaloniaFact]
    public async Task EmptyAndReportOnlyProjectsHaveVisibleCountsAndReadableDetails()
    {
        Reset();
        var window = new ProjectInformationWindow();
        try
        {
            Assert.Equal("Experiments", TabHeader(window, 0));
            Assert.Equal("Analysis Results", TabHeader(window, 1));
            Assert.Equal("Saved reports", TabHeader(window, 2));
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text == "0 experiments · 0 results · 0 reports");
            Assert.False(window.OpenButton.IsEnabled);
            var report = new AnalysisReport { Name = "Stored text", AuthorComments = "Preserve this note." };
            report.SetResultIds(new[] { "missing-result" });
            DataManager.AddReport(report);
            Assert.Equal("Saved reports", TabHeader(window, 2));
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), block => block.Text == "0 experiments · 0 results · 1 report");
            window.ReportsList.SelectedIndex = 0;
            Assert.Contains("1 of 1 result missing", ((ProjectInformationEntry)window.ReportsList.SelectedItem!).Detail);
            var overview = OverviewText(window);
            Assert.DoesNotContain("Preserve this note.", overview);
            Assert.Contains("Missing result", overview);
            Assert.False(window.OpenButton.IsEnabled);
            Assert.False(window.RenameButton.IsEnabled);
            Assert.True(window.DeleteButton.IsEnabled);
            Assert.True(window.DetailsButton.IsEnabled);
            Assert.True(window.CopyAllButton.IsEnabled);
            window.Show();
            var viewing = window.ShowDetailsAsync();
            var details = Assert.IsType<ReportDetailsWindow>(Assert.Single(window.OwnedWindows));
            Assert.False(viewing.IsCompleted);
            await window.ShowDetailsAsync();
            Assert.Single(window.OwnedWindows);
            Assert.Contains("Preserve this note.", details.DetailsTextBox.Text);
            Assert.Contains("Missing result", details.DetailsTextBox.Text);
            Assert.True(details.DetailsTextBox.IsReadOnly);
            await details.CopyAllAsync();
            Assert.Contains("Preserve this note.", await details.Clipboard!.TryGetTextAsync());
            details.Close();
            await viewing;
            Assert.Empty(window.OwnedWindows);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CountsIncludeInactiveExperimentsAndExcludeResultMemberCopies()
    {
        Reset();
        var first = CreateResult("First result");
        var second = CreateResult("Second result");
        second.Data.Include = false;
        DataManager.AddData(new ITCDataContainer[] { first.Data, second.Data, first.Result, second.Result });
        var report = Report(first.Result);
        var window = new ProjectInformationWindow();
        try
        {
            Assert.Equal(2, window.ExperimentsList.ItemCount);
            Assert.Equal(2, window.ResultsList.ItemCount);
            Assert.Equal(1, window.ReportsList.ItemCount);
            var row = Assert.IsType<ProjectInformationEntry>(window.ResultsList.Items[0]);
            Assert.Contains("1 experiment", row.Detail);
            Assert.Contains("One-Set-Of-Sites", row.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Experiments: " + first.Result.Solution.Solutions[0].Data.Name, row.Detail);
            Assert.Contains("°C", row.Detail);
            Assert.Contains("Fit: Converged", row.Detail);
            var experimentRow = Assert.IsType<ProjectInformationEntry>(window.ExperimentsList.Items[0]);
            Assert.Contains("Cell:", experimentRow.Detail);
            Assert.Contains("Syringe:", experimentRow.Detail);
            Assert.Contains("integrated · Fit:", experimentRow.Detail);
            window.ReportsList.SelectedIndex = 0;
            Assert.Equal("First result report", ((ProjectInformationEntry)window.ReportsList.SelectedItem!).Name);
            first.Result.Name = "Renamed result";
            Assert.Equal("Renamed result report", ((ProjectInformationEntry)window.ReportsList.SelectedItem!).Name);
            Assert.Same(report, ((ProjectInformationEntry)window.ReportsList.SelectedItem!).Report);
            Assert.Contains("Renamed result", OverviewText(window));
            Assert.Contains("1 fitted · 0 supporting selected", OverviewText(window));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MissingResultsStillAllowCopyingSavedUserText()
    {
        Reset();
        var report = new AnalysisReport { Name = "Missing data", AuthorComments = "Original author note 李明" };
        report.SetResultIds(new[] { "missing" });
        report.SetManualInterpretation("Approved text about β binding.");
        DataManager.AddReport(report);
        var window = new ProjectInformationWindow();
        window.Show();
        try
        {
            window.ReportsList.SelectedIndex = 0;
            await window.CopyAllAsync();
            var text = await window.Clipboard!.TryGetTextAsync();
            Assert.Contains("Original author note 李明", text);
            Assert.Contains("Approved text about β binding.", text);
            Assert.DoesNotContain("Missing result", text);
            await window.RenameSelectedAsync(_ => throw new InvalidOperationException("Missing reports cannot be renamed."));
            await window.DeleteSelectedAsync(_ => Task.FromResult(true));
            Assert.Empty(DataManager.Reports);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RenameCancellationPreservesContentAndClearingRestoresAutomaticTitle()
    {
        Reset();
        var fixture = CreateResult("OneSetOfSites (3)");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var report = Report(fixture.Result);
        report.Name = "Custom report name";
        report.UpdatePresentationSettings(new AnalysisReportOptions { Title = "Printed title", AutomaticTitle = false });
        DocumentDirtyTracker.MarkClean();
        var window = new ProjectInformationWindow();
        try
        {
            window.ReportsList.SelectedIndex = 0;
            var edited = report.LastEditedUtc;
            await window.RenameSelectedAsync(_ => Task.FromResult<string?>(null));
            Assert.Equal("Custom report name", report.Name);
            Assert.Equal(edited, report.LastEditedUtc);
            Assert.False(DocumentDirtyTracker.IsDirty);
            await window.RenameSelectedAsync(_ => Task.FromResult<string?>(""));
            Assert.Equal("", report.Name);
            Assert.Equal("OneSetOfSites (3) report", ((ProjectInformationEntry)window.ReportsList.SelectedItem!).Name);
            Assert.Equal("", report.PresentationSettings.Title);
            Assert.True(report.PresentationSettings.AutomaticTitle);
            Assert.True(DocumentDirtyTracker.IsDirty);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DeleteCancellationAndConfirmationAffectOnlyTheSelectedReport()
    {
        Reset();
        var fixture = CreateResult("Result");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var report = Report(fixture.Result);
        var retained = Report(fixture.Result);
        DocumentDirtyTracker.MarkClean();
        var window = new ProjectInformationWindow();
        try
        {
            window.ReportsList.SelectedIndex = 0;
            await window.DeleteSelectedAsync(_ => Task.FromResult(false));
            Assert.Equal(2, DataManager.Reports.Count);
            Assert.False(DocumentDirtyTracker.IsDirty);
            await window.DeleteSelectedAsync(_ => Task.FromResult(true));
            Assert.Equal(retained, Assert.Single(DataManager.Reports));
            Assert.DoesNotContain(report, DataManager.Reports);
            Assert.Same(fixture.Data, Assert.Single(DataManager.Data));
            Assert.Same(fixture.Result, Assert.Single(DataManager.Results));
            Assert.True(DocumentDirtyTracker.IsDirty);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingReportActionsCannotAffectAReplacementProject(bool rename)
    {
        Reset();
        var fixture = CreateResult("Original");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var report = Report(fixture.Result);
        report.Name = "Original name";
        var window = new ProjectInformationWindow();
        window.Show();
        try
        {
            window.ReportsList.SelectedIndex = 0;
            var completion = new TaskCompletionSource<bool>();
            var task = rename
                ? window.RenameSelectedAsync(async _ => { await completion.Task; return "Delayed rename"; })
                : window.DeleteSelectedAsync(async _ => { await completion.Task; return true; });
            DataManager.Clear(DataClearMode.ResetSession);
            // Even reusing an imported object cannot bypass document ownership.
            DataManager.AddData(fixture.Result);
            DataManager.AddReport(report);
            DocumentDirtyTracker.MarkClean();
            completion.SetResult(true);
            await task;
            Assert.Equal("Original name", report.Name);
            Assert.Same(report, Assert.Single(DataManager.Reports));
            Assert.False(DocumentDirtyTracker.IsDirty);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOnlyDetailsCloseWhenTheirReportOrProjectIsRemoved(bool replaceProject)
    {
        Reset();
        var report = new AnalysisReport { Name = "Saved note", AuthorComments = "Original user text" };
        DataManager.AddReport(report);
        var window = new ProjectInformationWindow();
        window.Show();
        try
        {
            window.ReportsList.SelectedIndex = 0;
            var viewing = window.ShowDetailsAsync();
            var details = Assert.IsType<ReportDetailsWindow>(Assert.Single(window.OwnedWindows));
            if (replaceProject) DataManager.Clear(DataClearMode.ResetSession);
            else DataManager.RemoveReport(report);
            await viewing;
            Assert.False(details.IsVisible);
            Assert.Empty(window.OwnedWindows);
            Assert.Empty(DataManager.Reports);
            Assert.Equal(!replaceProject, window.IsVisible);
            if (!replaceProject)
            {
                Assert.False(window.DetailsButton.IsEnabled);
                Assert.Empty(window.ReportOverviewPanel.Children);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ExactOlderReportOpeningPreservesIdentityAndStoredOrder()
    {
        Reset();
        var first = CreateResult("First");
        var second = CreateResult("Second");
        var supporting = new ExperimentData("supporting.itc");
        DataManager.AddData(new ITCDataContainer[] { first.Data, second.Data, supporting, second.Result, first.Result });
        var older = Report(first.Result, second.Result);
        older.SetDate(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        older.SetSupportingExperimentIds(new[] { supporting.UniqueID });
        older.SetManualInterpretation("Older variant text.");
        var newer = Report(first.Result, second.Result);
        newer.SetDate(older.Date.AddDays(1));
        newer.SetManualInterpretation("Newer variant text.");
        var edited = older.LastEditedUtc;
        var exact = new AnalysisReportWindow(savedReport: older);
        try
        {
            Assert.NotSame(older, Field<AnalysisReport>(exact, "report"));
            Assert.Equal(older.UniqueID, Field<AnalysisReport>(exact, "report").UniqueID);
            Assert.Equal(new[] { second.Result, first.Result }, Field<List<AnalysisResult>>(exact, "selectedResults"));
            Assert.Equal(new[] { first.Result.UniqueID, second.Result.UniqueID }, older.ResultIds);
            Assert.Equal(new[] { supporting }, Field<List<ExperimentData>>(exact, "selectedSupportingExperiments"));
            Assert.Contains("Older variant text.", Field<TextBox>(exact, "interpretationBox").Text);
            Assert.Equal(edited, older.LastEditedUtc);
            var ordinary = new AnalysisReportWindow(first.Result);
            try
            {
                Select(ordinary, new[] { first.Result, second.Result }, new[] { supporting });
                Assert.DoesNotContain(Field<AnalysisReport>(ordinary, "report"), DataManager.Reports);
                Assert.Equal(new[] { supporting }, Field<List<ExperimentData>>(ordinary, "selectedSupportingExperiments"));
                Assert.Empty(Field<TextBox>(ordinary, "interpretationBox").Text!);
                ordinary.LeavePromptOverride = () => Task.FromResult(1);
                Assert.Equal(2, DataManager.Reports.Count);
            }
            finally { ordinary.Close(); }
        }
        finally { exact.Close(); }
    }

    [AvaloniaFact]
    public void SupportingSelectionIsEditedInPlaceAndRemovalSurvivesReopening()
    {
        Reset();
        AppSettings.AutoSelectReportReferenceExperiments = true;
        var fixture = CreateResult("Tandem");
        var source = new ExperimentData("reference.itc");
        fixture.Data.SetTandemSourceExperimentIds(new[] { source.UniqueID });
        DataManager.AddData(new ITCDataContainer[] { source, fixture.Data, fixture.Result });
        var window = new AnalysisReportWindow(fixture.Result);
        try
        {
            Assert.Empty(DataManager.Reports);
            Assert.True(window.SaveReport(false));
            var report = Assert.Single(DataManager.Reports);
            Assert.Equal(new[] { source.UniqueID }, report.SupportingExperimentIds);
            Select(window, new[] { fixture.Result }, Array.Empty<ExperimentData>());
            Assert.Same(report, Assert.Single(DataManager.Reports));
            Assert.NotEmpty(report.SupportingExperimentIds);
            Assert.True(window.SaveReport(false));
            Assert.Empty(report.SupportingExperimentIds);
            var reopened = new AnalysisReportWindow(savedReport: report);
            try
            {
                Assert.NotSame(report, Field<AnalysisReport>(reopened, "report"));
                Assert.Equal(report.UniqueID, Field<AnalysisReport>(reopened, "report").UniqueID);
                Assert.Empty(Field<List<ExperimentData>>(reopened, "selectedSupportingExperiments"));
            }
            finally { reopened.Close(); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ResultAndSupportingEditsApplyTogetherAndRetainInterpretation()
    {
        Reset();
        AppSettings.AutoSelectReportReferenceExperiments = true;
        var first = CreateResult("First");
        var second = CreateResult("Second");
        var source = new ExperimentData("reference.itc");
        second.Data.SetTandemSourceExperimentIds(new[] { source.UniqueID });
        DataManager.AddData(new ITCDataContainer[] { first.Data, first.Result, source, second.Data, second.Result });
        var window = new AnalysisReportWindow(first.Result);
        try
        {
            Field<TextBox>(window, "interpretationBox").Text = "First-result interpretation.";
            typeof(AnalysisReportWindow).GetMethod("CommitInterpretationEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Select(window, new[] { second.Result }, Array.Empty<ExperimentData>());
            var report = Field<AnalysisReport>(window, "report");
            Assert.Equal("", report.Name);
            Assert.Empty(report.SupportingExperimentIds);
            Assert.Contains("First-result interpretation.", report.ApprovedInterpretation.InterpretationMarkdown);
            Assert.Contains("First-result interpretation.", Field<TextBox>(window, "interpretationBox").Text);
            window.LeavePromptOverride = () => Task.FromResult(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovedReportsCannotReturnThroughWindowCleanupOrDelayedEditorCommit()
    {
        Reset();
        var fixture = CreateResult("Result");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var report = Report(fixture.Result);
        var window = new AnalysisReportWindow(savedReport: report);
        Field<TextBox>(window, "interpretationBox").Text = "Uncommitted text.";
        DataManager.RemoveReport(report);
        typeof(AnalysisReportWindow).GetMethod("CommitInterpretationEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Field<TextBox>(window, "labelBox").Text = "Delayed edit";
        window.Close();
        Assert.Empty(DataManager.Reports);
        Assert.Null(report.ApprovedInterpretation);
        Assert.NotEqual("Delayed edit", report.PresentationSettings.DocumentLabel);
    }

    [AvaloniaFact]
    public async Task InformationAndReportToolsRemainMutuallyExclusive()
    {
        Reset();
        var fixture = CreateResult("Result");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var older = Report(fixture.Result);
        older.SetDate(new DateTime(2026, 1, 1));
        var newer = Report(fixture.Result);
        newer.SetDate(older.Date.AddDays(1));
        var owner = new MainWindow();
        owner.SetStartupReady();
        owner.Show();
        try
        {
            var flow = owner.OpenProjectInformationAsync();
            var information = Assert.IsType<ProjectInformationWindow>(Assert.Single(owner.OwnedWindows));
            Assert.False(owner.CanOpenProjectInformation());
            Assert.False(owner.MainMenuCommandsEnabled());
            await owner.OpenAnalysisReportAsync();
            Assert.Single(owner.OwnedWindows);
            information.ReportsList.SelectedIndex = 0;
            information.OpenSelectedReport();
            await Task.Yield();
            var reportWindow = Assert.IsType<AnalysisReportWindow>(Assert.Single(owner.OwnedWindows));
            Assert.Equal(older.UniqueID, Field<AnalysisReport>(reportWindow, "report").UniqueID);
            Assert.False(information.IsVisible);
            Assert.False(owner.CanOpenProjectInformation());
            var projectSaves = owner.KeyBindings.Where(binding => binding.Gesture?.Key == global::Avalonia.Input.Key.S).ToList();
            Assert.Equal(2, projectSaves.Count);
            Assert.All(projectSaves, binding => Assert.True(binding.Command!.CanExecute(null)));
            Assert.True(owner.KeyBindings.Single(binding => binding.Gesture?.Key == global::Avalonia.Input.Key.Q).Command!.CanExecute(null));
            reportWindow.Close();
            await flow;
            Assert.True(owner.CanOpenProjectInformation());
        }
        finally { DocumentDirtyTracker.MarkClean(); owner.Close(); }
    }

    [AvaloniaFact]
    public void InformationCommandWaitsForStartupAndProcessingToFinish()
    {
        Reset();
        var owner = new MainWindow();
        try
        {
            Assert.False(owner.CanOpenProjectInformation());
            owner.SetStartupReady();
            Assert.True(owner.CanOpenProjectInformation());
            using (ProcessingActivity.Begin()) Assert.False(owner.CanOpenProjectInformation());
            Assert.True(owner.CanOpenProjectInformation());
        }
        finally { DocumentDirtyTracker.MarkClean(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task ExternalFileOpeningWaitsUntilInformationHasClosed()
    {
        Reset();
        var fixture = CreateResult("Current result");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result });
        var report = Report(fixture.Result);
        var owner = new MainWindow();
        owner.SetStartupReady();
        owner.Show();
        var path = Path.Combine(Path.GetTempPath(), "queued-itc032-" + Guid.NewGuid() + ".ftxtc");
        File.WriteAllText(path, "");
        try
        {
            var informationFlow = owner.OpenProjectInformationAsync();
            var information = Assert.IsType<ProjectInformationWindow>(Assert.Single(owner.OwnedWindows));
            var documentId = DocumentDirtyTracker.CurrentDocumentId;
            var opening = owner.OpenQueuedPathsAsync(new[] { path });
            Assert.False(opening.IsCompleted);
            Assert.Same(report, Assert.Single(DataManager.Reports));
            Assert.Same(information, Assert.Single(owner.OwnedWindows));
            information.Close();
            await informationFlow;
            await Task.Yield();
            var prompt = Assert.Single(owner.OwnedWindows);
            Assert.Equal("Load Project", prompt.Title);
            prompt.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await opening;
            Assert.Equal(documentId, DocumentDirtyTracker.CurrentDocumentId);
            Assert.Same(report, Assert.Single(DataManager.Reports));
            Assert.True(owner.CanOpenProjectInformation());
        }
        finally
        {
            DocumentDirtyTracker.MarkClean();
            owner.Close();
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task DeletingTheFinalReportCanBeSavedAsAnEmptyExistingProject()
    {
        Reset();
        var path = Path.Combine(Path.GetTempPath(), "itc032-delete-" + Guid.NewGuid() + ".ftxtc");
        var report = new AnalysisReport { AuthorComments = "Only saved item" };
        DataManager.AddReport(report);
        FTITCFormat.CurrentAccessedAppDocumentPath = path;
        Assert.True(await ProjectWriter.SaveWithPathAsync());
        var owner = new MainWindow();
        var information = new ProjectInformationWindow();
        try
        {
            information.ReportsList.SelectedIndex = 0;
            await information.DeleteSelectedAsync(_ => Task.FromResult(true));
            Assert.True(owner.CanSaveDocument());
            await owner.SaveDocumentAsync();
            Assert.False(DocumentDirtyTracker.IsDirty);
            using var stream = File.OpenRead(path);
            var reopened = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
            Assert.Empty(reopened.Containers);
            Assert.Empty(reopened.Reports);
        }
        finally
        {
            information.Close();
            DocumentDirtyTracker.MarkClean();
            owner.Close();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task EmptySavedProjectWithUnsavedDeletionStillPromptsBeforeClear()
    {
        Reset();
        var owner = new MainWindow();
        owner.SetStartupReady();
        owner.Show();
        try
        {
            var report = new AnalysisReport();
            DataManager.AddReport(report);
            FTITCFormat.CurrentAccessedAppDocumentPath = Path.Combine(Path.GetTempPath(), "saved-project.ftxtc");
            DocumentDirtyTracker.MarkClean();
            DataManager.RemoveReport(report);
            Assert.True(DocumentDirtyTracker.IsDirty);
            var documentId = DocumentDirtyTracker.CurrentDocumentId;
            var clearing = owner.ClearDataWithConfirmationAsync();
            Assert.False(clearing.IsCompleted);
            var prompt = Assert.Single(owner.OwnedWindows);
            var cancel = prompt.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"));
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await clearing;
            Assert.True(DocumentDirtyTracker.IsDirty);
            Assert.Equal(documentId, DocumentDirtyTracker.CurrentDocumentId);
        }
        finally { DocumentDirtyTracker.MarkClean(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task LongNamesAndMixedScriptDetailsRemainScrollableWithActionsVisible()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FTITC_PROJECT_INFORMATION_QA_DIRECTORY")))
            Assert.Skip("Set FTITC_PROJECT_INFORMATION_QA_DIRECTORY to run real Skia layout and visual QA.");
        Reset();
        var fixture = CreateResult("OneSetOfSites (3)");
        var comparison = CreateResult("Temperature comparison");
        DataManager.AddData(new ITCDataContainer[] { fixture.Data, fixture.Result, comparison.Data, comparison.Result });
        var report = Report(fixture.Result);
        report.AuthorComments = "Authors: 李明, β-binding work; Árni Guðmundsson.";
        report.UpdatePresentationSettings(new AnalysisReportOptions { DocumentLabel = "Binding study at 25 °C" });
        report.SetManualInterpretation("The analysis describes one set of binding sites. Compare this result with the temperature study before drawing conclusions about changes in binding.");
        Report(comparison.Result).Name = "Temperature comparison";
        Report(fixture.Result, comparison.Result);
        FTITCFormat.CurrentAccessedAppDocumentPath = Path.Combine(Path.GetTempPath(), "Binding study.ftxtc");
        var application = Application.Current!;
        var originalVariant = application.RequestedThemeVariant;
        var colors = new ResourceInclude(new Uri("avares://FT-ITC Analysis/"))
        {
            Source = new Uri("avares://FT-ITC Analysis/Styling/AppTheme.axaml"),
        };
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        application.Resources.MergedDictionaries.Add(colors);
        application.Styles.Add(theme);
        application.RequestedThemeVariant = ThemeVariant.Light;
        try
        {
            await VerifyAndCapture("avalonia-project-information.png", minimum: false);
            fixture.Result.Name = "OneSetOfSites (3) — λβ and 李明 " + new string('x', 1000);
            report.SetManualInterpretation(string.Join("\n\n", Enumerable.Repeat("Long approved interpretation — λβ, 李明, Árni Guðmundsson.", 80)));
            for (var index = 0; index < 20; index++) Report(fixture.Result);
            await VerifyAndCapture("avalonia-project-information-minimum.png", minimum: true);
        }
        finally
        {
            application.RequestedThemeVariant = originalVariant;
            application.Styles.Remove(theme);
            application.Resources.MergedDictionaries.Remove(colors);
        }

        async Task VerifyAndCapture(string filename, bool minimum)
        {
            var window = new ProjectInformationWindow();
            if (minimum) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
            window.Tabs.SelectedIndex = 2;
            window.Show();
            try
            {
                window.ReportsList.SelectedIndex = 0;
                window.UpdateLayout();
                Assert.True(window.ReportOverviewScroll.Bounds.Height > 70,
                    $"Overview {window.ReportOverviewScroll.Bounds}; list {window.ReportsList.Bounds}; window {window.ClientSize}");
                Assert.True(window.ReportsList.Bounds.Height > 70);
                var value = window.ReportOverviewPanel.GetLogicalDescendants().OfType<SelectableTextBlock>().First();
                value.SelectionStart = 0;
                value.SelectionEnd = 6;
                Assert.False(string.IsNullOrWhiteSpace(value.SelectedText));
                value.SelectionEnd = 0;
                var reportActions = new[] { window.OpenButton, window.RenameButton, window.DeleteButton, window.DetailsButton, window.CopyAllButton };
                foreach (var button in reportActions
                    .Append(window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, ProjectInformationPresentation.CloseLabel))))
                {
                    var point = button.TranslatePoint(new Point(0, 0), window)!.Value;
                    Assert.True(point.Y + button.Bounds.Height <= window.ClientSize.Height);
                    Assert.True(point.X + button.Bounds.Width <= window.ClientSize.Width);
                    Assert.True(button.IsEffectivelyVisible);
                }
                if (minimum)
                {
                    Assert.True(window.ReportOverviewScroll.Extent.Height > window.ReportOverviewScroll.Viewport.Height);
                    var reportScroll = window.ReportsList.GetVisualDescendants().OfType<ScrollViewer>().Single();
                    Assert.True(reportScroll.Extent.Height > reportScroll.Viewport.Height);
                }
                foreach (var index in new[] { 0, 1 })
                {
                    window.Tabs.SelectedIndex = index;
                    window.UpdateLayout();
                    foreach (var button in reportActions)
                        Assert.False(button.IsEffectivelyVisible && ReferenceEquals(TopLevel.GetTopLevel(button), window));
                }
                window.Tabs.SelectedIndex = 2;
                window.UpdateLayout();
                var output = Environment.GetEnvironmentVariable("FTITC_PROJECT_INFORMATION_QA_DIRECTORY")!;
                Directory.CreateDirectory(output);
                Capture(window, Path.Combine(output, filename));
                var viewing = window.ShowDetailsAsync();
                var details = Assert.IsType<ReportDetailsWindow>(Assert.Single(window.OwnedWindows));
                if (minimum)
                {
                    details.Width = details.MinWidth;
                    details.Height = details.MinHeight;
                    Dispatcher.UIThread.RunJobs();
                }
                details.UpdateLayout();
                Assert.True(details.DetailsTextBox.IsReadOnly);
                details.DetailsTextBox.SelectionStart = 0;
                details.DetailsTextBox.SelectionEnd = 6;
                Assert.False(string.IsNullOrWhiteSpace(details.DetailsTextBox.SelectedText));
                details.DetailsTextBox.SelectionEnd = 0;
                var fullTextScroll = details.DetailsTextBox.GetVisualDescendants().OfType<ScrollViewer>().Single();
                if (minimum) Assert.True(fullTextScroll.Extent.Height > fullTextScroll.Viewport.Height);
                foreach (var button in details.GetLogicalDescendants().OfType<Button>())
                {
                    var point = button.TranslatePoint(new Point(0, 0), details)!.Value;
                    Assert.True(point.Y + button.Bounds.Height <= details.ClientSize.Height);
                    Assert.True(point.X + button.Bounds.Width <= details.ClientSize.Width);
                }
                Capture(details, Path.Combine(output, minimum ? "avalonia-report-details-minimum.png" : "avalonia-report-details.png"));
                details.Close();
                await viewing;
            }
            finally { window.Close(); }
        }
    }

    static void Capture(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
        bitmap.Render(window);
        bitmap.Save(path);
    }

    static string OverviewText(ProjectInformationWindow window) => string.Join("\n",
        window.ReportOverviewPanel.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text));

    static string? TabHeader(ProjectInformationWindow window, int index) =>
        (((TabItem)window.Tabs.Items[index]!).Header as TextBlock)?.Text;

    static void Reset()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.Initialize();
        DocumentDirtyTracker.MarkClean();
        AppSettings.AutoSelectReportReferenceExperiments = false;
    }

    public void Dispose() => Dispatcher.UIThread.Invoke(() =>
    {
        AppSettings.AutoSelectReportReferenceExperiments = originalAutomaticReferences;
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
    });

    static AnalysisReport Report(params AnalysisResult[] results)
    {
        var report = new AnalysisReport();
        report.SetResultIds(results.Select(result => result.UniqueID));
        DataManager.AddReport(report);
        return report;
    }

    static T Field<T>(AnalysisReportWindow window, string name) where T : class =>
        (T)typeof(AnalysisReportWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    static void Select(AnalysisReportWindow window, AnalysisResult[] results, ExperimentData[] supporting) =>
        typeof(AnalysisReportWindow).GetMethod("ApplyResultSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, new object[] { results, true, supporting });

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
        var solution = new GlobalSolution(new GlobalSolver { Model = globalModel },
            new List<SolutionInterface> { model.Solution }, convergence, reconstructBootstrap: false);
        globalModel.Solution = solution;
        return (data, new AnalysisResult(solution) { Name = name });
    }
}
