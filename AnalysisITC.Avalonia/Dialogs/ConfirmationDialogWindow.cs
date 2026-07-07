using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class ConfirmationDialogWindow : Window
{
    ConfirmationDialogWindow(string title, string message, string cancelButton, string confirmButton)
    {
        Title = title;
        Width = 420;
        Height = 190;
        MinWidth = 360;
        MinHeight = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#202832")),
            Margin = new Thickness(0, 0, 0, 18)
        };

        var cancel = new Button
        {
            Content = cancelButton,
            MinWidth = 82,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        cancel.Click += (_, _) => Close(false);

        var confirm = new Button
        {
            Content = confirmButton,
            MinWidth = 82,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        confirm.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, confirm }
        };

        Content = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(16),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    buttons,
                    messageText
                }
            }
        };

        DockPanel.SetDock(buttons, Dock.Bottom);
    }

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string cancelButton = "Cancel",
        string confirmButton = "Confirm")
    {
        var dialog = new ConfirmationDialogWindow(title, message, cancelButton, confirmButton);
        return await dialog.ShowDialog<bool>(owner);
    }
}
