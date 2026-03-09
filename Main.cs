using System;
using AppKit;
using System.Globalization;
using Foundation;

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

            AppSettings.Locale = NSLocale.CurrentLocale.CollatorIdentifier; // We need to know for dates 
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // We keep this for other formating...

            Console.WriteLine("test1");
            CitationManager.TryFetchOnlineCitation();
            Console.WriteLine("test2");

            // Not implemented yet if ever
            // BufferRegistry.Registry = BufferRegistry.LoadFromFile("./Buffers.json");

            NSApplication.Main(args);
        }
    }
}
