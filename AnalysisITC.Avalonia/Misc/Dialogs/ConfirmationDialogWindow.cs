using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Styling;

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
            Margin = new Thickness(0, 0, 0, 18)
        };
        AppTheme.Bind(messageText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

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

        var border = new Border
        {
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
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        Content = border;

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
