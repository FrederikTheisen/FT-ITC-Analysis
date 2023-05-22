using AppKit;
using Foundation;
using DataReaders;
using System;
using System.Collections.Generic;
using AnalysisITC.GUI.MacOS;

namespace AnalysisITC
{
    [Register("AppDelegate")]
    public partial class AppDelegate : NSApplicationDelegate
    {
        public static event EventHandler OpenFileDialog;
        public static event EventHandler StartPrintOperation;

        NSOpenPanel FileDialog { get; set; }
         
        public static void LaunchOpenFileDialog() => OpenFileDialog.Invoke(null,null);

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

        [Action("validateMenuItem:")]
        public bool ValidateMenuItem(NSMenuItem item)
        {
            Console.WriteLine(item.Identifier);
            // Take action based on the menu item type
            // (As specified in its Tag)
            switch (item.Identifier)
            {
                case "saveas": return DataManager.DataIsLoaded;
                case "save": return DataManager.DataIsLoaded && FTITCWriter.IsSaved;
                case "saveselected": return DataManager.SelectedIsData;
                case "exportdata": return DataManager.DataIsLoaded;
                case "exportpeaks": return DataManager.AnyDataIsBaselineProcessed;
                case "cleardata": return DataManager.DataIsLoaded;
                case "duplicatedata": return DataManager.SelectedIsData;
                case "print": return StateManager.StateCanPrint();
                case "undo": return StateManager.StateCanUndo();
                case "selectall": return DataManager.DataIsLoaded;
                case "deselectall": return DataManager.DataIsLoaded;
                case "sortbyname":
                case "sortbytemp": return DataManager.DataIsLoaded;
                case "sortbytype": return DataManager.DataIsLoaded && DataManager.Results.Count > 0;
                case "copyattributes": return DataManager.DataIsLoaded && DataManager.SelectedIsData && DataManager.Current.Attributes.Count > 0;
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
            var escapedstring = Uri.EscapeDataString(filename);
            var url = NSUrl.FromString(escapedstring);

            DataReader.Read(new List<NSUrl> { url });

            return true;
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

        partial void Sort(NSObject sender)
        {
            var mode = (sender as NSMenuItem).Identifier switch
            {
                "sortbytemp" => DataManager.SortMode.Temperature,
                "sortbytype" => DataManager.SortMode.Type,
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
            FTITCWriter.SaveWithPath();
        }

        partial void SaveSelectedMenuClick(NSObject sender)
        {
            
        }

        partial void DuplicateSelectedData(NSObject sender)
        {
            DataManager.DuplicateSelectedData(DataManager.Current);
        }

        partial void ExportAllCheckAction(NSObject sender)
        {
            AppSettings.ExportSelectionMode = AppSettings.ExportSelectionMode switch
            {
                Exporter.ExportDataSelection.SelectedData => Exporter.ExportDataSelection.IncludedData,
                Exporter.ExportDataSelection.IncludedData => Exporter.ExportDataSelection.AllData,
                Exporter.ExportDataSelection.AllData => Exporter.ExportDataSelection.SelectedData,
                _ => Exporter.ExportDataSelection.IncludedData,
            };

            (sender as NSMenuItem).State = AppSettings.ExportSelectionMode switch
            {
                Exporter.ExportDataSelection.SelectedData => NSCellStateValue.Off,
                Exporter.ExportDataSelection.IncludedData => NSCellStateValue.Mixed,
                Exporter.ExportDataSelection.AllData => NSCellStateValue.On,
                _ => NSCellStateValue.Off,
            };
        }

        partial void ExportAction(NSObject sender)
        {
            Exporter.Export(Exporter.ExportType.Data);
        }

        partial void ExportDataClick(NSMenuItem sender)
        {
            Exporter.Export(Exporter.ExportType.Data);
        }

        partial void ExportPeaksAction(NSMenuItem sender)
        {
            Exporter.Export(Exporter.ExportType.Peaks);
        }

        partial void ClearProcessingResult(NSObject sender)
        {
            DataManager.ClearProcessing();
        }

        partial void ClearAllData(NSObject sender)
        {
            var alert = new NSAlert();
            alert.MessageText = "Close all open data. Any unsaved results will be lost.";
            alert.AlertStyle = NSAlertStyle.Critical;
            alert.AddButton("Cancel");
            alert.AddButton("Remove Data");

            alert.Buttons[1].HasDestructiveAction = true;

            if (alert.RunModal() == 1001)
            {
                DataManager.Clear();
            }
        }

        partial void StartSupport(NSObject sender)
        {
            MacSupport.Test();
        }

        public override void WillTerminate(NSNotification notification)
        {
            // Insert code here to tear down your application
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
    }
}
