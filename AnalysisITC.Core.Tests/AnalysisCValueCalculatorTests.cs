using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisCValueCalculatorTests
{
    [Fact]
    public void OneSiteUsesFittedStoichiometryAndPropagatesStoredErrors()
    {
        var solution = Solution(AnalysisModel.OneSetOfSites);
        solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(2, .2);
        solution.Parameters[ParameterType.Affinity1] = new FloatWithError(6, .05);
        solution.Data.CellConcentration = new FloatWithError(10e-6, 1e-6);

        var actual = Assert.Single(AnalysisCValueCalculator.Calculate(solution));

        Assert.Equal("Wiseman c-value", actual.Label);
        Assert.Equal(20, actual.Estimate.Value.Value, 10);
        Assert.True(actual.Estimate.Value.SD > 0);
    }

    [Fact]
    public void SyringeCorrectionUsesFixedSiteCountRatherThanActiveFraction()
    {
        var solution = Solution(AnalysisModel.OneSetOfSites);
        solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(.25);
        solution.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = true;
        solution.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = 3;

        var actual = Assert.Single(AnalysisCValueCalculator.Calculate(solution));

        Assert.Equal(30, actual.Estimate.Value.Value, 10);

        solution.ModelOptions.Remove(AttributeKey.NumberOfSites1);
        Assert.False(Assert.Single(AnalysisCValueCalculator.Calculate(solution)).IsAvailable);
    }

    [Fact]
    public void TwoSiteReportsOneValueForEachSite()
    {
        var solution = Solution(AnalysisModel.TwoSetsOfSites);
        solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(1);
        solution.Parameters[ParameterType.Affinity1] = new FloatWithError(6);
        solution.Parameters[ParameterType.Nvalue2] = new FloatWithError(2);
        solution.Parameters[ParameterType.Affinity2] = new FloatWithError(Math.Log10(5e5));

        var actual = AnalysisCValueCalculator.Calculate(solution);

        Assert.Equal(2, actual.Count);
        Assert.Equal(10, actual[0].Estimate.Value.Value, 10);
        Assert.Equal(10, actual[1].Estimate.Value.Value, 10);
        Assert.Equal(new[] { 1, 2 }, actual.Select(item => item.Index.Value));

        solution.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = true;
        solution.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = 3;
        solution.ModelOptions[AttributeKey.NumberOfSites2].DoubleValue = 4;
        var corrected = AnalysisCValueCalculator.Calculate(solution);
        Assert.Equal(30, corrected[0].Estimate.Value.Value, 10);
        Assert.Equal(20, corrected[1].Estimate.Value.Value, 10);
    }

    [Fact]
    public void SequentialReportsOneValueForEachActiveStep()
    {
        var solution = Solution(AnalysisModel.SequentialBindingSites);
        solution.Parameters[ParameterType.Affinity1] = new FloatWithError(6);
        solution.Parameters[ParameterType.Affinity2] = new FloatWithError(5);

        var actual = AnalysisCValueCalculator.Calculate(solution);

        Assert.Equal(2, actual.Count);
        Assert.Equal(10, actual[0].Estimate.Value.Value, 10);
        Assert.Equal(1, actual[1].Estimate.Value.Value, 10);
        Assert.All(actual, item => Assert.Equal("sequential-step", item.Kind));
    }

    [Fact]
    public void CompetitiveReportsOnlyTheApparentValue()
    {
        var solution = (CompetitiveBinding.ModelSolution)Solution(AnalysisModel.CompetitiveBinding);
        solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(1.5);
        solution.Parameters[ParameterType.Affinity1] = new FloatWithError(7);

        var actual = Assert.Single(AnalysisCValueCalculator.Calculate(solution));

        Assert.Equal("Apparent c-value", actual.Label);
        Assert.Equal(1.5 * solution.Data.CellConcentration.Value / solution.Kdapp.Value,
            actual.Estimate.Value.Value, 10);
    }

    [Fact]
    public void DissociationDoesNotExposeACValue()
    {
        Assert.Empty(AnalysisCValueCalculator.Calculate(Solution(AnalysisModel.Dissociation)));
    }

    [Fact]
    public void TandemUsesTheEarliestSegmentAndInvalidInputsRemainUnavailable()
    {
        var solution = Solution(AnalysisModel.OneSetOfSites);
        solution.Data.AddSegment(new TandemExperimentSegment(2, 30e-6, 0));
        solution.Data.AddSegment(new TandemExperimentSegment(0, 20e-6, 0));
        var tandem = Assert.Single(AnalysisCValueCalculator.Calculate(solution));
        Assert.Equal(20, tandem.Estimate.Value.Value, 10);
        Assert.Equal("initial-tandem-segment", tandem.ConcentrationBasis);

        solution.Data.ReplaceSegments(Array.Empty<TandemExperimentSegment>());
        solution.Data.CellConcentration = new FloatWithError(0);
        Assert.False(Assert.Single(AnalysisCValueCalculator.Calculate(solution)).IsAvailable);
    }

    [Fact]
    public void CurrentConcentrationIsUsedWhenExperimentInputsChange()
    {
        var solution = Solution(AnalysisModel.OneSetOfSites);
        var before = Assert.Single(AnalysisCValueCalculator.Calculate(solution)).Estimate.Value.Value;

        solution.Data.CellConcentration = 2 * solution.Data.CellConcentration;
        var after = Assert.Single(AnalysisCValueCalculator.Calculate(solution)).Estimate.Value.Value;

        Assert.Equal(2 * before, after, 10);
    }

    static SolutionInterface Solution(AnalysisModel type)
    {
        var data = Experiment();
        Model model = type switch
        {
            AnalysisModel.OneSetOfSites => new OneSetOfSites(data),
            AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
            AnalysisModel.SequentialBindingSites => new SequentialBindingSites(data),
            AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
            AnalysisModel.Dissociation => new Dissociation(data),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        if (model.Parameters.Table.ContainsKey(ParameterType.Nvalue1))
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        if (model.Parameters.Table.ContainsKey(ParameterType.Affinity2))
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, 5);
        if (model.Parameters.Table.ContainsKey(ParameterType.Nvalue2))
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue2, 1);
        var solution = SolutionInterface.FromModel(model, Convergence());
        model.Solution = solution;
        data.UpdateSolution(model);
        return solution;
    }

    static ExperimentData Experiment()
    {
        var data = new ExperimentData("c-value.itc")
        {
            Name = "c-value",
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 200e-6,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        for (var index = 0; index < 4; index++)
        {
            var injection = new InjectionData(data, index, 2e-6,
                data.SyringeConcentration * 2e-6, include: true)
            {
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 2e-6,
                Ratio = index + 1,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        return data;
    }

    static SolverConvergence Convergence() => SolverConvergence.FromSnapshot(
        new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 1,
            Iterations = 1,
        });
}
