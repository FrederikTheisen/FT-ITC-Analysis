using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;

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
	public class CustomDrawingSegmentedControl : NSSegmentedControl
	{
        [Export("initWithFrame:")]
        public CustomDrawingSegmentedControl(CGRect frameRect) : base(frameRect)
        {
            
		}

        //public override nint SegmentCount
        //{
        //    get => base.SegmentCount;
        //    set
        //    {
        //        base.SegmentCount = value;
        //        attributedStrings = new NSAttributedString[value];
        //    }
        //}

        //public override void DrawCell(NSCell aCell)
        //{
        //    base.DrawCell(aCell);
        //}

        //public void SetAttributedStringValue(NSAttributedString attstring, int n)
        //{
        //    attributedStrings[n] = attstring;
        //}

        //public override void DrawCellInside(NSCell aCell)
        //{
        //    base.DrawCellInside(aCell);
        //}

        //public override void DrawRect(CGRect dirtyRect)
        //{
        //    base.DrawRect(dirtyRect);

        //    //(Cell as CustomDrawingSegmentedCell).DrawSegment(0, dirtyRect, this);
        //}
    }

    public class CustomKdKaDrawingSegmentedCell : NSSegmentedCell
    {
        public override void DrawSegment(nint segment, CGRect frame, NSView controlView)
        {
            base.DrawSegment(segment, frame, controlView);

            var str = (long)segment switch
            {
                1 => AnalysisITC.UI.MacOS.MacStrings.DissociationConstant(Font).MutableCopy() as NSMutableAttributedString,
                _ => AnalysisITC.UI.MacOS.MacStrings.AssociationConstant(Font).MutableCopy() as NSMutableAttributedString,
            };

            var isSelected = SelectedSegment == segment;

            NSColor color;
            if (isSelected)
                color = NSColor.SelectedMenuItemText; // often closer to selected segmented text
            else
                color = NSColor.Label;

            str.RemoveAttribute(NSStringAttributeKey.ForegroundColor, new NSRange(0, str.Length));
            str.AddAttribute(NSStringAttributeKey.ForegroundColor, color, new NSRange(0, str.Length));

            var rect = str.BoundingRectWithSize(frame.Size, NSStringDrawingOptions.UsesLineFragmentOrigin);

            var x = frame.X + (frame.Width - rect.Width) / 2.0f;
            var y = frame.Y + (frame.Height - rect.Height) / 2.0f + 1.0f;

            str.DrawString(new CGPoint(x, y));
        }
    }
}

