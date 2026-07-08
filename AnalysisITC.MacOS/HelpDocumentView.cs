using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC.UI.MacOS.Drawing;

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

namespace AnalysisITC
{
    class HelpDocumentView
    {
        static readonly nfloat MinimumSidebarWidth = 220;
        static readonly nfloat DefaultSidebarWidth = 260;
        static readonly nfloat MaximumSidebarWidth = 340;
        const string TopicCellIdentifier = "HelpTopicCell";

        readonly NSViewController owner;
        readonly NSTextView textView;
        readonly string resourcePath;
        readonly NSFont font;

        NSOutlineView outlineView;
        HelpSplitViewDelegate splitViewDelegate;
        HelpOutlineDataSource dataSource;
        HelpOutlineDelegate outlineDelegate;
        List<HelpTopic> topics = new List<HelpTopic>();
        HelpTopic displayedSection;
        bool installed;

        public HelpDocumentView(NSViewController owner, NSTextView textView, string resourcePath)
        {
            this.owner = owner;
            this.textView = textView;
            this.resourcePath = resourcePath;
            font = NSFont.FromFontName(textView.Font.DisplayName, 13) ?? NSFont.SystemFontOfSize(13);
        }

        public void Install()
        {
            if (!installed)
            {
                InstallSplitView();
                installed = true;
            }

            LoadDocument();
        }

        void InstallSplitView()
        {
            var textScrollView = textView.EnclosingScrollView;
            if (textScrollView == null) return;

            textView.Editable = false;
            textView.Selectable = true;
            textView.DrawsBackground = false;
            textView.TextColor = NSColor.Label;
            textView.HorizontallyResizable = false;
            textView.VerticallyResizable = true;
            textView.MinSize = new CGSize(0, textScrollView.ContentSize.Height);
            textView.MaxSize = new CGSize(10000000, 10000000);

            if (textView.TextContainer != null)
            {
                textView.TextContainer.ContainerSize = new CGSize(textScrollView.ContentSize.Width, 10000000);
                textView.TextContainer.WidthTracksTextView = true;
            }

            var outlineScrollView = CreateOutlineScrollView(textScrollView.Frame.Height);
            var splitView = new NSSplitView(textScrollView.Frame)
            {
                DividerStyle = NSSplitViewDividerStyle.Thin,
                IsVertical = true,
                ArrangesAllSubviews = true,
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                TranslatesAutoresizingMaskIntoConstraints = textScrollView.TranslatesAutoresizingMaskIntoConstraints,
            };
            splitViewDelegate = new HelpSplitViewDelegate();
            splitView.Delegate = splitViewDelegate;

            var parent = textScrollView.Superview;
            if (parent is NSStackView stackView)
            {
                var arrangedSubviews = stackView.ArrangedSubviews;
                var index = Array.IndexOf(arrangedSubviews, textScrollView);

                stackView.RemoveArrangedSubview(textScrollView);
                textScrollView.RemoveFromSuperview();

                ConfigureSplitViewPanes(outlineScrollView, textScrollView);
                splitView.AddArrangedSubview(outlineScrollView);
                splitView.AddArrangedSubview(textScrollView);

                if (index >= 0) stackView.InsertArrangedSubview(splitView, (nint)index);
                else stackView.AddArrangedSubview(splitView);
            }
            else if (parent != null)
            {
                splitView.Frame = textScrollView.Frame;
                splitView.AutoresizingMask = textScrollView.AutoresizingMask;

                textScrollView.RemoveFromSuperview();
                ConfigureSplitViewPanes(outlineScrollView, textScrollView);
                splitView.AddArrangedSubview(outlineScrollView);
                splitView.AddArrangedSubview(textScrollView);
                parent.AddSubview(splitView);

                if (!splitView.TranslatesAutoresizingMaskIntoConstraints)
                {
                    NSLayoutConstraint.ActivateConstraints(new[]
                    {
                        splitView.TopAnchor.ConstraintEqualToAnchor(parent.TopAnchor),
                        splitView.BottomAnchor.ConstraintEqualToAnchor(parent.BottomAnchor),
                        splitView.LeadingAnchor.ConstraintEqualToAnchor(parent.LeadingAnchor),
                        splitView.TrailingAnchor.ConstraintEqualToAnchor(parent.TrailingAnchor),
                    });
                }
            }

            splitView.SetPositionOfDivider(DefaultSidebarWidth, 0);
            splitView.SetHoldingPriority(1000, 0);
            splitView.SetHoldingPriority(249, 1);
            splitView.AdjustSubviews();
        }

