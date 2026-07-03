
namespace AnalysisITC.Platform
{
    public interface IAppNotificationService
    {
        void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string actionUrl = null);
    }
}
