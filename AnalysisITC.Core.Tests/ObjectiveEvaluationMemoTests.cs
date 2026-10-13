using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.DataReaders;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class ObjectiveEvaluationTests
{
    public enum ObjectiveEntryPoint
    {
        Loss,
        TryLoss,
        Residuals,
        TryResiduals,
    }

    public static TheoryData<string, DilutionMethod, ObjectiveEntryPoint> MemoCountCases
    {
        get
        {
            var cases = new TheoryData<string, DilutionMethod, ObjectiveEntryPoint>();
            foreach (var modelId in new[] { "sequential-2", "two-realistic" })
            foreach (var method in new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement })
            foreach (var entryPoint in Enum.GetValues<ObjectiveEntryPoint>())
                cases.Add(modelId, method, entryPoint);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(MemoCountCases))]
    public void ProductionModelsMemoizeHeatContentWithinEachObjectiveScope(
        string modelId, DilutionMethod method, ObjectiveEntryPoint entryPoint)
    {
        var model = CreateModel("PytcBookkeeping", modelId);
        KeepThreeIncludedPositiveInjections(model);
        SetMethod(model, method);
        model.ApplyModelOptions();
        var solveCount = HeatContentSolveCount(model);
        // Three included injections share four endpoint states; Dumas adds three midpoints.
        var objectiveSolves = method == DilutionMethod.Exponential ? 7 : 4;

        InvokeAndAssertSuccess(model, entryPoint);
        Assert.Equal(objectiveSolves, HeatContentSolveCount(model) - solveCount);

        // A completed objective scope must release its memo; a later scope starts fresh.
        solveCount = HeatContentSolveCount(model);
        InvokeAndAssertSuccess(model, entryPoint);
        Assert.Equal(objectiveSolves, HeatContentSolveCount(model) - solveCount);

        // Direct predictions run after objective completion without memo reuse; each visits endpoints repeatedly.
        solveCount = HeatContentSolveCount(model);
        foreach (var injection in model.Data.Injections)
            Assert.True(double.IsFinite(model.Evaluate(injection.ID)));
        var directSolves = method == DilutionMethod.Exponential ? 9 : 6;
        Assert.Equal(directSolves, HeatContentSolveCount(model) - solveCount);
    }

    static void KeepThreeIncludedPositiveInjections(Model model)
    {
        var injections = model.Data.Injections.Take(3).ToArray();
        Assert.Equal(3, injections.Length);
        Assert.All(injections, injection =>
        {
            Assert.True(injection.Volume > 0);
            injection.Include = true;
        });
        model.Data.Injections.Clear();
        model.Data.Injections.AddRange(injections);
        Assert.False(model.Data.IsTandemExperiment);
        Assert.All(model.Data.Injections, injection => Assert.True(injection.Include));
    }

    static long HeatContentSolveCount(Model model) => model switch
    {
        SequentialBindingSites sequential => sequential.HeatContentSolveCount,
        TwoSetsOfSites twoSite => twoSite.HeatContentSolveCount,
        _ => throw new ArgumentException("Expected a production binding model.", nameof(model)),
    };

    static void InvokeAndAssertSuccess(Model model, ObjectiveEntryPoint entryPoint)
    {
        var parameters = model.Parameters.GetFittedParameterArray();
        switch (entryPoint)
        {
            case ObjectiveEntryPoint.Loss:
                Assert.True(double.IsFinite(model.LossFunction(parameters, false)));
                break;
            case ObjectiveEntryPoint.TryLoss:
                Assert.True(model.TryLossFunction(parameters, false, out var loss));
                Assert.True(double.IsFinite(loss));
                break;
            case ObjectiveEntryPoint.Residuals:
                var residuals = model.LossFunctionResiduals(parameters, false);
                Assert.NotEmpty(residuals);
                Assert.All(residuals, residual => Assert.True(double.IsFinite(residual)));
                break;
            case ObjectiveEntryPoint.TryResiduals:
                Assert.True(model.TryLossFunctionResiduals(parameters, false, out var tryResiduals));
                Assert.NotEmpty(tryResiduals);
                Assert.All(tryResiduals, residual => Assert.True(double.IsFinite(residual)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entryPoint));
        }
    }
}