        void ConfigureSplitViewPanes(NSScrollView outlineScrollView, NSScrollView textScrollView)
        {
            outlineScrollView.TranslatesAutoresizingMaskIntoConstraints = false;
            textScrollView.TranslatesAutoresizingMaskIntoConstraints = false;

            outlineScrollView.AddConstraint(NSLayoutConstraint.Create(outlineScrollView, NSLayoutAttribute.Width, NSLayoutRelation.GreaterThanOrEqual, 1, MinimumSidebarWidth));
            outlineScrollView.AddConstraint(NSLayoutConstraint.Create(outlineScrollView, NSLayoutAttribute.Width, NSLayoutRelation.LessThanOrEqual, 1, MaximumSidebarWidth));

            outlineScrollView.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Horizontal);
            outlineScrollView.SetContentCompressionResistancePriority(1000, NSLayoutConstraintOrientation.Horizontal);
            textScrollView.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
            textScrollView.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        }

        NSScrollView CreateOutlineScrollView(nfloat height)
        {
            outlineView = new NSOutlineView(new CGRect(0, 0, DefaultSidebarWidth, height))
            {
                HeaderView = null,
                RowHeight = 24,
                AllowsMultipleSelection = false,
                AllowsEmptySelection = false,
                SelectionHighlightStyle = NSTableViewSelectionHighlightStyle.Regular,
                ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly,
            };

            var column = new NSTableColumn("Topic")
            {
                Title = "Topic",
                Width = DefaultSidebarWidth,
                MinWidth = MinimumSidebarWidth,
                MaxWidth = MaximumSidebarWidth,
                ResizingMask = NSTableColumnResizing.Autoresizing,
            };

            outlineView.AddColumn(column);
            outlineView.OutlineTableColumn = column;
            outlineView.SelectionDidChange += OutlineView_SelectionDidChange;

            return new NSScrollView(new CGRect(0, 0, DefaultSidebarWidth, height))
            {
                DocumentView = outlineView,
                HasVerticalScroller = true,
                HasHorizontalScroller = false,
                BorderType = NSBorderType.NoBorder,
                AutohidesScrollers = true,
            };
        }

        class HelpSplitViewDelegate : NSSplitViewDelegate
        {
            public override nfloat ConstrainSplitPosition(NSSplitView splitView, nfloat proposedPosition, nint subviewDividerIndex)
            {
                if (subviewDividerIndex != 0) return proposedPosition;

                if (proposedPosition < MinimumSidebarWidth) return MinimumSidebarWidth;
                if (proposedPosition > MaximumSidebarWidth) return MaximumSidebarWidth;
                return proposedPosition;
            }

            public override nfloat SetMinCoordinateOfSubview(NSSplitView splitView, nfloat proposedMinimumPosition, nint subviewDividerIndex)
            {
                return subviewDividerIndex == 0 ? MinimumSidebarWidth : proposedMinimumPosition;
            }

            public override nfloat SetMaxCoordinateOfSubview(NSSplitView splitView, nfloat proposedMaximumPosition, nint subviewDividerIndex)
            {
                return subviewDividerIndex == 0 ? MaximumSidebarWidth : proposedMaximumPosition;
            }

            public override bool CanCollapse(NSSplitView splitView, NSView subview)
            {
                return false;
            }

            public override bool ShouldAdjustSize(NSSplitView splitView, NSView view)
            {
                var subviews = splitView.Subviews;
                return subviews.Length == 0 || view != subviews[0];
            }
        }

        void LoadDocument()
        {
            var text = File.ReadAllText(resourcePath);
            topics = HelpDocumentParser.Parse(text);

            dataSource = new HelpOutlineDataSource(topics);
            outlineDelegate = new HelpOutlineDelegate(SelectTopic);

            outlineView.DataSource = dataSource;
            outlineView.Delegate = outlineDelegate;
            outlineView.ReloadData();

            foreach (var topic in topics)
                outlineView.ExpandItem(topic);

            if (topics.Count == 0)
            {
                RenderText(text);
                return;
            }

            RenderSection(topics[0], null);
            SelectFirstTopic();
        }

        void SelectFirstTopic()
        {
            if (outlineView.RowCount <= 0) return;

            var indexSet = new NSMutableIndexSet();
            indexSet.Add((nuint)0);
            outlineView.SelectRows(indexSet, false);
        }

        void OutlineView_SelectionDidChange(object sender, EventArgs e)
        {
            if (outlineView.SelectedRow < 0) return;

            SelectTopic(outlineView.ItemAtRow(outlineView.SelectedRow) as HelpTopic);
        }

        void SelectTopic(HelpTopic topic)
        {
            if (topic == null) return;

            var section = topic.Level == 2 ? topic : topic.Parent;
            RenderSection(section, topic.Level == 1 ? topic : null);
        }

        void RenderSection(HelpTopic section, HelpTopic anchor)
        {
            if (section == null) return;

            if (displayedSection != section)
            {
                RenderText(section.RawMarkdown);
                displayedSection = section;
            }

            if (anchor == null) ScrollToTop();
            else ScrollToHeading(anchor.RenderedTitle(font));
        }

        void RenderText(string text)
        {
            var paddedText = text.TrimEnd() + Environment.NewLine + Environment.NewLine + Environment.NewLine + Environment.NewLine;
            var processedText = MarkdownProcessor.ProcessWrittenText(paddedText);
            var attributedString = AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(processedText, font);

            textView.TextStorage.SetString(attributedString);
            textView.TextColor = NSColor.Label;
        }

