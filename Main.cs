using System;
using AppKit;
using System.Globalization;
using Foundation;
using System.Linq;
using System.Collections.Generic;

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

            // Not implemented yet if ever
            // BufferRegistry.Registry = BufferRegistry.LoadFromFile("./Buffers.json");

            AppEventHandler.PrintAndLog("Exceuting App Main Method...");
            NSApplication.Main(args);
        }
    }
}
