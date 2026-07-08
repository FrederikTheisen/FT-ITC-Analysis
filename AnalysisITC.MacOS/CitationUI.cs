using System;
using System.IO;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Utilities;
using AppKit;
using CoreGraphics;
using Foundation;

static class CitationUI
{
    public static void ShowCitationDialog(NSWindow parent)
    {
        var paperCitation = CitationManager.GetPaperCitation();
        var softwareCitation = CitationManager.SoftwareCitation;

        var alert = new NSAlert
        {
            MessageText = "How to cite FT-ITC Analysis",
            InformativeText = "Export writes both citation records for citation managers."
        };

        alert.AccessoryView = BuildCitationAccessory(
            BuildCitationDisplayText(paperCitation, softwareCitation),
            out var citationSelector);
        alert.AddButton("Copy BibTeX");
        alert.AddButton("Export .bib");
        alert.AddButton("Close");

        var response = alert.RunSheetModal(parent);

        if (response == 1000)
        {
            if (citationSelector.IndexOfSelectedItem == 0)
            {
                CopyToPasteboard(paperCitation.ToPaperBibTeX());
                StatusBarManager.SetStatus("Paper citation copied to clipboard", 3000);
            }
            else
            {
                CopyToPasteboard(softwareCitation.ToSoftwareBibTeX());
                StatusBarManager.SetStatus("Software citation copied to clipboard", 3000);
            }
        }
        else if (response == 1001)
        {
            ExportBibTeX(parent, CitationManager.BuildCombinedBibTeX());
        }
    }

    static string BuildCitationDisplayText(CitationInfo paperCitation, CitationInfo softwareCitation)
    {
        return paperCitation.ToMarkdownDisplayString(false, "Recommended: cite the paper") +
            "\n\n" +
            softwareCitation.ToMarkdownDisplayString(true, "For reproducibility: cite this software version");
    }

    static void CopyToPasteboard(string text)
    {
        var pb = NSPasteboard.GeneralPasteboard;
        pb.ClearContents();
        pb.SetStringForType(text, NSPasteboard.NSStringType);
    }

    static void ExportBibTeX(NSWindow parent, string bibTeX)
    {
        var panel = NSSavePanel.SavePanel;
        panel.Title = "Export Citation";
        panel.NameFieldStringValue = "ft-itc-analysis-citations.bib";
        panel.AllowedFileTypes = new[] { "bib" };
        panel.CanCreateDirectories = true;

        panel.BeginSheet(parent, result =>
        {
            if (result != (int)NSModalResponse.OK || panel.Url == null)
                return;

            try
            {
                File.WriteAllText(panel.Url.Path, bibTeX);
                StatusBarManager.SetStatus("Citation BibTeX exported", 3000);
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        });
    }

    static NSView BuildCitationAccessory(string text, out NSPopUpButton citationSelector, float width = 480)
    {
        var font = NSFont.SystemFontOfSize(NSFont.SystemFontSize);

        var attributed = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(text, font);

        var bounds = attributed.BoundingRectWithSize(
            new CGSize(width, nfloat.MaxValue),
            NSStringDrawingOptions.UsesLineFragmentOrigin | NSStringDrawingOptions.UsesFontLeading);

        var textHeight = (nfloat)Math.Ceiling(bounds.Height + 8);
        var controlHeight = 26f;
        var gap = 12f;
        var height = textHeight + controlHeight + gap;

        var container = new NSView(new CGRect(0, 0, width, height));

        citationSelector = new NSPopUpButton(new CGRect(0, 0, width, controlHeight), false)
        {
            Font = font
        };
        citationSelector.AddItems(new[]
        {
            "Paper citation (recommended)",
            "Software citation (current version)"
        });
        citationSelector.SelectItem(0);

        var tf = new NSTextField(new CGRect(0, controlHeight + gap, width, textHeight))
        {
            Editable = false,
            Bordered = false,
            DrawsBackground = false,
            Selectable = true,
            Font = font,
            AttributedStringValue = attributed
        };

        // Critical multiline settings
        tf.Cell.Wraps = true;
        tf.Cell.Scrollable = false;
        tf.Cell.UsesSingleLineMode = false;
        tf.Cell.LineBreakMode = NSLineBreakMode.ByWordWrapping;

        container.AddSubview(citationSelector);
        container.AddSubview(tf);
        return container;
    }
}
