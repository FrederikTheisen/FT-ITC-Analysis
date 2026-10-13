using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class NoBindingUncertaintySkipTests
{
    public static IEnumerable<object[]> WorkflowCases()
    {
        foreach (var method in EstimationMethods)
        {
            foreach (var scope in Enum.GetValues<BindingAssessmentScope>())
                foreach (var noBinding in new[] { false, true })
                    yield return new object[] { method, scope, noBinding, false, SolverAlgorithm.NelderMead };
            foreach (var noBinding in new[] { false, true })
                yield return new object[] { method, BindingAssessmentScope.Single, noBinding, true, SolverAlgorithm.NelderMead };
        }
        foreach (var scope in new[] { BindingAssessmentScope.Single, BindingAssessmentScope.Pooled })
            foreach (var noBinding in new[] { false, true })
                yield return new object[] { ErrorEstimationMethod.LeaveOneOut, scope, noBinding, false, SolverAlgorithm.LevenbergMarquardt };
    }

    static readonly ErrorEstimationMethod[] EstimationMethods =
    {
        ErrorEstimationMethod.BootstrapResiduals,
        ErrorEstimationMethod.LeaveOneOut,
        ErrorEstimationMethod.ProfileLikelihood,
    };

    // These fixtures test workflow policy, not model mathematics or parameter recovery.
    // Lock known reference binding parameters so uncertainty estimation has one identifiable
    // coordinate (Offset), with nonzero residuals and finite processing SDs.
    static Model PolicyModel(bool noBinding, string id = "policy", double temperature = 25)
    {
        var model = InjectionProcessingMethodTests.FittedModel();
        model.Data.SetID(id);
        model.Data.Name = id;
        model.Data.MeasuredTemperature = temperature;
        if (noBinding) model.Parameters.Table[ParameterType.Enthalpy1].Update(0);
        foreach (var parameter in model.Parameters.Table.Values)
            parameter.SetValue(parameter.Value, parameter.Key != ParameterType.Offset);
        var heats = model.Data.Injections.Select(injection => model.Evaluate(injection.ID)).ToArray();
        var noiseScale = 1e-8;
        for (var index = 0; index < heats.Length; index++)
            model.Data.Injections[index].SetPeakArea(new FloatWithError(
                heats[index] + noiseScale * Math.Sin(index + 1), noiseScale));
        model.ModelCloneOptions = new ModelCloneOptions
        {
            IncludeConcentrationErrorsInBootstrap = false,
            UnlockBootstrapParameters = false,
        };
        return model;
    }

    static SolverInterface PolicySolver(BindingAssessmentScope scope, ErrorEstimationMethod method,
        params Model[] models)
    {
        SolverInterface solver;
        if (scope == BindingAssessmentScope.Single)
            solver = new Solver { Model = Assert.Single(models) };
        else
        {
            var global = Independent(models);
            global.ModelCloneOptions = new ModelCloneOptions { IsGlobalClone = scope == BindingAssessmentScope.Pooled };
            if (scope == BindingAssessmentScope.Pooled)
            {
                // The Null comparison fits an Offset separately for each member. Give the
                // flat binding fit more coordinates than that Null fit, avoiding an artificial
                // AICc preference for a pooled model with fewer parameters.
                if (models.All(model => model.Parameters.Table[ParameterType.Enthalpy1].Value == 0))
                    foreach (var model in models)
                        model.Parameters.Table[ParameterType.Enthalpy1].SetValue(0, false);
                global.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
                global.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0);
                global.Parameters.SetIndividualFromGlobal();
            }
            solver = new GlobalSolver { Model = global };
        }
        solver.CanCreateAnalysisResult = false;
        solver.ErrorEstimationMethod = method;
        solver.BootstrapIterations = Iterations;
        solver.MaxOptimizerIterations = 3000;
        return solver;
    }

    static GlobalSolution GlobalSolutionOf(SolverInterface solver) => solver switch
    {
        Solver local => GlobalSolution.FromSingleExperimentSolver(local),
        GlobalSolver global => global.Model.Solution,
        _ => throw new ArgumentOutOfRangeException(nameof(solver)),
    };

    static void AssertSkipped(SolutionInterface member, ErrorEstimationMethod method)
    {
        Assert.True(member.Convergence.Success, member.Convergence.Message);
        // Shared-parameter uncertainty belongs to the global fit, not its member copies.
        Assert.Equal(method, member.IsGlobalAnalysisSolution ? member.ParentSolution.ErrorEstimationMethod : member.ErrorMethod);
        Assert.Equal(ErrorEstimationOutcome.SkippedNoBinding,
            (member.IsGlobalAnalysisSolution ? member.ParentSolution.Convergence : member.Convergence).ErrorEstimationOutcome);
        Assert.Empty(member.BootstrapSolutions);
        Assert.Null(member.ProfileLikelihoodRun);
        Assert.False(member.Parameters[ParameterType.Offset].HasError);
    }

    static void AssertEstimated(SolutionInterface member, ErrorEstimationMethod method, bool pooled = false)
    {
        Assert.True(member.Convergence.Success, member.Convergence.Message);
        Assert.Equal(method, pooled ? member.ParentSolution.ErrorEstimationMethod : member.ErrorMethod);
        if (method == ErrorEstimationMethod.ProfileLikelihood)
        {
            var run = pooled ? member.ParentSolution.ProfileLikelihoodRun : member.ProfileLikelihoodRun;
            Assert.NotNull(run);
            Assert.Equal(ErrorEstimationOutcome.Completed, run.Outcome);
            Assert.All(run.Coordinates, coordinate => Assert.True(coordinate.HasCompleteInterval));
            Assert.Empty(member.BootstrapSolutions);
        }
        else
        {
            Assert.Equal(ErrorEstimationOutcome.Completed,
                (pooled ? member.ParentSolution.Convergence : member.Convergence).ErrorEstimationOutcome);
            Assert.NotEmpty(member.BootstrapSolutions);
            // Pooled leave-one-out omits a whole experiment; a member can therefore
            // occur in just one retained replicate and have no member distribution SD.
            if (!pooled) Assert.True(member.Parameters[ParameterType.Offset].HasError);
            else Assert.NotEmpty(member.ParentSolution.BootstrapSolutions);
        }
    }

    [Theory]
    [MemberData(nameof(WorkflowCases))]
    public async Task EveryMethodHonorsAutomaticAssessmentAtItsFittingScope(
        ErrorEstimationMethod method, BindingAssessmentScope scope, bool noBinding, bool weighted, SolverAlgorithm algorithm)
    {
        var models = scope == BindingAssessmentScope.Single
            ? new[] { PolicyModel(noBinding) }
            : new[] { PolicyModel(noBinding, "first"), PolicyModel(noBinding, "second") };
        var solver = PolicySolver(scope, method, models);
        solver.UseErrorWeightedFitting = weighted;
        solver.SolverAlgorithm = algorithm;
        var convergence = await Run(solver);
        Assert.True(convergence.Success, convergence.Message);
        var solution = GlobalSolutionOf(solver);
        Assert.Equal(scope, BindingAssessmentScopes.For(solution));
        Assert.Equal(noBinding ? BindingAssessmentOutcome.NoBindingDetected : BindingAssessmentOutcome.BindingDetected,
            BindingAssessmentState.FromComparison(solution.NullComparison).AutomaticOutcome);
        foreach (var member in solution.Solutions)
        {
            Assert.Equal(weighted, member.UseWeightedFitting);
            if (noBinding) AssertSkipped(member, method);
            else AssertEstimated(member, method, scope == BindingAssessmentScope.Pooled);
        }
        Assert.Equal(noBinding ? ErrorEstimationOutcome.SkippedNoBinding : ErrorEstimationOutcome.Completed,
            convergence.ErrorEstimationOutcome);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public async Task MixedIndependentEstimationKeepsEligibleEvidenceAndAllMemberCentralValues(ErrorEstimationMethod method)
    {
        var binding = PolicyModel(false, "eligible", 20);
        var flat = PolicyModel(true, "skipped", 30);
        var solver = PolicySolver(BindingAssessmentScope.Independent, method, binding, flat);
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        AssertEstimated(binding.Solution, method);
        AssertSkipped(flat.Solution, method);
        Assert.Empty(solution.BootstrapSolutions);
        Assert.Equal(0, solution.BootstrapIterations);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected,
            BindingAssessmentState.FromComparison(flat.Solution.NullComparison).EffectiveOutcome);
        // Independent expectation: two members at 20/30 °C give their arithmetic mean at 25 °C.
        var expectedEnthalpy = (binding.Solution.Parameters[ParameterType.Enthalpy1].Value
            + flat.Solution.Parameters[ParameterType.Enthalpy1].Value) / 2;
        Assert.Equal(expectedEnthalpy, solution.TemperatureDependence[ParameterType.Enthalpy1].Evaluate(25).Value, 8);
        Assert.NotEqual(binding.Solution.Parameters[ParameterType.Enthalpy1].Value, expectedEnthalpy);
        if (method == ErrorEstimationMethod.ProfileLikelihood)
        {
            var summary = ProfileLikelihoodEstimator.Summarize(solution);
            Assert.Equal(ErrorEstimationOutcome.Completed, summary.Outcome);
            Assert.Equal(2, summary.TotalSides);
            Assert.Equal(2, summary.EndpointsFound);
            Assert.DoesNotContain("missing", summary.Diagnostics);
        }
        else if (method == ErrorEstimationMethod.BootstrapResiduals)
            Assert.Equal(Iterations.ToString(), UncertaintyEstimationPresentation.BootstrapIterations(solution));

        Assert.DoesNotContain(SolverConvergence.SkippedForNoBindingDescription,
            UncertaintyEstimationPresentation.Method(solution));
        if (method != ErrorEstimationMethod.BootstrapResiduals)
            Assert.Equal("Not applicable", UncertaintyEstimationPresentation.BootstrapIterations(solution));
        var result = new AnalysisResult(solution);
        foreach (var purpose in new[] { ResultOutputPurpose.Standard, ResultOutputPurpose.Diagnostic })
        {
            var report = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
            var rows = report.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment)
                .SelectMany(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>())
                .SelectMany(block => block.Items).Where(item => item.Label == "Uncertainty").ToList();
            Assert.Equal(2, rows.Count);
            Assert.Single(rows, row => row.Value.Contains("not estimated (no binding detected)"));
            Assert.Single(rows, row => row.Value.Contains("completed"));
        }
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, false)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, true)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, false)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, true)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood, false)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood, true)]
    public async Task SkippingPreservesThePrimaryFitAndComparison(ErrorEstimationMethod method, bool weighted)
    {
        var baseline = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.None, PolicyModel(true));
        var skipped = PolicySolver(BindingAssessmentScope.Single, method, PolicyModel(true));
        baseline.UseErrorWeightedFitting = skipped.UseErrorWeightedFitting = weighted;
        await Run(baseline);
        await Run(skipped);
        var before = GlobalSolutionOf(baseline);
        var after = GlobalSolutionOf(skipped);
        Assert.Equal(before.Solutions[0].Parameters, after.Solutions[0].Parameters);
        Assert.Equal(before.Solutions[0].Data.Injections.Select(injection => before.Solutions[0].Model.EvaluateEnthalpy(injection.ID)),
            after.Solutions[0].Data.Injections.Select(injection => after.Solutions[0].Model.EvaluateEnthalpy(injection.ID)));
        Assert.Equal(before.Objective, after.Objective);
        Assert.Equal(before.UnweightedRmsd, after.UnweightedRmsd);
        Assert.Equal(before.NullComparison.DeltaAicc, after.NullComparison.DeltaAicc);
        Assert.Equal(before.NullComparison.Members[0].Offset, after.NullComparison.Members[0].Offset);
        Assert.Equal(before.NullComparison.Members[0].Points.Select(point => point.PredictedHeatJoules),
            after.NullComparison.Members[0].Points.Select(point => point.PredictedHeatJoules));
        AssertSkipped(after.Solutions[0], method);
        Assert.Equal(ErrorEstimationOutcome.None, before.Convergence.ErrorEstimationOutcome);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public async Task DisabledSkippingEstimatesAnAutomaticNoBindingFit(ErrorEstimationMethod method)
    {
        var solver = PolicySolver(BindingAssessmentScope.Single, method, PolicyModel(true));
        solver.SkipNoBindingUncertainty = false;
        await Run(solver);
        AssertEstimated(GlobalSolutionOf(solver).Solutions[0], method);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected,
            BindingAssessmentState.FromComparison(GlobalSolutionOf(solver).NullComparison).AutomaticOutcome);
    }

    [Theory]
    [MemberData(nameof(LifecycleCases))]
    public async Task ManualAssessmentControlsEveryMethodAndFittingScope(
        ErrorEstimationMethod method, BindingAssessmentScope scope)
    {
        foreach (var manualNoBinding in new[] { false, true })
        {
            var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(!manualNoBinding) }
                : new[] { PolicyModel(!manualNoBinding, "first"), PolicyModel(!manualNoBinding, "second") };
            var solver = PolicySolver(scope, method, models);
            var manual = manualNoBinding ? BindingAssessmentOutcome.NoBindingDetected : BindingAssessmentOutcome.BindingDetected;
            if (scope == BindingAssessmentScope.Independent)
                ((GlobalSolver)solver).ManualMemberAssessmentOverrides = models.ToDictionary(model => model.Data.UniqueID, _ => manual);
            else solver.ManualAssessmentOverride = manual;
            await Run(solver);
            Assert.Equal(manualNoBinding ? BindingAssessmentOutcome.BindingDetected : BindingAssessmentOutcome.NoBindingDetected,
                BindingAssessmentState.FromComparison(GlobalSolutionOf(solver).NullComparison).AutomaticOutcome);
            foreach (var member in GlobalSolutionOf(solver).Solutions)
                if (manualNoBinding) AssertSkipped(member, method);
                else AssertEstimated(member, method, scope == BindingAssessmentScope.Pooled);
        }
    }

    [Theory]
    [InlineData(BindingAssessmentScope.Single)]
    [InlineData(BindingAssessmentScope.Independent)]
    [InlineData(BindingAssessmentScope.Pooled)]
    public async Task NoUncertaintyRequestedDoesNotClaimANoBindingSkip(BindingAssessmentScope scope)
    {
        var models = scope == BindingAssessmentScope.Single ? new[] { PolicyModel(true) }
            : new[] { PolicyModel(true, "first"), PolicyModel(true, "second") };
        var solver = PolicySolver(scope, ErrorEstimationMethod.None, models);
        solver.ManualAssessmentOverride = BindingAssessmentOutcome.NoBindingDetected;
        var convergence = await Run(solver);
        var solution = GlobalSolutionOf(solver);
        Assert.Equal(ErrorEstimationOutcome.None, convergence.ErrorEstimationOutcome);
        Assert.All(solution.Solutions, member =>
        {
            Assert.Empty(member.BootstrapSolutions);
            Assert.Null(member.ProfileLikelihoodRun);
        });
        Assert.DoesNotContain(SolverConvergence.SkippedForNoBindingDescription, UncertaintyEstimationPresentation.Method(solution));
        Assert.Equal("Not applicable", UncertaintyEstimationPresentation.BootstrapIterations(solution));
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public async Task InconclusivePooledComparisonStillRunsEveryEstimator(ErrorEstimationMethod method)
    {
        var models = new[] { PolicyModel(true, "first"), PolicyModel(true, "second") };
        var solver = PolicySolver(BindingAssessmentScope.Pooled, method, models);
        // Both models reduce to Offset. One shared fitted coordinate versus two local
        // Null coordinates produces small positive ΔAICc, not evidence of binding.
        foreach (var model in models) model.Parameters.Table[ParameterType.Enthalpy1].SetValue(0, true);
        await Run(solver);
        var solution = GlobalSolutionOf(solver);
        Assert.Equal(BindingAssessmentOutcome.Inconclusive, BindingAssessmentState.FromComparison(solution.NullComparison).AutomaticOutcome);
        Assert.All(solution.Solutions, member => AssertEstimated(member, method, pooled: true));
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood)]
    public async Task UnavailableAiccDoesNotSuppressAnEstimator(ErrorEstimationMethod method)
    {
        var model = PolicyModel(true);
        model.Data.Injections.RemoveRange(3, model.Data.Injections.Count - 3);
        var solver = PolicySolver(BindingAssessmentScope.Single, method, model);
        await Run(solver);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, BindingAssessmentState.FromComparison(model.Solution.NullComparison).AutomaticOutcome);
        Assert.False(model.Solution.NullComparison.BindingInformationCriteria.IsAiccAvailable);
        AssertEstimated(model.Solution, method);
    }

    [Theory]
    [InlineData("one-c100-v0.01")]
    [InlineData("two-site")]
    [InlineData("sequential-2")]
    [InlineData("competitive")]
    [InlineData("dissociation")]
    public async Task SupportedModelsCanSkipWithoutGeneratingUncertaintyArtifacts(string reference)
    {
        var model = InjectionProcessingMethodTests.FittedModel(reference);
        foreach (var parameter in model.Parameters.Table.Values)
            parameter.SetValue(parameter.Value, parameter.Key != ParameterType.Offset);
        var solver = PolicySolver(BindingAssessmentScope.Single, ErrorEstimationMethod.BootstrapResiduals, model);
        solver.ManualAssessmentOverride = BindingAssessmentOutcome.NoBindingDetected;
        await Run(solver);
        AssertSkipped(model.Solution, ErrorEstimationMethod.BootstrapResiduals);
        Assert.True(model.Solution.NullComparison.NullFitSucceeded, model.Solution.NullComparison.NullFitReason);
    }
}