        void ScrollToTop()
        {
            owner.BeginInvokeOnMainThread(() =>
            {
                textView.ScrollRangeToVisible(new NSRange(0, 0));
            });
        }

        void ScrollToHeading(string heading)
        {
            owner.BeginInvokeOnMainThread(() =>
            {
                var text = textView.Value ?? "";
                var index = text.IndexOf(heading, StringComparison.Ordinal);
                if (index < 0)
                {
                    ScrollToTop();
                    return;
                }

                textView.ScrollRangeToVisible(new NSRange(index, 0));
            });
        }

        class HelpTopic : NSObject
        {
            public string Title { get; }
            public int Level { get; }
            public HelpTopic Parent { get; }
            public List<HelpTopic> Children { get; } = new List<HelpTopic>();
            public string RawMarkdown { get; set; } = "";

            public HelpTopic(string title, int level, HelpTopic parent = null)
            {
                Title = title;
                Level = level;
                Parent = parent;
            }

            public string RenderedTitle(NSFont font)
            {
                var processedTitle = MarkdownProcessor.ProcessWrittenText(Title);
                return AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(processedTitle, font).Value;
            }
        }

        static class HelpDocumentParser
        {
            public static List<HelpTopic> Parse(string text)
            {
                var topics = new List<HelpTopic>();
                var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                var sectionLines = new List<string>();
                HelpTopic currentSection = null;

                void FinishSection()
                {
                    if (currentSection == null) return;

                    currentSection.RawMarkdown = string.Join(Environment.NewLine, sectionLines).Trim();
                    sectionLines.Clear();
                }

                foreach (var line in lines)
                {
                    if (line.StartsWith("## "))
                    {
                        FinishSection();

                        currentSection = new HelpTopic(line.Substring(3).Trim(), 2);
                        topics.Add(currentSection);
                        sectionLines.Add(line);
                        continue;
                    }

                    if (line.StartsWith("# ") && currentSection != null)
                    {
                        currentSection.Children.Add(new HelpTopic(line.Substring(2).Trim(), 1, currentSection));
                    }

                    if (currentSection == null)
                    {
                        currentSection = new HelpTopic("Overview", 2);
                        topics.Add(currentSection);
                    }

                    sectionLines.Add(line);
                }

                FinishSection();

                return topics.Where(topic => !string.IsNullOrWhiteSpace(topic.RawMarkdown)).ToList();
            }
        }

        class HelpOutlineDataSource : NSOutlineViewDataSource
        {
            readonly List<HelpTopic> topics;

            public HelpOutlineDataSource(List<HelpTopic> topics)
            {
                this.topics = topics;
            }

            public override nint GetChildrenCount(NSOutlineView outlineView, NSObject item)
            {
                if (item is HelpTopic topic) return topic.Children.Count;
                return topics.Count;
            }

            public override NSObject GetChild(NSOutlineView outlineView, nint childIndex, NSObject item)
            {
                if (item is HelpTopic topic) return topic.Children[(int)childIndex];
                return topics[(int)childIndex];
            }

            public override bool ItemExpandable(NSOutlineView outlineView, NSObject item)
            {
                return item is HelpTopic topic && topic.Children.Count > 0;
            }
        }

        class HelpOutlineDelegate : NSOutlineViewDelegate
        {
            readonly Action<HelpTopic> topicSelected;

            public HelpOutlineDelegate(Action<HelpTopic> topicSelected)
            {
                this.topicSelected = topicSelected;
            }

            public override void SelectionDidChange(NSNotification notification)
            {
                if (notification.Object is NSOutlineView outlineView && outlineView.SelectedRow >= 0)
                    topicSelected(outlineView.ItemAtRow(outlineView.SelectedRow) as HelpTopic);
            }

            public override NSView GetView(NSOutlineView outlineView, NSTableColumn tableColumn, NSObject item)
            {
                var topic = item as HelpTopic;
                var label = outlineView.MakeView(TopicCellIdentifier, this) as NSTextField;

                if (label == null)
                {
                    label = NSTextField.CreateLabel("");
                    label.Identifier = TopicCellIdentifier;
                    label.LineBreakMode = NSLineBreakMode.TruncatingTail;
                    label.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Vertical);
                }

                label.StringValue = topic?.RenderedTitle(NSFont.SystemFontOfSize(NSFont.SystemFontSize)) ?? "";
                label.Font = topic?.Level == 2
                    ? NSFont.SystemFontOfSize(NSFont.SystemFontSize, NSFontWeight.Semibold)
                    : NSFont.SystemFontOfSize(NSFont.SystemFontSize);
                label.TextColor = topic?.Level == 2 ? NSColor.Label : NSColor.SecondaryLabel;

                return label;
            }

            [Export("outlineView:heightOfRowByItem:")]
            public override nfloat GetRowHeight(NSOutlineView outlineView, NSObject item)
            {
                return 21;
            }
        }
    }
}
