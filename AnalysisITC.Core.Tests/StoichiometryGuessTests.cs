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

[Collection(AnalysisBuilderConstraintCollectionDefinition.Name)]
public sealed class StoichiometryGuessTests
{
    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites, 2.5)]
    [InlineData(AnalysisModel.OneSetOfSites, 5)]
    [InlineData(AnalysisModel.OneSetOfSites, 50e-6)]
    [InlineData(AnalysisModel.CompetitiveBinding, 2.5)]
    [InlineData(AnalysisModel.CompetitiveBinding, 5)]
    [InlineData(AnalysisModel.CompetitiveBinding, 50e-6)]
    public void InitialNUsesPhysicalConcentrationsRegardlessOfPlotCoordinate(AnalysisModel type, double coordinate)
    {
        var data = Data();
        data.Injections.Last().Ratio = coordinate;
        var context = Build(type, false, new[] { data });

        Assert.Equal(1.25, context.SingleModel.Parameters.Table[ParameterType.Nvalue1].Value, 12);
        Assert.Equal(coordinate, data.Injections.Last().Ratio);
    }

    [Theory]
    [InlineData(0, 20e-6, 0)]
    [InlineData(50e-6, 20e-6, 1.25)]
    [InlineData(50e-15, 20e-15, 1.25)]
    [InlineData(50e-15, 20e-6, 1.25e-9)]
    public void ZeroAndSmallConcentrationsKeepTheirDimensionlessGuess(double titrant, double cell, double expected)
    {
        var data = Data();
        data.Injections.Last().ActualTitrantConcentration = titrant;
        data.Injections.Last().ActualCellConcentration = cell;
        Assert.Equal(expected, new OneSetOfSites(data).GuessN(), 14);
    }

    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites, false)]
    [InlineData(AnalysisModel.OneSetOfSites, true)]
    [InlineData(AnalysisModel.CompetitiveBinding, false)]
    [InlineData(AnalysisModel.CompetitiveBinding, true)]
    public void SharedGlobalNUsesPhysicalGuessesFromEachExperiment(AnalysisModel type, bool shared)
    {
        var first = Data();
        var second = Data();
        second.Injections.Last().ActualTitrantConcentration = 70e-6;
        var context = Build(type, true, new[] { first, second }, shared: shared);
        context.FinalizeForSolver(false);

        if (shared)
        {
            Assert.Equal(1.5, context.GlobalModelParameters.GlobalTable[ParameterType.Nvalue1].Value, 12);
            Assert.All(context.GlobalModel.Models, model => Assert.Equal(1.5, model.Parameters.Table[ParameterType.Nvalue1].Value, 12));
        }
        else
        {
            Assert.Equal(1.25, context.GlobalModel.Models[0].Parameters.Table[ParameterType.Nvalue1].Value, 12);
            Assert.Equal(1.75, context.GlobalModel.Models[1].Parameters.Table[ParameterType.Nvalue1].Value, 12);
        }
        Assert.All(new[] { first, second }, data => Assert.Equal(50e-6, data.Injections.Last().Ratio));
    }

    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites, false, false)]
    [InlineData(AnalysisModel.OneSetOfSites, false, true)]
    [InlineData(AnalysisModel.OneSetOfSites, true, false)]
    [InlineData(AnalysisModel.OneSetOfSites, true, true)]
    [InlineData(AnalysisModel.CompetitiveBinding, false, false)]
    [InlineData(AnalysisModel.CompetitiveBinding, false, true)]
    [InlineData(AnalysisModel.CompetitiveBinding, true, false)]
    [InlineData(AnalysisModel.CompetitiveBinding, true, true)]
    public void AttachedNIsReusedOnlyWhenEnabled(AnalysisModel type, bool global, bool reuse)
    {
        var data = global ? new[] { Data(), Data() } : new[] { Data() };
        foreach (var experiment in data) AttachN(type, experiment);
        var attached = data.Select(experiment => experiment.Model).ToArray();
        var context = Build(type, global, data, reuse: reuse);
        var models = global ? context.GlobalModel.Models : new[] { context.SingleModel }.AsEnumerable();
        Assert.All(models, model => Assert.Equal(reuse ? 2.75 : 1.25, model.Parameters.Table[ParameterType.Nvalue1].Value, 12));
        for (var i = 0; i < data.Length; i++)
        {
            Assert.Same(attached[i], data[i].Model);
            Assert.Equal(2.75, data[i].Solution.Parameters[ParameterType.Nvalue1].Value);
        }
    }

    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites, false, false)]
    [InlineData(AnalysisModel.OneSetOfSites, false, true)]
    [InlineData(AnalysisModel.OneSetOfSites, true, false)]
    [InlineData(AnalysisModel.OneSetOfSites, true, true)]
    [InlineData(AnalysisModel.CompetitiveBinding, false, false)]
    [InlineData(AnalysisModel.CompetitiveBinding, false, true)]
    [InlineData(AnalysisModel.CompetitiveBinding, true, false)]
    [InlineData(AnalysisModel.CompetitiveBinding, true, true)]
    public void ExplicitNOverridesWinOverTheAutomaticGuess(AnalysisModel type, bool global, bool locked)
    {
        var data = global ? new[] { Data(), Data() } : new[] { Data() };
        foreach (var experiment in data) AttachN(type, experiment);
        var context = Build(type, global, data,
            shared: global, parameterOverride: new ParameterOverride { Value = 3.25, IsLocked = locked });
        context.FinalizeForSolver(false);
        var models = global ? context.GlobalModel.Models : new[] { context.SingleModel }.AsEnumerable();
        Assert.All(models, model => Assert.Equal(3.25, model.Parameters.Table[ParameterType.Nvalue1].Value));
        var parameter = global ? context.GlobalModelParameters.GlobalTable[ParameterType.Nvalue1]
            : context.SingleModel.Parameters.Table[ParameterType.Nvalue1];
        Assert.Equal(locked, parameter.IsLocked);
        Assert.All(data, experiment => Assert.Equal(2.75, experiment.Solution.Parameters[ParameterType.Nvalue1].Value));
    }

    [Theory]
    [InlineData(false, false, .05, .1)]
    [InlineData(false, false, 20, 10)]
    [InlineData(true, false, .05, .1)]
    [InlineData(true, false, 20, 10)]
    [InlineData(false, true, .05, .05)]
    [InlineData(false, true, 20, 20)]
    [InlineData(true, true, .05, .05)]
    [InlineData(true, true, 20, 20)]
    public void FitPreparationKeepsExistingLimitsAndLockedOverrides(bool global, bool locked, double guess, double expected)
    {
        var previous = AppSettings.ParameterLimitSetting;
        DataManager.Init();
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            var data = global ? new[] { Data(), Data() } : new[] { Data() };
            foreach (var experiment in data)
                experiment.Injections.Last().ActualTitrantConcentration = 40e-6 * guess;
            DataManager.AddData(data);
            var workspace = new AnalysisWorkspace();
            workspace.SetGlobalMode(global);
            if (global) workspace.SetConstraint(ParameterType.Nvalue1, VariableConstraint.SameForAll);
            if (locked) workspace.SetParameterOverride(ParameterType.Nvalue1, guess, true);
            Assert.True(workspace.TryRebuild());
            Assert.Equal(guess, workspace.Context.ExposedParameters.Single(p => p.Key == ParameterType.Nvalue1).Value, 12);

            var solver = workspace.PrepareForSolve(false);
            var models = global ? Assert.IsType<GlobalSolver>(solver).Model.Models
                : new[] { Assert.IsType<Solver>(solver).Model }.AsEnumerable();
            Assert.All(models, model => Assert.Equal(expected, model.Parameters.Table[ParameterType.Nvalue1].Value, 12));
            Assert.All(data, experiment => Assert.Equal(50e-6, experiment.Injections.Last().Ratio));
        }
        finally
        {
            AppSettings.ParameterLimitSetting = previous;
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static void AttachN(AnalysisModel type, ExperimentData data)
    {
        var model = Build(type, false, new[] { data }).SingleModel;
        model.Parameters.Table[ParameterType.Nvalue1].Update(2.75);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
    }

    static AnalysisContext Build(AnalysisModel type, bool global, IEnumerable<ExperimentData> data,
        bool reuse = true, bool shared = false, ParameterOverride parameterOverride = null)
    {
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = type;
        session.IsGlobal = global;
        if (shared) session.Global.Constraints[ParameterType.Nvalue1] = VariableConstraint.SameForAll;
        if (parameterOverride != null)
            session.Active.ParameterOverrides[new ParameterOverrideKey(type, ParameterType.Nvalue1)] = parameterOverride;
        return AnalysisBuilder.Build(session, data, reuseAttachedSolutionInitialValues: reuse);
    }

    static ExperimentData Data()
    {
        var data = new ExperimentData("physical-N.itc")
        {
            CellConcentration = new FloatWithError(20e-6), SyringeConcentration = new FloatWithError(500e-6),
            CellVolume = 200e-6, MeasuredTemperature = 25, TargetTemperature = 25,
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
