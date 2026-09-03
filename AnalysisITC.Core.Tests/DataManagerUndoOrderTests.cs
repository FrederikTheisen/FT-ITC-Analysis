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
public sealed class DataManagerUndoOrderTests : IDisposable
{
    public DataManagerUndoOrderTests()
    {
        DataManager.Clear(DataClearMode.ResetSession);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
    }

    [Fact]
    public void UndoRestoresMiddleItemAtItsOriginalPosition()
    {
        var items = AddContainers("A", "B", "C");

        DataManager.RemoveSourceItemAt(1);
        DataManager.UndoDeleteData();

        Assert.Equal(items, DataManager.SourceItems);
    }

    [Fact]
    public void UndoRestoresBetweenSurvivingNeighborsAfterAnItemWasAdded()
    {
        var items = AddContainers("A", "B", "C");
        var added = Container("D");

        DataManager.RemoveSourceItemAt(1);
        DataManager.AddData(added);
        DataManager.UndoDeleteData();

        Assert.Equal(new[] { items[0], items[1], items[2], added }, DataManager.SourceItems);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UndoRestoresUsingTheOnlyAvailableNeighbor(int removedIndex)
    {
        var items = AddContainers("A", "B", "C");

        DataManager.RemoveSourceItemAt(removedIndex);
        DataManager.UndoDeleteData();

        Assert.Equal(items, DataManager.SourceItems);
    }

    [Fact]
    public void UndoAfterClearRestoresTheCompleteOriginalOrder()
    {
        var items = AddContainers("A", "B", "C", "D");

        DataManager.Clear(DataClearMode.RecordUndo);
        DataManager.UndoDeleteData();

        Assert.Equal(items, DataManager.SourceItems);
    }

    [Fact]
    public void UndoClearProcessingRestoresInterleavedResultsIndividually()
    {
        var firstExperiment = Container("Experiment A");
        var firstResult = CreateResult("Result A");
        var secondExperiment = Container("Experiment B");
        var secondResult = CreateResult("Result B");
        var original = new ITCDataContainer[]
        {
            firstExperiment,
            firstResult,
            secondExperiment,
            secondResult,
        };
        DataManager.AddData(original);

        DataManager.ClearProcessing();
        Assert.Equal(new[] { firstExperiment, secondExperiment }, DataManager.SourceItems);

        DataManager.UndoDeleteData();

        Assert.Equal(original, DataManager.SourceItems);
    }

    [Fact]
    public void ReversedAnchorsUseTheOriginalIndexFallback()
    {
        var lastByName = Container("z");
        var removed = Container("m");
        var firstByName = Container("a");
        DataManager.AddData(new[] { lastByName, removed, firstByName });

        DataManager.RemoveSourceItemAt(1);
        DataManager.SortContent(DataManager.SortMode.Name);
        DataManager.UndoDeleteData();

        Assert.Equal(new[] { firstByName, removed, lastByName }, DataManager.SourceItems);
    }

    [Fact]
    public void UndoRemainsLifoAndSelectsTheRestoredExperiment()
    {
        var items = AddContainers("A", "B", "C", "D");

        DataManager.RemoveSourceItemAt(1); // B
        DataManager.RemoveSourceItemAt(1); // C

        DataManager.UndoDeleteData();
        Assert.Equal(new[] { items[0], items[2], items[3] }, DataManager.SourceItems);
        Assert.Same(items[2], DataManager.Current);

        DataManager.UndoDeleteData();
        Assert.Equal(items, DataManager.SourceItems);
        Assert.Same(items[1], DataManager.Current);
    }

    static List<ITCDataContainer> AddContainers(params string[] names)
    {
        var items = names.Select(name => (ITCDataContainer)Container(name)).ToList();
        DataManager.AddData(items);
        return items;
    }

    static ExperimentData Container(string name) => new ExperimentData(name + ".itc")
    {
        Name = name,
    };

    static AnalysisResult CreateResult(string name)
    {
        var experiment = new ExperimentData(name + ".itc")
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
                ActualCellConcentration = experiment.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = index + 1,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            experiment.Injections.Add(injection);
        }

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.ModelCloneOptions = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.None,
        };
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
            }));

        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
        };
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(solver))
        {
            Name = name,
        };
        return result;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DataManagerUndoOrderCollectionDefinition
{
    public const string Name = "Data manager undo order";
}
