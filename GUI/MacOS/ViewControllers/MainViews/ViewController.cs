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

            DataManager.Init();

            DataManager.DataDidChange += OnDataChanged;
            DataManager.SelectionDidChange += OnSelectionChanged;
            StateManager.UpdateStateDependentUI += StateManager_UpdateStateDependentUI;
            AppDelegate.StartPrintOperation += AppDelegate_StartPrintOperation;

            ShowLoadDataPrompt();
        }

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();

            UpdateGraph();
        }

        private void AppDelegate_StartPrintOperation(object sender, EventArgs e)
        {
            if (StateManager.CurrentState != ProgramState.Load) return;

            GVC.Print();
        }

        private void StateManager_UpdateStateDependentUI(object sender, EventArgs e)
        {
            ShowLoadDataPrompt();
        }

        partial void LoadDataButtonClick(NSObject sender)
        {
            //LoadDataPrompt.Hidden = true;

            AppDelegate.LaunchOpenFileDialog();
        }

        partial void LoadLastFile(NSObject sender)
        {
            DataReaders.DataReader.Read(AppSettings.LastDocumentUrl);

            //LoadDataPrompt.Hidden = true;
        }

        //partial void OpenFileButtonClick(NSObject sender)
        //{
        //    LoadDataPrompt.Hidden = true;
        //}

        private void OnSelectionChanged(object sender, ExperimentData e) => UpdateGraph();
        private void OnDataChanged(object sender, ExperimentData e) => UpdateGraph();

        private void UpdateGraph()
        {
            GVC.Initialize(DataManager.Current);

            UpdateLabel();
        }

        void UpdateLabel()
        {
            if (GVC.Graph != null) InfoLabel.AttributedStringValue = Utilities.MacStrings.FromMarkDownString((GVC.Graph as FileInfoGraph).Info.Aggregate((s1, s2) => s1 + "\n" + s2), InfoLabel.Font);
            else InfoLabel.StringValue = "";
        }

        partial void ClearButtonClick(NSObject sender)
        {
            DataManager.Clear();
        }

        partial void ContinueClick(NSObject sender)
        {
            
        }

        async void ShowLoadDataPrompt()
        {
            LoadLastButton.Enabled = false;

            if (AppSettings.LastDocumentUrl != null)
            {
                var format = DataReaders.DataReader.GetFormat(AppSettings.LastDocumentUrl.Path);

                if (format != DataReaders.ITCDataFormat.Unknown) LoadLastButton.Enabled = true;
            }

            LoadDataPrompt.Hidden = DataManager.DataIsLoaded;
        }
    }
}
