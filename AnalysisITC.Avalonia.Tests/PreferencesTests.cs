using System;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

using Xunit;

using AnalysisITC.Avalonia.Preferences;

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
        Assert.Equal("Fast", window.OptimizerToleranceValueLabel.Text);
        Assert.Equal(300_000.ToString("N0", CultureInfo.CurrentCulture), window.MaximumIterationsValueLabel.Text);

        window.AutoSaveEnabledCheck.IsChecked = false;
        Assert.False(window.AutoSaveIntervalSlider.IsEnabled);
        Assert.False(window.AutoSaveIntervalValueLabel.IsEnabled);
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
        Assert.Equal(300_000, result.MaximumOptimizerIterations);
        Assert.Equal(savedBootstrapIterations, AnalysisITC.Core.Application.AppSettings.DefaultBootstrapIterations);
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
