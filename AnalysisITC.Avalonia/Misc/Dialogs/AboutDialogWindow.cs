using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Avalonia.Styling;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class AboutDialogWindow : Window
{
    AboutDialogWindow()
    {
        Title = "About FT-ITC Analysis";
        Width = 420;
        Height = 400;
        MinWidth = 380;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint= -1;

        var icon = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new System.Uri("avares://AnalysisITC.Avalonia/Resources/appicon.ico"))),
            Width = 120,
            Height = 120,
            Margin = new Thickness(0, 30, 0, 20),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = "FT-ITC Analysis",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        AppTheme.Bind(title, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

        var version = new TextBlock
        {
            Text = $"Version {AppVersion.ShortVersionString}",
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        AppTheme.Bind(version, TextBlock.ForegroundProperty, AppTheme.MutedText);
        var showingBuildVersion = false;
        version.PointerPressed += (_, _) =>
        {
            showingBuildVersion = !showingBuildVersion;
            version.Text = showingBuildVersion
                ? $"Version {AppVersion.FullVersionString}"
                : $"Version {AppVersion.ShortVersionString}";
        };

        var description = new TextBlock
        {
            Text = "Analysis and visualization of isothermal titration calorimetry experiments.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 0)
        };
        AppTheme.Bind(description, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

        var note = new TextBlock
        {
            Text = "Copyright © 2026 Frederik Theisen. All rights reserved.",
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0)
        };
        AppTheme.Bind(note, TextBlock.ForegroundProperty, AppTheme.MutedText);

        var textContent = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                icon,
                title,
                version
            }
        };

        var content = new StackPanel
        {
            Children =
            {
                textContent,
                description,
                note
            },
            Margin = new Thickness(0, 0, 0, 16)
        };

        var ok = new Button
        {
            Content = "OK",
            MinWidth = 82,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ok.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok }
        };

        var border = new Border
        {
            Padding = new Thickness(18),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    buttons,
                    content
                }
            }
        };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        Content = border;

        DockPanel.SetDock(buttons, Dock.Bottom);
    }

    public static Task ShowAsync(Window owner)
    {
        var dialog = new AboutDialogWindow();
        return dialog.ShowDialog(owner);
    }
}
