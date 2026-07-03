using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Processing;

namespace AnalysisITC.Platform
{
    public static class PlatformServices
    {
        static readonly ISettingsStore DefaultSettingsStore = new InMemorySettingsStore();
        static readonly IMainThreadDispatcher DefaultMainThreadDispatcher = new ImmediateMainThreadDispatcher();
        static readonly IAppEnvironment DefaultAppEnvironment = new DefaultAppEnvironmentService();
        static readonly IAppNotificationService DefaultAppNotificationService = new FallbackAppNotificationService();
        static readonly IImportPromptService DefaultImportPromptService = new FallbackImportPromptService();
        static readonly IExportPromptService DefaultExportPromptService = new FallbackExportPromptService();
        static readonly IFileSavePromptService DefaultFileSavePromptService = new FallbackFileSavePromptService();
        static readonly IDataValidationPromptService DefaultDataValidationPromptService = new FallbackDataValidationPromptService();
        static readonly ITandemImportPromptService DefaultTandemImportPromptService = new FallbackTandemImportPromptService();
        static readonly IClipboardService DefaultClipboardService = new FallbackClipboardService();
        static readonly IConfirmationPromptService DefaultConfirmationPromptService = new FallbackConfirmationPromptService();
        static readonly ITextDownloadService DefaultTextDownloadService = new FallbackTextDownloadService();

        public static ISettingsStore SettingsStore { get; private set; } = DefaultSettingsStore;
        public static IMainThreadDispatcher MainThreadDispatcher { get; private set; } = DefaultMainThreadDispatcher;
        public static IAppEnvironment AppEnvironment { get; private set; } = DefaultAppEnvironment;
        public static IAppNotificationService AppNotificationService { get; private set; } = DefaultAppNotificationService;
        public static IImportPromptService ImportPromptService { get; private set; } = DefaultImportPromptService;
        public static IExportPromptService ExportPromptService { get; private set; } = DefaultExportPromptService;
        public static IFileSavePromptService FileSavePromptService { get; private set; } = DefaultFileSavePromptService;
        public static IDataValidationPromptService DataValidationPromptService { get; private set; } = DefaultDataValidationPromptService;
        public static ITandemImportPromptService TandemImportPromptService { get; private set; } = DefaultTandemImportPromptService;
        public static IClipboardService ClipboardService { get; private set; } = DefaultClipboardService;
        public static IConfirmationPromptService ConfirmationPromptService { get; private set; } = DefaultConfirmationPromptService;
        public static ITextDownloadService TextDownloadService { get; private set; } = DefaultTextDownloadService;

        public static void RegisterSettingsStore(ISettingsStore settingsStore)
        {
            SettingsStore = settingsStore ?? DefaultSettingsStore;
        }

        public static void RegisterMainThreadDispatcher(IMainThreadDispatcher dispatcher)
        {
            MainThreadDispatcher = dispatcher ?? DefaultMainThreadDispatcher;
        }

        public static void RegisterAppEnvironment(IAppEnvironment appEnvironment)
        {
            AppEnvironment = appEnvironment ?? DefaultAppEnvironment;
        }

        public static void RegisterAppNotificationService(IAppNotificationService notificationService)
        {
            AppNotificationService = notificationService ?? DefaultAppNotificationService;
        }

        public static void RegisterImportPromptService(IImportPromptService importPromptService)
        {
            ImportPromptService = importPromptService ?? DefaultImportPromptService;
        }

        public static void RegisterExportPromptService(IExportPromptService exportPromptService)
        {
            ExportPromptService = exportPromptService ?? DefaultExportPromptService;
        }

        public static void RegisterFileSavePromptService(IFileSavePromptService fileSavePromptService)
        {
            FileSavePromptService = fileSavePromptService ?? DefaultFileSavePromptService;
        }

        public static void RegisterDataValidationPromptService(IDataValidationPromptService dataValidationPromptService)
        {
            DataValidationPromptService = dataValidationPromptService ?? DefaultDataValidationPromptService;
        }

