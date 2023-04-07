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

            NSAttributedString str = null;
            switch (segment)
            {
                default:
                case 0: str = Utils.MacStrings.AssociationConstant(Font); break;
                case 1: str = Utils.MacStrings.DissociationConstant(Font); break;
            }

            var rect = str.BoundingRectWithSize(frame.Size, NSStringDrawingOptions.UsesLineFragmentOrigin);

            // Calculate the center point for the string
            var x = frame.Size.Width / 2 - rect.Size.Width / 2 + rect.Location.X;
            var y = frame.Size.Height / 2 - rect.Size.Height / 2 + rect.Location.Y;
            var point = new CGPoint(frame.X + x, y + 2);

            // Draw the attributed string at the center of the rectangle
            str.DrawString(point);
        }
    }
}

