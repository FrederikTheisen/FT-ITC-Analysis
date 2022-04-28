// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
	public partial class FinalFigureViewController : NSViewController
	{
		public FinalFigureViewController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            FinalFigureGraphView.PlotSizeChanged += FinalFigureGraphView_PlotSizeChanged;
        }

        public override void ViewDidAppear()
        {
            FinalFigureGraphView.Invalidate();
        }

        private void FinalFigureGraphView_PlotSizeChanged(object sender, EventArgs e)
        {
            var x = BoxView.Frame.Width / 2 - FinalFigureGraph.Frame.Width / 2;
            var y = BoxView.Frame.Height / 2 - FinalFigureGraph.Frame.Height / 2;

            FinalFigureGraph.SetFrameOrigin(new CoreGraphics.CGPoint(x, y));
        }

        partial void ExportGraphButtonClick(NSObject sender)
        {
            FinalFigureGraph.Export();
        }

        public override void ViewWillLayout()
        {
            var x = BoxView.Frame.Width / 2 - FinalFigureGraph.Frame.Width / 2;
            var y = BoxView.Frame.Height / 2 - FinalFigureGraph.Frame.Height / 2;

            FinalFigureGraph.SetFrameOrigin(new CoreGraphics.CGPoint(x, y));

            base.ViewWillLayout();
        }
    }
}
