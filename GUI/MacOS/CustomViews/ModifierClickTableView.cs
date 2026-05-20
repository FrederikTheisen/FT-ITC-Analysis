using System;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC
{
    [Register("ModifierClickTableView")]
    public class ModifierClickTableView : NSTableView
    {
        public event EventHandler<ModifierClickTableViewEventArgs> RowModifierClicked;

        public ModifierClickTableView(IntPtr handle) : base(handle)
        {
        }

        public override void MouseDown(NSEvent theEvent)
        {
            var tablePoint = ConvertPointFromView(theEvent.LocationInWindow, null);
            var row = GetRow(tablePoint);
            var col = GetColumn(tablePoint);

            if (row >= 0
                && col >= 0
                && !ClickIsOnControl(row, col, tablePoint)
                && IsToggleModifier(theEvent.ModifierFlags))
            {
                var args = new ModifierClickTableViewEventArgs((int)row, theEvent.ModifierFlags);
                RowModifierClicked?.Invoke(this, args);

                if (args.Handled) return;
            }

            base.MouseDown(theEvent);
        }

        static bool IsToggleModifier(NSEventModifierMask flags)
        {
            return (flags & NSEventModifierMask.ShiftKeyMask) != 0
                || (flags & NSEventModifierMask.CommandKeyMask) != 0;
        }

        bool ClickIsOnControl(nint row, nint col, CGPoint tablePoint)
        {
            var cellView = GetView(col, row, makeIfNecessary: false);
            if (cellView == null) return false;

            var localPoint = cellView.ConvertPointFromView(tablePoint, this);
            var hit = cellView.HitTest(localPoint);

            for (var view = hit; view != null && view != cellView; view = view.Superview)
            {
                if (view is NSButton) return true;
            }

            return hit is NSButton;
        }
    }

    public class ModifierClickTableViewEventArgs : EventArgs
    {
        public int Row { get; }
        public NSEventModifierMask ModifierFlags { get; }
        public bool Handled { get; set; }

        public ModifierClickTableViewEventArgs(int row, NSEventModifierMask modifierFlags)
        {
            Row = row;
            ModifierFlags = modifierFlags;
        }
    }
}
