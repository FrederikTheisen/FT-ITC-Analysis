using System;

using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC
{
    [Register("SheetCommentTextView")]
    public sealed class SheetCommentTextView : NSTextView
    {
        const string Placeholder = "Comment";

        public SheetCommentTextView(IntPtr handle) : base(handle)
        {
            Configure();
        }

        [Export("initWithFrame:")]
        public SheetCommentTextView(CGRect frame) : base(frame)
        {
            Configure();
        }

        void Configure()
        {
            RichText = false;
            ImportsGraphics = false;
            DrawsBackground = false;
            Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);
            TextColor = NSColor.ControlText;
            TextContainerInset = new CGSize(7, 5);
            HorizontallyResizable = false;
            VerticallyResizable = true;
            AllowsUndo = true;
            AutoresizingMask = NSViewResizingMask.WidthSizable;

            if (TextContainer != null)
            {
                TextContainer.WidthTracksTextView = true;
                TextContainer.LineFragmentPadding = 0;
            }
        }

        public override void AwakeFromNib()
        {
            base.AwakeFromNib();
            Configure();
        }

        public override void DidChangeText()
        {
            base.DidChangeText();
            NeedsDisplay = true;
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            base.DrawRect(dirtyRect);
            if (!string.IsNullOrEmpty(String)) return;

            using (var placeholder = new NSAttributedString(
                       Placeholder,
                       new NSStringAttributes
                       {
                           Font = Font
                               ?? NSFont.SystemFontOfSize(
                                   NSFont.SystemFontSize),
                           ForegroundColor = NSColor.PlaceholderText,
                       }))
            {
                var inset = TextContainerInset;
                placeholder.DrawString(new CGRect(
                    inset.Width,
                    inset.Height,
                    Math.Max(0, Bounds.Width - 2 * inset.Width),
                    Math.Max(0, Bounds.Height - 2 * inset.Height)));
            }
        }
    }
}
