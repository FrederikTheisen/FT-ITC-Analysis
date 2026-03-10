using System;
using System.Collections.Generic;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
	public class CustomDrawingSegmentedControl : NSSegmentedControl
	{
        NSAttributedString[] attributedStrings;

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
                1 => Utilities.MacStrings.DissociationConstant(Font).MutableCopy() as NSMutableAttributedString,
                _ => Utilities.MacStrings.AssociationConstant(Font).MutableCopy() as NSMutableAttributedString,
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

