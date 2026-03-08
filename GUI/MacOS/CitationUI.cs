using System;
using AnalysisITC;
using AppKit;
using CoreGraphics;
using Foundation;

static class CitationUI
{
    public static void ShowCitationDialog(NSWindow parent)
    {
        var citation = CitationManager.GetCitation();

        var alert = new NSAlert
        {
            MessageText = "How to cite FT-ITC Analysis",
            InformativeText = "Copy one of the formats below (or open the repository CITATION file)."
        };

        alert.AccessoryView = BuildWrappedLabelAccessory(citation.ToDisplayString());
        alert.AddButton("Copy");
        alert.AddButton("OK");

        var response = alert.RunSheetModal(parent);
        if (response == 1000) // first button
        {
            var pb = NSPasteboard.GeneralPasteboard;
            pb.ClearContents();
            pb.SetStringForType(citation.ToBibTeX(), NSPasteboard.NSStringType);
        }
    }

    static NSView BuildWrappedLabelAccessory(string text, float width = 350)
    {
        var font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);

        // Measure height for the given width
        var attrs = new NSStringAttributes { Font = font };
        var attributed = new NSAttributedString(text, attrs);

        var bounds = attributed.BoundingRectWithSize(
            new CGSize(width, nfloat.MaxValue),
            NSStringDrawingOptions.UsesLineFragmentOrigin | NSStringDrawingOptions.UsesFontLeading);

        var pad = 8f;
        var height = (nfloat)Math.Ceiling(bounds.Height + pad);

        var container = new NSView(new CGRect(0, 0, width, height));

        var tf = new NSTextField(new CGRect(0, 0, width, height))
        {
            Editable = false,
            Bordered = false,
            DrawsBackground = false,
            Selectable = true,
            Font = font,
            StringValue = text
        };

        // Critical multiline settings
        tf.Cell.Wraps = true;
        tf.Cell.Scrollable = false;
        tf.Cell.UsesSingleLineMode = false;
        tf.Cell.LineBreakMode = NSLineBreakMode.ByWordWrapping;

        container.AddSubview(tf);
        return container;
    }
}