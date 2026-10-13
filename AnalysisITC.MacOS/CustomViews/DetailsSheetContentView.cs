using AppKit;

namespace AnalysisITC
{
    sealed class DetailsSheetContentView : NSScrollView
    {
        public DetailsSheetContentView(NSView document, bool verticalScrolling = true)
        {
            DrawsBackground = true;
            BackgroundColor = NSColor.WindowBackground;
            BorderType = NSBorderType.NoBorder;
            HasHorizontalScroller = false;
            HasVerticalScroller = verticalScrolling;
            AutohidesScrollers = true;
            TranslatesAutoresizingMaskIntoConstraints = false;
            document.TranslatesAutoresizingMaskIntoConstraints = false;
            DocumentView = document;
            document.WidthAnchor.ConstraintEqualToAnchor(ContentView.WidthAnchor).Active = true;
            if (!verticalScrolling)
                document.HeightAnchor.ConstraintEqualToAnchor(ContentView.HeightAnchor).Active = true;
        }
    }
}
