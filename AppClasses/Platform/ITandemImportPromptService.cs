using AnalysisITC.Core.Processing;

namespace AnalysisITC.Platform
{
    public interface ITandemImportPromptService
    {
        TandemConcatenation.BackMixingSettings AskBackMixingSettings(
            string fileName,
            int segmentCount,
            TandemConcatenation.BackMixingSettings defaults);
    }
}
