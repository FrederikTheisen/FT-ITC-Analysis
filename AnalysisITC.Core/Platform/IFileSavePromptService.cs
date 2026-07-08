using System.Collections.Generic;
using System.Threading.Tasks;


namespace AnalysisITC.Platform
{
    public interface IFileSavePromptService
    {
        Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes);
    }
}
