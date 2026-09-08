using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(DataManagerUndoOrderCollectionDefinition.Name)]
public sealed class ResultSelectionNavigationTests : IDisposable
{
    public ResultSelectionNavigationTests()
    {
        DataManager.Clear(DataClearMode.ResetSession);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
    }

    [Fact]
    public void SwitchingResultsRetainsMatchingMemberInstanceAcrossReorderedResults()
    {
        var firstExperiment = CreateExperiment("selection-a.itc");
        var secondExperiment = CreateExperiment("selection-b.itc");
        var firstResult = CreateResult(firstExperiment, secondExperiment);
        var secondResult = CreateResult(secondExperiment, firstExperiment);

        DataManager.AddData(new ITCDataContainer[] { firstResult, secondResult });
        DataManager.SelectIndex(0);
        var firstMember = firstResult.Solution.Solutions[0];
        DataManager.SelectResultSolution(firstMember);

        // Simulate the native result callback clearing the transient selection
        // before DataManager completes navigation.
        EventHandler<AnalysisResult> clearDuringNavigation = (_, _) =>
            DataManager.ClearResultSolutionSelection();
        DataManager.AnalysisResultSelected += clearDuringNavigation;
        try
        {
            DataManager.SelectIndex(1);
        }
        finally
        {
            DataManager.AnalysisResultSelected -= clearDuringNavigation;
        }

        var expectedSecondMember = secondResult.Solution.Solutions
            .Single(solution => solution.Data.UniqueID == firstExperiment.UniqueID);
        Assert.Same(expectedSecondMember, DataManager.SelectedResultSolution);
        Assert.NotSame(firstMember, DataManager.SelectedResultSolution);

        DataManager.SelectIndex(0);
        Assert.Same(firstMember, DataManager.SelectedResultSolution);
    }

    [Fact]
    public void MissingOrAbsentMemberSelectionIsClearedForEveryNavigationTarget()
    {
        var experiment = CreateExperiment("selection-present.itc");
        var missingExperiment = CreateExperiment("selection-missing.itc");
        var firstResult = CreateResult(experiment);
        var resultWithoutMatch = CreateResult(missingExperiment);
        var standaloneExperiment = CreateExperiment("selection-entry.itc");

        DataManager.AddData(new ITCDataContainer[]
        {
            firstResult,
            resultWithoutMatch,
            standaloneExperiment,
        });

        // There was no initial member selection, so selecting a result stays
        // unselected rather than choosing its first row.
        DataManager.SelectIndex(0);
        Assert.Null(DataManager.SelectedResultSolution);

        DataManager.SelectResultSolution(firstResult.Solution.Solutions[0]);
        DataManager.SelectIndex(1);
        Assert.Null(DataManager.SelectedResultSolution);

        DataManager.SelectResultSolution(firstResult.Solution.Solutions[0]);
        DataManager.SelectIndex(2);
        Assert.Null(DataManager.SelectedResultSolution);

        DataManager.SelectResultSolution(firstResult.Solution.Solutions[0]);
        DataManager.SelectIndex(-1);
        Assert.Null(DataManager.SelectedResultSolution);
    }

    static AnalysisResult CreateResult(params ExperimentData[] experiments)
    {
        var models = experiments.Select(CreateModel).ToList();
        var globalModel = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
        };
        foreach (var model in models)
            globalModel.Parameters.AddIndivdualParameter(model.Parameters);

        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
        });
        var global = new GlobalSolution(
            new GlobalSolver { Model = globalModel },
            models.Select(model => model.Solution).ToList(),
            convergence);
        globalModel.Solution = global;
        return new AnalysisResult(global);
    }

    static Model CreateModel(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
                Termination = SolverTermination.Converged,
            }));
        return model;
    }

    static ExperimentData CreateExperiment(string fileName)
    {
        var experiment = new ExperimentData(fileName)
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        experiment.SetID(Guid.NewGuid().ToString());

        var injection = new InjectionData(experiment, 0, 2e-6, 2e-10, true)
        {
            ActualCellConcentration = experiment.CellConcentration,
            ActualTitrantConcentration = 2e-6,
            Ratio = 1,
        };
        injection.SetPeakArea(new FloatWithError(-1e-6, 1e-8));
        experiment.Injections.Add(injection);
        return experiment;
    }
}
