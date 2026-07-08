using System.Threading.Tasks;

using AnalysisITC.Core.Application;

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
        Height = 310;
        MinWidth = 380;
        MinHeight = 290;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var icon = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new System.Uri("avares://AnalysisITC.Avalonia/Resources/appicon.ico"))),
            Width = 120,
            Height = 120,
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = "FT-ITC Analysis",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#202832"),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var version = new TextBlock
        {
            Text = $"Version {AppVersion.ShortVersionString}",
            FontSize = 13,
            Foreground = Brush("#607080"),
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
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
            Foreground = Brush("#202832"),
            Margin = new Thickness(0, 18, 0, 0)
        };

        var note = new TextBlock
        {
            Text = "Copyright © 2026 Frederik Theisen. All rights reserved.",
            FontSize = 12,
            Foreground = Brush("#607080"),
            Margin = new Thickness(0, 8, 0, 0)
        };

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

        Content = new Border
        {
            Background = Brushes.White,
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

        DockPanel.SetDock(buttons, Dock.Bottom);
    }

    public static Task ShowAsync(Window owner)
    {
        var dialog = new AboutDialogWindow();
        return dialog.ShowDialog(owner);
    }

    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
