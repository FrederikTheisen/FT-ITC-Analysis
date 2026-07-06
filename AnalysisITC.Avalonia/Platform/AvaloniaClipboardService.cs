using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;

using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaClipboardService : IClipboardService
    {
        public void SetString(string value)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                    return;

                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(value ?? "");
            });
        }
    }
}
