using System.Globalization;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Platform;
using AnalysisITC.Platform.Avalonia;

namespace AnalysisITC.Avalonia
{
    public static class CoreStartup
    {
        static bool initialized;

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            AppEventHandler.PrintAndLog("Initializing Avalonia application...");
            AvaloniaPlatformBootstrapper.Register();

            StateManager.Init();
            AppSettings.Initialize();
            AvaloniaThemeService.Register();
            AvaloniaSettingsApplier.Register();
            BufferAttribute.Init();

            AppSettings.Locale = PlatformServices.AppEnvironment.LocaleIdentifier;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            _ = CitationManager.TryFetchOnlineCitation();
        }
    }
}
