using AnalysisITC.Core.Units;

namespace AnalysisITC.Platform
{
    public readonly struct EnergyUnitPromptResult
    {
        public EnergyUnit? Unit { get; }
        public bool UseForRemainingFilesInQueue { get; }
        public bool IsCancelled { get; }
        /// <summary>Experiment temperature in °C, when the prompt asked for it.</summary>
        public double? Temperature { get; }

        public EnergyUnitPromptResult(EnergyUnit? unit, bool useForRemainingFilesInQueue, bool isCancelled, double? temperature = null)
        {
            Unit = unit;
            UseForRemainingFilesInQueue = useForRemainingFilesInQueue;
            IsCancelled = isCancelled;
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
            EnergyUnit? reusedUnit,
            bool showTemperatureInput,
            double defaultTemperature);
    }
}
