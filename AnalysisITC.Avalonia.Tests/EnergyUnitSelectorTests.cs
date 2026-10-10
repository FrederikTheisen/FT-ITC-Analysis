using System.Linq;

using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class EnergyUnitSelectorTests
{
    [AvaloniaFact]
    public void FigureSelectorChoosesFamilyForTheSessionAndResolvesMagnitudeAutomatically()
    {
        var original = FinalFigureModelPresentation.EnergyUnitFamily;
        try
        {
            FinalFigureModelPresentation.EnergyUnitFamily = EnergyUnitFamily.Joules;
            var workspace = new FinalFigureWorkspaceControl();

            Assert.Equal(new[] { "Joule", "Calories" }, workspace.EnergyUnitSelectorForTesting.Options);
            Assert.Equal(0, workspace.EnergyUnitSelectorForTesting.SelectedIndex);
            Assert.Null(workspace.GetOptionsSnapshot().EnergyUnitOverride);
            Assert.Equal(EnergyUnitFamily.Joules, workspace.GetOptionsSnapshot().EnergyUnitFamily);

            workspace.EnergyUnitSelectorForTesting.SelectedIndex = 1;
            Assert.Equal(EnergyUnitFamily.Calories, FinalFigureModelPresentation.EnergyUnitFamily);
            Assert.Equal(EnergyUnitFamily.Calories, workspace.GetOptionsSnapshot().EnergyUnitFamily);
            Assert.Null(workspace.GetOptionsSnapshot().EnergyUnitOverride);
            // The family lasts for the session; a new workspace starts from it.
            Assert.Equal(1, new FinalFigureWorkspaceControl().EnergyUnitSelectorForTesting.SelectedIndex);
        }
        finally
        {
            FinalFigureModelPresentation.EnergyUnitFamily = original;
        }
    }

    [AvaloniaFact]
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
