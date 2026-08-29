using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(AnalysisBuilderConstraintCollectionDefinition.Name)]
public sealed class AnalysisWorkspaceSolutionRetentionTests
{
    [Fact]
    public void SingleInitialLimitDetectionPreservesAttachedSolution()
    {
        var experiment = CreateReadyExperiment("single-detection.itc", 25);
        var attachedModel = AttachFittedSolution(experiment);
        var attachedSolution = experiment.Solution;
        var context = AnalysisBuilder.Build(
            AnalysisSessionState.CreateDefault(),
            new[] { experiment });

        Assert.NotSame(attachedModel, context.SingleModel);

        Assert.Empty(context.DetectInitialParameterLimitViolations());

        Assert.Same(attachedModel, experiment.Model);
        Assert.Same(attachedSolution, experiment.Solution);
    }

    [Fact]
    public void GlobalInitialLimitDetectionPreservesSolutionsAndPropagatesConstraints()
    {
        var first = CreateReadyExperiment("global-detection-first.itc", 20);
        var second = CreateReadyExperiment("global-detection-second.itc", 30);
        var firstAttachedModel = AttachFittedSolution(first);
        var secondAttachedModel = AttachFittedSolution(second);
        var firstAttachedSolution = first.Solution;
        var secondAttachedSolution = second.Solution;
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Nvalue1] = VariableConstraint.SameForAll;
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.OneSetOfSites, ParameterType.Nvalue1)] =
            new ParameterOverride { Value = 11, IsLocked = false };
        var context = AnalysisBuilder.Build(session, new[] { first, second });

        var violation = Assert.Single(context.DetectInitialParameterLimitViolations());

        Assert.Equal(ParameterType.Nvalue1, violation.Parameter);
        Assert.Equal(ParameterBoundaryScope.Shared, violation.Scope);
        Assert.All(context.GlobalModel.Models, model =>
            Assert.True(model.Parameters.Table[ParameterType.Nvalue1].IsGloballyDetermined));
        Assert.Same(firstAttachedModel, first.Model);
        Assert.Same(firstAttachedSolution, first.Solution);
        Assert.Same(secondAttachedModel, second.Model);
        Assert.Same(secondAttachedSolution, second.Solution);
    }

    [Fact]
    public void FailedPreflightPreservesAttachedSolution()
    {
        var previousLimits = AppSettings.ParameterLimitSetting;
        DataManager.Init();
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            var experiment = CreateReadyExperiment("failed-preflight.itc", 25);
            var attachedModel = AttachFittedSolution(experiment);
            attachedModel.Parameters.Table[ParameterType.Offset].Update(30001);
            attachedModel.Solution = SolutionInterface.FromModel(
                attachedModel,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            var attachedSolution = experiment.Solution;
            DataManager.AddData(experiment);
            var workspace = new AnalysisWorkspace();

            Assert.True(workspace.TryRebuild());
            Assert.Throws<InitialParameterLimitException>(() => workspace.PrepareForSolve());

            Assert.Same(attachedModel, experiment.Model);
            Assert.Same(attachedSolution, experiment.Solution);
        }
        finally
        {
            DataManager.Init();
            AppSettings.ParameterLimitSetting = previousLimits;
        }
    }

    [Fact]
    public void SuccessfulGlobalPreflightAttachesPreparedModels()
    {
        DataManager.Init();
        try
        {
            var first = CreateReadyExperiment("successful-preflight-first.itc", 20);
            var second = CreateReadyExperiment("successful-preflight-second.itc", 30);
            var firstAttachedModel = AttachFittedSolution(first);
            var secondAttachedModel = AttachFittedSolution(second);
            DataManager.AddData(new ITCDataContainer[] { first, second });
            var workspace = new AnalysisWorkspace();
            workspace.Session.IsGlobal = true;

            Assert.True(workspace.TryRebuild());
            var preparedGlobalModel = workspace.Context.GlobalModel;
            var preparedModels = preparedGlobalModel.Models.ToList();

            var solver = Assert.IsType<GlobalSolver>(workspace.PrepareForSolve());

            Assert.Same(preparedGlobalModel, solver.Model);
            Assert.NotSame(firstAttachedModel, first.Model);
            Assert.NotSame(secondAttachedModel, second.Model);
            Assert.Same(preparedModels[0], first.Model);
            Assert.Same(preparedModels[1], second.Model);
        }
        finally
        {
            DataManager.Init();
        }
    }

    static Model AttachFittedSolution(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        experiment.Model = model;
        return model;
    }

    static ExperimentData CreateReadyExperiment(string fileName, double temperature)
    {
        var experiment = new ExperimentData(fileName)
        {
            CellConcentration = new FloatWithError(1e-3),
            SyringeConcentration = new FloatWithError(10e-3),
            CellVolume = 1e-3,
            MeasuredTemperature = temperature,
            TargetTemperature = temperature,
        };

        for (var index = 0; index < 5; index++)
        {
            var ratio = 0.25 * (index + 1);
            var injection = new InjectionData(
                experiment,
                index,
                1e-6,
                experiment.SyringeConcentration * 1e-6,
                include: true)
            {
                ActualCellConcentration = experiment.CellConcentration,
                ActualTitrantConcentration = ratio * experiment.CellConcentration,
                Ratio = ratio,
                Temperature = temperature,
            };
            injection.SetPeakArea(new FloatWithError(-1e-6 * (index + 1), 1e-8));
            experiment.Injections.Add(injection);
        }

        return experiment;
    }
}
