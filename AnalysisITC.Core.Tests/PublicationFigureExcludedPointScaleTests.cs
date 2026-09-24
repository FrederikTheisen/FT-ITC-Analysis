using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class PublicationFigureExcludedPointScaleTests
{
    [Fact]
    public void ExcludedPointsCanAffectAutomaticFitAndResidualYScales()
    {
        var (experiment, solution) = CreateExperiment();
        var ignoresExcluded = Build(experiment, solution, new PublicationFigureOptions
        {
            AutoAxesIgnoresBadData = true,
        });
        var includesExcluded = Build(experiment, solution, new PublicationFigureOptions
        {
            AutoAxesIgnoresBadData = false,
        });

        Assert.True(includesExcluded.FitPanel.YAxis.Maximum > ignoresExcluded.FitPanel.YAxis.Maximum * 10);
        Assert.True(includesExcluded.ResidualPanel!.YAxis.Maximum > ignoresExcluded.ResidualPanel.YAxis.Maximum * 10);
    }

    [Fact]
    public void ExplicitFitAndResidualYLimitsOverrideAutomaticExcludedPointScaling()
    {
        var (experiment, solution) = CreateExperiment();
        var document = Build(experiment, solution, new PublicationFigureOptions
        {
            AutoAxesIgnoresBadData = false,
            FitYAxisMinimum = -2,
            FitYAxisMaximum = 3,
            ResidualYAxisMinimum = -4,
            ResidualYAxisMaximum = 5,
        });

        Assert.Equal(-2, document.FitPanel.YAxis.Minimum);
        Assert.Equal(3, document.FitPanel.YAxis.Maximum);
        Assert.Equal(-4, document.ResidualPanel!.YAxis.Minimum);
        Assert.Equal(5, document.ResidualPanel.YAxis.Maximum);
    }

    static PublicationFigureDocument Build(ExperimentData experiment, SolutionInterface solution, PublicationFigureOptions options)
    {
        options.ShowThermogram = false;
        options.ShowFitParameters = false;
        options.ShowConfidenceBand = false;
        return PublicationFigureBuilder.Build(new PublicationFigureSource(experiment, solution), options);
    }

    static (ExperimentData Experiment, SolutionInterface Solution) CreateExperiment()
    {
        var experiment = new ExperimentData("excluded-scale.itc")
        {
            CellConcentration = new FloatWithError(1e-3),
            SyringeConcentration = new FloatWithError(1e-3),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };

        for (var i = 0; i < 4; i++)
        {
            var injection = new InjectionData(experiment, i, 1e-6, 1e-9, include: i != 3)
            {
                ActualCellConcentration = 10e-6,
                ActualTitrantConcentration = i * 2e-6,
            };
            injection.SetPeakArea(new FloatWithError(i == 3 ? 1e-3 : 1e-9));
            experiment.Injections.Add(injection);
        }

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -10);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution = solution;
        experiment.UpdateSolution(model);
        return (experiment, solution);
    }
}
