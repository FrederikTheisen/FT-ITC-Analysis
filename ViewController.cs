using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using Foundation;

namespace AnalysisITC
{
    public partial class ViewController : NSViewController
    {
        public ViewController(IntPtr handle) : base(handle)
        {
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            // Do any additional setup after loading the view.

            DataManager.Init();

            DataManager.SelectionDidChange += OnSelectionChanged;
            DataManager.DataDidChange += OnDataChanged;
        }

        private void OnSelectionChanged(object sender, ExperimentData e)
        {
            GVC.Initialize(DataManager.Current());
        }

        private void OnDataChanged(object sender, ExperimentData e)
        {
            ClearDataButton.Enabled = DataManager.DataIsLoaded;
            ContinueButton.Enabled = DataManager.DataIsLoaded;

            GVC.Initialize(e);
        }

        partial void ButtonClick(NSButton sender)
        {
            var dlg = NSOpenPanel.OpenPanel;
            dlg.CanChooseFiles = true;
            dlg.AllowsMultipleSelection = true;
            dlg.CanChooseDirectories = true;
            dlg.AllowedFileTypes = DataReaders.ITCFormatAttribute.GetAllExtensions();

            if (dlg.RunModal() == 1)
            {
                // Nab the first file
                var urls = new List<string>();

                foreach (var url in dlg.Urls)
                {
                    Console.WriteLine(url.Path);
                    urls.Add(url.Path);
                }


                DataReaders.DataReader.Read(urls);
            }
        }

        partial void ClearButtonClick(NSObject sender)
        {
            DataManager.Clear();
        }

        partial void ContinueClick(NSObject sender)
        {
            DataManager.SetMode(1);
        }

        public override NSObject RepresentedObject
        {
            get
            {
                return base.RepresentedObject;
            }
            set
            {
                base.RepresentedObject = value;
                // Update the view, if already loaded.
            }
        }
    }
}
