using System;
using System.IO;
using System.Text.Json;

using AnalysisITC.Platform.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class AvaloniaJsonSettingsStoreTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-settings-" + Guid.NewGuid().ToString("N"));
    string SettingsPath => Path.Combine(directory, "settings.json");
    string BackupPath => SettingsPath + ".bak";

    public AvaloniaJsonSettingsStoreTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"IsSaved\":true,\"EnergyUnitFamily\":")]
    [InlineData("null")]
    [InlineData("[]")]
    public void InvalidSettingsUseDefaultsAndCanBeSavedAgain(string json)
    {
        File.WriteAllText(SettingsPath, json);

        var store = new AvaloniaJsonSettingsStore(directory);

        Assert.Equal(0, store.Count);
        Assert.Equal(25, store.GetDouble("ReferenceTemperature", 25));
        store.SetDouble("ReferenceTemperature", 30);
        store.Synchronize();
        Assert.Equal(30, new AvaloniaJsonSettingsStore(directory).GetDouble("ReferenceTemperature"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrInvalidPrimaryRecoversBackupAndPreservesItDuringNextSave(bool corruptPrimary)
    {
        const string backup = "{\"ReferenceTemperature\":18,\"UserName\":\"Zoë 李\"}";
        File.WriteAllText(BackupPath, backup);
        if (corruptPrimary) File.WriteAllText(SettingsPath, "{\"ReferenceTemperature\":");

        var store = new AvaloniaJsonSettingsStore(directory);

        Assert.Equal(18, store.GetDouble("ReferenceTemperature"));
        Assert.Equal("Zoë 李", store.GetString("UserName"));
        store.SetDouble("ReferenceTemperature", 25);
        store.Synchronize();
        Assert.Equal(25, new AvaloniaJsonSettingsStore(directory).GetDouble("ReferenceTemperature"));
        Assert.Equal(backup, File.ReadAllText(BackupPath));

        // A second interrupted startup must still have valid prior settings.
        File.WriteAllText(SettingsPath, "{");
        Assert.Equal(18, new AvaloniaJsonSettingsStore(directory).GetDouble("ReferenceTemperature"));
    }

    [Fact]
    public void InvalidPrimaryAndBackupUseDefaultsWithoutBackingUpCorruptSettings()
    {
        File.WriteAllText(SettingsPath, "{");
        File.WriteAllText(BackupPath, "[]");

        var store = new AvaloniaJsonSettingsStore(directory);
        Assert.Equal(0, store.Count);
        store.SetBool("IsSaved", true);
        store.Synchronize();

        Assert.True(new AvaloniaJsonSettingsStore(directory).GetBool("IsSaved"));
        Assert.Equal("[]", File.ReadAllText(BackupPath));
    }

    [Fact]
    public void ValidPrimaryTakesPrecedenceOverBackupAndUnfinishedTemporaryFiles()
    {
        File.WriteAllText(SettingsPath, "{\"ReferenceTemperature\":25}");
        File.WriteAllText(BackupPath, "{\"ReferenceTemperature\":18}");
        File.WriteAllText(Path.Combine(directory, "settings.json.tmp-abandoned"), "{");

        Assert.Equal(25, new AvaloniaJsonSettingsStore(directory).GetDouble("ReferenceTemperature"));
    }

    [Fact]
    public void SynchronizeKeepsPreviousCompleteSettingsAsBackup()
    {
        var store = new AvaloniaJsonSettingsStore(directory);
        store.SetInt("DefaultBootstrapIterations", 100);
        store.Synchronize();
        var first = File.ReadAllText(SettingsPath);

        store.SetInt("DefaultBootstrapIterations", 200);
        store.Synchronize();

        Assert.Equal(first, File.ReadAllText(BackupPath));
        Assert.Equal(200, new AvaloniaJsonSettingsStore(directory).GetInt("DefaultBootstrapIterations"));
        store.SetInt("DefaultBootstrapIterations", 300);
        store.Synchronize();
        using var backup = JsonDocument.Parse(File.ReadAllText(BackupPath));
        Assert.Equal(200, backup.RootElement.GetProperty("DefaultBootstrapIterations").GetInt32());
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public void FailedReplacementLeavesSavedSettingsIntactAndCanBeRetried()
    {
        var store = new AvaloniaJsonSettingsStore(directory);
        store.SetInt("DefaultBootstrapIterations", 100);
        store.Synchronize();
        var original = File.ReadAllText(SettingsPath);
        Directory.CreateDirectory(BackupPath); // Deterministic replacement failure, including on Windows.

        store.SetInt("DefaultBootstrapIterations", 200);
        var error = Record.Exception(store.Synchronize);
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "The save unexpectedly succeeded.");

        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Equal(100, new AvaloniaJsonSettingsStore(directory).GetInt("DefaultBootstrapIterations"));
        Assert.Single(Directory.GetFiles(directory));
        Directory.Delete(BackupPath);
        store.Synchronize();
        Assert.Equal(200, new AvaloniaJsonSettingsStore(directory).GetInt("DefaultBootstrapIterations"));
        Assert.Equal(original, File.ReadAllText(BackupPath));
    }

    [Fact]
    public void ExistingSettingsRoundTripTypesUnknownKeysAndRemovals()
    {
        File.WriteAllText(SettingsPath, "{\"FutureSetting\":{\"enabled\":true},\"RemoveMe\":\"old\"}");
        var store = new AvaloniaJsonSettingsStore(directory);
        store.SetBool("IsSaved", true);
        store.SetInt("DefaultBootstrapIterations", 250);
        store.SetDouble("ReferenceTemperature", 25.5);
        store.SetString("UserName", "Zoë 李");
        store.SetDoubleArray("FinalFigureDimensions", new[] { 6.5, 10.0 });
        store.SetStringArray("LastDocumentUrls", new[] { "/tmp/α.ftxtc", "/tmp/b.ftxtc" });
        store.SetString("RemoveMe", null);
        store.Synchronize();

        var reopened = new AvaloniaJsonSettingsStore(directory);
        Assert.True(reopened.GetBool("IsSaved"));
        Assert.Equal(250, reopened.GetInt("DefaultBootstrapIterations"));
        Assert.Equal(25.5, reopened.GetDouble("ReferenceTemperature"));
        Assert.Equal("Zoë 李", reopened.GetString("UserName"));
        Assert.Equal(new[] { 6.5, 10.0 }, reopened.GetDoubleArray("FinalFigureDimensions"));
        Assert.Equal(new[] { "/tmp/α.ftxtc", "/tmp/b.ftxtc" }, reopened.GetStringArray("LastDocumentUrls"));
        Assert.False(reopened.Contains("RemoveMe"));
        using var saved = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.True(saved.RootElement.GetProperty("FutureSetting").GetProperty("enabled").GetBoolean());
    }
}
