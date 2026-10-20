using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(AnalysisBuilderConstraintCollectionDefinition.Name)]
public sealed class CompetitiveAffinityInitializationTests
{
    [Fact]
    public void CompetitiveBindingReusesAttachedCanonicalAffinityWhenEnabled()
    {
        var data = CreateExperiment("competitive-attached.itc");
        AttachSolution(data, AnalysisModel.CompetitiveBinding, 7);

        var context = Build(AnalysisModel.CompetitiveBinding, data, reuse: true);

        Assert.Equal(7, context.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
        Assert.Equal(7, data.Solution.Parameters[ParameterType.Affinity1].Value, 12);
    }

    [Fact]
    public void CompetitiveBindingUsesDefaultAffinityWhenReuseIsDisabledOrNoAffinityIsAttached()
    {
        var attached = CreateExperiment("competitive-disabled.itc");
        AttachSolution(attached, AnalysisModel.CompetitiveBinding, 7);
        var noAttached = CreateExperiment("competitive-no-solution.itc");

        Assert.Equal(9, Build(AnalysisModel.CompetitiveBinding, attached, reuse: false)
            .SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
        Assert.Equal(9, Build(AnalysisModel.CompetitiveBinding, noAttached, reuse: true)
            .SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
    }

    [Fact]
    public void CompetitiveBindingUsesDefaultWhenAttachedOffsetSolutionHasNoAffinity()
    {
        var data = CreateExperiment("offset-attached.itc");
        AttachSolution(data, AnalysisModel.Offset, 0);

        var context = Build(AnalysisModel.CompetitiveBinding, data, reuse: true);

        Assert.Equal(9, context.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
        Assert.Equal(AnalysisModel.Offset, data.Solution.Model.ModelType);
    }

    [Fact]
    public void CompetitiveBindingUsesSharedAttachedSolutionLookupAcrossModelTypes()
    {
        var data = CreateExperiment("one-site-attached.itc");
        AttachSolution(data, AnalysisModel.OneSetOfSites, 7);

        var context = Build(AnalysisModel.CompetitiveBinding, data, reuse: true);

        Assert.Equal(7, context.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
    }

    [Fact]
    public void SavedZeroLogAffinityIsAValidReusableValue()
    {
        var data = CreateExperiment("zero-log-affinity.itc");
        AttachSolution(data, AnalysisModel.CompetitiveBinding, 0);

        var context = Build(AnalysisModel.CompetitiveBinding, data, reuse: true);

        Assert.Equal(0, context.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitAffinityOverrideWinsOverAttachedValueOrDefault(bool attach, bool locked)
    {
        var data = CreateExperiment("competitive-override.itc");
        if (attach) AttachSolution(data, AnalysisModel.CompetitiveBinding, 7);
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = AnalysisModel.CompetitiveBinding;
        session.Single.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.CompetitiveBinding, ParameterType.Affinity1)] =
            new ParameterOverride { Value = 5.25, IsLocked = locked };

        var context = AnalysisBuilder.Build(session, new[] { data }, reuseAttachedSolutionInitialValues: true);
        var parameter = context.SingleModel.Parameters.Table[ParameterType.Affinity1];

        Assert.Equal(5.25, parameter.Value, 12);
        Assert.Equal(locked, parameter.IsLocked);
    }

    [Fact]
    public void SingleAndGlobalBuildersKeepEachAttachedAffinityAndShareArithmeticMeanLogCoordinate()
    {
        var first = CreateExperiment("competitive-global-first.itc");
        var second = CreateExperiment("competitive-global-second.itc");
        AttachSolution(first, AnalysisModel.CompetitiveBinding, 6);
        AttachSolution(second, AnalysisModel.CompetitiveBinding, 8);

        var singleContext = Build(AnalysisModel.CompetitiveBinding, first, reuse: true);
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = AnalysisModel.CompetitiveBinding;
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Affinity1] = VariableConstraint.None;
        var independentContext = AnalysisBuilder.Build(session, new[] { first, second }, reuseAttachedSolutionInitialValues: true);

        Assert.Equal(6, singleContext.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
        Assert.Equal(new[] { 6d, 8d }, independentContext.GlobalModel.Models
            .Select(model => model.Parameters.Table[ParameterType.Affinity1].Value).ToArray());

        var disabledIndependent = AnalysisBuilder.Build(session, new[] { first, second }, reuseAttachedSolutionInitialValues: false);
        Assert.Equal(new[] { 9d, 9d }, disabledIndependent.GlobalModel.Models
            .Select(model => model.Parameters.Table[ParameterType.Affinity1].Value).ToArray());

        session.Global.Constraints[ParameterType.Affinity1] = VariableConstraint.SameForAll;
        var sharedContext = AnalysisBuilder.Build(session, new[] { first, second }, reuseAttachedSolutionInitialValues: true);
        sharedContext.FinalizeForSolver(false);

        Assert.Equal(7, sharedContext.GlobalModelParameters.GlobalTable[ParameterType.Affinity1].Value, 12);
        Assert.All(sharedContext.GlobalModel.Models,
            model => Assert.Equal(7, model.Parameters.Table[ParameterType.Affinity1].Value, 12));

        var disabledShared = AnalysisBuilder.Build(session, new[] { first, second }, reuseAttachedSolutionInitialValues: false);
        disabledShared.FinalizeForSolver(false);
        Assert.Equal(9, disabledShared.GlobalModelParameters.GlobalTable[ParameterType.Affinity1].Value, 12);
        Assert.All(disabledShared.GlobalModel.Models,
            model => Assert.Equal(9, model.Parameters.Table[ParameterType.Affinity1].Value, 12));
        Assert.Equal(new[] { 6d, 8d }, new[] { first, second }
            .Select(data => data.Solution.Parameters[ParameterType.Affinity1].Value).ToArray());
    }

    [Fact]
    public void DefaultParameterLookupBypassesAttachedAffinity()
    {
        var data = CreateExperiment("competitive-default-lookup.itc");
        AttachSolution(data, AnalysisModel.CompetitiveBinding, 7);

        var value = AnalysisBuilder.GetDefaultParameterValue(
            AnalysisModel.CompetitiveBinding, data, ParameterType.Affinity1);

        Assert.Equal(9, value, 12);
        Assert.Equal(7, data.Solution.Parameters[ParameterType.Affinity1].Value, 12);
    }

    [Fact]
    public void WorkspaceResetDisablesAffinityReuseAndPreservesSavedSolution()
    {
        DataManager.Init();
        try
        {
            var data = CreateExperiment("competitive-workspace-reset.itc");
            AttachSolution(data, AnalysisModel.CompetitiveBinding, 7);
            DataManager.AddData(data);
            var workspace = new AnalysisWorkspace();
            workspace.SetModelType(AnalysisModel.CompetitiveBinding);
            Assert.True(workspace.ReuseAttachedSolutionInitialValues);
            Assert.True(workspace.TryRebuild());
            Assert.Equal(7, workspace.Context.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);

            var savedSolution = data.Solution;
            workspace.ResetStoredAnalysisState();

            Assert.False(workspace.ReuseAttachedSolutionInitialValues);
            Assert.True(workspace.IsReady);
            Assert.Equal(9, workspace.Context.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
            Assert.Same(savedSolution, data.Solution);
            Assert.Equal(7, data.Solution.Parameters[ParameterType.Affinity1].Value, 12);

            workspace.Rebuild(new[] { data });
            Assert.False(workspace.ReuseAttachedSolutionInitialValues);
            Assert.Equal(9, workspace.Context.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);

            var previewContext = AnalysisBuilder.Build(
                workspace.Session,
                new[] { data },
                reuseAttachedSolutionInitialValues: workspace.ReuseAttachedSolutionInitialValues);
            Assert.Equal(9, previewContext.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
            Assert.Same(savedSolution, data.Solution);
            Assert.Equal(7, savedSolution.Parameters[ParameterType.Affinity1].Value, 12);

            workspace.PrepareForSolve(false);
            Assert.True(workspace.ReuseAttachedSolutionInitialValues);
            Assert.Equal(9, workspace.Context.SingleModel.Parameters.Table[ParameterType.Affinity1].Value, 12);
            Assert.Equal(7, savedSolution.Parameters[ParameterType.Affinity1].Value, 12);
        }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    [Fact]
    public void LegacyFactoryRebuildPreservesUserChangedAffinity()
    {
        var data = CreateExperiment("competitive-factory-rebuild.itc");
        AttachSolution(data, AnalysisModel.CompetitiveBinding, 7);
        var factory = new SingleModelFactory(AnalysisModel.CompetitiveBinding);
        factory.InitializeModel(data);
        Assert.Equal(7, factory.Model.Parameters.Table[ParameterType.Affinity1].Value, 12);
        factory.SetCustomParameterValue(ParameterType.Affinity1, 4.5, locked: false);

        factory.InitializeModel(data);

        var parameter = factory.Model.Parameters.Table[ParameterType.Affinity1];
        Assert.Equal(4.5, parameter.Value, 12);
        Assert.True(parameter.ChangedByUser);
        Assert.False(parameter.IsLocked);
    }

    static AnalysisContext Build(AnalysisModel type, ExperimentData data, bool reuse)
    {
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = type;
        return AnalysisBuilder.Build(session, new[] { data }, reuseAttachedSolutionInitialValues: reuse);
    }

    static void AttachSolution(ExperimentData data, AnalysisModel modelType, double logAffinity)
    {
        var model = AnalysisBuilder.ConstructModel(modelType, data);
        model.ReuseAttachedSolutionInitialValues = false;
        model.InitializeParameters(data);
        if (model.Parameters.Table.TryGetValue(ParameterType.Affinity1, out var affinity))
            affinity.Update(logAffinity);
        model.Solution = SolutionInterface.FromModel(
            model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
    }

    static ExperimentData CreateExperiment(string fileName)
    {
        var data = new ExperimentData(fileName)
        {
            CellConcentration = new FloatWithError(20e-6),
            SyringeConcentration = new FloatWithError(500e-6),
            CellVolume = 200e-6,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        for (var i = 0; i < 5; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 500e-6 * 2e-6, true)
            {
                ActualCellConcentration = 20e-6,
                ActualTitrantConcentration = (i + 1) * 10e-6,
                Ratio = (i + 1) * 10e-6,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + i * 1e-7));
            data.Injections.Add(injection);
        }
        return data;
    }
}
