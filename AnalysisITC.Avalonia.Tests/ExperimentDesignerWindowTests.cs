using System.Globalization;

using Avalonia.Threading;

using AnalysisITC.Avalonia.Tools;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ExperimentDesignerWindowTests
{
    public ExperimentDesignerWindowTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void NoiseLevelDefaultsToTheExistingSimulationMagnitudeAndFollowsNoiseEnablement()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new ExperimentDesignerWindow();
            try
            {
                var slider = window.NoiseLevelSliderForTesting;

                Assert.Equal(0.1, slider.Minimum, 6);
                Assert.Equal(5, slider.Maximum, 6);
                Assert.Equal(0.1, slider.TickFrequency, 6);
                Assert.Equal(1, slider.Value, 6);
                Assert.Equal(1, window.NoiseMultiplierForTesting, 6);
                Assert.Equal(FormatNoiseLevel(1), window.NoiseLevelTextForTesting.Text);
                Assert.False(slider.IsEnabled);

                window.SimulateNoiseCheckForTesting.IsChecked = true;

                Assert.True(slider.IsEnabled);
                slider.Value = 2.5;
                Assert.Equal(2.5, window.NoiseMultiplierForTesting, 6);
                Assert.Equal(FormatNoiseLevel(2.5), window.NoiseLevelTextForTesting.Text);

                window.SimulateNoiseCheckForTesting.IsChecked = false;

                Assert.False(slider.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    static string FormatNoiseLevel(double value) => value.ToString("0.0", CultureInfo.CurrentCulture) + "×";
}
