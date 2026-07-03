using System.Collections.Generic;
using System.Threading.Tasks;

using AnalysisITC.Core.Export;

namespace AnalysisITC.Platform
{
    public interface IExportPromptService
    {
        Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings);
        bool ConfirmOverwrite(IEnumerable<string> outputPaths);
    }
}
