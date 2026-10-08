using AnalysisITC.Core.DataReaders;

namespace AnalysisITC.Platform
{
    public enum FtxtcDuplicateAction
    {
        SkipDuplicates,
        ImportCopies,
    }

    public interface IFtxtcDuplicatePromptService
    {
        FtxtcDuplicateAction ChooseAction(FtxtcDuplicateSummary summary);
    }
}
