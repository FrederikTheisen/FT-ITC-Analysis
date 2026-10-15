using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Help;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class HelpWindowTests
{
    [AvaloniaFact]
    public void DisplayEquationsRemainCenteredAndStyledAtMinimumWidth()
    {
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var document = HelpDocumentParser.Parse("## Injection heat uncertainty\n"
            + "The heat error combines integration noise and baseline uncertainty.\n\n"
            + "$$\n\\sigma{q,i} = √( \\sigma{q,i,int}^2^ + \\sigma{q,i,bl}^2^ )\n$$\n\n"
            + "Integration noise contribution:\n\n"
            + "$$\n\\sigma{q,i,int} = \\sigma{p,i} · ∆*t* · √*N*{int,eff}\n$$\n\n"
            + "Baseline level contribution:\n\n"
            + "$$\n\\sigma{q,i,bl} = \\sigma{p,i} · (*T*{int} / √*N*{bl,eff})\n$$\n\n"
            + "These processing-derived heat SDs remain distinct from parameter uncertainty.");
        var window = new HelpWindow("Help and Guide", document) { Width = 720, Height = 480 };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var scroll = Assert.IsType<DockPanel>(window.Content).Children.OfType<ScrollViewer>().Single();
            var content = Assert.IsType<StackPanel>(scroll.Content);
            var equations = content.Children.OfType<TextBlock>().Where(block => block.TextAlignment == TextAlignment.Center).ToArray();
            Assert.Equal(3, equations.Length);
            Assert.All(equations, block =>
            {
                var point = block.TranslatePoint(default, scroll)!.Value;
                Assert.InRange(point.X, 0, scroll.Viewport.Width);
                Assert.True(point.X + block.Bounds.Width <= scroll.Viewport.Width + 1);
                Assert.True(block.Bounds.Height >= block.LineHeight);
            });
            Assert.Contains(equations[0].Inlines!.OfType<Run>(), run => run.BaselineAlignment == BaselineAlignment.Superscript);
            Assert.Contains(equations[0].Inlines!.OfType<Run>(), run => run.BaselineAlignment == BaselineAlignment.Subscript);
            Assert.NotEqual(TextAlignment.Center, content.Children.OfType<TextBlock>().Last().TextAlignment);
            var output = Environment.GetEnvironmentVariable("FTITC_HELP_QA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(output, "avalonia-help-equations.png"));
            }
        }
        finally
        {
            window.Close();
            Application.Current.Styles.Remove(theme);
        }
    }

    [AvaloniaFact]
    public void GuideOpensAndNavigatesEveryBundledTopicAndSubsection()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var owner = new MainWindow();
        owner.Show();
        try
        {
            var pending = owner.OpenHelpGuideAsync();
            var help = Assert.IsType<HelpWindow>(Assert.Single(owner.OwnedWindows));
            var layout = Assert.IsType<DockPanel>(help.Content);
            var list = Assert.IsType<ListBox>(layout.Children.OfType<Border>().Single().Child);
            var scroll = layout.Children.OfType<ScrollViewer>().Single();
            var content = Assert.IsType<StackPanel>(scroll.Content);
            var document = HelpDocumentParser.Parse(AvaloniaHelpResourceLoader.LoadText("HelpTextResource.txt"));
            var topics = document.Topics.SelectMany(topic => new[] { topic }.Concat(topic.Children)).ToArray();
            Assert.Equal(topics.Length, list.ItemCount);
            var repeatedRoot = false;
            var repeatedChild = false;
            var equationsChecked = 0;
            for (var index = 0; index < topics.Length; index++)
            {
                list.SelectedIndex = index;
                help.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                help.UpdateLayout();
                var topic = topics[index];
                Assert.True(scroll.Viewport.Height > 100, "Help reading pane was not laid out");
                var section = topic.Parent ?? topic;
                var title = string.Concat(MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(section.Title))
                    .Select(segment => segment.Text));
                Assert.Equal(title, Assert.IsType<TextBlock>(content.Children[0]).Text);
                Assert.True(content.Children.Count > 1, "Topic has no rendered content: " + section.Title);
                if (topic.Parent == null)
                {
                    var equations = HelpContent.Parse(section.RawMarkdown).Where(block => block.IsEquation).ToArray();
                    foreach (var equation in equations)
                    {
                        var plain = PlainText(equation.Text);
                        var rendered = Assert.Single(content.Children.OfType<TextBlock>(), block => InlineText(block) == plain);
                        Assert.Equal(TextAlignment.Center, rendered.TextAlignment);
                        Assert.True(rendered.FontSize > 13);
                        Assert.True(rendered.Margin.Top > 0 && rendered.Margin.Bottom > 0);
                        Assert.True(rendered.Bounds.Width <= scroll.Viewport.Width);
                        var segments = MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(equation.Text));
                        if (segments.Any(segment => segment.Property == MarkdownProperty.Subscript))
                            Assert.Contains(rendered.Inlines!.OfType<Run>(), run => run.BaselineAlignment == BaselineAlignment.Subscript);
                        if (segments.Any(segment => segment.Property == MarkdownProperty.Superscript))
                            Assert.Contains(rendered.Inlines!.OfType<Run>(), run => run.BaselineAlignment == BaselineAlignment.Superscript);
                        equationsChecked++;
                    }
                }
                var selectedTitle = string.Concat(MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(topic.Title))
                    .Select(segment => segment.Text));
                var heading = content.Children.OfType<TextBlock>().First(block => block.Text == selectedTitle);
                var top = heading.TranslatePoint(default, scroll)!.Value.Y;
                var maximumOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                if (topic.Parent == null) Assert.Equal(0, scroll.Offset.Y, precision: 1);
                else Assert.True(Math.Abs(top) < 1 || Math.Abs(scroll.Offset.Y - maximumOffset) < 1,
                    "Selected heading was visible but not aligned to the top: " + topic.Title);
                if (maximumOffset > 100 && ((topic.Parent == null && !repeatedRoot)
                    || (topic.Parent != null && !repeatedChild && Math.Abs(top) < 1)))
                {
                    list.ScrollIntoView(list.SelectedItem);
                    help.UpdateLayout();
                    var item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(index));
                    var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), help)!.Value;
                    scroll.Offset = new Vector(0, topic.Parent == null ? maximumOffset : 0);
                    help.MouseDown(point, MouseButton.Left);
                    help.MouseUp(point, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                    help.UpdateLayout();
                    if (topic.Parent == null)
                    {
                        Assert.Equal(0, scroll.Offset.Y, precision: 1);
                        repeatedRoot = true;
                    }
                    else
                    {
                        Assert.InRange(Math.Abs(heading.TranslatePoint(default, scroll)!.Value.Y), 0, 1);
                        repeatedChild = true;
                    }
                }
                foreach (var block in content.Children.OfType<TextBlock>())
                {
                    var rendered = block.Text ?? string.Concat(block.Inlines!.OfType<Run>()
                        .Select(run => run.Text));
                    Assert.DoesNotContain("\\appname", rendered);
                    Assert.DoesNotContain("$$", rendered);
                }
            }
            Assert.True(repeatedRoot && repeatedChild, "Repeated clicks were not exercised for both topic levels");
            Assert.True(equationsChecked > 0, "Bundled display equations were not exercised");
            help.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(pending.IsCompletedSuccessfully);
        }
        finally
        {
            foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close();
            DocumentDirtyTracker.MarkClean();
            owner.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            Application.Current.Styles.Remove(theme);
        }
    }

    static string InlineText(TextBlock block) => block.Text ?? string.Concat(block.Inlines!.OfType<Run>().Select(run => run.Text));
    static string PlainText(string text) => string.Concat(MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(text))
        .Select(segment => segment.Text));
}
