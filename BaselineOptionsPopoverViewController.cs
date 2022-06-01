// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
	public partial class BaselineOptionsPopoverViewController : NSViewController
	{
		ExperimentData Data => DataManager.Current;

        public static event EventHandler Updated;

		public BaselineOptionsPopoverViewController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();

            if (Data.Processor.Interpolator != null)
            {
                LockButton.Title = Data.Processor.IsLocked ? "Unlock Processor" : "Lock Processor";
                ToSplineButton.Enabled = Data.Processor.Interpolator is not SplineInterpolator;
            }
            else
            {
                LockButton.Enabled = false;
                ToSplineButton.Enabled = false;
            }
        }

        partial void LockAction(NSObject sender)
        {
            Data.Processor.IsLocked = !Data.Processor.IsLocked;

            Updated?.Invoke(this, null);

            DismissViewController(this);
        }

        partial void SplineAction(NSObject sender)
        {
            Data.Processor.Interpolator.ConvertToSpline();

            DismissViewController(this);
        }
    }
}
