using System;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnalysisBuilderConstraintCollectionDefinition
{
    public const string Name = "Analysis builder constraints";
}

[Collection(AnalysisBuilderConstraintCollectionDefinition.Name)]
public sealed class AnalysisBuilderConstraintTests
{
    [Fact]
    public void ActiveConstraintAndDependentParametersRemainAligned()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Enthalpy1] = VariableConstraint.TemperatureDependent;

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(20), CreateExperiment(20.5) });

        Assert.Equal(
            VariableConstraint.TemperatureDependent,
            context.GlobalModelParameters.GetConstraintForParameter(ParameterType.Enthalpy1));
        Assert.Contains(
            VariableConstraint.TemperatureDependent,
            context.ExposedConstraintOptions[ParameterType.Enthalpy1]);
        Assert.True(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Enthalpy1));
        Assert.True(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.HeatCapacity1));
    }

    [Fact]
    public void RemovingConstraintRemovesItsDependentParametersOnNextBuild()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Enthalpy1] = VariableConstraint.TemperatureDependent;
        var experiments = new[] { CreateExperiment(20), CreateExperiment(20.5) };

        var constrained = AnalysisBuilder.Build(session, experiments);
        Assert.True(constrained.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Enthalpy1));
        Assert.True(constrained.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.HeatCapacity1));

        session.Global.Constraints[ParameterType.Enthalpy1] = VariableConstraint.None;
        var unconstrained = AnalysisBuilder.Build(session, experiments);

        Assert.Equal(
            VariableConstraint.None,
            unconstrained.GlobalModelParameters.GetConstraintForParameter(ParameterType.Enthalpy1));
        Assert.False(unconstrained.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Enthalpy1));
        Assert.False(unconstrained.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.HeatCapacity1));
    }

    [Fact]
    public void FailedRebuildInvalidatesPreviousParameterSnapshot()
    {
        DataManager.Init();
        try
        {
            var experiment = CreateExperiment(20);
            DataManager.AddData(experiment);
            var workspace = new AnalysisWorkspace();

            Assert.True(workspace.TryRebuild());
            Assert.True(workspace.IsReady);

            experiment.SyringeConcentration = new FloatWithError(0);

            Assert.False(workspace.TryRebuild());
            Assert.False(workspace.IsReady);
        }
        finally
        {
            DataManager.Init();
        }
    }

    [Fact]
    public void ThrowingRebuildInvalidatesPreviousParameterSnapshot()
    {
        DataManager.Init();
        try
        {
            DataManager.AddData(CreateExperiment(20));
            var workspace = new AnalysisWorkspace();

            Assert.True(workspace.TryRebuild());
            Assert.True(workspace.IsReady);

            workspace.Session.IsGlobal = true;

            Assert.Throws<InvalidOperationException>(() => workspace.Rebuild());
            Assert.False(workspace.IsReady);
        }
        finally
        {
            DataManager.Init();
        }
    }

    static ExperimentData CreateExperiment(double temperature)
    {
        var experiment = new ExperimentData($"constraint-test-{temperature}.itc")
        {
            TargetTemperature = temperature,
            MeasuredTemperature = temperature,
            CellConcentration = new FloatWithError(1e-3),
            SyringeConcentration = new FloatWithError(1e-3),
            CellVolume = 1e-3,
        };

        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(experiment, index, 1e-6, 0, include: true)
            {
                Ratio = index + 1,
            };
            injection.SetPeakArea(new FloatWithError(-1e-6));
            experiment.Injections.Add(injection);
        }

        return experiment;
    }
}
