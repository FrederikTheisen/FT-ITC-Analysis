using AnalysisITC.Core.Units;

namespace AnalysisITC.Platform
{
    public readonly struct EnergyUnitPromptResult
    {
        public EnergyUnit? Unit { get; }
        public bool UseForRemainingFilesInQueue { get; }
        public bool IsCancelled { get; }

        public EnergyUnitPromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled)
        {
            Unit = unit;
            UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
            IsCancelled = isCancelled;
        }
    }

    public interface IImportPromptService
    {
        EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse);
    }
}
