using System;
namespace AnalysisITC
{
    public static class StatusBarManager
    {
        public static event EventHandler<string> FileDidLoad;

        public static void FileLoaded(string filename)
        {
            FileDidLoad?.Invoke(null, filename);
        }
    }
}
