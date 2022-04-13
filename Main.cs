using AppKit;

namespace AnalysisITC
{
    static class MainClass
    {
        static void Main(string[] args)
        {
            NSApplication.Init();

            StateManager.Init();

            NSApplication.Main(args);
        }
    }
}
