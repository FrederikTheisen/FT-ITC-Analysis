using System;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

using Xunit;

using AnalysisITC.Avalonia.Preferences;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;

namespace AnalysisITC.Avalonia.Tests;

[CollectionDefinition("Avalonia UI", DisableParallelization = true)]
public sealed class AvaloniaUiCollection { }

[Collection("Avalonia UI")]
public sealed class PreferencesTests
{
    public PreferencesTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
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
        Assert.Equal(300_000.ToString("N0", CultureInfo.CurrentCulture), window.MaximumIterationsValueLabel.Text);

        window.AutoSaveEnabledCheck.IsChecked = false;
        Assert.False(window.AutoSaveIntervalSlider.IsEnabled);
        Assert.False(window.AutoSaveIntervalValueLabel.IsEnabled);
    }

    [Fact]
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

    [Theory]
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

    [Theory]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
        Assert.Equal(300_000, result.MaximumOptimizerIterations);
        Assert.Equal(savedBootstrapIterations, AnalysisITC.Core.Application.AppSettings.DefaultBootstrapIterations);
    }

    [Theory]
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

    [Fact]
    public void RemembersActiveTabOnlyForCurrentSession()
    {
        var originalStore = PlatformServices.SettingsStore;
        var store = new InMemorySettingsStore();
        PlatformServices.RegisterSettingsStore(store);

        try
        {
            store.SetInt("Avalonia.Preferences.ActiveTab", 2);
            var window = new PreferencesWindow();
            var root = Assert.IsType<DockPanel>(window.Content);
            var tabs = Assert.IsType<TabControl>(root.Children.Single(control => control is TabControl));

            Assert.Equal(0, tabs.SelectedIndex);

            tabs.SelectedIndex = 3;

            Assert.Equal(2, store.GetInt("Avalonia.Preferences.ActiveTab"));

            var reopenedWindow = new PreferencesWindow();
            var reopenedRoot = Assert.IsType<DockPanel>(reopenedWindow.Content);
            var reopenedTabs = Assert.IsType<TabControl>(
                reopenedRoot.Children.Single(control => control is TabControl));

            Assert.Equal(3, reopenedTabs.SelectedIndex);
        }
        finally
        {
            PlatformServices.RegisterSettingsStore(originalStore);
        }
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

internal static class AvaloniaTestBootstrap
{
    static readonly object setupLock = new();
    static bool initialized;

    internal static void EnsureInitialized()
    {
        lock (setupLock)
        {
            if (initialized) return;
            AppBuilder.Configure<HeadlessTestApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            initialized = true;
        }
    }

    sealed class HeadlessTestApplication : Application { }
}
