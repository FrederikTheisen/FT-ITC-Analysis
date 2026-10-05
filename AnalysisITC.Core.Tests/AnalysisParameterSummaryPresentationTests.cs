using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisParameterSummaryPresentationTests
{
    [Fact]
    public void InspectorSummaryWithoutSolutionIsEmpty()
    {
        var summary = AnalysisParameterSummaryPresentation.BuildInspectorSummary(null);

        Assert.True(summary.IsEmpty);
        Assert.Null(summary.Header);
        Assert.Empty(summary.Rows);
        Assert.Equal("No fit result for the selected experiment.", AnalysisParameterSummaryPresentation.NoFitText);
    }

    [Fact]
    public void InspectorSummaryShowsModelHeaderRmsdFittedAndDerivedParameters()
    {
        var model = CreateFittedOneSetOfSites();

        var summary = AnalysisParameterSummaryPresentation.BuildInspectorSummary(model.Solution);

        Assert.False(summary.IsEmpty);
        Assert.Equal(model.ModelName, summary.ModelTitle);
        Assert.Equal(AnalysisParameterSummaryPresentation.IndividualScope, summary.Scope);
        Assert.Equal("No shared parameters", summary.ScopeToolTip);

        var labels = summary.Rows.Select(row => row.Label).ToList();
        Assert.Equal("RMSD", labels[0]);
        Assert.Equal(model.Solution.Loss.ToString("G3"), summary.Rows[0].Value);
        Assert.Contains("N", labels);
        Assert.Contains(MarkdownStrings.DissociationConstant, labels);
        Assert.Contains(MarkdownStrings.Enthalpy, labels);
        Assert.Contains("Offset", labels);
        Assert.Contains(MarkdownStrings.EntropyContribution, labels);
        Assert.Contains(MarkdownStrings.GibbsFreeEnergy, labels);
        Assert.DoesNotContain(summary.Rows, row => row.IsModelHeader);
    }

    [Fact]
    public void GraphBoxAlwaysIncludesModelAndFittedParametersAndAddsPreferredCategories()
    {
        var withoutPreference = AnalysisParameterSummaryPresentation.GraphBoxDisplay(FinalFigureDisplayParameters.None);
        var withDerived = AnalysisParameterSummaryPresentation.GraphBoxDisplay(FinalFigureDisplayParameters.Derived);

        Assert.Equal(FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Fitted, withoutPreference);
        Assert.False(withoutPreference.HasFlag(FinalFigureDisplayParameters.Derived));
        Assert.True(withDerived.HasFlag(FinalFigureDisplayParameters.Model));
        Assert.True(withDerived.HasFlag(FinalFigureDisplayParameters.Fitted));
        Assert.True(withDerived.HasFlag(FinalFigureDisplayParameters.Derived));
    }

    [Fact]
    public void GraphBoxLinesPutRmsdOnTheModelHeaderLine()
    {
        var model = CreateFittedOneSetOfSites();
        var display = AnalysisParameterSummaryPresentation.GraphBoxDisplay(FinalFigureDisplayParameters.None);

        var lines = AnalysisParameterSummaryPresentation.BuildLines(model.Solution, display);

        Assert.Equal($"{model.Solution.SolutionName} | RMSD = {model.Solution.Loss.ToString("G3")}", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("N = "));
        Assert.DoesNotContain(lines, line => line.StartsWith(MarkdownStrings.GibbsFreeEnergy + " = "));
    }

    static Model CreateFittedOneSetOfSites()
    {
        var experiment = new ExperimentData("fit-summary.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };

        for (var index = 0; index < 5; index++)
        {
            var injection = new InjectionData(
                experiment,
                index,
                2e-6,
                experiment.SyringeConcentration * 2e-6,
                include: true)
            {
                ActualCellConcentration = experiment.CellConcentration * 0.99,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) * 5e-6 / (experiment.CellConcentration * 0.99),
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7));
            experiment.Injections.Add(injection);
        }

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        experiment.Model = model;
        return model;
    }
}
