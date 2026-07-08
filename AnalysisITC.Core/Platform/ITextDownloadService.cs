using System.Threading.Tasks;

namespace AnalysisITC.Platform
{
    public interface ITextDownloadService
    {
        Task<string> DownloadStringAsync(string url);
    }
}
