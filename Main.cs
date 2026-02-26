using System;
using AppKit;

namespace AnalysisITC
{
    static class MainClass
    {
        static void Main(string[] args)
        {
            NSApplication.Init();

            StateManager.Init();
            AppSettings.Initialize();
            BufferAttribute.Init();

            // Not implemented yet if ever
            // BufferRegistry.Registry = BufferRegistry.LoadFromFile("./Buffers.json");

            NSApplication.Main(args);
        }
    }
}
