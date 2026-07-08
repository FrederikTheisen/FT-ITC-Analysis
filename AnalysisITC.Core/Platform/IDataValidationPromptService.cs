
namespace AnalysisITC.Platform
{
    public enum DataValidationPromptAction
    {
        AttemptFix,
        Discard,
        Keep,
    }

    public readonly struct DataValidationPromptResult
    {
        public DataValidationPromptAction Action { get; }
        public string Input { get; }

        public DataValidationPromptResult(DataValidationPromptAction action, string input = null)
        {
            Action = action;
            Input = input;
        }
    }

    public interface IDataValidationPromptService
    {
        DataValidationPromptResult AskValidationIssue(string title, string message, bool canFix, bool requiresInput);
    }
}
