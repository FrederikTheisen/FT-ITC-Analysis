using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Platform
{
    public interface IAnalysisResultUpdatePromptService
    {
        /// <summary>
        /// Returns null when the user cancels the update.
        /// </summary>
        Task<AnalysisResultUpdateOptions> ChooseOptionsAsync(AnalysisResult result);
    }
}
