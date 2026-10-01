using AnalysisITC.Core.Units;

namespace AnalysisITC.Platform
{
    public readonly struct EnergyUnitPromptResult
    {
        public EnergyUnit? Unit { get; }
        public bool UseForRemainingFilesInQueue { get; }
        public bool IsCancelled { get; }
        public bool? ReprocessIntegratedHeatData { get; }
        /// <summary>Experiment temperature in °C, when the prompt asked for it.</summary>
        public double? Temperature { get; }

        public EnergyUnitPromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled)
            : this(unit, useForRemainingFilesInQueue, isCancelled, null)
        {
        }

        public EnergyUnitPromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled, bool? reprocessIntegratedHeatData)
            : this(unit, useForRemainingFilesInQueue, isCancelled, reprocessIntegratedHeatData, null)
        {
        }

        public EnergyUnitPromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled, bool? reprocessIntegratedHeatData, double? temperature)
        {
            Unit = unit;
            UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
            IsCancelled = isCancelled;
            ReprocessIntegratedHeatData = reprocessIntegratedHeatData;
            Temperature = temperature;
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
            EnergyUnit? reusedUnit,
            bool showTemperatureInput,
            double defaultTemperature);
    }
}
