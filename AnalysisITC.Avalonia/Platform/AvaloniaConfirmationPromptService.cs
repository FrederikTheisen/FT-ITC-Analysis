using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia;

public sealed class AvaloniaConfirmationPromptService : IConfirmationPromptService
{
    public bool ConfirmDestructiveAction(string message, string cancelButton = "Keep", string confirmButton = "Overwrite")
    {
        bool Confirm()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow == null)
                return false;

            return ConfirmationDialogWindow.ConfirmModal(
                desktop.MainWindow,
                "Confirm change",
                message,
                cancelButton,
                confirmButton);
        }

        return Dispatcher.UIThread.CheckAccess()
            ? Confirm()
            : Dispatcher.UIThread.Invoke(Confirm);
    }
}
