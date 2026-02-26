using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AnalysisITC;
using AppKit;
using CoreGraphics;
using Foundation;

static class CitationManager
{
    // 1) Embedded fallback (keep this short and robust)
    public static string EmbeddedPlainText => @"Theisen, Frederik. FT-ITC Analysis: a macOS desktop application for processing and analysis of isothermal titration calorimetry data.
Version {VERSION}. URL: https://github.com/FrederikTheisen/FT-ITC-Analysis (accessed {DATE}).";

    // 2) Remote override (raw GitHub file you can edit anytime)
    public static string RemoteUrl =>
        "https://raw.githubusercontent.com/FrederikTheisen/FT-ITC-Analysis/refs/heads/master/CITATION.cff";

    // 3) Local cache path (outside app bundle; safe for signed apps)
    static string CachePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FT-ITC", "CITATION.txt");

    public static string GetBestEffortCitation()
    {
        // Prefer cached file if present
        try
        {
            if (File.Exists(CachePath))
                return File.ReadAllText(CachePath);
        }
        catch { /* ignore */ }

        // Fallback to embedded template
        var version = NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "unknown";
        return EmbeddedPlainText
            .Replace("{VERSION}", version)
            .Replace("{DATE}", DateTime.Now.ToString("yyyy-MM-dd"));
    }

    public static async Task TryRefreshCacheAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);

            var txt = await http.GetStringAsync(RemoteUrl);

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, txt);
        }
        catch (Exception ex)
        {
            AppEventHandler.DisplayHandledException(ex);
        }
    }
}

static class CitationUI
{
    public static void ShowCitationDialog(NSWindow parent)
    {
        var citation = CitationManager.GetBestEffortCitation();

        var alert = new NSAlert
        {
            MessageText = "How to cite FT-ITC Analysis",
            InformativeText = "Copy one of the formats below (or open the repository CITATION file)."
        };

        alert.AccessoryView = BuildWrappedLabelAccessory(citation);
        alert.AddButton("Copy");
        alert.AddButton("OK");

        // Optionally kick off a refresh attempt (non-blocking)
        _ = CitationManager.TryRefreshCacheAsync();

        var response = alert.RunSheetModal(parent);
        if (response == 1000) // first button
        {
            var pb = NSPasteboard.GeneralPasteboard;
            pb.ClearContents();
            pb.SetStringForType(citation, NSPasteboard.NSStringType);
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

    static NSView BuildCitationAccessory(string citation, float width = 520, float height = 180)
    {
        // Container gives NSAlert a concrete size to work with.
        var container = new NSView(new CGRect(0, 0, width, height));

        var scroll = new NSScrollView(container.Bounds)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            HasVerticalScroller = true,
            HasHorizontalScroller = false,
            BorderType = NSBorderType.BezelBorder
        };

        // IMPORTANT: give the text view a frame.
        var textView = new NSTextView(new CGRect(0, 0, width, height))
        {
            Editable = false,
            Selectable = true,
            RichText = false,
            UsesFontPanel = false,
            Value = citation
        };

        // Make wrapping behave predictably.
        textView.HorizontallyResizable = false;
        textView.VerticallyResizable = true;
        textView.AutoresizingMask = NSViewResizingMask.WidthSizable;

        if (textView.TextContainer != null)
        {
            textView.TextContainer.WidthTracksTextView = true;
            textView.TextContainer.ContainerSize = new CGSize(width, nfloat.MaxValue);
        }

        scroll.DocumentView = textView;
        container.AddSubview(scroll);

        return container;
    }
}