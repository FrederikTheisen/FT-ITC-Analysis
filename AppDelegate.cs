using AppKit;
using Foundation;
using DataReaders;
using System;
using System.Collections.Generic;
using AnalysisITC.GUI.MacOS;
using System.Linq;
using System.Threading.Tasks;

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

        public static event EventHandler OpenFileDialog;
        public static event EventHandler StartPrintOperation;
        public static event EventHandler OpenMergeTool;
        public static event EventHandler OpenSubtractionTool;
        public static event EventHandler ShowHint;
        public static event EventHandler ShowCitation;

        static bool skipDirtyCheckOnNextTerminate;

        NSOpenPanel FileDialog { get; set; }
         
        public static void LaunchOpenFileDialog() => OpenFileDialog.Invoke(null,null);
        public static void CloseAllData() => _ = CloseAllDataAsync();

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

            switch (PromptSaveChanges())
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
                case "saveselected": return DataManager.SelectedIsData;
                case "export": return DataManager.Data.Count > 0;
                case "exportdata": return DataManager.DataIsLoaded;
                case "exportpeaks": return DataManager.AnyDataIsBaselineProcessed;
                case "cleardata": return DataManager.DataIsLoaded;
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
                case "mergetool": return DataManager.Data.Count(data => data.HasThermogram) >= 2;
                case "buffersub": return DataManager.Data.Count >= 2;
            }

            return true;
        }

        [Export("openDocument:")]
        void OpenDocumentMenuClicked(NSObject sender)
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

        public override void WillTerminate(NSNotification notification)
        {
            // Insert code here to tear down your application
        }

        async Task SaveBeforeTerminateAsync()
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

        public static PendingSaveAction PromptSaveChanges()
        {
            var alert = new NSAlert
            {
                MessageText = "Do you want to save changes before closing?",
                InformativeText = "Unsaved changes will be lost.",
                AlertStyle = NSAlertStyle.Warning
            };

            alert.AddButton("Save");
            alert.AddButton("Cancel");
            alert.AddButton("Don't Save");
            alert.Buttons[2].HasDestructiveAction = true;

            return (int)alert.RunModal() switch
            {
                1000 => PendingSaveAction.Save,
                1001 => PendingSaveAction.Cancel,
                _ => PendingSaveAction.Discard
            };
        }

        public static void AllowNextTerminateWithoutPrompt()
        {
            skipDirtyCheckOnNextTerminate = true;
        }

        public static async Task<bool> CloseAllDataAsync()
        {
            if (DataManager.SourceItems == null || DataManager.SourceItems.Count == 0)
            {
                DataManager.Clear();
                DocumentDirtyTracker.MarkClean();
                return true;
            }

            if (DocumentDirtyTracker.IsDirty)
            {
                switch (PromptSaveChanges())
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

            DataManager.Clear();
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
