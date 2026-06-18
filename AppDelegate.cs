using AppKit;
using Foundation;
using DataReaders;
using System;
using System.Collections.Generic;
using AnalysisITC.GUI.MacOS;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UniformTypeIdentifiers;

namespace AnalysisITC
{
    [Register("AppDelegate")]
    public partial class AppDelegate : NSApplicationDelegate
    {
        public enum PendingSaveAction
        {
            Save,
            Cancel,
            Discard
        }

        public enum SavePromptReason
        {
            CloseWindow,
            QuitApplication,
            ClearAllData
        }

        public enum ProjectLoadAction
        {
            Replace,
            Append,
            Cancel
        }

        public static event EventHandler OpenFileDialog;
        public static event EventHandler StartPrintOperation;
        public static event EventHandler OpenMergeTool;
        public static event EventHandler OpenSubtractionTool;
        public static event EventHandler OpenResultExporterTool;
        public static event EventHandler ShowHint;
        public static event EventHandler ShowCitation;

        private static bool skipDirtyCheckOnNextTerminate;

        private NSOpenPanel FileDialog { get; set; }

        public static void LaunchOpenFileDialog() => OpenFileDialog.Invoke(null, null);
        public static void CloseAllData() => _ = CloseAllDataAsync();
        public static void LaunchResultExporter() => OpenResultExporterTool?.Invoke(null, null);

        public AppDelegate()
        {
            
        }

        public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

        public override void DidFinishLaunching(NSNotification notification)
        {
            OpenFileDialog += AppDelegate_OpenFileDialog;

            FileDialog = NSOpenPanel.OpenPanel;
            FileDialog.CanChooseFiles = true;
            FileDialog.AllowsMultipleSelection = true;
            FileDialog.CanChooseDirectories = true;
            FileDialog.AllowedContentTypes = DataReaders.ITCFormatAttribute.GetAllUTTypes();

#if DEBUG
            UnhideProcessedTandemCsvImporter();
            AddTandemMixingScanMenuItem();
#endif
        }

        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
        {
            if (skipDirtyCheckOnNextTerminate)
            {
                skipDirtyCheckOnNextTerminate = false;
                return NSApplicationTerminateReply.Now;
            }

            if (!DocumentDirtyTracker.IsDirty)
            {
                return NSApplicationTerminateReply.Now;
            }

            switch (PromptSaveChanges(SavePromptReason.QuitApplication))
            {
                case PendingSaveAction.Save:
                    _ = SaveBeforeTerminateAsync();
                    return NSApplicationTerminateReply.Later;
                case PendingSaveAction.Cancel:
                    return NSApplicationTerminateReply.Cancel;
                default:
                    return NSApplicationTerminateReply.Now;
            }
        }

        [Action("validateMenuItem:")]
        public bool ValidateMenuItem(NSMenuItem item)
        {
            // Take action based on the menu item type
            // (As specified in its Tag)
            switch (item.Identifier)
            {
                case "saveas": return DataManager.DataIsLoaded;
                case "save": return DataManager.DataIsLoaded;
                case "toolbarsave": return DataManager.DataIsLoaded;
                case "saveselected": return DataManager.SelectedIsData;
                case "export": return DataManager.Data.Count > 0;
                case "toolbarexport": return DataManager.Data.Count > 0;
                case "exportdata": return DataManager.DataIsLoaded;
                case "exportpeaks": return DataManager.AnyDataIsBaselineProcessed;
                case "cleardata": return DataManager.DataIsLoaded;
                case "toolbarcleardata": return DataManager.DataIsLoaded;
                case "duplicatedata": return DataManager.SelectedIsData;
                case "print": return StateManager.StateCanPrint();
                case "undo": return StateManager.StateCanUndo();
                case "selectall": return DataManager.DataIsLoaded;
                case "deselectall": return DataManager.DataIsLoaded;
                case "sortbyname":
                case "sortbytemp":
                case "sortbydate": return DataManager.DataIsLoaded;
                case "sortbytype": return DataManager.DataIsLoaded && DataManager.Results.Count > 0;
                case "sortbyprotonation": return DataManager.DataIsLoaded && DataManager.Data.Any(d => d.Attributes.Count > 0);
                case "sortbyionic": return DataManager.DataIsLoaded && DataManager.Data.Any(d => d.Attributes.Count > 0);
                case "copyattributes": return DataManager.DataIsLoaded && DataManager.SelectedIsData && DataManager.Current.Attributes.Count > 0;
                case "mergetool":
                case "toolbarmergetool": return DataManager.TandemMergerToolEnabled;
                case "buffersub": return DataManager.Data.Count >= 2;
                case "toolbarbuffersub": return DataManager.Data.Count >= 2;
                case "resultexporter": return DataManager.Results.Count > 0;
                case "toolbarresultexporter": return DataManager.Results.Count > 0;
            }

            return true;
        }

