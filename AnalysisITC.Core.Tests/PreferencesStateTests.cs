using System;
using System.Linq;
using System.Runtime.Loader;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Export;
using AnalysisITC.Platform;
using Xunit;
using AnalysisITC.Core.Interpretation;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition("Preferences", DisableParallelization = true)]
public sealed class PreferencesCollection { }

[Collection("Preferences")]
public sealed class PreferencesStateTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ISettingsStore originalStore = PlatformServices.SettingsStore;
    readonly InMemorySettingsStore store = new();

    public PreferencesStateTests() => PlatformServices.RegisterSettingsStore(store);

    public void Dispose()
    {
        original.ApplyToSettings();
        AppSettings.ApplySettings();
        PlatformServices.RegisterSettingsStore(originalStore);
    }

    [Fact]
    public void InterpretationAccessCacheRoundTripsOnlyForExactCodeAndRejectsCorruption()
    {
        var options = new InterpretationOperatorOptionsResponse
        {
            AccessTier = "administrator", Mode = "custom", PresetRevision = "test-1",
            DefaultModel = "mist-a", DefaultReasoningEffort = "medium",
            Models = new System.Collections.Generic.List<InterpretationOperatorModelOption>
            {
                new InterpretationOperatorModelOption { Id = "mist-a", ReasoningEfforts = new System.Collections.Generic.List<string> { "low", "medium" } }
            }
        };
        AppSettings.InterpretationOperatorCode = "operator-A";
        AppSettings.CacheInterpretationAccess("operator-A", options);
        PreferencesState.FromSettings().Apply();
        AppSettings.Reset(); AppSettings.Load();
        Assert.True(PreferencesState.FromSettings().TryGetInterpretationAccessOptions(out var restored));
        Assert.Equal("mist-a", restored.Models.Single().Id);
        Assert.Equal("administrator", AppSettings.InterpretationAccessTier);
        Assert.Equal("administrator", PreferencesState.FromSettings().InterpretationAccessTier);
        var changed = PreferencesState.FromSettings(); changed.InterpretationOperatorCode = "operator-B";
        Assert.False(changed.TryGetInterpretationAccessOptions(out _));
        var corrupt = PreferencesState.FromSettings(); corrupt.InterpretationAccessOptionsJson = "{\"Models\":[null]}";
        Assert.False(corrupt.TryGetInterpretationAccessOptions(out _));
    }

    [Fact]
    public void InterpretationAccessMetadataRoundTripsAndDistinguishesMissingDetails()
    {
        var expiry = DateTime.UtcNow.AddDays(4);
        var options = new InterpretationOperatorOptionsResponse
        {
            AccessTier = "standard", Mode = "presets",
            AccessDetails = new InterpretationAccessDetails { Name = "Evaluation team", ExpiresAtUtc = expiry },
            Presets = new System.Collections.Generic.List<InterpretationPresetOption>
            { new InterpretationPresetOption { Id = "instant", Name = "Instant" } }
        };
        AppSettings.InterpretationOperatorCode = "operator-meta";
        AppSettings.CacheInterpretationAccess("operator-meta", options);
        Assert.True(PreferencesState.FromSettings().TryGetInterpretationAccessOptions(out var restored));
        Assert.Equal("Evaluation team", restored.AccessDetails.Name);
        Assert.Equal(expiry, restored.AccessDetails.ExpiresAtUtc);

        options.AccessDetails.ExpiresAtUtc = null;
        AppSettings.CacheInterpretationAccess("operator-meta", options);
        Assert.True(PreferencesState.FromSettings().TryGetInterpretationAccessOptions(out restored));
        Assert.NotNull(restored.AccessDetails);
        Assert.Null(restored.AccessDetails.ExpiresAtUtc);

        options.AccessDetails = null;
        AppSettings.CacheInterpretationAccess("operator-meta", options);
        Assert.True(PreferencesState.FromSettings().TryGetInterpretationAccessOptions(out restored));
        Assert.Null(restored.AccessDetails);
    }

    [Fact]
    public void AccessDisplayUsesCodePresenceInsteadOfLegacyCheckbox()
    {
        var options = new InterpretationOperatorOptionsResponse
        {
            AccessTier = "standard", Mode = "presets",
            Presets = new System.Collections.Generic.List<InterpretationPresetOption>
            { new InterpretationPresetOption { Id = "standard", Name = "Standard" } }
        };
        AppSettings.InterpretationOperatorCode = "operator-display";
        AppSettings.InterpretationGenerationPreset = "standard";
        AppSettings.UseInterpretationEvaluationSettings = false;
        AppSettings.CacheInterpretationAccess("operator-display", options);
        Assert.Equal("Selected interpretation: Standard preset", InterpretationAccessDisplay.CurrentSetting());

        AppSettings.InterpretationOperatorCode = "";
        AppSettings.UseInterpretationEvaluationSettings = true;
        Assert.Equal("Selected interpretation: Default", InterpretationAccessDisplay.CurrentSetting());
    }

    [Fact]
    public void AccessDisplayNamesSelectedCustomModelAndReasoning()
    {
        var options = new InterpretationOperatorOptionsResponse
        {
            AccessTier = "administrator", Mode = "custom",
            DefaultModel = "default-model", DefaultReasoningEffort = "medium",
            Models = new System.Collections.Generic.List<InterpretationOperatorModelOption>
            {
                new InterpretationOperatorModelOption { Id = "selected-model", ReasoningEfforts = new System.Collections.Generic.List<string> { "high" } }
            }
        };
        AppSettings.InterpretationOperatorCode = "operator-custom-display";
        AppSettings.InterpretationEvaluationModel = "selected-model";
        AppSettings.InterpretationEvaluationReasoningEffort = "high";
        AppSettings.CacheInterpretationAccess("operator-custom-display", options);

        Assert.Equal("Selected interpretation: selected-model model · high reasoning", InterpretationAccessDisplay.CurrentSetting());
    }

    [Fact]
    public void SuccessfulInterpretationVerificationPersistsWithoutApplyingOtherPreferences()
    {
        var options = new InterpretationOperatorOptionsResponse
        {
            AccessTier = "advanced", Mode = "presets",
            Presets = new System.Collections.Generic.List<InterpretationPresetOption>
            { new InterpretationPresetOption { Id = "in-depth", Name = "In-depth" } }
        };

        AppSettings.PersistInterpretationAccessVerification("operator-persisted", options);
        AppSettings.Reset();
        AppSettings.Load();

        Assert.Equal("operator-persisted", AppSettings.InterpretationOperatorCode);
        Assert.Equal("advanced", AppSettings.InterpretationAccessTier);
        Assert.True(AppSettings.TryGetInterpretationAccessOptions("operator-persisted", out var restored));
        Assert.Equal("in-depth", restored.Presets.Single().Id);
    }

    [Fact]
    public void InterpretationAccountSnapshotRoundTripsForTheVerifiedCode()
    {
        var options = new InterpretationOperatorOptionsResponse
        {
            AccessTier = "advanced", Mode = "presets",
            Presets = new System.Collections.Generic.List<InterpretationPresetOption>
            { new InterpretationPresetOption { Id = "instant", Name = "Default" } }
        };
        var started = new DateTime(2026, 9, 10, 12, 30, 0, DateTimeKind.Utc);
        AppSettings.PersistInterpretationAccessVerification("operator-account", options);
        AppSettings.PersistInterpretationAccount("operator-account", new InterpretationAccountResponse
        {
            Status = "verified", Label = "Lab", Name = "Alice", Email = "alice@example.org",
            AccessTier = "advanced", AccessTierName = "Advanced", ExpiresAtUtc = started.AddDays(1),
            Usage = new InterpretationAccountUsage { Limited = true, RemainingPercent = 75, SpentUsd = 2.5m, LimitUsd = 10m, ResetsAtUtc = started.AddDays(20) },
            TotalRequests = 7,
            MostRecentRequest = new InterpretationMostRecentRequest { StartedAtUtc = started, CompletedAtUtc = started.AddSeconds(4), Outcome = "success", HttpStatus = 200 },
        });

        AppSettings.Reset();
        AppSettings.Load();

        Assert.True(AppSettings.TryGetInterpretationAccount("operator-account", out var restored, out var fetchedAt));
        Assert.Equal("Lab", restored.Label);
        Assert.Equal("alice@example.org", restored.Email);
        Assert.Equal(75, restored.Usage.RemainingPercent);
        Assert.Equal(7, restored.TotalRequests);
        Assert.Equal("success", restored.MostRecentRequest.Outcome);
        Assert.NotNull(fetchedAt);
        Assert.True(PreferencesState.FromSettings().TryGetInterpretationAccount(out var stateRestored, out _));
        Assert.Equal("Alice", stateRestored.Name);

        Assert.False(AppSettings.TryGetInterpretationAccount("different-code", out _, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshStaticSettingsAndResetUseApprovedDefaults(bool reset)
    {
        // An isolated assembly exercises the real static initializer regardless
        // of the preferences modified by earlier tests in this process.
        var context = new AssemblyLoadContext("Fresh preferences", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(AppSettings).Assembly.Location);
            var settings = assembly.GetType(typeof(AppSettings).FullName)!;
            if (reset)
            {
                settings.GetProperty("MaximumOptimizerIterations")!.SetValue(null, 17);
                settings.GetProperty("ConcentrationAutoVariance")!.SetValue(null, 0.0);
                settings.GetProperty("IsConcentrationAutoVarianceEnabled")!.SetValue(null, false);
                settings.GetMethod("Reset")!.Invoke(null, null);
            }

            Assert.Equal(20_000, settings.GetProperty("MaximumOptimizerIterations")!.GetValue(null));
            Assert.Equal(0.1, settings.GetProperty("ConcentrationAutoVariance")!.GetValue(null));
            Assert.Equal(true, settings.GetProperty("IsConcentrationAutoVarianceEnabled")!.GetValue(null));
            Assert.Equal(false, settings.GetProperty("EnableExtendedParameterLimits")!.GetValue(null));
            Assert.Equal(3.0, settings.GetProperty("MinimumTemperatureSpanForFitting")!.GetValue(null));
            Assert.Equal(0.03, settings.GetProperty("MinimumIonSpanForFitting")!.GetValue(null));
            Assert.Equal(1, settings.GetProperty("NumOfDecimalsToExport")!.GetValue(null));
            Assert.Equal("IncludedData", settings.GetProperty("ExportSelectionMode")!.GetValue(null)!.ToString());
            Assert.Equal(new[] { 6.5, 10.0 }, settings.GetProperty("FinalFigureDimensions")!.GetValue(null));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void EmptyStoreLoadsDefaultsAndSavedCustomValuesSurviveUnchangedApply()
    {
        PreferencesState.Defaults().ApplyToSettings();
        AppSettings.Load();
        Assert.Equal(20_000, AppSettings.MaximumOptimizerIterations);
        Assert.Equal(0.1, AppSettings.ConcentrationAutoVariance);
        Assert.Equal(ExportDataSelection.IncludedData, AppSettings.ExportSelectionMode);

        store.SetInt("MaximumOptimizerIterations", 456_789);
        store.SetInt("DefaultBootstrapIterations", 77);
        store.SetDouble("OptimizerTolerance", 0.73);
        store.SetDouble("ConcentrationAutoVariance", 0);
        store.SetInt("ParameterLimitSetting", (int)ParameterLimitSetting.NoLimit);
        store.SetBool("EnableExtendedParameterLimits", false);
        store.SetBool("UnifyTimeAxisForExport", false);
        store.SetBool("UseLargeAnalysisParameterText", true);
        store.SetString("ExportOutputBaseName", "Custom export");
        AppSettings.Load();
        PreferencesState.FromSettings().Apply();

        Assert.Equal(456_789, AppSettings.MaximumOptimizerIterations);
        Assert.Equal(456_789, store.GetInt("MaximumOptimizerIterations"));
        Assert.Equal(77, store.GetInt("DefaultBootstrapIterations"));
        Assert.Equal(0.73, store.GetDouble("OptimizerTolerance"));
        Assert.False(AppSettings.IsConcentrationAutoVarianceEnabled);
        Assert.False(FittingOptionsController.EnableAutoConcentrationVariance);
        Assert.True(AppSettings.EnableExtendedParameterLimits);
        Assert.True(store.GetBool("EnableExtendedParameterLimits"));
        Assert.False(store.GetBool("UnifyTimeAxisForExport"));
        Assert.True(store.GetBool("UseLargeAnalysisParameterText"));
        Assert.Equal("Custom export", store.GetString("ExportOutputBaseName"));
    }

    [Fact]
    public void SnapshotsAndStagedDefaultsDoNotMutateSettingsOrPersistUntilApplied()
    {
        AppSettings.MaximumOptimizerIterations = 123_456;
        AppSettings.FinalFigureDimensions = new[] { 12.3, 8.7 };
        AppSettings.ConcentrationAutoVariance = 0;
        AppSettings.ParameterLimitSetting = ParameterLimitSetting.Extended;
        var captured = PreferencesState.FromSettings();
        AppSettings.FinalFigureDimensions[0] = 19;
        Assert.Equal(12.3, captured.FinalFigureWidthCentimeters);

        var defaults = PreferencesState.Defaults();
        defaults.FinalFigureWidthCentimeters = 7;
        Assert.Equal(123_456, AppSettings.MaximumOptimizerIterations);
        Assert.Equal(19, AppSettings.FinalFigureDimensions[0]);
        Assert.Equal(0, store.Count);
        Assert.Equal(6.5, PreferencesState.Defaults().FinalFigureWidthCentimeters);

        defaults.Apply();
        Assert.Equal(20_000, AppSettings.MaximumOptimizerIterations);
        Assert.Equal(7, AppSettings.FinalFigureDimensions[0]);
        Assert.True(AppSettings.IsConcentrationAutoVarianceEnabled);
        Assert.False(AppSettings.EnableExtendedParameterLimits);
        Assert.Equal(20_000, store.GetInt("MaximumOptimizerIterations"));
    }
}
