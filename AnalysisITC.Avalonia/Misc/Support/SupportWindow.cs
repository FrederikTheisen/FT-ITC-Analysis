using System;
using System.IO;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using AnalysisITC.Core.Application;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Platform;

namespace AnalysisITC.Avalonia.Support;

public sealed class SupportWindow : Window
{
    readonly TextBlock statusText = Text("", 12, AppTheme.MutedText);

    SupportWindow()
    {
        Title = "Contact Support";
        Width = 620;
        Height = 520;
        MinWidth = 520;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var title = Text("Contact Support", 20, AppTheme.PrimaryText, FontWeight.SemiBold);
        var summary = Text(
            $"Support email: {SupportReportBuilder.SupportAddress}\n\n" +
            "The support report includes the app version, operating system, recent activity, and the full application log.",
            13,
            AppTheme.PrimaryText);

        var preview = new TextBox
        {
            Text = SupportReportBuilder.BuildEmailBody(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = FontFamily.Parse("Menlo, Consolas, monospace"),
            FontSize = 12
        };
        AppTheme.Bind(preview, TextBox.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(preview, TextBox.ForegroundProperty, AppTheme.PrimaryText);

        var previewBorder = new Border
        {
            BorderThickness = new Thickness(1),
            Child = preview
        };
        AppTheme.Bind(previewBorder, Border.BackgroundProperty, AppTheme.PanelBackground);
        AppTheme.Bind(previewBorder, Border.BorderBrushProperty, AppTheme.PanelBorder);

        var copy = Button("Copy Report");
        copy.Click += (_, _) => CopyReport("Support report copied");

        var save = Button("Save Report...");
        save.Click += async (_, _) => await SaveReportAsync();

        var email = Button("Open Email");
        email.Click += (_, _) => OpenEmail();

        var repository = Button("Open Repository");
        repository.Click += (_, _) => SetStatus(ExternalLinkLauncher.TryOpen(CitationInfo.SoftwareRepositoryUrl)
            ? "Repository opened"
            : "Could not open repository");

        var close = Button("Close");
        close.Click += (_, _) => Close();

        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        AddAction(actions, copy, 0, 0);
        AddAction(actions, save, 0, 1);
        AddAction(actions, email, 0, 2);
        AddAction(actions, repository, 0, 3);
        Grid.SetRow(statusText, 1);
        Grid.SetColumnSpan(statusText, 3);
        actions.Children.Add(statusText);
        Grid.SetRow(close, 1);
        Grid.SetColumn(close, 3);
        actions.Children.Add(close);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12
        };
        Grid.SetRow(title, 0);
        Grid.SetRow(summary, 1);
        Grid.SetRow(previewBorder, 2);
        Grid.SetRow(actions, 3);
        layout.Children.Add(title);
        layout.Children.Add(summary);
        layout.Children.Add(previewBorder);
        layout.Children.Add(actions);

        var content = new Border
        {
            Padding = new Thickness(18),
            Child = layout
        };
        AppTheme.Bind(content, Border.BackgroundProperty, AppTheme.WorkspaceBackground);
        Content = content;
    }

    public static Task ShowAsync(Window owner)
    {
        var dialog = new SupportWindow();
        return dialog.ShowDialog(owner);
    }

    public static void CopyReportToClipboard()
    {
        PlatformServices.ClipboardService.SetString(SupportReportBuilder.BuildFullReport());
    }

    void CopyReport(string status)
    {
        CopyReportToClipboard();
        SetStatus(status);
    }

    async Task SaveReportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Support Report",
            SuggestedFileName = $"ft-itc-support-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Text")
                {
                    Patterns = new[] { "*.txt" }
                },
                FilePickerFileTypes.All
            }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
            path += ".txt";

        try
        {
            File.WriteAllText(path, SupportReportBuilder.BuildFullReport());
            SetStatus("Support report saved");
        }
        catch (Exception ex)
        {
            AppEventHandler.AddLog(ex);
            SetStatus(ex.Message);
        }
    }

    void OpenEmail()
    {
        var subject = Uri.EscapeDataString("FT-ITC Analysis Support");
        var body = Uri.EscapeDataString(SupportReportBuilder.BuildEmailBody());
        var uri = $"mailto:{SupportReportBuilder.SupportAddress}?subject={subject}&body={body}";

        if (ExternalLinkLauncher.TryOpen(uri))
        {
            SetStatus("Email client opened");
            return;
        }

        CopyReport("Could not open email client. Support report copied.");
    }

    void SetStatus(string status)
    {
        statusText.Text = status ?? "";
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
        MinWidth = 112,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness(10, 6)
    };

    static TextBlock Text(string text, double size, string resourceKey, FontWeight weight = default)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight == default ? FontWeight.Normal : weight,
            TextWrapping = TextWrapping.Wrap
        };
        AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, resourceKey);
        return textBlock;
    }
}
