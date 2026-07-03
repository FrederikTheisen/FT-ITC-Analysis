using AppKit;
using AnalysisITC.Platform;
using AnalysisITC.UI.MacOS;
using AnalysisITC.Platform.MacOS;
using System.Globalization;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC
{
    static class MainClass
    {
        static void Main(string[] args)
        {
            AppEventHandler.PrintAndLog("Initializing Application...");
            NSApplication.Init();
            MacPlatformBootstrapper.Register();
            PlatformServices.RegisterAppNotificationService(new MacAppNotificationService());
            PlatformServices.RegisterImportPromptService(new MacImportPromptService());
            PlatformServices.RegisterExportPromptService(new MacExportPromptService());
            PlatformServices.RegisterFileSavePromptService(new MacFileSavePromptService());
            PlatformServices.RegisterDataValidationPromptService(new MacDataValidationPromptService());
            PlatformServices.RegisterTandemImportPromptService(new MacTandemImportPromptService());
            PlatformServices.RegisterClipboardService(new MacClipboardService());
            PlatformServices.RegisterConfirmationPromptService(new MacConfirmationPromptService());
            MacSettingsApplier.Register();

            StateManager.Init();
            AppSettings.Initialize();
            BufferAttribute.Init();

            AppSettings.Locale = PlatformServices.AppEnvironment.LocaleIdentifier; // We need to know for dates
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); // We keep this for other formating...

            // Not implemented yet if ever
            // BufferRegistry.Registry = BufferRegistry.LoadFromFile("./Buffers.json");

            AppEventHandler.PrintAndLog("Executing App Main Method...");
            NSApplication.Main(args);
        }
    }
}
