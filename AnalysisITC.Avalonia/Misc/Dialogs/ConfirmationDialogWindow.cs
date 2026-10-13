using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using System.Linq;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class ConfirmationDialogWindow : Window
{
    ConfirmationDialogWindow(string title, string message, string cancelButton, string confirmButton,
        string? cancelTooltip = null, string? confirmTooltip = null)
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
        if (cancelTooltip != null) ToolTip.SetTip(cancel, cancelTooltip);
        cancel.Click += (_, _) => Close(false);

        var confirm = new Button
        {
            Content = confirmButton,
            MinWidth = 82,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (confirmTooltip != null) ToolTip.SetTip(confirm, confirmTooltip);
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

    public static Task<bool> ConfirmProcessingClearAsync(Window owner, int count)
    {
        var dialog = new ConfirmationDialogWindow("Clear Processing", ProcessingClearPresentation.Confirmation(count),
            "Keep", "Clear Processing", ProcessingClearPresentation.KeepTooltip, ProcessingClearPresentation.ConfirmTooltip);
        return dialog.ShowDialog<bool>(owner);
    }

    public static Task<bool> ConfirmDeleteReportAsync(Window owner, AnalysisReport report)
    {
        var dialog = new ConfirmationDialogWindow(ProjectInformationPresentation.DeleteDialogTitle,
            ProjectInformationPresentation.DeleteConfirmation(report,
                id => DataManager.Results.FirstOrDefault(result => result.UniqueID == id)),
            ProjectInformationPresentation.CancelLabel, ProjectInformationPresentation.DeleteLabel,
            ProjectInformationPresentation.CancelTooltip, ProjectInformationPresentation.DeleteTooltip)
        {
            Height = 280,
            CanResize = true,
        };
        // Long report names remain readable without pushing the buttons away.
        var host = (Border)dialog.Content!;
        var panel = (DockPanel)host.Child!;
        var message = panel.Children[1];
        panel.Children.RemoveAt(1);
        panel.Children.Add(new ScrollViewer { Content = message });
        return dialog.ShowDialog<bool>(owner);
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

    public static bool ConfirmModal(
        Window owner,
        string title,
        string message,
        string cancelButton = "Cancel",
        string confirmButton = "Confirm")
    {
        var dialog = new ConfirmationDialogWindow(title, message, cancelButton, confirmButton);
        var task = dialog.ShowDialog<bool>(owner);
        var frame = new DispatcherFrame();

        task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
        Dispatcher.UIThread.PushFrame(frame);

        return task.IsCompletedSuccessfully && task.Result;
    }
}
