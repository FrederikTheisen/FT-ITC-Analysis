using AppKit;
using Foundation;
using DataReaders;
using System;
using System.Collections.Generic;

namespace AnalysisITC
{
    [Register("AppDelegate")]
    public partial class AppDelegate : NSApplicationDelegate
    {
        public static event EventHandler OpenFileDialog;

        NSOpenPanel FileDialog { get; set; }
         
        public static void LaunchOpenFileDialog() => OpenFileDialog.Invoke(null,null);

        public AppDelegate()
        {
        }

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

            }

            return true;
        }

        [Export("openDocument:")]
        void OpenDocumentMenuClicked(NSObject sender)
        {
            AppDelegate_OpenFileDialog(sender, null);
        }

        private void AppDelegate_OpenFileDialog(object sender, EventArgs e)
        {
            StatusBarManager.StartInderminateProgress();

            FileDialog = NSOpenPanel.OpenPanel;
            FileDialog.CanChooseFiles = true;
            FileDialog.AllowsMultipleSelection = true;
            FileDialog.CanChooseDirectories = true;
            FileDialog.AllowedContentTypes = DataReaders.ITCFormatAttribute.GetAllUTTypes();

            if (FileDialog.RunModal() == 1)
            {
                var urls = new List<string>();

                foreach (var url in FileDialog.Urls)
                {
                    Console.WriteLine(url.Path);
                    urls.Add(url.Path);
                }

                DataReaders.DataReader.Read(urls);
            }

            FileDialog.Dispose();

            StatusBarManager.StopIndeterminateProgress();
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
            throw new NotImplementedException();
        }

        partial void ExportAllCheckAction(NSObject sender)
        {
            
        }

        partial void ExportDataClick(NSMenuItem sender)
        {
            throw new NotImplementedException();
        }

        partial void ExportPeaksAction(NSMenuItem sender)
        {
            throw new NotImplementedException();
        }

        partial void ClearProcessingResult(NSObject sender)
        {
            
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

        public override void WillTerminate(NSNotification notification)
        {
            // Insert code here to tear down your application
        }
    }
}
