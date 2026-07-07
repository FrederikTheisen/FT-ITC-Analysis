using System.Threading.Tasks;

using AnalysisITC.Core.Application;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class AboutDialogWindow : Window
{
    AboutDialogWindow()
    {
        Title = "About FT-ITC Analysis";
        Width = 420;
        Height = 260;
        MinWidth = 380;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var title = new TextBlock
        {
            Text = "FT-ITC Analysis",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#202832")
        };

        var version = new TextBlock
        {
            Text = $"Version {AppVersion.FullVersionString}",
            FontSize = 13,
            Foreground = Brush("#607080"),
            Margin = new Thickness(0, 4, 0, 0)
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
            Text = "Avalonia cross-platform application preview.",
            FontSize = 12,
            Foreground = Brush("#607080"),
            Margin = new Thickness(0, 8, 0, 0)
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
                    new StackPanel
                    {
                        Children =
                        {
                            title,
                            version,
                            description,
                            note
                        }
                    }
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
