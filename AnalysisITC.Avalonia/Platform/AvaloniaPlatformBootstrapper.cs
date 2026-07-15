using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public static class AvaloniaPlatformBootstrapper
    {
        public static void Register()
        {
            var environment = new AvaloniaAppEnvironment();

            PlatformServices.RegisterAppEnvironment(environment);
            PlatformServices.RegisterSettingsStore(new AvaloniaJsonSettingsStore(environment.ApplicationDataDirectory));
            PlatformServices.RegisterMainThreadDispatcher(new AvaloniaMainThreadDispatcher());
            PlatformServices.RegisterAppNotificationService(new AvaloniaAppNotificationService());
            PlatformServices.RegisterClipboardService(new AvaloniaClipboardService());
            PlatformServices.RegisterFileSavePromptService(new AvaloniaFileSavePromptService());
            PlatformServices.RegisterExportPromptService(new AvaloniaExportPromptService());
            PlatformServices.RegisterImportPromptService(new AvaloniaImportPromptService());
        }
    }
}
