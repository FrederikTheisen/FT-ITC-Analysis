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

            //DataManager.DataDidChange += OnDataChanged;
            DataManager.SelectionDidChange += DataManager_SelectionDidChange;
            

            DataProcessor.BaselineInterpolationCompleted += DataProcessor_InterpolationCompleted;

            StatusBarManager.ProgressUpdate += OnProgressUpdated;
            StatusBarManager.StatusUpdated += OnStatusUpdated;
            StatusBarManager.SecondaryStatusUpdated += OnSecondaryStatusUpdated;

            StateManager_UpdateStateDependentUI(null, null);
        }

        private void OnSecondaryStatusUpdated(object sender, string e)
        {
            StatusbarSecondaryLabel.StringValue = e;
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

        private void DataProcessor_InterpolationCompleted(object sender, EventArgs e) //TODO probably obsolete
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

                if (!e.HideProgressWheel)
                    return;

                StatusbarProgressIndicator.StopAnimation(this);
                StatusbarProgressIndicator.Hidden = true;
                StopProcessButton.Hidden = true;
            }
            else if (e.Indeterminate)
            {
                StatusbarProgressIndicator.Indeterminate = true;
                StatusbarProgressIndicator.StartAnimation(this);
                StatusbarProgressIndicator.Hidden = false;
                StopProcessButton.Hidden = true;
            }
            else if (e.IsProgressFinished)
            {
                StatusbarProgressIndicator.DoubleValue = 100;

                await Task.Delay(500);

                if (!e.IsProgressFinished)
                    return;

                StatusbarProgressIndicator.StopAnimation(this);
                StatusbarProgressIndicator.Hidden = true;
                StopProcessButton.Hidden = true;
            }
            else
            {
                StatusbarProgressIndicator.Hidden = false;
                StatusbarProgressIndicator.Indeterminate = false;
                StatusbarProgressIndicator.DoubleValue = e.Progress*100;

                StopProcessButton.Hidden = false;
            }
        }

        private void OnDataChanged(object sender, ExperimentData e)
        {
            //if (DataManager.Count == 0)
            //{
            //    StepControl.SetEnabled(false, 1);
            //    StepControl.SetEnabled(false, 2);
            //    StepControl.SetEnabled(false, 3);
            //}

            //DataLoadSegControl.SetEnabled(DataManager.DataIsLoaded, 1);
            //DataLoadSegControl.SetEnabled(DataManager.DataIsLoaded, 2);
        }

        private void OnProgramStateChanged(object sender, ProgramState e)
        {
            //StepControl.SelectedSegment = (int)e;

            //StepControl.SetEnabled(true, (int)e);
        }

        partial void NavigationArrowControlClicked(NSSegmentedControl sender)
        {
            switch (sender.SelectedSegment)
            {
                case 0: AppDelegate.LaunchOpenFileDialog(); break;
                case 2: StateManager.PreviousState(); break;
                case 3: StateManager.NextState(); break;
            }
        }

        partial void DataLoadSegControlClick(NSSegmentedControl sender)
        {
            switch (sender.SelectedSegment)
            {
                case 0: AppDelegate.LaunchOpenFileDialog(); break;
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
            if (!DataManager.DataIsLoaded) AppDelegate.LaunchOpenFileDialog();
            else if (StateManager.CurrentState == 0) StateManager.SetProgramState(ProgramState.Process);
            else if (StateManager.CurrentState == ProgramState.Process && !DataManager.AllDataIsBaselineProcessed)
            {

            }
        }

        partial void StepControlClick(NSSegmentedControl sender)
        {
            var index = (int)sender.SelectedSegment;

            StateManager.SetProgramState((ProgramState)index);
        }

        partial void StopButtonClick(NSObject sender)
        {
            Analysis.StopAnalysisProcess = true;
        }
    }
}