        [Export("openDocument:")]
        private void OpenDocumentMenuClicked(NSObject sender)
        {
            AppDelegate_OpenFileDialog(sender, null);
        }

        [Export("application:openFile:")]
        public override bool OpenFile(NSApplication sender, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            var url = NSUrl.CreateFileUrl(filename, null);
            if (url == null)
            {
                return false;
            }

            DataReader.Read(url);

            return true;
        }

        partial void OpenMergeToolAction(NSObject sender)
        {
            OpenMergeTool.Invoke(this, null);
        }

        partial void OpenBufferSubTool(NSObject sender)
        {
            OpenSubtractionTool?.Invoke(this, null);
        }

        partial void OpenResultExporter(NSObject sender)
        {
            OpenResultExporterTool?.Invoke(this, null);
        }

        partial void Print(NSMenuItem sender)
        {
            StartPrintOperation?.Invoke(null, null);
        }

        partial void Undo(NSObject sender)
        {
            StateManager.Undo();
        }

        partial void SetIncludeAll(NSObject sender)
        {
            DataManager.SetAllIncludeState(true);
        }

        partial void SetIncludeNone(NSObject sender)
        {
            DataManager.SetAllIncludeState(false);
        }

        partial void InvertActive(NSObject sender)
        {
            var included = DataManager.IncludedData;

            DataManager.SetAllIncludeState(true);

            foreach (var dat in included) dat.Include = false;

            DataManager.InvokeDataInclusionDidChange();
        }

        partial void Sort(NSObject sender)
        {
            var mode = (sender as NSMenuItem).Identifier switch
            {
                "sortbytemp" => DataManager.SortMode.Temperature,
                "sortbytype" => DataManager.SortMode.Type,
                "sortbyionic" => DataManager.SortMode.IonicStrength,
                "sortbyprotonation" => DataManager.SortMode.ProtonationEnthalpy,
                "sortbydate" => DataManager.SortMode.Date,
                _ => DataManager.SortMode.Name,
            };
            DataManager.SortContent(mode);
        }

        partial void CopyAttributesToAll(NSObject sender)
        {
            DataManager.CopySelectedAttributesToAll();
        }

        private void AppDelegate_OpenFileDialog(object sender, EventArgs e)
        {
            FileDialog = NSOpenPanel.OpenPanel;
            FileDialog.CanChooseFiles = true;
            FileDialog.AllowsMultipleSelection = true;
            FileDialog.CanChooseDirectories = true;
            FileDialog.AllowedContentTypes = DataReaders.ITCFormatAttribute.GetAllUTTypes();

            if (FileDialog.RunModal() == 1)
            {
                DataReaders.DataReader.Read(FileDialog.Urls);
            }

            FileDialog.Dispose();
        }

#if DEBUG
        private void UnhideProcessedTandemCsvImporter()
        {
            var helpMenu = NSApplication.SharedApplication.MainMenu?
                .Items.FirstOrDefault(item => item.Title == "Help")?
                .Submenu;
            if (helpMenu == null) return;

            var importer = helpMenu.Items.FirstOrDefault(item => item.Identifier == "importprocessedtandemcsv");

            if (importer != null) importer.Hidden = false;
        }

        private void AddTandemMixingScanMenuItem()
        {
            const string identifier = "runtandemmixingscan";
            var helpMenu = NSApplication.SharedApplication.MainMenu?
                .Items.FirstOrDefault(item => item.Title == "Help")?
                .Submenu;
            if (helpMenu == null || helpMenu.Items.Any(item => item.Identifier == identifier)) return;

            var scanItem = new NSMenuItem("Run Tandem Back-Mixing Scan...", async (_, __) => await RunTandemMixingScan())
            {
                Identifier = identifier,
            };
            helpMenu.AddItem(scanItem);
        }

