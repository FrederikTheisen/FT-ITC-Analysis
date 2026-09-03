using System;
using System.Collections.Generic;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnalysisResultNameParserCollectionDefinition
{
    public const string Name = "Analysis result name parser";
}

[Collection(AnalysisResultNameParserCollectionDefinition.Name)]
public sealed class AnalysisResultNameParserTests : IDisposable
{
    public AnalysisResultNameParserTests() => DataManager.Init();

    public void Dispose() => DataManager.Init();

    [Fact]
    public void SingleExperimentNameIsIncludedEvenWhenItIsTheOnlyLoadedExperiment()
    {
        var experiment = CreateExperiment("sample-17.itc");
        DataManager.AddData(experiment);

        var solution = CreateSingleExperimentSolution(experiment);

        Assert.Equal(
            "sample-17 | OneSetOfSites",
            AnalysisResultNameParser.GenerateSuggestedName(solution));
    }

    [Fact]
    public void SingleExperimentNameUsesTheAssignedDisplayNameAndNormalizesWhitespace()
    {
        var experiment = CreateExperiment("sample-17.itc");
        experiment.Name = "  Protein A\n  repeat 2  ";
        DataManager.AddData(experiment);

        var solution = CreateSingleExperimentSolution(experiment);

        Assert.Equal(
            "Protein A repeat 2 | OneSetOfSites",
            AnalysisResultNameParser.GenerateSuggestedName(solution));
    }

    [Fact]
    public void NewSingleExperimentResultUsesTheSuggestedName()
    {
        var experiment = CreateExperiment("sample-17.itc");
        DataManager.AddData(experiment);

        var result = new AnalysisResult(CreateSingleExperimentSolution(experiment));

        Assert.Equal("sample-17 | OneSetOfSites", result.Name);
    }

    [Fact]
    public void SingleExperimentSummariesUseTheExperimentName()
    {
        var experiment = CreateExperiment("sample-17.itc");
        DataManager.AddData(experiment);

        var result = new AnalysisResult(CreateSingleExperimentSolution(experiment));

        Assert.StartsWith("Fit of sample-17" + Environment.NewLine, result.GetResultString());
        Assert.StartsWith("Fit of sample-17" + Environment.NewLine, result.GetListDescriptionString());
    }

    static ExperimentData CreateExperiment(string fileName)
    {
        var experiment = new ExperimentData(fileName)
        {
            CellConcentration = new FloatWithError(1e-3),
            SyringeConcentration = new FloatWithError(1e-3),
            CellVolume = 1e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };

        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(experiment, index, 2e-6, 2e-10, include: true)
            {
                Ratio = index + 1,
            };
            injection.SetPeakArea(new FloatWithError(-1e-6));
            experiment.Injections.Add(injection);
        }

        return experiment;
    }

    static GlobalSolution CreateSingleExperimentSolution(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);

        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 0,
        });
        var memberSolution = SolutionInterface.FromModel(model, convergence);
        model.Solution = memberSolution;

        var globalModel = new GlobalModel(new List<Model> { model })
        {
            Parameters = new GlobalModelParameters(),
        };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);

        var globalSolution = new GlobalSolution(
            new GlobalSolver { Model = globalModel },
            new List<SolutionInterface> { memberSolution },
            convergence);
        globalModel.Solution = globalSolution;
        return globalSolution;
    }
}
