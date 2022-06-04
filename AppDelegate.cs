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
            FileDialog.AllowedFileTypes = DataReaders.ITCFormatAttribute.GetAllExtensions();
        }

        private void AppDelegate_OpenFileDialog(object sender, EventArgs e)
        {
            StatusBarManager.StartInderminateProgress();

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

            FileDialog = NSOpenPanel.OpenPanel;
            FileDialog.CanChooseFiles = true;
            FileDialog.AllowsMultipleSelection = true;
            FileDialog.CanChooseDirectories = true;
            FileDialog.AllowedFileTypes = DataReaders.ITCFormatAttribute.GetAllExtensions();

            StatusBarManager.StopIndeterminateProgress();
        }

        partial void SaveMenuClick(NSMenuItem sender)
        {
            FTITCWriter.SaveState2();
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
