using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition("Two-site entropy presentation", DisableParallelization = true)]
public sealed class TwoSiteEntropyPresentationCollection { }

[Collection("Two-site entropy presentation")]
public sealed class TwoSiteEntropyPresentationTests
{
    [Theory]
    [InlineData(EnergyUnit.KiloJoule, UncertaintyDisplayStyle.StandardDeviation)]
    [InlineData(EnergyUnit.KCal, UncertaintyDisplayStyle.StandardDeviation)]
    [InlineData(EnergyUnit.KiloJoule, UncertaintyDisplayStyle.None)]
    [InlineData(EnergyUnit.KCal, UncertaintyDisplayStyle.None)]
    public void InspectorGraphAndFigureShowBothSites(EnergyUnit unit, UncertaintyDisplayStyle style)
    {
        var previousStyle = AppSettings.UncertaintyDisplayStyle;
        try
        {
            AppSettings.UncertaintyDisplayStyle = style;
            var solution = CreateSolution();
            solution.PresentationEnergyUnitOverride = unit;
            var inspector = AnalysisParameterSummaryPresentation.BuildInspectorSummary(solution);
            var graph = AnalysisParameterSummaryPresentation.BuildLines(solution,
                AnalysisParameterSummaryPresentation.GraphBoxDisplay(FinalFigureDisplayParameters.Derived));
            var figure = PublicationFigureBuilder.Build(
                new PublicationFigureSource(solution.Model.Data, solution),
                new PublicationFigureOptions
                {
                    ShowThermogram = false,
                    ShowFitParameters = true,
                    DisplayParameters = FinalFigureDisplayParameters.Fitted | FinalFigureDisplayParameters.Derived,
                    EnergyUnitOverride = unit,
                    TextUncertaintyStyle = style,
                });
            var annotation = figure.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines).ToList();

            for (var site = 1; site <= 2; site++)
            {
                var label = MarkdownStrings.EntropyContribution + "{" + site + "}";
                // log10 K = 0 gives ΔG = 0, so −TΔS = −ΔH, including its SD.
                var expected = new Energy(new FloatWithError(site * 1000, site * 100))
                    .ToFormattedString(unit, permole: true);
                Assert.Equal(expected, Assert.Single(inspector.Rows, row => row.Label == label).Value);
                Assert.Contains(label + " = " + expected, graph);
                using (new InvariantNumericCultureScope())
                {
                    var figureExpected = new Energy(new FloatWithError(site * 1000, site * 100))
                        .ToFormattedString(unit, permole: true);
                    Assert.Contains(label + " = " + figureExpected, annotation);
                }
                var labels = inspector.Rows.Select(row => row.Label).ToList();
                Assert.Equal(labels.IndexOf(MarkdownStrings.Enthalpy + "{" + site + "}") + 1,
                    labels.IndexOf(label));
                Assert.Equal(labels.IndexOf(label) + 1,
                    labels.IndexOf(MarkdownStrings.GibbsFreeEnergy + "{" + site + "}"));
            }
            Assert.Equal(unit, solution.PresentationEnergyUnitOverride);
            Assert.Equal(style, AppSettings.UncertaintyDisplayStyle);
        }
        finally
        {
            AppSettings.UncertaintyDisplayStyle = previousStyle;
        }
    }

    [Theory]
    [InlineData(FinalFigureDisplayParameters.Entropy, true)]
    [InlineData(FinalFigureDisplayParameters.Derived, true)]
    [InlineData(FinalFigureDisplayParameters.Fitted, false)]
    [InlineData(FinalFigureDisplayParameters.Gibbs, false)]
    public void EntropyRowsFollowDisplayFlag(FinalFigureDisplayParameters display, bool expected)
    {
        var solution = CreateSolution();
        var rows = solution.UISolutionParameters(display);
        Assert.Equal(expected ? 2 : 0,
            rows.Count(row => row.Item1.StartsWith(MarkdownStrings.EntropyContribution)));
        var graph = AnalysisParameterSummaryPresentation.BuildLines(solution,
            AnalysisParameterSummaryPresentation.GraphBoxDisplay(display));
        Assert.Equal(expected ? 2 : 0,
            graph.Count(line => line.StartsWith(MarkdownStrings.EntropyContribution)));
        var figure = PublicationFigureBuilder.Build(
            new PublicationFigureSource(solution.Model.Data, solution),
            new PublicationFigureOptions
            {
                ShowThermogram = false,
                ShowFitParameters = true,
                DisplayParameters = display,
            });
        Assert.Equal(expected ? 2 : 0, figure.FitPanel.AnnotationBoxes
            .SelectMany(box => box.Lines).Count(line => line.StartsWith(MarkdownStrings.EntropyContribution)));
    }

    static SolutionInterface CreateSolution()
    {
        var data = new ExperimentData("two-site-summary.itc")
        {
            MeasuredTemperature = 25,
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
        };
        for (var index = 0; index < 5; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 420e-6 * 2e-6, include: true)
            {
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) * 5e-6 / data.CellConcentration,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7));
            data.Injections.Add(injection);
        }
        var model = new TwoSetsOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 0);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, 0);
        var solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        solution.Parameters[ParameterType.Enthalpy1] = new FloatWithError(-1000, 100);
        solution.Parameters[ParameterType.Enthalpy2] = new FloatWithError(-2000, 200);
        return solution;
    }
}
