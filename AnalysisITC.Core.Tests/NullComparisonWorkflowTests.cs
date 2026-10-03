using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class NullComparisonWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GlobalAnalysisPublishesOneComparisonToEveryMemberWithoutAutoCreatingAResult(bool sharedOffset)
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var members = new[] { first, second };
        var global = new GlobalModel(members.ToList())
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        foreach (var member in members) global.Parameters.AddIndivdualParameter(member.Parameters);
        if (sharedOffset)
        {
            global.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0);
            global.Parameters.SetIndividualFromGlobal();
        }

        var solver = new GlobalSolver
        {
            Model = global,
            SolverAlgorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 300,
            CanCreateAnalysisResult = false,
        };
        var priorAutoResultSetting = AppSettings.CreateSingleAnalysisResult;
        var priorResultIds = new HashSet<string>(DataManager.Results.Select(result => result.UniqueID));
        AppSettings.CreateSingleAnalysisResult = false;

        var finished = new TaskCompletionSource<SolverConvergence>(TaskCreationOptions.RunContinuationsAsynchronously);
        var solverFinishedCount = 0;
        EventHandler<SolverConvergence> handler = (sender, value) =>
        {
            if (ReferenceEquals(sender, solver))
            {
                solverFinishedCount++;
                finished.TrySetResult(value);
            }
        };
        SolverInterface.AnalysisFinished += handler;
        try
        {
            SolverInterface.TerminateAnalysisFlag.Lower();
            solver.Analyze();
            var convergence = await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(1, solverFinishedCount);
            Assert.NotNull(global.Solution);
            var comparison = Assert.IsType<NullModelComparison>(global.Solution.NullComparison);
            Assert.Equal(convergence.Success, comparison.BindingFitSucceeded);
            Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
            Assert.Equal(2, comparison.Members.Count);
            Assert.All(global.Solution.Solutions, member =>
            {
                var local = Assert.IsType<NullModelComparison>(member.NullComparison);
                if (sharedOffset)
                {
                    Assert.Same(comparison, local);
                    Assert.Equal(2, local.Members.Count);
                }
                else
                {
                    Assert.NotSame(comparison, local);
                    Assert.Single(local.Members);
                }
                Assert.All(local.Members, item => Assert.Equal("local", item.Scope));
            });
            Assert.All(comparison.Members, member => Assert.Equal("local", member.Scope));
            Assert.DoesNotContain(DataManager.Results, result => !priorResultIds.Contains(result.UniqueID));

            var result = new AnalysisResult(global.Solution);
            if (result.IsIndependentAssessmentCollection)
            {
                foreach (var member in global.Solution.Solutions)
                    result.SetMemberBindingAssessmentOverride(member.Guid, BindingAssessmentOutcome.NoBindingDetected);
            }
            else
                result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
            var previousComparison = result.NullComparison;
            var updatedComparison = new NullModelComparison
            {
                BindingFitSucceeded = true,
                NullFitSucceeded = false,
                NullFitReason = "The updated comparison is unavailable.",
            };
            global.Solution.NullComparison = updatedComparison;
            result.UpdateSolution(global.Solution);
            Assert.NotSame(previousComparison, result.NullComparison);
            Assert.Same(updatedComparison, result.NullComparison);
            if (result.IsIndependentAssessmentCollection)
                Assert.All(result.MemberAssessments, member =>
                {
                    Assert.Null(member.Assessment.ManualOverride);
                    Assert.Equal(member.Assessment.AutomaticOutcome, member.Assessment.EffectiveOutcome);
                });
            else
            {
                Assert.Null(result.BindingAssessment.ManualOverride);
                Assert.Equal(result.BindingAssessment.AutomaticOutcome, result.BindingAssessment.EffectiveOutcome);
            }
        }
        catch
        {
            SolverInterface.TerminateAnalysisFlag.Raise();
            try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            throw;
        }
        finally
        {
            SolverInterface.AnalysisFinished -= handler;
            SolverInterface.TerminateAnalysisFlag.Lower();
            Assert.False(SolverInterface.TerminateAnalysisFlag.Up);
            AppSettings.CreateSingleAnalysisResult = priorAutoResultSetting;
        }
    }

    [Fact]
    public async Task SingleAnalysisCapturesNullComparisonWithoutCreatingAnAnalysisResult()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 300,
            CanCreateAnalysisResult = false,
        };
        var priorAutoResultSetting = AppSettings.CreateSingleAnalysisResult;
        var priorResultIds = new HashSet<string>(DataManager.Results.Select(result => result.UniqueID));
        AppSettings.CreateSingleAnalysisResult = false;

        var finished = new TaskCompletionSource<SolverConvergence>(TaskCreationOptions.RunContinuationsAsynchronously);
        var solverFinishedCount = 0;
        SolutionInterface primaryAtFinishEvent = null;
        EventHandler<SolverConvergence> handler = (sender, value) =>
        {
            if (ReferenceEquals(sender, solver))
            {
                solverFinishedCount++;
                primaryAtFinishEvent = solver.Model.Solution;
                finished.TrySetResult(value);
            }
        };
        SolverInterface.AnalysisFinished += handler;
        try
        {
            SolverInterface.TerminateAnalysisFlag.Lower();
            solver.Analyze();
            var convergence = await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(1, solverFinishedCount);
            Assert.NotNull(model.Solution);
            Assert.Same(primaryAtFinishEvent, model.Solution);
            Assert.NotNull(model.Solution.NullComparison);
            Assert.Equal(convergence.Success, model.Solution.NullComparison.BindingFitSucceeded);
            Assert.Equal("offset", model.Solution.NullComparison.NullModelId);
            Assert.NotEqual(AnalysisModel.Offset, model.ModelType);
            Assert.DoesNotContain(DataManager.Results, result => !priorResultIds.Contains(result.UniqueID));
        }
        catch
        {
            SolverInterface.TerminateAnalysisFlag.Raise();
            try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            throw;
        }
        finally
        {
            SolverInterface.AnalysisFinished -= handler;
            SolverInterface.TerminateAnalysisFlag.Lower();
            Assert.False(SolverInterface.TerminateAnalysisFlag.Up);
            AppSettings.CreateSingleAnalysisResult = priorAutoResultSetting;
        }
    }

    [Fact]
    public void DirectSolveDoesNotRunTheNullComparisonWorkflow()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.NelderMead,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 300,
            CanCreateAnalysisResult = false,
        };
        SolverInterface.TerminateAnalysisFlag.Lower();
        try
        {
            solver.Solve();
            Assert.Null(model.Solution.NullComparison);
        }
        finally
        {
            SolverInterface.TerminateAnalysisFlag.Lower();
            Assert.False(SolverInterface.TerminateAnalysisFlag.Up);
        }
    }

    [Fact]
    public void FailedInitialBindingFitWithSuccessfulNullFitCannotCreateResult()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var failedConvergence = SolverConvergence.FromException(new InvalidOperationException("Primary fit failed."), DateTime.Now);
        model.Solution = SolutionInterface.FromModel(model, failedConvergence);
        var solver = new ResultCreationProbe { Model = model, CanCreateAnalysisResult = true };
        var priorResultIds = new HashSet<string>(DataManager.Results.Select(result => result.UniqueID));

        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);

        Assert.False(model.Solution.NullComparison.BindingFitSucceeded);
        Assert.True(model.Solution.NullComparison.NullFitSucceeded, model.Solution.NullComparison.NullFitReason);
        Assert.False(solver.CanCreateResult(failedConvergence));
        Assert.DoesNotContain(DataManager.Results, result => !priorResultIds.Contains(result.UniqueID));
    }

    sealed class ResultCreationProbe : Solver
    {
        public bool CanCreateResult(SolverConvergence convergence) => ShouldCreateAnalysisResult(convergence);
    }

    [Fact]
    public void FailedNullFitLeavesThePrimaryBindingSolutionInPlace()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var primarySolution = model.Solution;
        var primaryModelType = model.ModelType;
        var primaryParameters = primarySolution.ReportParameters.ToDictionary(pair => pair.Key, pair => pair.Value.Value);

        // Zero delivered moles make a free Offset unidentifiable. The calculator
        // should retain the binding fit and report only the independent comparison failure.
        model.Data.SyringeConcentration = new FloatWithError(0);
        NullModelComparisonCalculator.Calculate(primarySolution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 300, toleranceModifier: 1);

        Assert.Same(primarySolution, model.Solution);
        Assert.Equal(primaryModelType, model.ModelType);
        Assert.True(primarySolution.IsValid);
        Assert.False(primarySolution.NullComparison.NullFitSucceeded);
        Assert.Contains("zero", primarySolution.NullComparison.NullFitReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(primaryParameters, primarySolution.ReportParameters.ToDictionary(pair => pair.Key, pair => pair.Value.Value));
    }
}
