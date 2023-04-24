using AppKit;

namespace AnalysisITC
{
    static class MainClass
    {
        static void Main(string[] args)
        {
            NSApplication.Init();

            StateManager.Init();
            AppSettings.Load();
            AppClasses.AnalysisClasses.BufferAttribute.Init();

            NSApplication.Main(args);
        }
    }
}
