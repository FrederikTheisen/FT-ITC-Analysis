using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Styling;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class MissingAttributeDialogWindow : Window
{
    internal MissingAttributeDialogWindow(string message)
    {
        Title = "Attribute missing";
        Width = 520;
        Height = 300;
        MinWidth = 420;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        var details = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        };
        AppTheme.Bind(details, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

        var dismiss = new Button
        {
            Content = "OK",
            MinWidth = 82,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        dismiss.Click += (_, _) => Close();

        var content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                dismiss,
                new ScrollViewer { Content = details }
            }
        };
        DockPanel.SetDock(dismiss, Dock.Bottom);

        var border = new Border
        {
            Padding = new Thickness(16),
            Child = content
        };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        Content = border;
    }
}
