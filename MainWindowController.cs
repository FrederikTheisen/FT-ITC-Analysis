// This file has been autogenerated from a class added in the UI designer.

using System;
using Foundation;
using AppKit;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AnalysisITC
{
	public partial class MainWindowController : NSWindowController
	{
		public MainWindowController (IntPtr handle) : base (handle)
		{
		}

        public override void WindowDidLoad()
        {
            base.WindowDidLoad();

            StateManager.ProgramStateChanged += OnProgramStateChanged;
            StateManager.UpdateStateDependentUI += StateManager_UpdateStateDependentUI;

            DataManager.DataDidChange += OnDataChanged;
            DataManager.SelectionDidChange += DataManager_SelectionDidChange;
            DataProcessor.BaselineInterpolationCompleted += DataProcessor_InterpolationCompleted;

            StatusBarManager.ProgressUpdate += OnProgressUpdated;
            StatusBarManager.StatusUpdated += OnStatusUpdated;

            StateManager_UpdateStateDependentUI(null, null);
        }

        private void StateManager_UpdateStateDependentUI(object sender, EventArgs e)
        {
            NavigationArrowControl.SetEnabled(StateManager.PreviousState(true), 2);
            NavigationArrowControl.SetEnabled(StateManager.NextState(true), 3);
        }

        private void OnStatusUpdated(object sender, string e)
        {
            StatusbarPrimaryLabel.StringValue = e;
        }

        private void DataProcessor_InterpolationCompleted(object sender, EventArgs e)
        {
            ProcessSegControl.SetEnabled(DataManager.Current.Processor.BaselineCompleted, 1);
            ProcessSegControl.SetEnabled(DataManager.AllDataIsBaselineProcessed, 2);
        }

        private void DataManager_SelectionDidChange(object sender, ExperimentData e)
        {
            if (e == null)
            {
                ProcessSegControl.SetEnabled(false, 1);
                AnalysisSegControl.SetEnabled(false, 1);
            }
            else
            {
                ProcessSegControl.SetEnabled(e.Processor.BaselineCompleted, 1);
                AnalysisSegControl.SetEnabled(e.Processor.BaselineCompleted, 1);
            }
        }

        private async void OnProgressUpdated(object sender, ProgressIndicatorEventData e)
        {
            if (e.HideProgressWheel)
            {
                await Task.Delay(100);

                if (!e.HideProgressWheel) return;

                StatusbarProgressIndicator.StopAnimation(this);
                StatusbarProgressIndicator.Hidden = true;
            }
            else if (e.InDeterminate)
            {
                StatusbarProgressIndicator.Indeterminate = true;
                StatusbarProgressIndicator.StartAnimation(this);
                StatusbarProgressIndicator.Hidden = false;
            }
            else if (e.IsProgressFinished)
            {
                StatusbarProgressIndicator.DoubleValue = 100;

                await Task.Delay(500);

                if (!e.IsProgressFinished) return;

                StatusbarProgressIndicator.StopAnimation(this);
                StatusbarProgressIndicator.Hidden = true;
            }
            else
            {
                StatusbarProgressIndicator.Hidden = false;
                StatusbarProgressIndicator.Indeterminate = false;
                StatusbarProgressIndicator.DoubleValue = e.Progress*100;
            }
        }

        private void OnDataChanged(object sender, ExperimentData e)
        {
            if (DataManager.Count == 0)
            {
                StepControl.SetEnabled(false, 1);
                StepControl.SetEnabled(false, 2);
                StepControl.SetEnabled(false, 3);
            }

            DataLoadSegControl.SetEnabled(DataManager.DataIsLoaded, 1);
            DataLoadSegControl.SetEnabled(DataManager.DataIsLoaded, 2);
        }

        private void OnProgramStateChanged(object sender, ProgramState e)
        {
            StepControl.SelectedSegment = (int)e;

            StepControl.SetEnabled(true, (int)e);

            //Window.Toolbar.RemoveItem(Window.Toolbar.Items.Length - 1);

            switch (e)
            {
                case ProgramState.Load:
                    //Window.Toolbar.InsertItem("LoadControl", Window.Toolbar.Items.Length - 1);
                   break;
                case ProgramState.Process:
                    //Window.Toolbar.InsertItem("ProcessingControl", Window.Toolbar.Items.Length - 1);
                    break;
                case ProgramState.Analyze:
                     //TODO move to separate function and only allow change to analysis mode if all are integrated;
                    //Window.Toolbar.InsertItem("AnalysisControl", Window.Toolbar.Items.Length - 1); break;
                    break;
                case ProgramState.Publish:
                    break;
            }
        }

        partial void NavigationArrowControlClicked(NSSegmentedControl sender)
        {
            switch (sender.SelectedSegment)
            {
                case 0: OpenFileBrowser(); break;
                case 2: StateManager.PreviousState(); break;
                case 3: StateManager.NextState(); break;
            }
        }

        partial void DataLoadSegControlClick(NSSegmentedControl sender)
        {
            switch (sender.SelectedSegment)
            {
                case 0: OpenFileBrowser(); break;
                case 1: DataManager.Clear(); break;
                case 2: StateManager.SetProgramState(ProgramState.Process); break;
            }
        }

        partial void ProcessSegControlClick(NSSegmentedControl sender)
        {
            switch (sender.SelectedSegment)
            {
                case 0: StateManager.SetProgramState(ProgramState.Load); break;
                case 1: DataManager.CopySelectedProcessToAll(); break;
                case 2: StateManager.SetProgramState(ProgramState.Analyze); break;
            }
        }

        partial void AnalysisSegControlClicked(NSSegmentedControl sender)
        {
            switch (sender.SelectedSegment)
            {
                case 0: StateManager.SetProgramState(ProgramState.Process); break;
                case 1: break;
                case 3: StateManager.SetProgramState(ProgramState.Publish); break;
            }
        }

        partial void ContextButtonClick(NSObject sender)
        {
            if (!DataManager.DataIsLoaded) OpenFileBrowser();
            else if (StateManager.CurrentState == 0) StateManager.SetProgramState(ProgramState.Process);
            else if (StateManager.CurrentState == ProgramState.Process && !DataManager.AllDataIsBaselineProcessed)
            {

            }
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

            StatusBarManager.StopInderminateProgress();
        }

        partial void StepControlClick(NSSegmentedControl sender)
        {
            var index = (int)sender.SelectedSegment;

            StateManager.SetProgramState((ProgramState)index);
        }
    }
}
