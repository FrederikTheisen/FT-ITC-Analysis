using System.Linq;

using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class EnergyUnitSelectorTests
{
    public EnergyUnitSelectorTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void FigureSelectorOffersAutomaticAndSupportedExactOverrides()
    {
        var workspace = new FinalFigureWorkspaceControl();

        Assert.Equal(
            new[] { "Automatic", "J", "kJ", "cal", "kcal" },
            workspace.EnergyUnitComboForTesting.ItemsSource!
                .Cast<object>()
                .Select(item => item.ToString())
                .ToArray());
        Assert.Equal(0, workspace.EnergyUnitComboForTesting.SelectedIndex);
        Assert.Null(workspace.GetOptionsSnapshot().EnergyUnitOverride);
    }

    [Fact]
    public void ResultExporterOffersAutomaticAndSupportedExactOverrides()
    {
        var window = new AnalysisResultExporterWindow();

        Assert.Equal(
            new[] { "Automatic", "J", "kJ", "cal", "kcal" },
            window.EnergyUnitComboForTesting.ItemsSource!
                .Cast<object>()
                .Select(item => item.ToString())
                .ToArray());
        Assert.Equal(0, window.EnergyUnitComboForTesting.SelectedIndex);
    }
}
