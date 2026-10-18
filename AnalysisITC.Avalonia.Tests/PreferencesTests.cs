using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using Xunit;

using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;
using AnalysisITC.Platform.Avalonia;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class PreferencesTests
{
    public PreferencesTests() => PreferencesWindow.InterpretationHttpClientFactory =
        () => new HttpClient(new PreferencesHttpHandler());

    [AvaloniaFact]
    public void RegistrationButtonShowsUntilAccessIsVerified()
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();

        window.LoadState(state);
        Assert.True(window.RegisterInterpretationButton.IsVisible);

        state.InterpretationOperatorCode = "pending-code";
        window.LoadState(state);
        Assert.True(window.RegisterInterpretationButton.IsVisible);
    }

    [AvaloniaFact]
    public async Task PublicOptionsResponseAfterWindowClosureDoesNotUpdateWindowButIssuedCodeIsSaved()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        var originalFactory = PreferencesWindow.InterpretationHttpClientFactory;
        PreferencesWindow? window = null;
        var handler = new ClosingOptionsHandler();
        try
        {
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.InterpretationPublicClientCode = "";
            PreferencesWindow.InterpretationHttpClientFactory = () => new HttpClient(handler);
            window = new PreferencesWindow();
            await window.WaitForInterpretationOperationsAsync();
            AppSettings.InterpretationPublicClientCode = "";
            var optionsBeforeClose = typeof(PreferencesWindow).GetField("interpretationOptions",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

            var refresh = window.RefreshPublicInterpretationOptionsAsync();
            await handler.DelayedOptionsRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            handler.ReleaseDelayedOptions();
            await refresh;
            await window.WaitForInterpretationOperationsAsync();

            Assert.Equal("ftitc_pub_issued_for_closed_window", AppSettings.InterpretationPublicClientCode);
            Assert.Same(optionsBeforeClose, typeof(PreferencesWindow).GetField("interpretationOptions",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
        }
        finally
        {
            handler.ReleaseDelayedOptions();
            window?.Close();
            await (window?.WaitForInterpretationOperationsAsync() ?? Task.CompletedTask);
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
            PreferencesWindow.InterpretationHttpClientFactory = originalFactory;
        }
    }

    [AvaloniaFact]
    public async Task LateVerificationDenialCannotClearCredentialsAfterPreferencesReload()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        var originalFactory = PreferencesWindow.InterpretationHttpClientFactory;
        PreferencesWindow? window = null;
        var handler = new LateDenialHandler();
        try
        {
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.InterpretationOperatorCode = "pending-code";
            AppSettings.PersistInterpretationAccessVerification("pending-code", CachedOptions());
            PreferencesWindow.InterpretationHttpClientFactory = () => new HttpClient(handler);
            window = new PreferencesWindow();
            await window.WaitForInterpretationOperationsAsync();

            var verify = window.VerifyInterpretationAccessAsync();
            await handler.DenialRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.LoadState(PreferencesState.FromSettings());
            await window.WaitForInterpretationOperationsAsync();
            handler.ReleaseDenial();
            await verify;

            Assert.True(AppSettings.InterpretationAccessVerified);
            Assert.False(window.RegisterInterpretationButton.IsVisible);
            var verifyButton = (Button)typeof(PreferencesWindow).GetField("verifyInterpretationAccessButton",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.True(verifyButton.IsEnabled);
            var status = (TextBlock)typeof(PreferencesWindow).GetField("interpretationAccessStatus",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.StartsWith("Access: Verified (cached", status.Text);
        }
        finally
        {
            handler.ReleaseDenial();
            window?.Close();
            if (window != null) await window.WaitForInterpretationOperationsAsync();
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
            PreferencesWindow.InterpretationHttpClientFactory = originalFactory;
        }
    }

    [AvaloniaFact]
    public async Task SupersededPublicOptionsResponseCannotReplaceNewerOptions()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        var originalFactory = PreferencesWindow.InterpretationHttpClientFactory;
        PreferencesWindow? window = null;
        var handler = new SupersededOptionsHandler();
        try
        {
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.InterpretationPublicClientCode = "ftitc_pub_saved_for_test";
            PreferencesWindow.InterpretationHttpClientFactory = () => new HttpClient(handler);
            window = new PreferencesWindow();
            await window.WaitForInterpretationOperationsAsync();
            handler.DelayNextOptions();

            var stale = window.RefreshPublicInterpretationOptionsAsync();
            await handler.DelayedOptionsRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var current = window.RefreshPublicInterpretationOptionsAsync();
            await current;
            handler.ReleaseDelayedOptions();
            await stale;

            var options = (InterpretationOperatorOptionsResponse)typeof(PreferencesWindow).GetField("interpretationOptions",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.Equal("newer", options.Presets.Single().Id);
        }
        finally
        {
            handler.ReleaseDelayedOptions();
            window?.Close();
            if (window != null) await window.WaitForInterpretationOperationsAsync();
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
            PreferencesWindow.InterpretationHttpClientFactory = originalFactory;
        }
    }

    static InterpretationOperatorOptionsResponse CachedOptions() => new()
    {
        AccessTier = "standard", Mode = "presets", AccessDetails = new() { Name = "Test" },
        Presets = new() { new() { Id = "instant", Name = "Instant" } }, Models = new(), GuidanceVariants = new()
    };

    [AvaloniaFact]
    public void TraceabilityControlsLoadAndStageWithoutDiscardingTheImportPromptChoice()
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();
        state.TraceabilityModeEnabled = true;
        state.PromptForIdentifiersOnImport = false;
        state.UserName = "Zoë 李";

        window.LoadState(state);
        Assert.True(window.TraceabilityModeCheck.IsChecked);
        Assert.False(window.PromptForIdentifiersCheck.IsChecked);
        Assert.True(window.PromptForIdentifiersCheck.IsEnabled);
        Assert.Equal("Zoë 李", window.UserNameBox.Text);

        window.TraceabilityModeCheck.IsChecked = false;
        Assert.False(window.PromptForIdentifiersCheck.IsEnabled);
        Assert.True(window.TryBuildState(out var staged));
        Assert.False(staged.TraceabilityModeEnabled);
        Assert.False(staged.PromptForIdentifiersOnImport);
        Assert.Equal("Zoë 李", staged.UserName);
    }

    [AvaloniaFact]
    public void ApplyRejectsBlankOperatorWhenTraceabilityModeIsEnabled()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
        try
        {
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.UserName = "Previous operator";
            var window = new PreferencesWindow();
            window.LoadState(PreferencesState.FromSettings());
            window.TraceabilityModeCheck.IsChecked = true;
            window.UserNameBox.Text = "   ";

            // Validation returns before the first await, so the task completes on the UI thread.
            var apply = window.ApplyAsync();
            Assert.True(apply.IsCompleted);

            Assert.False(window.Applied);
            Assert.Equal("An operator name is required when Traceability Mode is enabled.", window.StatusText.Text);
            Assert.False(AppSettings.TraceabilityModeEnabled);
            Assert.Equal("Previous operator", AppSettings.UserName);
        }
        finally
        {
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [AvaloniaTheory]
    [InlineData(1, "Ideal continuous mixing")]
    [InlineData(2, "Discrete displacement")]
    public void BookkeepingPreferenceUsesStableValuesWithThreeLabels(int value, string label)
    {
        var window = new PreferencesWindow();
        var combo = (ComboBox)typeof(PreferencesWindow).GetField("dilutionMethodCombo",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Equal(new[] { "MicroCal", "Ideal continuous mixing", "Discrete displacement" }, combo.ItemsSource!.Cast<object>().Select(item => item.ToString()));
        var state = PreferencesState.Defaults();
        state.DilutionCalculationMethod = (AnalysisITC.Core.DataReaders.DilutionMethod)value;
        window.LoadState(state);
        Assert.Equal(label, combo.SelectedItem!.ToString());
        Assert.True(window.TryBuildState(out var restored));
        Assert.Equal(value, (int)restored.DilutionCalculationMethod);
    }

    [AvaloniaFact]
    public void BookkeepingDescriptionTracksSelectionAndDefaultsToDiscrete()
    {
        var window = new PreferencesWindow();
        var combo = (ComboBox)typeof(PreferencesWindow).GetField("dilutionMethodCombo",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        var description = (TextBlock)typeof(PreferencesWindow).GetField("dilutionMethodDescription",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;

        Assert.Equal(DilutionMethod.DiscreteDisplacement, PreferencesState.Defaults().DilutionCalculationMethod);
        window.LoadState(PreferencesState.Defaults());
        Assert.Equal(InjectionBookkeeping.Description(DilutionMethod.DiscreteDisplacement), description.Text);

        combo.SelectedItem = combo.ItemsSource!.Cast<object>().Single(item => item.ToString() == "MicroCal");
        Assert.Equal(InjectionBookkeeping.Description(DilutionMethod.MicroCal), description.Text);

        window.RestoreDefaults();
        Assert.Equal("Discrete displacement", combo.SelectedItem!.ToString());
        Assert.Equal(InjectionBookkeeping.Description(DilutionMethod.DiscreteDisplacement), description.Text);
    }

    [AvaloniaFact]
    public void DiscreteSlidersUseIndexedTicksAndExpectedLabels()
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();
        window.LoadState(state);

        AssertSlider(window.AutoSaveIntervalSlider, 5);
        AssertSlider(window.BootstrapIterationsSlider, 8);
        AssertSlider(window.OptimizerToleranceSlider, 4);
        AssertSlider(window.MaximumIterationsSlider, 7);
        Assert.Equal("5 min", window.AutoSaveIntervalValueLabel.Text);
        Assert.Equal(100.ToString("N0", CultureInfo.CurrentCulture), window.BootstrapIterationsValueLabel.Text);
        Assert.Equal("Balanced", window.OptimizerToleranceValueLabel.Text);
        Assert.Equal(20_000.ToString("N0", CultureInfo.CurrentCulture), window.MaximumIterationsValueLabel.Text);

        window.AutoSaveEnabledCheck.IsChecked = false;
        Assert.False(window.AutoSaveIntervalSlider.IsEnabled);
        Assert.False(window.AutoSaveIntervalValueLabel.IsEnabled);
    }

    [AvaloniaFact]
    public void EnergyUnitsPreferenceOffersOnlyAutomaticJouleAndCalorieFamilies()
    {
        var window = new PreferencesWindow();
        var labels = window.EnergyUnitCombo.ItemsSource!
            .Cast<object>()
            .Select(item => item.ToString())
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Joule",
                "Calories"
            },
            labels);

        var state = PreferencesState.Defaults();
        Assert.Equal(EnergyUnitFamily.Joules, state.EnergyUnitFamily);
        window.LoadState(state);
        Assert.True(window.TryBuildState(out var result));
        Assert.Equal(EnergyUnitFamily.Joules, result.EnergyUnitFamily);
    }

    [AvaloniaTheory]
    [InlineData(ITCInstrument.MicroCalITC200)]
    [InlineData(ITCInstrument.TAInstrumentsITCStandard)]
    public void DesignerInstrumentPreferenceLoadsAndRoundTrips(ITCInstrument instrument)
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();
        state.DefaultDesignerInstrument = instrument;

        window.LoadState(state);

        Assert.Equal(instrument.GetProperties().Name, window.DefaultDesignerInstrumentCombo.SelectedItem?.ToString());
        Assert.True(window.TryBuildState(out var result));
        Assert.Equal(instrument, result.DefaultDesignerInstrument);
    }

    [AvaloniaTheory]
    [InlineData(0, 0, 0.0, 0, "Fast")]
    [InlineData(60, 77, 0.7, 500_000, "Strict")]
    public void UntouchedDiscreteSlidersPreserveLegacyValues(
        int autoSaveInterval,
        int bootstrapIterations,
        double optimizerTolerance,
        int maximumIterations,
        string toleranceLabel)
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();
        state.AutoSaveIntervalMinutes = autoSaveInterval;
        state.DefaultBootstrapIterations = bootstrapIterations;
        state.OptimizerTolerance = optimizerTolerance;
        state.MaximumOptimizerIterations = maximumIterations;

        window.LoadState(state);

        Assert.Equal($"{autoSaveInterval} min", window.AutoSaveIntervalValueLabel.Text);
        Assert.Equal(bootstrapIterations.ToString("N0", CultureInfo.CurrentCulture), window.BootstrapIterationsValueLabel.Text);
        Assert.Equal(toleranceLabel, window.OptimizerToleranceValueLabel.Text);
        Assert.Equal(maximumIterations.ToString("N0", CultureInfo.CurrentCulture), window.MaximumIterationsValueLabel.Text);
        Assert.True(window.TryBuildState(out var result));
        Assert.Equal(autoSaveInterval, result.AutoSaveIntervalMinutes);
        Assert.Equal(bootstrapIterations, result.DefaultBootstrapIterations);
        Assert.Equal(optimizerTolerance, result.OptimizerTolerance, 6);
        Assert.Equal(maximumIterations, result.MaximumOptimizerIterations);
    }

    [AvaloniaFact]
    public void ExactSupportedStopsRemainUnchangedWhenUntouched()
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();
        state.AutoSaveIntervalMinutes = 20;
        state.DefaultBootstrapIterations = 2_000;
        state.OptimizerTolerance = 0.5;
        state.MaximumOptimizerIterations = 30_000;
        window.LoadState(state);

        Assert.True(window.TryBuildState(out var result));
        Assert.Equal(20, result.AutoSaveIntervalMinutes);
        Assert.Equal(2_000, result.DefaultBootstrapIterations);
        Assert.Equal(0.5, result.OptimizerTolerance, 6);
        Assert.Equal(30_000, result.MaximumOptimizerIterations);
    }

    [AvaloniaFact]
    public void MovingDiscreteSlidersSelectsSupportedStops()
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();
        state.AutoSaveIntervalMinutes = 0;
        state.DefaultBootstrapIterations = 77;
        state.OptimizerTolerance = 0.7;
        state.MaximumOptimizerIterations = 500_000;
        window.LoadState(state);

        window.AutoSaveIntervalSlider.Value = 4;
        window.BootstrapIterationsSlider.Value = 6;
        window.OptimizerToleranceSlider.Value = 1;
        window.MaximumIterationsSlider.Value = 6;

        Assert.Equal("20 min", window.AutoSaveIntervalValueLabel.Text);
        Assert.Equal(2_000.ToString("N0", CultureInfo.CurrentCulture), window.BootstrapIterationsValueLabel.Text);
        Assert.Equal("Relaxed", window.OptimizerToleranceValueLabel.Text);
        Assert.Equal(20_000.ToString("N0", CultureInfo.CurrentCulture), window.MaximumIterationsValueLabel.Text);
        Assert.True(window.TryBuildState(out var result));
        Assert.Equal(20, result.AutoSaveIntervalMinutes);
        Assert.Equal(2_000, result.DefaultBootstrapIterations);
        Assert.Equal(0.25, result.OptimizerTolerance, 6);
        Assert.Equal(20_000, result.MaximumOptimizerIterations);
    }

    [AvaloniaFact]
    public void RestoreDefaultsResetsDiscreteSliderPreservationWithoutSaving()
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();
        state.AutoSaveIntervalMinutes = 60;
        state.DefaultBootstrapIterations = 77;
        state.MaximumOptimizerIterations = 500_000;
        window.LoadState(state);
        var savedBootstrapIterations = AnalysisITC.Core.Application.AppSettings.DefaultBootstrapIterations;

        window.RestoreDefaults();

        Assert.True(window.TryBuildState(out var result));
        Assert.Equal(5, result.AutoSaveIntervalMinutes);
        Assert.Equal(100, result.DefaultBootstrapIterations);
        Assert.Equal(0.5, result.OptimizerTolerance, 6);
        Assert.Equal(20_000, result.MaximumOptimizerIterations);
        Assert.Equal(savedBootstrapIterations, AnalysisITC.Core.Application.AppSettings.DefaultBootstrapIterations);
    }

    [AvaloniaTheory]
    [InlineData(PublicationFont.Native, "Resolved on this computer:")]
    [InlineData(PublicationFont.Inter, "Resolved on this computer: Inter")]
    [InlineData(PublicationFont.LiberationSans, "Resolved on this computer: Liberation Sans")]
    public void PublicationFontRoundTripsAndShowsResolvedFamily(PublicationFont font, string expectedResolution)
    {
        var window = new PreferencesWindow();
        var state = PreferencesState.Defaults();
        state.PublicationFigureFont = font;

        window.LoadState(state);

        Assert.Contains(expectedResolution, window.PublicationFontResolutionText.Text);
        Assert.True(window.TryBuildState(out var result));
        Assert.Equal(font, result.PublicationFigureFont);

        window.RestoreDefaults();
        Assert.True(window.TryBuildState(out var defaults));
        Assert.Equal(PublicationFont.Native, defaults.PublicationFigureFont);
    }

    [AvaloniaFact]
    public void UnchangedApplyPreservesCustomSlidersAndPreferencesAbsentFromDialog()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        var store = new InMemorySettingsStore();
        PlatformServices.RegisterSettingsStore(store);
        try
        {
            var custom = PreferencesState.Defaults();
            custom.MaximumOptimizerIterations = 456_789;
            custom.DefaultBootstrapIterations = 77;
            custom.OptimizerTolerance = 0.73;
            custom.UnifyTimeAxisForExport = true;
            custom.UseLargeAnalysisParameterText = true;
            custom.ColorScheme = ColorSchemes.Viridis;
            custom.ColorSchemeGradientMode = ColorSchemeGradientMode.Stepwise;
            custom.ExportOutputBaseName = "Custom export";
            custom.Apply();
            var window = new PreferencesWindow();

            Assert.True(window.TryBuildState(out var state));
            state.Apply();

            Assert.Equal(456_789, store.GetInt("MaximumOptimizerIterations"));
            Assert.Equal(77, store.GetInt("DefaultBootstrapIterations"));
            Assert.Equal(0.73, store.GetDouble("OptimizerTolerance"));
            Assert.True(AppSettings.UnifyTimeAxisForExport);
            Assert.True(AppSettings.UseLargeAnalysisParameterText);
            Assert.Equal(ColorSchemes.Viridis, AppSettings.ColorScheme);
            Assert.Equal(ColorSchemeGradientMode.Stepwise, AppSettings.ColorSchemeGradientMode);
            Assert.Equal("Custom export", AppSettings.ExportOutputBaseName);
        }
        finally
        {
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [AvaloniaFact]
    public void RestoreDefaultsThenCancelLeavesStoredAndCurrentPreferencesUntouched()
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        var store = new InMemorySettingsStore();
        PlatformServices.RegisterSettingsStore(store);
        try
        {
            var custom = PreferencesState.Defaults();
            custom.MaximumOptimizerIterations = 456_789;
            custom.UnifyTimeAxisForExport = false;
            custom.ColorScheme = ColorSchemes.Viridis;
            custom.Apply();
            var window = new PreferencesWindow();
            window.RestoreDefaults();
            Assert.True(window.TryBuildState(out var defaults));
            Assert.Equal(20_000, defaults.MaximumOptimizerIterations);
            Assert.True(defaults.UnifyTimeAxisForExport);
            Assert.Equal(ColorSchemes.Default, defaults.ColorScheme);
            window.Close(); // Cancel only closes the window.

            Assert.Equal(456_789, AppSettings.MaximumOptimizerIterations);
            Assert.Equal(456_789, store.GetInt("MaximumOptimizerIterations"));
            Assert.False(AppSettings.UnifyTimeAxisForExport);
            Assert.Equal(ColorSchemes.Viridis, AppSettings.ColorScheme);
            var reopened = new PreferencesWindow();
            Assert.True(reopened.TryBuildState(out var current));
            Assert.Equal(456_789, current.MaximumOptimizerIterations);

            reopened.RestoreDefaults();
            Assert.True(reopened.TryBuildState(out var appliedDefaults));
            appliedDefaults.Apply();
            Assert.Equal(20_000, store.GetInt("MaximumOptimizerIterations"));
            Assert.True(AppSettings.UnifyTimeAxisForExport);
            Assert.Equal(ColorSchemes.Default, AppSettings.ColorScheme);
        }
        finally
        {
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [AvaloniaFact]
    public void RemembersActiveTabOnlyForCurrentSession()
    {
        var originalStore = PlatformServices.SettingsStore;
        var store = new InMemorySettingsStore();
        PlatformServices.RegisterSettingsStore(store);
        PreferencesWindow? initialWindow = null;
        PreferencesWindow? window = null;
        PreferencesWindow? reopenedWindow = null;
        TabControl? tabs = null;
        var sessionIndex = 0;

        try
        {
            initialWindow = new PreferencesWindow();
            var initialTabs = initialWindow.GetLogicalDescendants().OfType<TabControl>().Single();
            sessionIndex = initialTabs.SelectedIndex;
            var storedIndex = (sessionIndex + 1) % initialTabs.Items.Count;
            store.SetInt("Avalonia.Preferences.ActiveTab", storedIndex);
            window = new PreferencesWindow();
            var root = Assert.IsType<DockPanel>(window.Content);
            tabs = Assert.IsType<TabControl>(root.Children.Single(control => control is TabControl));

            Assert.Equal(sessionIndex, tabs.SelectedIndex);
            Assert.NotEqual(storedIndex, tabs.SelectedIndex);

            var selectedIndex = (sessionIndex + 2) % tabs.Items.Count;
            tabs.SelectedIndex = selectedIndex;

            Assert.Equal(storedIndex, store.GetInt("Avalonia.Preferences.ActiveTab"));

            reopenedWindow = new PreferencesWindow();
            var reopenedRoot = Assert.IsType<DockPanel>(reopenedWindow.Content);
            var reopenedTabs = Assert.IsType<TabControl>(
                reopenedRoot.Children.Single(control => control is TabControl));

            Assert.Equal(selectedIndex, reopenedTabs.SelectedIndex);
        }
        finally
        {
            if (tabs != null) tabs.SelectedIndex = sessionIndex;
            reopenedWindow?.Close();
            window?.Close();
            initialWindow?.Close();
            PlatformServices.RegisterSettingsStore(originalStore);
        }
    }

    [AvaloniaTheory]
    [InlineData("standard", "presets")]
    [InlineData("advanced", "presets")]
    [InlineData("administrator", "custom")]
    public async Task SavedAccessSurvivesOpeningApplyingAndReopeningPreferences(string tier, string mode)
    {
        var original = PreferencesState.FromSettings();
        var originalStore = PlatformServices.SettingsStore;
        var directory = Path.Combine(Path.GetTempPath(), "ftitc-preferences-" + Guid.NewGuid().ToString("N"));
        PreferencesWindow? window = null;
        try
        {
            PlatformServices.RegisterSettingsStore(new AvaloniaJsonSettingsStore(directory));
            PreferencesState.Defaults().ApplyToSettings();
            AppSettings.InterpretationGenerationPreset = "in-depth";
            AppSettings.InterpretationEvaluationModel = "synthetic-model";
            AppSettings.InterpretationEvaluationReasoningEffort = "high";
            AppSettings.PersistInterpretationAccessVerification("synthetic-code", new InterpretationOperatorOptionsResponse
            {
                AccessTier = tier, Mode = mode,
                AccessDetails = new() { Name = "Synthetic tester" },
                Presets = new() { new() { Id = "in-depth", Name = "In-depth" } },
                Models = new() { new() { Id = "synthetic-model", ReasoningEfforts = new() { "low", "high" } } },
                DefaultGuidanceVariant = "3.7.0",
                GuidanceVariants = new() { new() { Id = "3.7.0", DisplayName = "Guidance 3.7.0", Revision = "itc-scientific-guidance-3.7.0-experimentdesign" } }
            });
            var expiry = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            AppSettings.PersistInterpretationAccount("synthetic-code", new InterpretationAccountResponse
            {
                Label = "Fallback label", Name = "Synthetic tester", Email = "synthetic@example.org",
                Organization = "Synthetic Institute", AccessTierName = tier, ExpiresAtUtc = expiry,
                Usage = new InterpretationAccountUsage { Limited = true, RemainingPercent = 75, ResetsAtUtc = expiry.AddDays(30) },
            });

            for (var opening = 0; opening < 2; opening++)
            {
                PlatformServices.RegisterSettingsStore(new AvaloniaJsonSettingsStore(directory));
                AppSettings.ClearInterpretationAccessVerification();
                AppSettings.Load();
                Assert.Equal(tier, AppSettings.InterpretationAccessTier);
                window = new PreferencesWindow();
                window.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 4;
                window.Show();
                await window.WaitForInterpretationOperationsAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.False(window.RegisterInterpretationButton.IsVisible);

                var labels = window.GetLogicalDescendants().OfType<TextBlock>().ToArray();
                Assert.Contains(labels, label => (label.Text ?? "").StartsWith("Access: Verified (cached"));
                var accountDetails = window.GetLogicalDescendants().OfType<Grid>().Single(grid =>
                    AutomationProperties.GetName(grid) == "Automated interpretation account details");
                Assert.True(accountDetails.IsVisible);
                Assert.Equal(new[]
                {
                    "Name:", "Synthetic tester (" + tier + ")",
                    "Email:", "synthetic@example.org (Synthetic Institute)",
                    "Expiry:", LocalDateTimeFormatter.ShortDate(expiry.ToLocalTime()),
                    "Quota:", "75% remaining · resets " + LocalDateTimeFormatter.ShortDate(expiry.AddDays(30).ToLocalTime()),
                }, accountDetails.GetLogicalDescendants().OfType<TextBlock>().Select(label => label.Text));
                Assert.DoesNotContain(labels, label => (label.Text ?? "").Contains("Most recent request"));
                Assert.DoesNotContain(labels, label => (label.Text ?? "").Contains("$"));
                Assert.Equal(mode == "presets", ((Control)labels.Single(label => label.Text == "Interpretation depth").Parent!).IsVisible);
                Assert.Equal(mode == "custom", ((Control)labels.Single(label => label.Text == "Model").Parent!).IsVisible);
                Assert.Equal(mode == "custom", ((Control)labels.Single(label => label.Text == "Reasoning effort").Parent!).IsVisible);
                Assert.Equal(mode == "custom", ((Control)labels.Single(label => label.Text == "Scientific guidance").Parent!).IsVisible);
                Assert.True(window.TryBuildState(out var restored));
                Assert.True(restored.InterpretationAccessVerified);
                Assert.Equal(tier, restored.InterpretationAccessTier);
                Assert.Equal("in-depth", restored.InterpretationGenerationPreset);
                Assert.Equal("synthetic-model", restored.InterpretationEvaluationModel);
                Assert.Equal("3.7.0", restored.InterpretationEvaluationGuidanceVariant);
                Assert.Equal("high", restored.InterpretationEvaluationReasoningEffort);
                restored.Apply();
                if (opening == 0) { window.Close(); await window.WaitForInterpretationOperationsAsync(); window = null; }
            }

            var codeBox = window!.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Text == "synthetic-code");
            codeBox.Text = "different-code";
            Dispatcher.UIThread.RunJobs();
            Assert.True(window!.RegisterInterpretationButton.IsVisible);
            var existingDetails = window!.GetLogicalDescendants().OfType<Grid>().Single(grid =>
                AutomationProperties.GetName(grid) == "Automated interpretation account details");
            Assert.True(existingDetails.IsVisible);
            Assert.True(window!.TryBuildState(out var edited));
            Assert.False(edited.InterpretationAccessVerified);
            Assert.Empty(edited.InterpretationAccessTier);
        }
        finally
        {
            window?.Close();
            if (window != null) await window.WaitForInterpretationOperationsAsync();
            Dispatcher.UIThread.RunJobs();
            original.ApplyToSettings();
            AppSettings.ApplySettings();
            PlatformServices.RegisterSettingsStore(originalStore);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    sealed class PreferencesHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri.AbsolutePath switch
            {
                "/api/interpretation/public-access" => "{\"publicAccessCode\":\"ftitc_pub_test\",\"publicClientId\":\"test\"}",
                "/api/interpretation/account" => "{\"code\":\"temporary_failure\"}",
                _ => "{\"mode\":\"presets\",\"presets\":[{\"id\":\"instant\",\"name\":\"Instant\"}],\"models\":[],\"guidanceVariants\":[]}"
            };
            return Task.FromResult(new HttpResponseMessage(request.RequestUri.AbsolutePath == "/api/interpretation/public-access"
                ? HttpStatusCode.Created : request.RequestUri.AbsolutePath == "/api/interpretation/account" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    sealed class ClosingOptionsHandler : HttpMessageHandler
    {
        int optionsCalls;
        readonly TaskCompletionSource<HttpResponseMessage> delayedResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DelayedOptionsRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseDelayedOptions() => delayedResponse.TrySetResult(OptionsResponse());

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri.AbsolutePath == "/api/interpretation/public-access")
                return new HttpResponseMessage(HttpStatusCode.Created)
                { Content = new StringContent("{\"publicAccessCode\":\"ftitc_pub_issued_for_closed_window\",\"publicClientId\":\"test\"}", Encoding.UTF8, "application/json") };
            if (request.RequestUri.AbsolutePath == "/api/interpretation/options" && Interlocked.Increment(ref optionsCalls) == 1)
                return OptionsResponse();
            if (request.RequestUri.AbsolutePath == "/api/interpretation/options")
            {
                DelayedOptionsRequested.TrySetResult();
                return await delayedResponse.Task;
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        static HttpResponseMessage OptionsResponse() => new(HttpStatusCode.OK)
        { Content = new StringContent("{\"mode\":\"presets\",\"presets\":[{\"id\":\"instant\",\"name\":\"Instant\"}],\"models\":[],\"guidanceVariants\":[]}", Encoding.UTF8, "application/json") };
    }

    sealed class LateDenialHandler : HttpMessageHandler
    {
        readonly TaskCompletionSource<HttpResponseMessage> delayedResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DenialRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseDenial() => delayedResponse.TrySetResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri.AbsolutePath == "/api/interpretation/options")
            {
                DenialRequested.TrySetResult();
                return await delayedResponse.Task;
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }

    sealed class SupersededOptionsHandler : HttpMessageHandler
    {
        readonly TaskCompletionSource<HttpResponseMessage> delayedResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool delayNext;
        public TaskCompletionSource DelayedOptionsRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void DelayNextOptions() => delayNext = true;
        public void ReleaseDelayedOptions() => delayedResponse.TrySetResult(OptionsResponse("stale"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri.AbsolutePath == "/api/interpretation/options")
            {
                if (delayNext)
                {
                    delayNext = false;
                    DelayedOptionsRequested.TrySetResult();
                    return await delayedResponse.Task;
                }
                return OptionsResponse("newer");
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        static HttpResponseMessage OptionsResponse(string id) => new(HttpStatusCode.OK)
        { Content = new StringContent("{\"mode\":\"presets\",\"presets\":[{\"id\":\"" + id + "\",\"name\":\"Test\"}],\"models\":[],\"guidanceVariants\":[]}", Encoding.UTF8, "application/json") };
    }

    static void AssertSlider(Slider slider, int maximum)
    {
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(maximum, slider.Maximum);
        Assert.Equal(1, slider.TickFrequency);
        Assert.True(slider.IsSnapToTickEnabled);
        Assert.Equal(TickPlacement.None, slider.TickPlacement);
        Assert.Equal(new GridLength(4), slider.Resources["SliderPreContentMargin"]);
        Assert.Equal(new GridLength(4), slider.Resources["SliderPostContentMargin"]);
        Assert.Equal(28d, slider.Resources["SliderHorizontalHeight"]);
        Assert.Null(slider.RenderTransform);
    }

}