        private async Task RunTandemMixingScan()
        {
            var sources = DataManager.IncludedData
                .Where(experiment => !experiment.IsTandemExperiment)
                .ToList();
            if (sources.Count != 3)
                throw new InvalidOperationException("Include exactly three non-tandem base experiments before running the tandem back-mixing scan.");

            var removeOverflowModes = PromptTandemMixingScanSettings();
            if (removeOverflowModes == null) return;

            var savePanel = NSSavePanel.SavePanel;
            savePanel.Title = "Save Tandem Back-Mixing RMSD Matrix";
            savePanel.NameFieldStringValue = $"tandem_mixing_scan_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            savePanel.AllowedFileTypes = new[] { "csv" };
            savePanel.CanCreateDirectories = true;

            if ((int)savePanel.RunModal() != (int)NSModalResponse.OK || savePanel.Url == null)
            {
                savePanel.Dispose();
                return;
            }

            var matrixPath = savePanel.Url.Path;
            savePanel.Dispose();
            var scanFractions = TandemMixingScanner.MixingFractionsForStep(0.0, 1.0, 0.01);
            var gridSize = scanFractions.Count;
            var pointsPerScan = gridSize * gridSize;
            var totalPoints = pointsPerScan * removeOverflowModes.Count;

            StatusBarManager.SetStatus("Running two-dimensional tandem back-mixing scan...", 0, priority: 1);
            StatusBarManager.SetSecondaryStatus($"0/{totalPoints}", 0);
            StatusBarManager.SetProgress(0);

            try
            {
                var results = await Task.Run(() =>
                {
                    var scanResults = new List<(bool removeOverflow, TandemMixingScanResult result)>();

                    for (var scanIndex = 0; scanIndex < removeOverflowModes.Count; scanIndex++)
                    {
                        var index = scanIndex;
                        var removeOverflow = removeOverflowModes[index];
                        var settings = TandemConcatenation.BackMixingSettings.MicroCalDefault();
                        settings.UseBackMixingMethod = true;
                        settings.DidRemoveOverflow = removeOverflow;

                        var result = TandemMixingScanner.Run(
                            sources,
                            settings,
                            (completed, total) => NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                            {
                                var overallCompleted = index * total + completed;
                                var overallTotal = total * removeOverflowModes.Count;
                                StatusBarManager.SetProgress(overallCompleted / (double)overallTotal);
                                StatusBarManager.SetSecondaryStatus($"{overallCompleted}/{overallTotal}", 0);
                            }),
                            scanFractions);
                        scanResults.Add((removeOverflow, result));
                    }

                    return scanResults;
                });

                foreach (var (removeOverflow, result) in results)
                {
                    var suffix = results.Count > 1
                        ? removeOverflow ? "_remove_overflow" : "_keep_overflow"
                        : "";
                    var resultMatrixPath = AddFileNameSuffix(matrixPath, suffix);
                    var resultSummaryPath = AddFileNameSuffix(matrixPath, suffix + "_summary");

                    File.WriteAllText(resultMatrixPath, result.BuildMatrixCsv());
                    File.WriteAllText(resultSummaryPath, result.BuildSummaryCsv());
                }

                StatusBarManager.SetProgress(1);
                StatusBarManager.SetSecondaryStatus("", 0);
                StatusBarManager.SetStatus(
                    results.Count > 1
                        ? $"Tandem back-mixing scans saved with prefix: {Path.GetFileNameWithoutExtension(matrixPath)}"
                        : $"Tandem back-mixing scan saved: {Path.GetFileName(matrixPath)}",
                    6000,
                    priority: 1);
            }
            catch (Exception ex)
            {
                StatusBarManager.ClearAppStatus();
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        private List<bool> PromptTandemMixingScanSettings()
        {
            var accessory = new NSView(new CoreGraphics.CGRect(0, 0, 340, 48));
            var removeOverflow = MakeTandemMixingScanCheckbox("Scan with overflow liquid removal", 26);
            var keepOverflow = MakeTandemMixingScanCheckbox("Scan without overflow liquid removal", 0);
            accessory.AddSubview(removeOverflow);
            accessory.AddSubview(keepOverflow);

            using var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = "Tandem Back-Mixing Scan Settings",
                InformativeText = "Each selected mode scans both transitions from 0% to 100% in 1% steps.",
                AccessoryView = accessory,
            };
            alert.AddButton("Run Scan");
            alert.AddButton("Cancel");
            alert.Layout();

            if ((int)alert.RunModal() != 1000) return null;

            var modes = new List<bool>();
            if (removeOverflow.State == NSCellStateValue.On) modes.Add(true);
            if (keepOverflow.State == NSCellStateValue.On) modes.Add(false);
            if (modes.Count == 0)
                throw new InvalidOperationException("Select at least one overflow liquid removal mode.");

            return modes;
        }

        private NSButton MakeTandemMixingScanCheckbox(string title, nfloat y)
        {
            var checkbox = new NSButton(new CoreGraphics.CGRect(0, y, 330, 20))
            {
                Title = title,
                State = NSCellStateValue.On,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };
            checkbox.SetButtonType(NSButtonType.Switch);
            return checkbox;
        }

