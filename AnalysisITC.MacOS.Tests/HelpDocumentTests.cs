// Run with run-analysis-null-graph-tests.sh HelpDocumentTests; no app bundle is created.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

static class HelpDocumentTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static int Main(string[] args)
    {
        NSApplication.Init();
        try
        {
            var path = Environment.GetEnvironmentVariable("FTITC_HELP_RESOURCE_PATH")
                ?? Path.Combine(args[0], "Resources", "HelpTextResource.txt");
            var expected = HelpDocumentParser.Parse(File.ReadAllText(path));
            var type = typeof(MainWindowController).Assembly.GetType("AnalysisITC.HelpDocumentView");
            using (var owner = new NSViewController())
            using (var text = new NSTextView(new CGRect(0, 0, 680, 640)))
            using (var scroll = new NSScrollView(new CGRect(0, 0, 940, 640)))
            {
                owner.View = new NSView(new CGRect(0, 0, 940, 640));
                text.Font = NSFont.SystemFontOfSize(13);
                scroll.DocumentView = text;
                owner.View.AddSubview(scroll);
                var view = Activator.CreateInstance(type, new object[] { owner, text, path });
                type.GetMethod("Install", Instance).Invoke(view, null);
                var outline = (NSOutlineView)type.GetField("outlineView", Instance).GetValue(view);
                var roots = ((IEnumerable)type.GetField("topics", Instance).GetValue(view)).Cast<object>().ToArray();
                Check(roots.Length == expected.Topics.Count, "Native help lost a topic");
                Check(outline.RowCount == expected.Topics.Sum(topic => 1 + topic.Children.Count), "Native help lost a subsection");
                for (var index = 0; index < roots.Length; index++)
                {
                    var topic = roots[index];
                    var topicType = topic.GetType();
                    var expectedTopic = expected.Topics[index];
                    Check((string)topicType.GetProperty("RawMarkdown").GetValue(topic) == expectedTopic.RawMarkdown,
                        "Native and shared help parsers disagree");
                    var children = ((IEnumerable)topicType.GetProperty("Children").GetValue(topic)).Cast<object>().ToArray();
                    Check(children.Length == expectedTopic.Children.Count, "Native help lost topic children");
                    foreach (var entry in new[] { topic }.Concat(children))
                    {
                        outline.SelectRows(new NSIndexSet((nuint)outline.RowForItem((NSObject)entry)), false);
                        outline.SendAction(outline.Action, outline.Target);
                        NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
                        var title = (string)topicType.GetProperty("Title").GetValue(entry);
                        var plainTitle = string.Concat(MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(title))
                            .Select(segment => segment.Text));
                        Check(text.Value.Contains(plainTitle), "Native help cannot display " + title);
                        Check(text.Value.Length > plainTitle.Length, "Native help rendered an empty topic");
                        Check(!text.Value.Contains("\\appname"), "Native help left a macro unexpanded");
                        Check(!text.Value.Contains("$$"), "Native help left an equation fence visible");
                        if (entry == topic)
                        {
                            foreach (var block in HelpContent.Parse(expectedTopic.RawMarkdown))
                            {
                                var plain = string.Concat(MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(block.Text))
                                    .Select(segment => segment.Text));
                                if (!block.IsEquation)
                                {
                                    plain = plain.TrimStart();
                                    if (plain.Length == 0) continue;
                                }
                                var equationIndex = text.Value.IndexOf(plain, StringComparison.Ordinal);
                                Check(equationIndex >= 0, "Native help lost content around an equation");
                                NSRange effective;
                                var style = text.TextStorage.GetAttribute(NSStringAttributeKey.ParagraphStyle, equationIndex, out effective) as NSParagraphStyle;
                                if (!block.IsEquation)
                                {
                                    Check(style == null || style.Alignment != NSTextAlignment.Center, "Equation alignment leaked into help prose");
                                    continue;
                                }
                                Check(style != null && style.Alignment == NSTextAlignment.Center, "Native equation was not centered");
                                var equationFont = text.TextStorage.GetAttribute(NSStringAttributeKey.Font, equationIndex, out effective) as NSFont;
                                Check(equationFont != null && equationFont.PointSize > 13, "Native equation lost its display font");
                                var position = equationIndex;
                                foreach (var segment in MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(block.Text)))
                                {
                                    if (segment.Property == MarkdownProperty.Subscript || segment.Property == MarkdownProperty.Superscript)
                                    {
                                        var baseline = text.TextStorage.GetAttribute(NSStringAttributeKey.BaselineOffset, position, out effective) as NSNumber;
                                        Check(baseline != null && (segment.Property == MarkdownProperty.Subscript
                                            ? baseline.DoubleValue < 0 : baseline.DoubleValue > 0), "Native equation lost subscript or superscript styling");
                                    }
                                    position += segment.Text.Length;
                                }
                            }
                        }
                        text.LayoutManager.EnsureLayoutForTextContainer(text.TextContainer);
                        var headingIndex = entry == topic ? 0 : text.Value.IndexOf("\n" + plainTitle + "\n", StringComparison.Ordinal) + 1;
                        Check(entry == topic || headingIndex > 0, "Native help lost the heading paragraph: " + title);
                        var range = text.LayoutManager.GetGlyphRange(new NSRange(headingIndex, 1));
                        var headingY = text.LayoutManager.BoundingRectForGlyphRange(range, text.TextContainer).Y + text.TextContainerOrigin.Y;
                        var offset = text.EnclosingScrollView.ContentView.Bounds.Y;
                        var maximum = Math.Max(0, text.Frame.Height - text.EnclosingScrollView.ContentView.Bounds.Height);
                        if (entry == topic) Check(Math.Abs(offset) < 1, "Topic did not reset the reading pane");
                        else Check(Math.Abs(headingY - offset) < 1 || Math.Abs(offset - maximum) < 1,
                            "Selected native heading was visible but not aligned to the top: " + title);
                        if (maximum > 100)
                        {
                            var clip = text.EnclosingScrollView.ContentView;
                            clip.ScrollToPoint(new CGPoint(0, entry == topic ? maximum : 0));
                            outline.SendAction(outline.Action, outline.Target);
                            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
                            offset = clip.Bounds.Y;
                            if (entry == topic) Check(Math.Abs(offset) < 1, "Repeated topic click did not return to the top");
                            else Check(Math.Abs(headingY - offset) < 1 || Math.Abs(offset - maximum) < 1,
                                "Repeated subsection click did not realign its heading");
                        }
                    }
                }
            }
            Console.WriteLine("PASS: native unified help loads and renders every topic and scientific subsection");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
