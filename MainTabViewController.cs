// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
	public partial class MainTabViewController : NSTabViewController
	{
		public MainTabViewController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            DataManager.ChangeProgramMode += OnProgramModeChanged;
        }

        private void OnProgramModeChanged(object sender, int e)
        {
            TabView.SelectAt(e);
        }
    }
}