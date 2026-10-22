using System;
using System.IO;
using System.Text.Json;

using AnalysisITC.Avalonia;
using AnalysisITC.Core.Application;
using AnalysisITC.Platform;
using AnalysisITC.Platform.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AvaloniaSettingsStartupTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSettingsAllowInitializationAndMainWindowCreation(bool hasBackup)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ftitc-settings-startup-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        const string backup = "{\"IsSaved\":true,\"EnergyUnit\":1,\"ReferenceTemperature\":18}";
        var originalStore = PlatformServices.SettingsStore;
        var originalSettings = PreferencesState.FromSettings();
        var originalLastPath = AppSettings.LastDocumentPath;
        var originalLastPaths = AppSettings.LastDocumentPaths;
        MainWindow? window = null;
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(path, "{\"IsSaved\":true,\"ReferenceTemperature\":");
            if (hasBackup) File.WriteAllText(path + ".bak", backup);
            PreferencesState.Defaults().ApplyToSettings();
            var defaultTemperature = AppSettings.ReferenceTemperature;
            PlatformServices.RegisterSettingsStore(new AvaloniaJsonSettingsStore(directory));

            AppSettings.Initialize(); // Includes the startup preference migration and immediate save.

            Assert.Equal(hasBackup ? 18 : defaultTemperature, AppSettings.ReferenceTemperature);
            using var saved = JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(saved.RootElement.TryGetProperty("EnergyUnitFamily", out _));
            if (hasBackup)
            {
                // Startup can persist multiple migrations, rotating the first
                // valid migrated snapshot into the backup on the next save.
                using var recovered = JsonDocument.Parse(File.ReadAllText(path + ".bak"));
                Assert.True(recovered.RootElement.GetProperty("IsSaved").GetBoolean());
                Assert.Equal(1, recovered.RootElement.GetProperty("EnergyUnit").GetInt32());
                Assert.Equal(18d, recovered.RootElement.GetProperty("ReferenceTemperature").GetDouble());
            }

            AppSettings.PerformOnlineChecksOnLaunch = false;
            window = new MainWindow();
            window.Show();
            window.SetStartupReady();
            Assert.True(window.IsVisible);
            Assert.True(window.CanOpenFiles());
        }
        finally
        {
            window?.Close();
            originalSettings.ApplyToSettings();
            // These setters save; keep all cleanup writes inside the temporary store.
            AppSettings.LastDocumentPath = originalLastPath;
            AppSettings.LastDocumentPaths = originalLastPaths;
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
            Directory.Delete(directory, recursive: true);
        }
    }
}
