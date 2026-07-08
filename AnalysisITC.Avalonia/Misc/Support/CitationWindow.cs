using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;

namespace AnalysisITC.Avalonia.Support;

public sealed class CitationWindow : Window
{
    readonly CitationInfo paperCitation = CitationManager.GetPaperCitation();
    readonly CitationInfo softwareCitation = CitationManager.SoftwareCitation;
    readonly TextBlock statusText = Text("", 12, "#607080");

    CitationWindow()
    {
        Title = "Citation";
        Width = 620;
        Height = 520;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var display = MarkdownBlock(BuildCitationDisplayText(paperCitation, softwareCitation));
        var displayScroll = new ScrollViewer
        {
            Content = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush("#d3dce5"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Child = display
            },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var copyPaper = Button("Copy Paper BibTeX");
        copyPaper.Click += (_, _) => Copy(paperCitation.ToPaperBibTeX(), "Paper citation copied");

        var copySoftware = Button("Copy Software BibTeX");
        copySoftware.Click += (_, _) => Copy(softwareCitation.ToSoftwareBibTeX(), "Software citation copied");

        var copyCombined = Button("Copy Combined BibTeX");
        copyCombined.Click += (_, _) => Copy(CitationManager.BuildCombinedBibTeX(), "Combined citation copied");

        var export = Button("Export .bib");
        export.Click += async (_, _) => await ExportBibTeXAsync();

        var doi = Button("Open DOI");
        doi.Click += (_, _) => Open(CitationInfo.SoftwareDoiUrl, "DOI opened");

        var repository = Button("Open Repository");
        repository.Click += (_, _) => Open(CitationInfo.SoftwareRepositoryUrl, "Repository opened");

        var close = Button("Close");
        close.Click += (_, _) => Close();

        var actionGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        AddAction(actionGrid, copyPaper, 0, 0);
        AddAction(actionGrid, copySoftware, 0, 1);
        AddAction(actionGrid, copyCombined, 0, 2);
        AddAction(actionGrid, export, 1, 0);
        AddAction(actionGrid, doi, 1, 1);
        AddAction(actionGrid, repository, 1, 2);
        Grid.SetRow(statusText, 2);
        Grid.SetColumnSpan(statusText, 2);
        actionGrid.Children.Add(statusText);
        Grid.SetRow(close, 2);
        Grid.SetColumn(close, 2);
        actionGrid.Children.Add(close);

        var title = Text("How to cite FT-ITC Analysis", 20, "#202832", FontWeight.SemiBold);
        var description = Text("Export writes both citation records for citation managers.", 13, "#607080");

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12
        };
        Grid.SetRow(title, 0);
        Grid.SetRow(description, 1);
        Grid.SetRow(displayScroll, 2);
        Grid.SetRow(actionGrid, 3);
        layout.Children.Add(title);
        layout.Children.Add(description);
        layout.Children.Add(displayScroll);
        layout.Children.Add(actionGrid);

        Content = new Border
        {
            Background = Brush("#f4f7fa"),
            Padding = new Thickness(18),
            Child = layout
        };
    }

    public static Task ShowAsync(Window owner)
    {
        var dialog = new CitationWindow();
        return dialog.ShowDialog(owner);
    }

    static string BuildCitationDisplayText(CitationInfo paperCitation, CitationInfo softwareCitation)
    {
        return paperCitation.ToMarkdownDisplayString(false, "Recommended: cite the paper") +
            "\n\n" +
            softwareCitation.ToMarkdownDisplayString(true, "For reproducibility: cite this software version");
    }

    void Copy(string text, string status)
    {
        PlatformServices.ClipboardService.SetString(text);
        SetStatus(status);
    }

    async Task ExportBibTeXAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Citation",
            SuggestedFileName = "ft-itc-analysis-citations.bib",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("BibTeX")
                {
                    Patterns = new[] { "*.bib" }
                },
                FilePickerFileTypes.All
            }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
            path += ".bib";

        try
        {
            File.WriteAllText(path, CitationManager.BuildCombinedBibTeX());
            SetStatus("Citation BibTeX exported");
        }
        catch (Exception ex)
        {
            AppEventHandler.AddLog(ex);
            SetStatus(ex.Message);
        }
    }

    void Open(string url, string success)
    {
        SetStatus(ExternalLinkLauncher.TryOpen(url) ? success : "Could not open link");
    }

    void SetStatus(string status)
    {
        statusText.Text = status ?? "";
    }

    static TextBlock MarkdownBlock(string text)
    {
        var textBlock = Text("", 13, "#202832");
        textBlock.LineHeight = 20;
        textBlock.TextWrapping = TextWrapping.Wrap;

        foreach (var segment in MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(text ?? "")))
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
            case MarkdownProperty.Header1:
            case MarkdownProperty.Header2:
                run.FontWeight = FontWeight.SemiBold;
                break;
            case MarkdownProperty.Small:
                run.FontSize = 11;
                break;
        }

        textBlock.Inlines ??= new InlineCollection();
        textBlock.Inlines.Add(run);
    }

    static void AddAction(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    static Button Button(string text) => new()
    {
        Content = text,
        MinWidth = 120,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness(10, 6)
    };

    static TextBlock Text(string text, double size, string color, FontWeight weight = default) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight == default ? FontWeight.Normal : weight,
        Foreground = Brush(color),
        TextWrapping = TextWrapping.Wrap
    };

    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
