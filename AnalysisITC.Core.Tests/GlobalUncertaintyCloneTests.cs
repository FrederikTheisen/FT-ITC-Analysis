using System;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class GlobalUncertaintyCloneTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnlockOptionAppliesOnlyToResidualBootstrapAndPreservesGlobalPropagation(
        bool leaveOneOut)
    {
        var source = BuildLockedSharedOffsetModel(
            unlockGlobalParameters: true,
            leaveOneOut ? ErrorEstimationMethod.LeaveOneOut : ErrorEstimationMethod.BootstrapResiduals);
        var sourceOffset = source.Parameters.GlobalTable[ParameterType.Offset];

        Assert.True(sourceOffset.IsLocked);
        Assert.False(source.Parameters.RequiresGlobalFitting);

        var clone = CreateUncertaintyClone(source, leaveOneOut);
        var clonedOffset = clone.Parameters.GlobalTable[ParameterType.Offset];

        Assert.Equal(
            VariableConstraint.SameForAll,
            clone.Parameters.GetConstraintForParameter(ParameterType.Offset));
        Assert.Equal(leaveOneOut, clonedOffset.IsLocked);
        Assert.Equal(!leaveOneOut, clonedOffset.IsFitted);
        Assert.Equal(!leaveOneOut, clone.Parameters.RequiresGlobalFitting);
        Assert.All(clone.Models, member =>
        {
            var memberOffset = member.Parameters.Table[ParameterType.Offset];
            Assert.True(memberOffset.IsGloballyDetermined);
            Assert.False(memberOffset.IsFitted);
        });

        if (leaveOneOut)
        {
            Assert.DoesNotContain(clone.Parameters.GetFittedParameters(), parameter =>
                parameter.Key == ParameterType.Offset);
            Assert.True(sourceOffset.IsLocked);
            Assert.Equal(-1234, sourceOffset.Value);
            return;
        }

        Assert.Single(clone.Parameters.GetFittedParameters(), parameter =>
            parameter.Key == ParameterType.Offset);

        var parameters = clone.Parameters.GetFittedParameterArray();
        parameters[0] = -4321;
        clone.Parameters.UpdateFromArray(parameters);

        Assert.Equal(-4321, clonedOffset.Value);
        Assert.All(clone.Models, member =>
            Assert.Equal(-4321, member.Parameters.Table[ParameterType.Offset].Value));
        Assert.True(sourceOffset.IsLocked);
        Assert.Equal(-1234, sourceOffset.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledUnlockOptionPreservesGlobalLock(bool leaveOneOut)
    {
        var source = BuildLockedSharedOffsetModel(
            unlockGlobalParameters: false,
            leaveOneOut ? ErrorEstimationMethod.LeaveOneOut : ErrorEstimationMethod.BootstrapResiduals);

        var clone = CreateUncertaintyClone(source, leaveOneOut);
        var clonedOffset = clone.Parameters.GlobalTable[ParameterType.Offset];

        Assert.True(clonedOffset.IsLocked);
        Assert.False(clonedOffset.IsFitted);
        Assert.False(clone.Parameters.RequiresGlobalFitting);
        Assert.DoesNotContain(clone.Parameters.GetFittedParameters(), parameter =>
            parameter.Key == ParameterType.Offset);
        Assert.Equal(
            VariableConstraint.SameForAll,
            clone.Parameters.GetConstraintForParameter(ParameterType.Offset));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnlockedTemperatureCoordinateRemainsSingleAndUpdatesMemberAffinities(
        bool leaveOneOut)
    {
        const double updatedGibbs = -27000;
        var source = BuildLockedTemperatureDependentAffinityModel(
            leaveOneOut ? ErrorEstimationMethod.LeaveOneOut : ErrorEstimationMethod.BootstrapResiduals);

        var clone = CreateUncertaintyClone(source, leaveOneOut);
        var clonedGibbs = clone.Parameters.GlobalTable[ParameterType.Gibbs1];

        Assert.Equal(
            VariableConstraint.TemperatureDependent,
            clone.Parameters.GetConstraintForParameter(ParameterType.Affinity1));

        if (leaveOneOut)
        {
            Assert.True(clonedGibbs.IsLocked);
            Assert.DoesNotContain(clone.Parameters.GetFittedParameters(), parameter =>
                parameter.Key == ParameterType.Gibbs1);
            Assert.All(clone.Models, member =>
                Assert.True(member.Parameters.Table[ParameterType.Affinity1].IsGloballyDetermined));
            return;
        }

        Assert.False(clonedGibbs.IsLocked);
        Assert.Single(clone.Parameters.GetFittedParameters(), parameter =>
            parameter.Key == ParameterType.Gibbs1);

        var parameters = clone.Parameters.GetFittedParameterArray();
        parameters[0] = updatedGibbs;
        clone.Parameters.UpdateFromArray(parameters);

        var memberAffinities = clone.Models.Select(member =>
        {
            var affinity = member.Parameters.Table[ParameterType.Affinity1];
            Assert.True(affinity.IsGloballyDetermined);
            Assert.False(affinity.IsFitted);
            Assert.Equal(
                GlobalConstraintSemantics.Log10AffinityFromGibbs(
                    updatedGibbs,
                    member.Data.MeasuredTemperatureKelvin),
                affinity.Value,
                12);
            return affinity.Value;
        }).ToArray();

        Assert.Equal(memberAffinities.Length, memberAffinities.Distinct().Count());
        Assert.True(source.Parameters.GlobalTable[ParameterType.Gibbs1].IsLocked);
    }

    [Fact]
    public void MissingGlobalCloneOptionsPreserveGlobalLocks()
    {
        var source = BuildLockedSharedOffsetModel(
            unlockGlobalParameters: false,
            ErrorEstimationMethod.BootstrapResiduals);
        source.ModelCloneOptions = null;

        var clone = source.GenerateSyntheticModel(new Random(12345));

        Assert.True(clone.Parameters.GlobalTable[ParameterType.Offset].IsLocked);
        Assert.False(clone.Parameters.RequiresGlobalFitting);
    }

    static GlobalModel BuildLockedSharedOffsetModel(
        bool unlockGlobalParameters,
        ErrorEstimationMethod method)
    {
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Offset] = VariableConstraint.SameForAll;
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.OneSetOfSites, ParameterType.Offset)] =
            new ParameterOverride
            {
                Value = -1234,
                IsLocked = true,
            };

        var context = AnalysisBuilder.Build(
            session,
            new[]
            {
                CreateExperiment(20),
                CreateExperiment(25),
                CreateExperiment(30),
            },
            reuseAttachedSolutionInitialValues: false);
        context.FinalizeForSolver();
        ApplyCloneOptions(context.GlobalModel, unlockGlobalParameters, method);

        return context.GlobalModel;
    }

    static GlobalModel BuildLockedTemperatureDependentAffinityModel(
        ErrorEstimationMethod method)
    {
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Affinity1] =
            VariableConstraint.TemperatureDependent;
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.OneSetOfSites, ParameterType.Gibbs1)] =
            new ParameterOverride
            {
                Value = -25000,
                IsLocked = true,
            };

        var context = AnalysisBuilder.Build(
            session,
            new[]
            {
                CreateExperiment(20),
                CreateExperiment(25),
                CreateExperiment(30),
            },
            reuseAttachedSolutionInitialValues: false);
        context.FinalizeForSolver();
        ApplyCloneOptions(context.GlobalModel, unlockGlobalParameters: true, method);

        return context.GlobalModel;
    }

    static void ApplyCloneOptions(
        GlobalModel model,
        bool unlockGlobalParameters,
        ErrorEstimationMethod method)
    {
        model.ModelCloneOptions = CreateCloneOptions(unlockGlobalParameters, method);
        foreach (var member in model.Models)
        {
            member.ModelCloneOptions = CreateCloneOptions(unlockGlobalParameters, method);
            if (method == ErrorEstimationMethod.BootstrapResiduals)
            {
                member.Solution = SolutionInterface.FromModel(
                    member,
                    SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            }
        }
    }

    static ModelCloneOptions CreateCloneOptions(
        bool unlockGlobalParameters,
        ErrorEstimationMethod method)
    {
        return new ModelCloneOptions
        {
            IsGlobalClone = true,
            ErrorEstimationMethod = method,
            UnlockBootstrapParameters = unlockGlobalParameters,
        };
    }

    static GlobalModel CreateUncertaintyClone(GlobalModel source, bool leaveOneOut)
    {
        return leaveOneOut
            ? source.LeaveOneOut(0)
            : source.GenerateSyntheticModel(new Random(12345));
    }

    static ExperimentData CreateExperiment(double temperature)
    {
        var experiment = new ExperimentData($"global-uncertainty-{temperature}.itc")
        {
            TargetTemperature = temperature,
            MeasuredTemperature = temperature,
            CellConcentration = new FloatWithError(1e-3),
            SyringeConcentration = new FloatWithError(10e-3),
            CellVolume = 1e-3,
        };

        for (var index = 0; index < 4; index++)
        {
            var ratio = 0.25 * (index + 1);
            var injection = new InjectionData(experiment, index, 1e-6, 1e-8, include: true)
            {
                ActualCellConcentration = 1e-3,
                ActualTitrantConcentration = ratio * 1e-3,
                Ratio = ratio,
                Temperature = temperature,
            };
            injection.SetPeakArea(new FloatWithError(-1e-6 * (index + 1), 1e-8));
            experiment.Injections.Add(injection);
        }

        return experiment;
    }
}
