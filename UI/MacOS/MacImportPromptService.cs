using AnalysisITC.Platform;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS
{
    public sealed class MacImportPromptService : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
        {
            var result = EnergyUnitPrompt.AskForEnergyUnit(null, fileName, encounteredValue, allowQueueReuse);
            return new EnergyUnitPromptResult(result.Unit, result.UseForRemainingFilesInQueue, result.IsCancelled);
        }
    }
}
