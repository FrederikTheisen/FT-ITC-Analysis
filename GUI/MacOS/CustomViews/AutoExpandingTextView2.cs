using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
	public partial class AutoExpandingTextView2 : AppKit.NSTextField
	{
		#region Constructors

		// Called when created from unmanaged code
		public AutoExpandingTextView2 (IntPtr handle) : base (handle)
		{
			Initialize ();
		}

		// Called when created directly from a XIB file
		[Export ("initWithCoder:")]
		public AutoExpandingTextView2 (NSCoder coder) : base (coder)
		{
			Initialize ();
		}

        [Export("initWithFrame:")]
        public AutoExpandingTextView2(CGRect frameRect) : base(frameRect)
        {
            Initialize();
        }

        // Shared initialization code
        void Initialize ()
		{
		}

        #endregion

        public override CGSize IntrinsicContentSize
        {
			get
			{
				// Guard the cell exists and wraps
				if (Cell != null && Cell.Wraps)
				{
					// Use intrinsic width to jive with autolayout
					var width = base.IntrinsicContentSize.Width;

					// Set the frame height to a reasonable number
					this.Frame = new CGRect(this.Frame.Location, new CGSize(this.Frame.Width, 750.0));

					// Calculate height
					nfloat height = Cell.CellSizeForBounds(this.Frame).Height;

					return new CGSize(width, height);
				}
				else
				{
					return base.IntrinsicContentSize;
				}
			}
		}

        public override void DidChange(NSNotification notification)
        {
			base.DidChange(notification);
			InvalidateIntrinsicContentSize();
        }
    }
}
