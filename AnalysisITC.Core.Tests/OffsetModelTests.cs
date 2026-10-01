using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition("OffsetSolverTests", DisableParallelization = true)]
public sealed class OffsetSolverTestCollection { }

[Collection("OffsetSolverTests")]
public sealed class OffsetModelTests
{
    [Fact]
    public void OffsetPredictionUsesInjectedMolesAndSupportsUnequalShotVolumes()
    {
        var experiment = CreateExperiment();
        var model = new Offset(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.Table[ParameterType.Offset].SetValue(2400, true);

        foreach (var injection in experiment.Injections)
        {
            Assert.Equal(2400 * experiment.SyringeConcentration * injection.Volume,
                model.Evaluate(injection.ID), 12);
            // The whole Offset prediction is the offset term.
            Assert.Equal(0, model.Evaluate(injection.ID, withoffset: false));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticNullFitRecoversTheIndependentLeastSquaresOffset(bool weighted)
    {
        var experiment = CreateExperiment();
        var bindingModel = CreateBindingModel(experiment, knownOffset: 1350, weighted);
        var convergence = SolverConvergence.FromFixedFit(0, 0);
        var bindingSolution = SolutionInterface.FromModel(bindingModel, convergence);
        bindingSolution.UseWeightedFitting = weighted;
        bindingModel.Solution = bindingSolution;

        NullModelComparisonCalculator.Calculate(bindingSolution, weighted,
            SolverAlgorithm.LevenbergMarquardt, 3000, 1);

        var comparison = Assert.IsType<NullModelComparison>(bindingSolution.NullComparison);
        Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
        Assert.True(comparison.BindingInformationCriteria.IsAiccAvailable);
        Assert.True(comparison.NullInformationCriteria.IsAiccAvailable);
        Assert.Equal(comparison.NullInformationCriteria.Aicc.Value - comparison.BindingInformationCriteria.Aicc.Value,
            comparison.DeltaAicc.Value, 10);
        Assert.Equal(ExpectedAicc(bindingModel, weighted), comparison.BindingInformationCriteria.Aicc.Value, 10);
        Assert.Equal(ExpectedAicc(comparison.NullSolutions.Single().Model, weighted),
            comparison.NullInformationCriteria.Aicc.Value, 10);
        Assert.Equal("offset", comparison.NullModelId);
        Assert.Equal(experiment.Injections.Count, Assert.Single(comparison.Members).Points.Count);

        Assert.Equal(LeastSquaresOffset(experiment, weighted), comparison.Members[0].Offset, 4);
    }

    [Fact]
    public void FreeOffsetCannotBeEstimatedWhenEveryIncludedInjectionAmountIsZero()
    {
        var experiment = CreateExperiment();
        experiment.SyringeConcentration = new FloatWithError(0);
        var model = new Offset(experiment);
        model.InitializeParameters(experiment);
        var bindingModel = CreateBindingModel(experiment, knownOffset: 0, weighted: false);
        var solution = SolutionInterface.FromModel(bindingModel, SolverConvergence.FromFixedFit(0, 0));
        bindingModel.Solution = solution;

        NullModelComparisonCalculator.Calculate(solution, false, SolverAlgorithm.NelderMead, 1500, 1);

        Assert.False(solution.NullComparison.NullFitSucceeded);
        Assert.Contains("zero", solution.NullComparison.NullFitReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(solution.NullComparison.NullInformationCriteria);
    }

    [Fact]
    public void FailedBindingFitDoesNotPreventAnIndependentNullFit()
    {
        var experiment = CreateExperiment();
        var bindingModel = CreateBindingModel(experiment, knownOffset: 1350, weighted: false);
        var failed = SolverConvergence.FromException(new InvalidOperationException("binding optimizer failed"), DateTime.Now);
        var solution = SolutionInterface.FromModel(bindingModel, failed);
        bindingModel.Solution = solution;

        NullModelComparisonCalculator.Calculate(solution, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);

        Assert.False(solution.NullComparison.BindingFitSucceeded);
        Assert.False(string.IsNullOrWhiteSpace(solution.NullComparison.BindingFitReason));
        Assert.True(solution.NullComparison.NullFitSucceeded, solution.NullComparison.NullFitReason);
        Assert.Null(solution.NullComparison.BindingInformationCriteria);
        Assert.Null(solution.NullComparison.DeltaAicc);
    }

    [Fact]
    public void AiccIsUnavailableForTooFewObservations()
    {
        var experiment = CreateExperiment();
        var bindingModel = CreateBindingModel(experiment, knownOffset: 400, weighted: false);
        bindingModel.Parameters.Table[ParameterType.Offset].SetValue(400, true);
        foreach (var injection in experiment.Injections.Skip(1)) injection.Include = false;
        var solution = SolutionInterface.FromModel(bindingModel, SolverConvergence.FromFixedFit(0, 0));
        bindingModel.Solution = solution;

        NullModelComparisonCalculator.Calculate(solution, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);

        Assert.True(solution.NullComparison.NullFitSucceeded, solution.NullComparison.NullFitReason);
        Assert.False(solution.NullComparison.NullInformationCriteria.IsAiccAvailable);
        Assert.Equal(1, solution.NullComparison.NullInformationCriteria.ObservationCount);
    }

    [Fact]
    public void NullOffsetIsNotLimitedByTheBindingOffsetBounds()
    {
        var experiment = CreateExperiment();
        var bindingModel = CreateBindingModel(experiment, knownOffset: 80000, weighted: false);
        bindingModel.Parameters.Table[ParameterType.Offset].SetLimits(new[] { -1000.0, 1000.0 });
        var solution = SolutionInterface.FromModel(bindingModel, SolverConvergence.FromFixedFit(0, 0));
        bindingModel.Solution = solution;

        NullModelComparisonCalculator.Calculate(solution, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);

        Assert.True(solution.NullComparison.NullFitSucceeded, solution.NullComparison.NullFitReason);
        Assert.Equal(LeastSquaresOffset(experiment, weighted: false), solution.NullComparison.Members[0].Offset, 4);
        Assert.False(solution.NullComparison.NullSolutions.Single().ParameterBoundaryHit);
    }

    [Fact]
    public void LockedBindingOffsetDoesNotLockTheNullOffset()
    {
        var experiment = CreateExperiment();
        var bindingModel = CreateBindingModel(experiment, knownOffset: 1350, weighted: false);
        bindingModel.Parameters.Table[ParameterType.Offset].SetValue(400, true);
        var solution = SolutionInterface.FromModel(bindingModel, SolverConvergence.FromFixedFit(0, 0));
        bindingModel.Solution = solution;

        NullModelComparisonCalculator.Calculate(solution, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);

        var comparison = solution.NullComparison;
        Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
        Assert.Equal(1, comparison.NullInformationCriteria.FittedParameterCount);
        Assert.Equal(LeastSquaresOffset(experiment, weighted: false), comparison.Members[0].Offset, 3);
    }

    [Fact]
    public void NonfiniteIncludedHeatIsRecordedAsAnIndependentNullFailure()
    {
        var experiment = CreateExperiment();
        var bindingModel = CreateBindingModel(experiment, knownOffset: 1000, weighted: false);
        experiment.Injections[0].SetPeakArea(new FloatWithError(double.NaN));
        var solution = SolutionInterface.FromModel(bindingModel, SolverConvergence.FromFixedFit(0, 0));
        bindingModel.Solution = solution;

        NullModelComparisonCalculator.Calculate(solution, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);

        Assert.False(solution.NullComparison.NullFitSucceeded);
        Assert.Contains("finite", solution.NullComparison.NullFitReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(solution.NullComparison.NullInformationCriteria);
    }

    [Fact]
    public void ExistingCancellationIsRespectedWithoutCreatingAComparison()
    {
        var experiment = CreateExperiment();
        var bindingModel = CreateBindingModel(experiment, knownOffset: 1000, weighted: false);
        var solution = SolutionInterface.FromModel(bindingModel, SolverConvergence.FromFixedFit(0, 0));
        bindingModel.Solution = solution;
        var prior = new NullModelComparison { NullFitReason = "prior" };
        solution.NullComparison = prior;
        SolverInterface.TerminateAnalysisFlag.Raise();
        try
        {
            NullModelComparisonCalculator.Calculate(solution, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);
            Assert.Same(prior, solution.NullComparison);
        }
        finally
        {
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalSharedOffsetIsCountedOnceAndFittedEvenWhenTheBindingOffsetIsLocked(bool locked)
    {
        var firstExperiment = CreateExperiment("offset-global-a.itc");
        var secondExperiment = CreateExperiment("offset-global-b.itc");
        var first = CreateBindingModel(firstExperiment, knownOffset: 1200, weighted: false);
        var second = CreateBindingModel(secondExperiment, knownOffset: 1200, weighted: false);
        var members = new[] { first, second };
        var globalModel = new GlobalModel(members.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var member in members) globalModel.Parameters.AddIndivdualParameter(member.Parameters);
        globalModel.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
        globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 1200, locked);
        globalModel.Parameters.SetIndividualFromGlobal();

        var convergence = SolverConvergence.FromFixedFit(0, 0);
        var solutions = members.Select(member => SolutionInterface.FromModel(member, convergence)).ToList();
        for (var index = 0; index < members.Length; index++) members[index].Solution = solutions[index];
        var primarySolver = new GlobalSolver { Model = globalModel, UseErrorWeightedFitting = false };
        var primary = new GlobalSolution(primarySolver, solutions, convergence, reconstructBootstrap: false);
        globalModel.Solution = primary;

        NullModelComparisonCalculator.Calculate(primary, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);

        var comparison = Assert.IsType<NullModelComparison>(primary.NullComparison);
        Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
        Assert.Equal(2, comparison.Members.Count);
        var pooledInjections = first.Data.Injections.Concat(second.Data.Injections).Where(injection => injection.Include).ToList();
        var expected = pooledInjections
            .Sum(injection => injection.InjectionMass * injection.PeakArea)
            / pooledInjections.Sum(injection => injection.InjectionMass * injection.InjectionMass);
        Assert.All(comparison.Members, member =>
        {
            Assert.Equal("shared", member.Scope);
            Assert.Equal(expected, member.Offset, 3);
        });
        Assert.Equal(1, comparison.NullInformationCriteria.FittedParameterCount);
        Assert.Equal(ExpectedPooledAicc(comparison.NullSolutions, weighted: false,
            extraFittedParameters: 1), comparison.NullInformationCriteria.Aicc.Value, 8);
    }

    [Fact]
    public void GlobalLocalOffsetsAreFittedSeparatelyAndUsePooledInformationCriteria()
    {
        var firstExperiment = CreateExperiment("offset-global-local-a.itc");
        var secondExperiment = CreateExperiment("offset-global-local-b.itc");
        var first = CreateBindingModel(firstExperiment, knownOffset: 700, weighted: false);
        var second = CreateBindingModel(secondExperiment, knownOffset: 1700, weighted: false);
        var members = new[] { first, second };
        var globalModel = new GlobalModel(members.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var member in members) globalModel.Parameters.AddIndivdualParameter(member.Parameters);
        var convergence = SolverConvergence.FromFixedFit(0, 0);
        var solutions = members.Select(member => SolutionInterface.FromModel(member, convergence)).ToList();
        for (var index = 0; index < members.Length; index++) members[index].Solution = solutions[index];
        var primarySolver = new GlobalSolver { Model = globalModel, UseErrorWeightedFitting = false };
        var primary = new GlobalSolution(primarySolver, solutions, convergence, reconstructBootstrap: false);
        globalModel.Solution = primary;

        NullModelComparisonCalculator.Calculate(primary, false, SolverAlgorithm.LevenbergMarquardt, 3000, 1);

        var comparison = Assert.IsType<NullModelComparison>(primary.NullComparison);
        Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
        Assert.All(comparison.Members, member => Assert.Equal("local", member.Scope));
        Assert.Equal(2, comparison.NullInformationCriteria.FittedParameterCount);
        Assert.Equal(ExpectedPooledAicc(comparison.NullSolutions, weighted: false, extraFittedParameters: 0),
            comparison.NullInformationCriteria.Aicc.Value, 8);
    }

    [Fact]
    public void OffsetIsNotExposedAsASelectableBindingModel()
    {
        Assert.DoesNotContain(AnalysisModel.Offset, AnalysisModelAttribute.GetAll());
        Assert.Empty(ModelOptionCatalog.CreateOptions(AnalysisModel.Offset));
    }

    static Model CreateBindingModel(ExperimentData experiment, double knownOffset, bool weighted)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.Table[ParameterType.Nvalue1].SetValue(1, true);
        model.Parameters.Table[ParameterType.Enthalpy1].SetValue(-12000, true);
        model.Parameters.Table[ParameterType.Affinity1].SetValue(6, true);
        model.Parameters.Table[ParameterType.Offset].SetValue(0, false);

        foreach (var injection in experiment.Injections)
        {
            var residual = (injection.ID % 2 == 0 ? 1 : -1) * 0.13e-6;
            var heat = knownOffset * injection.InjectionMass + residual;
            injection.SetPeakArea(new FloatWithError(heat, weighted ? (injection.ID + 1) * 0.2e-6 : 0.4e-6));
        }
        return model;
    }

    static double LeastSquaresOffset(ExperimentData experiment, bool weighted)
    {
        var points = experiment.Injections.Where(injection => injection.Include).ToList();
        var numerator = points.Sum(injection =>
            injection.InjectionMass * injection.PeakArea / (weighted ? injection.PeakAreaError * injection.PeakAreaError : 1));
        var denominator = points.Sum(injection =>
            injection.InjectionMass * injection.InjectionMass / (weighted ? injection.PeakAreaError * injection.PeakAreaError : 1));
        return numerator / denominator;
    }

    static double ExpectedAicc(Model model, bool weighted)
    {
        var included = model.Data.Injections.Where(injection => injection.Include).ToList();
        var rss = included.Sum(injection =>
        {
            var residual = injection.PeakArea - model.Evaluate(injection.ID);
            return residual * residual;
        });
        var n = included.Count;
        double minusTwoLogLikelihood;
        if (!weighted)
            minusTwoLogLikelihood = n * (Math.Log(2 * Math.PI * rss / n) + 1);
        else
        {
            var standardizedRss = included.Sum(injection =>
            {
                var residual = (injection.PeakArea - model.Evaluate(injection.ID)) / injection.PeakAreaError;
                return residual * residual;
            });
            var logSigmaSquaredSum = included.Sum(injection => 2 * Math.Log(injection.PeakAreaError));
            minusTwoLogLikelihood = n * (Math.Log(2 * Math.PI * standardizedRss / n) + 1) + logSigmaSquaredSum;
        }
        var likelihoodParameters = model.NumberOfParameters + 1;
        var aic = minusTwoLogLikelihood + 2 * likelihoodParameters;
        return aic + 2.0 * likelihoodParameters * (likelihoodParameters + 1)
            / (n - likelihoodParameters - 1);
    }

    static double ExpectedPooledAicc(System.Collections.Generic.IEnumerable<SolutionInterface> solutions,
        bool weighted, int extraFittedParameters)
    {
        var members = solutions.Select(solution => solution.Model).ToList();
        var included = members.SelectMany(model => model.Data.Injections
            .Where(injection => injection.Include).Select(injection => (Model: model, Injection: injection))).ToList();
        var rss = included.Sum(item => Math.Pow(item.Injection.PeakArea - item.Model.Evaluate(item.Injection.ID), 2));
        var n = included.Count;
        double minusTwoLogLikelihood;
        if (!weighted) minusTwoLogLikelihood = n * (Math.Log(2 * Math.PI * rss / n) + 1);
        else
        {
            var standardizedRss = included.Sum(item => Math.Pow((item.Injection.PeakArea - item.Model.Evaluate(item.Injection.ID))
                / item.Injection.PeakAreaError, 2));
            var logSigmaSquaredSum = included.Sum(item => 2 * Math.Log(item.Injection.PeakAreaError));
            minusTwoLogLikelihood = n * (Math.Log(2 * Math.PI * standardizedRss / n) + 1) + logSigmaSquaredSum;
        }
        var k = members.Sum(model => model.NumberOfParameters) + extraFittedParameters + 1;
        var aic = minusTwoLogLikelihood + 2 * k;
        return aic + 2.0 * k * (k + 1) / (n - k - 1);
    }

    static ExperimentData CreateExperiment(string name = "offset-test.itc")
    {
        var experiment = new ExperimentData(name)
        {
            TargetTemperature = 25,
            MeasuredTemperature = 25,
            CellConcentration = new FloatWithError(1e-3),
            SyringeConcentration = new FloatWithError(1e-3),
            CellVolume = 1e-3,
        };
        for (var index = 0; index < 8; index++)
        {
            var volume = (0.5 + index * 0.1) * 1e-6;
            var injection = new InjectionData(experiment, index, volume, 0, include: true)
            {
                ActualCellConcentration = 1e-3 / (1 + index * 0.1),
                ActualTitrantConcentration = index * 1e-4,
                Ratio = index * 0.1,
            };
            injection.SetPeakArea(new FloatWithError(0, 0.4e-6));
            experiment.Injections.Add(injection);
        }
        return experiment;
    }
}
