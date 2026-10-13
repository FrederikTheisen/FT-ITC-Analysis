using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisAxisPresentationTests
{
    [Theory]
    [InlineData(AnalysisXAxisType.MolarRatio, 0.125, 0.125)]
    [InlineData(AnalysisXAxisType.TitrantConcentration, 3e-6, 3)]
    [InlineData(AnalysisXAxisType.ID, 7, 7)]
    public void CoordinatesMatchTheAxisMeaning(AnalysisXAxisType axisType, double expectedNative, double expectedDisplay)
    {
        var data = new ExperimentData("axis.itc");
        var injection = new InjectionData(data, 6, 2e-6, 8.4e-10, include: true)
        {
            ActualTitrantConcentration = 3e-6,
            Ratio = 0.125,
        };

        Assert.Equal(expectedNative, AnalysisAxisPresentation.XValue(axisType, injection), 15);
        Assert.Equal(expectedDisplay, AnalysisAxisPresentation.DisplayXValue(axisType, injection), 12);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    public void SavedConcentrationCoordinatesDoNotDependOnTheRatioField(double ratio)
    {
        var data = new ExperimentData("saved-axis.itc");
        var injection = new InjectionData(data, 0, 2e-6, 8.4e-10, include: true)
        {
            ActualTitrantConcentration = 5e-6,
            Ratio = ratio,
        };

        Assert.Equal(5e-6, AnalysisAxisPresentation.XValue(AnalysisXAxisType.TitrantConcentration, injection), 15);
        Assert.Equal(5, AnalysisAxisPresentation.DisplayXValue(AnalysisXAxisType.TitrantConcentration, injection), 12);
        Assert.Equal(ratio, injection.Ratio);
    }

    [Fact]
    public void DissociationFigureUsesSavedConcentrationsForObservationsFitAndResiduals()
    {
        var data = new ExperimentData("dissociation-axis.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            TargetTemperature = 25,
        };
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8.4e-10, include: true)
            {
                ActualCellConcentration = 35e-6,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) / 7.0,
            };
            injection.SetPeakArea(new FloatWithError(2e-6 - index * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        var model = new Dissociation(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;

        var figure = PublicationFigureBuilder.Build(new PublicationFigureSource(data, model.Solution),
            new PublicationFigureOptions { ShowThermogram = false, ShowFitParameters = false, ShowConfidenceBand = false });

        Assert.Equal(AnalysisXAxisType.TitrantConcentration, data.AxisType);
        Assert.Equal("[Titrant] (µM)", figure.FitPanel.XAxis.Title);
        var expected = new[] { 5d, 10d, 15d };
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], figure.FitPanel.Points[index].X, 12);
            Assert.Equal(expected[index], figure.FitPanel.Series.Single().Points[index].X, 12);
            Assert.Equal(expected[index], figure.ResidualPanel.Points[index].X, 12);
        }
        Assert.InRange(figure.FitPanel.XAxis.Maximum, 15, 20);
        Assert.True(figure.FitPanel.XAxis.MajorTicks.Count > 2);
    }
}
