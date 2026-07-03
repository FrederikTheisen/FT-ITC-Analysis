
namespace AnalysisITC.Platform
{
    public interface IConfirmationPromptService
    {
        bool ConfirmDestructiveAction(string message, string cancelButton = "Keep", string confirmButton = "Overwrite");
    }
}
