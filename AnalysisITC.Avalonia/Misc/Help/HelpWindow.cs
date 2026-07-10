using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Avalonia.Styling;

namespace AnalysisITC.Avalonia.Help;

public sealed class HelpWindow : Window
{
    readonly ListBox topicList = new ListBox();
    readonly ScrollViewer documentScroll = new ScrollViewer();
    readonly StackPanel documentPanel = new StackPanel { Spacing = 10, Margin = new Thickness(26, 22, 32, 32) };
    readonly Dictionary<HelpTopic, Control> headingControls = new Dictionary<HelpTopic, Control>();
    readonly HelpDocument document;
    readonly List<HelpTopicListItem> topicItems;

    HelpTopic? displayedSection;

    HelpWindow(string title, HelpDocument document)
    {
        this.document = document;
        topicItems = BuildTopicItems(document).ToList();

        Title = title;
        Width = 940;
        Height = 680;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        BuildLayout();
        SelectInitialTopic();
    }

    public static async Task ShowAsync(Window owner, string title, string resourceName)
    {
        HelpDocument document;
        try
        {
            var text = AvaloniaHelpResourceLoader.LoadText(resourceName);
            document = HelpDocumentParser.Parse(text);
        }
        catch (Exception ex)
        {
            document = HelpDocumentParser.Parse(
                "## Help unavailable\nThe bundled help resource could not be loaded.\n\n" + ex.Message);
        }

        var window = new HelpWindow(title, document);
        await window.ShowDialog(owner);
    }

    void BuildLayout()
    {
        var root = new DockPanel
        {
            LastChildFill = true
        };
        AppTheme.Bind(root, Panel.BackgroundProperty, AppTheme.WorkspaceBackground);

        topicList.ItemsSource = topicItems;
        topicList.ItemTemplate = new FuncDataTemplate<HelpTopicListItem>((item, _) => TopicCell(item));
        topicList.SelectionChanged += (_, _) => SelectTopic((topicList.SelectedItem as HelpTopicListItem)?.Topic);

        var topicPane = new Border
        {
            Width = 260,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = topicList
        };
        AppTheme.Bind(topicPane, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(topicPane, Border.BorderBrushProperty, AppTheme.PanelBorder);

        DockPanel.SetDock(topicPane, Dock.Left);
        root.Children.Add(topicPane);

        documentScroll.Content = documentPanel;
        root.Children.Add(documentScroll);
        Content = root;
    }

    Control TopicCell(HelpTopicListItem? item)
    {
        if (item == null) return new TextBlock();

        var textBlock = new TextBlock
        {
            Text = PlainText(item.Topic.Title),
            FontSize = item.Topic.Level == 2 ? 13 : 12,
            FontWeight = item.Topic.Level == 2 ? FontWeight.SemiBold : FontWeight.Normal,
            Margin = new Thickness(item.Indent, 4, 8, 4),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, item.Topic.Level == 2 ? AppTheme.PrimaryText : AppTheme.SecondaryText);
        return textBlock;
    }

    void SelectInitialTopic()
    {
        if (topicItems.Count == 0)
        {
            RenderMessage("No help topics were found.");
            return;
        }

        topicList.SelectedIndex = 0;
        SelectTopic(topicItems[0].Topic);
    }

    void SelectTopic(HelpTopic? topic)
    {
        if (topic == null) return;

        var section = topic.Level == 2 ? topic : topic.Parent;
        if (section == null) return;

        if (displayedSection != section)
            RenderSection(section);

        if (topic.Level == 2)
        {
            documentScroll.Offset = new Vector(documentScroll.Offset.X, 0);
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (headingControls.TryGetValue(topic, out var heading))
                heading.BringIntoView();
        }, DispatcherPriority.Background);
    }

    void RenderSection(HelpTopic section)
    {
        displayedSection = section;
        headingControls.Clear();
        documentPanel.Children.Clear();

        var processed = MarkdownProcessor.ProcessWrittenText(section.RawMarkdown ?? "");
        foreach (var block in SplitBlocks(processed))
        {
            if (block.StartsWith("## "))
            {
                var heading = Heading(block.Substring(3).Trim(), 24, FontWeight.SemiBold);
                headingControls[section] = heading;
                documentPanel.Children.Add(heading);
                continue;
            }

            if (block.StartsWith("# "))
            {
                var title = block.Substring(2).Trim();
                var heading = Heading(title, 18, FontWeight.SemiBold);
                var child = section.Children.FirstOrDefault(topic => PlainText(topic.Title) == PlainText(title));
                if (child != null) headingControls[child] = heading;
                documentPanel.Children.Add(heading);
                continue;
            }

            documentPanel.Children.Add(Paragraph(block));
        }
    }

    void RenderMessage(string message)
    {
        documentPanel.Children.Clear();
        var textBlock = new TextBlock
        {
            Text = message,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
        documentPanel.Children.Add(textBlock);
    }

    static IEnumerable<string> SplitBlocks(string text)
    {
        var normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var paragraph = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("# ") || line.StartsWith("## "))
            {
                if (paragraph.Count > 0)
                {
                    yield return string.Join(Environment.NewLine, paragraph).TrimEnd();
                    paragraph.Clear();
                }

                yield return line.TrimEnd();
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (paragraph.Count > 0)
                {
                    yield return string.Join(Environment.NewLine, paragraph).TrimEnd();
                    paragraph.Clear();
                }

                continue;
            }

            paragraph.Add(line);
        }

        if (paragraph.Count > 0)
            yield return string.Join(Environment.NewLine, paragraph).TrimEnd();
    }

    static TextBlock Heading(string text, double size, FontWeight weight)
    {
        var textBlock = new TextBlock
        {
            Text = PlainText(text),
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, size > 20 ? 0 : 8, 0, 0)
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        return textBlock;
    }

    static TextBlock Paragraph(string text)
    {
        var textBlock = new TextBlock
        {
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.SecondaryText);

        foreach (var segment in MarkdownProcessor.GetSegments(text ?? ""))
            AddInline(textBlock, segment);

        return textBlock;
    }

    static void AddInline(TextBlock textBlock, Segment segment)
    {
        var run = new Run(segment.Text);
        switch (segment.Property)
        {
            case MarkdownProperty.Bold:
                run.FontWeight = FontWeight.SemiBold;
                break;
            case MarkdownProperty.Cursive:
                run.FontStyle = FontStyle.Italic;
                break;
            case MarkdownProperty.Subscript:
                run.BaselineAlignment = BaselineAlignment.Subscript;
                run.FontSize = 10;
                break;
            case MarkdownProperty.Superscript:
                run.BaselineAlignment = BaselineAlignment.Superscript;
                run.FontSize = 10;
                break;
            case MarkdownProperty.Small:
                run.FontSize = 11;
                break;
        }

        textBlock.Inlines ??= new InlineCollection();
        textBlock.Inlines.Add(run);
    }

    static string PlainText(string text)
    {
        return string.Concat(MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(text ?? ""))
            .Select(segment => segment.Text));
    }

    static IEnumerable<HelpTopicListItem> BuildTopicItems(HelpDocument document)
    {
        foreach (var topic in document.Topics)
        {
            yield return new HelpTopicListItem(topic, 10);
            foreach (var child in topic.Children)
                yield return new HelpTopicListItem(child, 26);
        }
    }

    sealed class HelpTopicListItem
    {
        public HelpTopic Topic { get; }
        public double Indent { get; }

        public HelpTopicListItem(HelpTopic topic, double indent)
        {
            Topic = topic;
            Indent = indent;
        }
    }
}
