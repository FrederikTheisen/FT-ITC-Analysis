using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS.CustomViews
{
	public partial class AutoExpandingTextView2 : AppKit.NSTextField
	{
        const float MinimumFieldHeight = 22;
        const int MaximumVisibleLines = 3;
        nfloat lastMeasuredWidth;

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
            if (Cell != null)
            {
                Cell.Wraps = true;
                Cell.Scrollable = false;
                Cell.UsesSingleLineMode = false;
            }

            LineBreakMode = NSLineBreakMode.ByWordWrapping;
            MaximumNumberOfLines = MaximumVisibleLines;
            HorizontalContentSizeConstraintActive = false;
            SetContentCompressionResistancePriority(
                1000,
                NSLayoutConstraintOrientation.Vertical);
		}

        #endregion

        public override CGSize IntrinsicContentSize
        {
			get
			{
                var intrinsicSize = base.IntrinsicContentSize;
                if (Cell == null || !Cell.Wraps)
                    return intrinsicSize;

                var width = Bounds.Width > 0
                    ? Bounds.Width
                    : Frame.Width;
                if (width <= 0)
                    return intrinsicSize;

                var font = Font
                    ?? NSFont.SystemFontOfSize(
                        NSFont.SystemFontSize);
                var lineHeight =
                    (nfloat)Math.Ceiling(
                        font.Ascender
                        - font.Descender
                        + font.Leading);
                var singleLineHeight = Math.Max(
                    MinimumFieldHeight,
                    intrinsicSize.Height);
                var maximumFieldHeight =
                    singleLineHeight
                    + (MaximumVisibleLines - 1) * lineHeight;
                var measuredHeight = Cell.CellSizeForBounds(
                    new CGRect(0, 0, width, 10000)).Height;

                return new CGSize(
                    intrinsicSize.Width,
                    Math.Min(
                        maximumFieldHeight,
                        Math.Max(
                        MinimumFieldHeight,
                            measuredHeight)));
			}
		}

        public override void SetFrameSize(CGSize newSize)
        {
            var widthChanged =
                Math.Abs(newSize.Width - lastMeasuredWidth) > 0.5;
            base.SetFrameSize(newSize);

            if (!widthChanged) return;

            lastMeasuredWidth = newSize.Width;
            InvalidateIntrinsicContentSize();
        }

        public override void DidChange(NSNotification notification)
        {
			base.DidChange(notification);
			InvalidateIntrinsicContentSize();
        }
    }
}
