using System.Collections.Generic;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class MultiExperimentOffsetConstraintTests
{
    [Fact]
    public void OffsetRemainsMemberSpecificByDefault()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment("first", -1000), CreateExperiment("second", -2000) },
            reuseAttachedSolutionInitialValues: false);

        Assert.Equal(
            new[] { VariableConstraint.None, VariableConstraint.SameForAll },
            context.ExposedConstraintOptions[ParameterType.Offset]);
        Assert.False(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Offset));
        Assert.Equal(8, context.FittingVariableCount);

        context.FinalizeForSolver();

        Assert.Equal(-800, context.GlobalModel.Models[0].Parameters.Table[ParameterType.Offset].Value, 10);
        Assert.Equal(-1600, context.GlobalModel.Models[1].Parameters.Table[ParameterType.Offset].Value, 10);
        Assert.All(context.GlobalModel.Models, model =>
        {
            var memberOffset = model.Parameters.Table[ParameterType.Offset];
            Assert.False(memberOffset.IsGloballyDetermined);
            Assert.True(memberOffset.IsFitted);
        });
    }

    [Fact]
    public void BuilderExposesAndAppliesSharedOffset()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Offset] = VariableConstraint.SameForAll;
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.OneSetOfSites, ParameterType.Offset)] = new ParameterOverride
            {
                Value = -1234,
                IsLocked = false,
            };

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment("first", -1000), CreateExperiment("second", -2000) },
            reuseAttachedSolutionInitialValues: false);

        Assert.Equal(
            new[] { VariableConstraint.None, VariableConstraint.SameForAll },
            context.ExposedConstraintOptions[ParameterType.Offset]);
        Assert.Equal(
            VariableConstraint.SameForAll,
            context.GlobalModelParameters.GetConstraintForParameter(ParameterType.Offset));

        var offset = Assert.Contains(ParameterType.Offset, context.GlobalModelParameters.GlobalTable);
        Assert.Equal(-1234, offset.Value);
        Assert.False(offset.IsLocked);
        Assert.Equal(7, context.FittingVariableCount);

        context.FinalizeForSolver();

        Assert.All(context.GlobalModel.Models, model =>
        {
            var memberOffset = model.Parameters.Table[ParameterType.Offset];
            Assert.Equal(-1234, memberOffset.Value);
            Assert.True(memberOffset.IsGloballyDetermined);
            Assert.False(memberOffset.IsFitted);
        });
    }

    [Fact]
    public void LockedSharedOffsetIsNotCountedAsFittingVariable()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Offset] = VariableConstraint.SameForAll;
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.OneSetOfSites, ParameterType.Offset)] = new ParameterOverride
            {
                Value = 0,
                IsLocked = true,
            };

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment("first", -1000), CreateExperiment("second", -2000) },
            reuseAttachedSolutionInitialValues: false);

        Assert.True(context.GlobalModelParameters.GlobalTable[ParameterType.Offset].IsLocked);
        Assert.Equal(6, context.FittingVariableCount);
    }

    [Fact]
    public void LegacyFactorySupportsSharedOffsetForStoredResultUpdates()
    {
        GlobalModelFactory.ClearPreviousParameters();
        var factory = new GlobalModelFactory(AnalysisModel.OneSetOfSites);
        factory.InitializeModel(new List<ExperimentData>
        {
            CreateExperiment("first", -1000),
            CreateExperiment("second", -2000),
        });

        Assert.Equal(
            new[] { VariableConstraint.None, VariableConstraint.SameForAll },
            factory.GetExposedConstraints()[ParameterType.Offset]);

        factory.GlobalModelParameters.SetConstraintForParameter(
            ParameterType.Offset,
            VariableConstraint.SameForAll);
        factory.InitializeGlobalParameters();
        factory.SetCustomParameterValue(ParameterType.Offset, -4321, locked: true);
        factory.BuildModel();

        var sharedOffset = factory.GlobalModelParameters.GlobalTable[ParameterType.Offset];
        Assert.Equal(-4321, sharedOffset.Value);
        Assert.True(sharedOffset.IsLocked);
        Assert.All(factory.Model.Models, model =>
        {
            var memberOffset = model.Parameters.Table[ParameterType.Offset];
            Assert.Equal(-4321, memberOffset.Value);
            Assert.True(memberOffset.IsGloballyDetermined);
        });
    }

    static ExperimentData CreateExperiment(string name, double enthalpy)
    {
        var experiment = new ExperimentData($"{name}.itc")
        {
            TargetTemperature = 25,
            MeasuredTemperature = 25,
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
            injection.SetPeakArea(new FloatWithError(enthalpy * injection.InjectionMass));
            experiment.Injections.Add(injection);
        }

        return experiment;
    }
}
