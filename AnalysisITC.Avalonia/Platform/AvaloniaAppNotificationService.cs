using AnalysisITC.Core.Application;
using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaAppNotificationService : IAppNotificationService
    {
        public void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string? actionUrl = null)
        {
            AppEventHandler.PrintAndLog($"{title}: {message}");
        }
    }
}