        private string AddFileNameSuffix(string path, string suffix)
        {
            return Path.Combine(
                Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + suffix + Path.GetExtension(path));
        }
#endif

        [Action("ImportProcessedTandemCsv:")]
        private void ImportProcessedTandemCsv(NSObject sender)
        {
            var dialog = NSOpenPanel.OpenPanel;
            dialog.Title = "Import Processed Tandem CSV";
            dialog.CanChooseFiles = true;
            dialog.CanChooseDirectories = false;
            dialog.AllowsMultipleSelection = true;
            dialog.AllowedContentTypes = UTType.GetTypes("csv", UTTagClass.FilenameExtension, UTTypes.Data);

            if ((int)dialog.RunModal() != 1)
            {
                dialog.Dispose();
                return;
            }

            StatusBarManager.SetStatus("Reading processed tandem data...", 0);
            StatusBarManager.StartInderminateProgress();

            try
            {
                var settings = new ProcessedTandemCsvImportSettings
                {
                    CellConcentration = 125e-6,
                    SyringeConcentration = 1e-3,
                    RegularInjectionVolume = 3e-6,
                };
                var reconstructed = ProcessedTandemCsvReader.ReadExperiments(
                    dialog.Urls.Select(url => url.Path),
                    settings);
                var valid = reconstructed
                    .Where(ImportValidator.ValidateData)
                    .Cast<ITCDataContainer>()
                    .ToArray();

                if (valid.Length > 0)
                {
                    using (DocumentDirtyTracker.Suspend())
                    {
                        DataManager.AddData(valid);
                        DataManager.ApplyOptions();
                    }

                    DocumentDirtyTracker.MarkDirty();
                    StatusBarManager.SetStatus($"Imported {valid.Length} reconstructed experiments.", 4000);
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
            finally
            {
                StatusBarManager.StopIndeterminateProgress();
                dialog.Dispose();
            }
        }

        partial void SaveAsMenuClick(NSObject sender)
        {
            FTITCWriter.SaveState2();
        }

        partial void SaveMenuClick(NSMenuItem sender)
        {
            if (FTITCWriter.IsSaved)
            {
                FTITCWriter.SaveWithPath();
            }
            else
            {
                FTITCWriter.SaveState2();
            }
        }

        partial void SaveSelectedMenuClick(NSObject sender)
        {
            if (!DataManager.SelectedIsData || DataManager.Current == null)
            {
                return;
            }

            FTITCWriter.SaveSelected(DataManager.Current);
        }

        partial void DuplicateSelectedData(NSObject sender)
        {
            DataManager.DuplicateSelectedData(DataManager.Current);
        }

        partial void ExportAllCheckAction(NSObject sender)
        {
            AppSettings.ExportSelectionMode = AppSettings.ExportSelectionMode switch
            {
                ExportDataSelection.SelectedData => ExportDataSelection.IncludedData,
                ExportDataSelection.IncludedData => ExportDataSelection.AllData,
                ExportDataSelection.AllData => ExportDataSelection.SelectedData,
                _ => ExportDataSelection.IncludedData,
            };

            (sender as NSMenuItem).State = AppSettings.ExportSelectionMode switch
            {
                ExportDataSelection.SelectedData => NSCellStateValue.Off,
                ExportDataSelection.IncludedData => NSCellStateValue.Mixed,
                ExportDataSelection.AllData => NSCellStateValue.On,
                _ => NSCellStateValue.Off,
            };
        }

        partial void ExportAction(NSObject sender)
        {
            Exporter.Export(ExportType.Data);
        }

        partial void ExportDataClick(NSMenuItem sender)
        {
            Exporter.Export(ExportType.Data);
        }

        partial void ExportPeaksAction(NSMenuItem sender)
        {
            Exporter.Export(ExportType.Peaks);
        }

        partial void ClearProcessingResult(NSObject sender)
        {
            if (DataManager.Results.Count == 0) return;
            if (!ConfirmationDialog.ConfirmRemoveOrDelete(
                "Confirm Delete All Results",
                $"Are you sure you wish to delete all {DataManager.Results.Count} analysis results?",
                "Delete All Results")) return;

            DataManager.ClearProcessing();
        }

        partial void ClearAllData(NSObject sender)
        {
            CloseAllData();
        }

        partial void StartSupport(NSObject sender)
        {
            MacSupport.StartSupportEmail();
        }

        partial void CopySupportReport(NSObject sender)
        {
            MacSupport.CopySupportReportToClipboard();
        }

        partial void OpenSourceRepository(NSObject sender)
        {
            NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(CitationInfo.SoftwareRepositoryUrl));
        }

        public override void WillTerminate(NSNotification notification)
        {
            // Insert code here to tear down your application
        }

        private async Task SaveBeforeTerminateAsync()
        {
            var didSave = FTITCWriter.IsSaved
                ? await FTITCWriter.SaveWithPathAsync()
                : await FTITCWriter.SaveState2Async();

            NSApplication.SharedApplication.ReplyToApplicationShouldTerminate(didSave);
        }

        public static bool PromptOverwrite(string message, string peacebtn = "Keep", string destructbtn = "Overwrite")
        {
            var alert = new NSAlert();
            alert.MessageText = message;
            alert.AlertStyle = NSAlertStyle.Critical;
            alert.AddButton(peacebtn);
            alert.AddButton(destructbtn);

            alert.Buttons[1].HasDestructiveAction = true;

            return (alert.RunModal() == 1001);
        }

        public static PendingSaveAction PromptSaveChanges(SavePromptReason reason = SavePromptReason.CloseWindow)
        {
            var (messageText, informativeText, discardButtonText) = reason switch
            {
                SavePromptReason.QuitApplication => (
                    "Do you want to save changes before quitting?",
                    "Unsaved changes will be lost if you quit without saving.",
                    "Don't Save"),
                SavePromptReason.ClearAllData => (
                    "Do you want to save changes before clearing all data?",
                    "Clearing all data will remove the current project from the program. Unsaved changes will be lost.",
                    "Clear All"),
                _ => (
                    "Do you want to save changes before closing?",
                    "Unsaved changes will be lost if you close without saving.",
                    "Don't Save")
            };

            var alert = new NSAlert
            {
                MessageText = messageText,
                InformativeText = informativeText,
                AlertStyle = NSAlertStyle.Warning
            };

            alert.AddButton("Save");
            alert.AddButton("Cancel");
            alert.AddButton(discardButtonText);
            alert.Buttons[2].HasDestructiveAction = true;

            return (int)alert.RunModal() switch
            {
                1000 => PendingSaveAction.Save,
                1001 => PendingSaveAction.Cancel,
                _ => PendingSaveAction.Discard
            };
        }

        public static ProjectLoadAction PromptProjectLoadAction()
        {
            var alert = new NSAlert
            {
                MessageText = "Load project into the current session?",
                InformativeText = "You can replace the current data before loading this project, or append the project contents to what is already open.",
                AlertStyle = NSAlertStyle.Warning
            };

            alert.AddButton("Replace Data");
            alert.AddButton("Append");
            alert.AddButton("Cancel");
            alert.Buttons[0].HasDestructiveAction = true;

            return (int)alert.RunModal() switch
            {
                1000 => ProjectLoadAction.Replace,
                1001 => ProjectLoadAction.Append,
                _ => ProjectLoadAction.Cancel
            };
        }

        public static void AllowNextTerminateWithoutPrompt()
        {
            skipDirtyCheckOnNextTerminate = true;
        }

        public static async Task<bool> CloseAllDataAsync(DataClearMode clearMode = DataClearMode.RecordUndo)
        {
            if (DataManager.SourceItems == null || DataManager.SourceItems.Count == 0)
            {
                DataManager.Clear(clearMode);
                DocumentDirtyTracker.MarkClean();
                return true;
            }

            if (DocumentDirtyTracker.IsDirty)
            {
                switch (PromptSaveChanges(SavePromptReason.ClearAllData))
                {
                    case PendingSaveAction.Save:
                        {
                            var didSave = FTITCWriter.IsSaved
                                ? await FTITCWriter.SaveWithPathAsync()
                                : await FTITCWriter.SaveState2Async();

                            if (!didSave) return false;
                            break;
                        }
                    case PendingSaveAction.Cancel:
                        return false;
                    case PendingSaveAction.Discard:
                        break;
                }
            }

            if (!ConfirmationDialog.ConfirmRemoveOrDelete(
                "Confirm Clear All Data",
                $"Are you sure you wish to clear all {DataManager.SourceItems.Count} data and results?",
                "Clear All Data")) return false;

            DataManager.Clear(clearMode);
            DocumentDirtyTracker.MarkClean();

            return true;
        }

        partial void OpenHint(NSObject sender)
        {
            ShowHint?.Invoke(null, null);
        }

        partial void OpenCitation(NSObject sender)
        {
            ShowCitation?.Invoke(this, null);
        }
    }
}
