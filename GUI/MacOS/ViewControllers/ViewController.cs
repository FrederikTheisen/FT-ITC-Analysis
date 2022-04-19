using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using Foundation;
using MathNet.Numerics.LinearAlgebra.Solvers;
using MathNet.Numerics.LinearAlgebra.Double;

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
            //DataManager.DataDidChange += OnDataChanged;

            StateManager.UpdateStateDependentUI += StateManager_UpdateStateDependentUI;
        }

        private void StateManager_UpdateStateDependentUI(object sender, EventArgs e)
        {
            LoadDataPrompt.Hidden = DataManager.DataIsLoaded;
        }

        partial void LoadDataButtonClick(NSObject sender)
        {
            LoadDataPrompt.Hidden = true;

            OpenFileBrowser();
        }

        partial void OpenFileButtonClick(NSObject sender)
        {
            LoadDataPrompt.Hidden = true;
        }

        private void OnSelectionChanged(object sender, ExperimentData e)
        {
            GVC.Initialize(DataManager.Current);

            UpdateLabel();
        }

        private void OnDataChanged(object sender, ExperimentData e)
        {
            GVC.Initialize(e);

            UpdateLabel();
        }

        void UpdateLabel()
        {
            if (GVC.Graph != null) InfoLabel.StringValue = (GVC.Graph as FileInfoGraph).Info.Aggregate((s1, s2) => s1 + "\n" + s2);
            else InfoLabel.StringValue = "";
        }

        void OpenFileBrowser()
        {
            StatusBarManager.StartInderminateProgress();

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

            StatusBarManager.StopIndeterminateProgress();
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
