using AnalysisITC.Core.Units;

namespace AnalysisITC.Platform
{
    public readonly struct EnergyUnitPromptResult
    {
        public EnergyUnit? Unit { get; }
        public bool UseForRemainingFilesInQueue { get; }
        public bool IsCancelled { get; }
        public bool? ReprocessIntegratedHeatData { get; }

        public EnergyUnitPromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled)
            : this(unit, useForRemainingFilesInQueue, isCancelled, null)
        {
        }

        public EnergyUnitPromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled, bool? reprocessIntegratedHeatData)
        {
            Unit = unit;
            UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
            IsCancelled = isCancelled;
            ReprocessIntegratedHeatData = reprocessIntegratedHeatData;
        }
    }

    public interface IImportPromptService
    {
        EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse);

    }

    public interface IIntegratedHeatImportPromptService : IImportPromptService
    {
        EnergyUnitPromptResult AskForEnergyUnit(
            string fileName,
            string encounteredValue,
            bool allowQueueReuse,
            bool showReprocessChoice,
            bool defaultReprocess,
            EnergyUnit? reusedUnit);
    }
}
