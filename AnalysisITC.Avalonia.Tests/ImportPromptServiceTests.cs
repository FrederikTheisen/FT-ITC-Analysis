using System.Globalization;

using Avalonia.Threading;

using AnalysisITC.Core.Units;
using AnalysisITC.Platform.Avalonia;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ImportPromptServiceTests
{
    public ImportPromptServiceTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void TemperatureInputStartsFromProposedTemperatureAndBlocksInvalidValues()
    {
        var window = CreateWindow(showTemperatureInput: true, defaultTemperature: 21.5);

        Assert.NotNull(window.TemperatureBox);
        Assert.Equal(21.5.ToString("G6", CultureInfo.CurrentCulture), window.TemperatureBox!.Text);
        Assert.True(window.ImportButton.IsEnabled);

        window.TemperatureBox.Text = "298";
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.ImportButton.IsEnabled);
        Assert.True(window.TemperatureMessage!.IsVisible);

        window.TemperatureBox.Text = "abc";
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.ImportButton.IsEnabled);

        window.TemperatureBox.Text = "25,5";
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ImportButton.IsEnabled);
        Assert.False(window.TemperatureMessage.IsVisible);
    }

    [Fact]
    public void EnergyUnitOnlyPromptHasNoTemperatureInput()
    {
        var window = CreateWindow(showTemperatureInput: false, defaultTemperature: 21.5);

        Assert.Null(window.TemperatureBox);
        Assert.True(window.ImportButton.IsEnabled);
    }

    static AvaloniaImportPromptService.EnergyUnitPromptWindow CreateWindow(bool showTemperatureInput, double defaultTemperature)
        => new(
            EnergyUnitAttribute.GetSelectableUnits(),
            0,
            "CURVE-1.aff",
            "DH = 1",
            allowQueueReuse: true,
            showReprocessChoice: true,
            defaultReprocess: false,
            reusedUnit: null,
            showTemperatureInput: showTemperatureInput,
            defaultTemperature: defaultTemperature);
}
