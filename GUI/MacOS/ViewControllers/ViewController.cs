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
        }

        private void AppDelegate_StartPrintOperation(object sender, EventArgs e)
        {
            if (StateManager.CurrentState != ProgramState.Load) return;

            GVC.Print();
        }

        private void StateManager_UpdateStateDependentUI(object sender, EventArgs e)
        {
            LoadDataPrompt.Hidden = DataManager.DataIsLoaded;
        }

        partial void LoadDataButtonClick(NSObject sender)
        {
            LoadDataPrompt.Hidden = true;

            AppDelegate.LaunchOpenFileDialog();
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
