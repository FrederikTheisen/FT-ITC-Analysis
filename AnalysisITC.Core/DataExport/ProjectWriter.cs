using System.Threading.Tasks;

using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Export
{
    /// <summary>
    /// Format-neutral production save service.  Native projects and autosaves are
    /// FTXTC; the FTITC writer remains only as a legacy import/test serializer.
    /// </summary>
    public static class ProjectWriter
    {
        public static bool IsSaved => FTITCWriter.IsSaved;
        public static bool IsWriteInProgress => FTITCWriter.IsWriteInProgress;
        public static void Save() => FTITCWriter.SaveState2();
        public static Task<bool> SaveAsync() => FTITCWriter.SaveState2Async();
        public static void SaveWithPath() => FTITCWriter.SaveWithPath();
        public static Task<bool> SaveWithPathAsync() => FTITCWriter.SaveWithPathAsync();
        public static void SaveSelected(ITCDataContainer data) => FTITCWriter.SaveSelected(data);
        public static Task<bool> SaveSelectedAsync(ITCDataContainer data) => FTITCWriter.SaveSelectedAsync(data);
        public static Task<bool> WriteAutoSaveAsync(string path) => FTITCWriter.WriteAutoSaveAsync(path);
    }
}
