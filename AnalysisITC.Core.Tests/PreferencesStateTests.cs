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
        var changed = PreferencesState.FromSettings(); changed.InterpretationOperatorCode = "operator-B";
        Assert.False(changed.TryGetInterpretationAccessOptions(out _));
        var corrupt = PreferencesState.FromSettings(); corrupt.InterpretationAccessOptionsJson = "{\"Models\":[null]}";
        Assert.False(corrupt.TryGetInterpretationAccessOptions(out _));
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