        public static void RegisterTandemImportPromptService(ITandemImportPromptService tandemImportPromptService)
        {
            TandemImportPromptService = tandemImportPromptService ?? DefaultTandemImportPromptService;
        }

        public static void RegisterClipboardService(IClipboardService clipboardService)
        {
            ClipboardService = clipboardService ?? DefaultClipboardService;
        }

        public static void RegisterConfirmationPromptService(IConfirmationPromptService confirmationPromptService)
        {
            ConfirmationPromptService = confirmationPromptService ?? DefaultConfirmationPromptService;
        }

        public static void RegisterTextDownloadService(ITextDownloadService textDownloadService)
        {
            TextDownloadService = textDownloadService ?? DefaultTextDownloadService;
        }

        sealed class ImmediateMainThreadDispatcher : IMainThreadDispatcher
        {
            public void Invoke(Action action) => action?.Invoke();
        }

        sealed class DefaultAppEnvironmentService : IAppEnvironment
        {
            public string LocaleIdentifier => "en-US";
            public string ShortVersion => typeof(PlatformServices).Assembly.GetName().Version?.ToString();
            public string BuildVersion => ShortVersion;
            public string ApplicationDataDirectory => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            public string GetResourcePath(string name, string extension)
            {
                var filename = string.IsNullOrWhiteSpace(extension)
                    ? name
                    : name + "." + extension.TrimStart('.');

                var basePath = AppContext.BaseDirectory;
                var baseCandidate = Path.Combine(basePath, filename);
                if (File.Exists(baseCandidate)) return baseCandidate;

                var currentCandidate = Path.Combine(Environment.CurrentDirectory, filename);
                return File.Exists(currentCandidate) ? currentCandidate : null;
            }
        }

        sealed class FallbackAppNotificationService : IAppNotificationService
        {
            public void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string actionUrl = null)
            {
                AppEventHandler.PrintAndLog($"{title}: {message}");
            }
        }

        sealed class FallbackImportPromptService : IImportPromptService
        {
            public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
            {
                return new EnergyUnitPromptResult(AppSettings.EnergyUnit, false, false);
            }
        }

        sealed class FallbackExportPromptService : IExportPromptService
        {
            public Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings)
            {
                return Task.FromResult<string>(null);
            }

            public bool ConfirmOverwrite(IEnumerable<string> outputPaths)
            {
                return true;
            }
        }

        sealed class FallbackFileSavePromptService : IFileSavePromptService
        {
            public Task<string> ChooseSaveFilePathAsync(string title, IEnumerable<string> allowedFileTypes)
            {
                return Task.FromResult<string>(null);
            }
        }

        sealed class FallbackDataValidationPromptService : IDataValidationPromptService
        {
            public DataValidationPromptResult AskValidationIssue(string title, string message, bool canFix, bool requiresInput)
            {
                return new DataValidationPromptResult(DataValidationPromptAction.Keep);
            }
        }

        sealed class FallbackTandemImportPromptService : ITandemImportPromptService
        {
            public TandemConcatenation.BackMixingSettings AskBackMixingSettings(
                string fileName,
                int segmentCount,
                TandemConcatenation.BackMixingSettings defaults)
            {
                return defaults;
            }
        }

        sealed class FallbackClipboardService : IClipboardService
        {
            public void SetString(string value)
            {
            }
        }

        sealed class FallbackConfirmationPromptService : IConfirmationPromptService
        {
            public bool ConfirmDestructiveAction(string message, string cancelButton = "Keep", string confirmButton = "Overwrite")
            {
                return false;
            }
        }

        sealed class FallbackTextDownloadService : ITextDownloadService
        {
            public Task<string> DownloadStringAsync(string url)
            {
                return Task.Run(() =>
                {
                    using var client = new WebClient();
                    return client.DownloadString(url);
                });
            }
        }
    }
}
