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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnalysisBuilderConstraintCollectionDefinition
{
    public const string Name = "Analysis builder constraints";
}

[Collection(AnalysisBuilderConstraintCollectionDefinition.Name)]
public sealed class AnalysisBuilderConstraintTests
{
    [Theory]
    [InlineData(ParameterType.Affinity3, "Kd3")]
    [InlineData(ParameterType.Enthalpy4, "∆H4")]
    [InlineData(ParameterType.Gibbs3, "∆G3")]
    [InlineData(ParameterType.HeatCapacity4, "∆Cp4")]
    [InlineData(ParameterType.Entropy3, "∆S3")]
    [InlineData(ParameterType.EntropyContribution4, "-T∆S4")]
    public void NumberedThermodynamicHeadersSupportStepsThreeAndFour(
        ParameterType key,
        string expected)
    {
        Assert.Equal(expected, ParameterTypeAttribute.TableHeaderTitle(
            new Dictionary<AttributeKey, ExperimentAttribute>(),
            key,
            containstwo: true));
    }

    [Fact]
    public void SequentialCountFourIsEffectiveBeforeSingleParameterOverrides()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = AnalysisModel.SequentialBindingSites;
        session.Single.ModelOptions[AttributeKey.SequentialSiteCount] =
            ExperimentAttribute.Int(AttributeKey.SequentialSiteCount, "Steps", 4);
        session.Single.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.SequentialBindingSites, ParameterType.Affinity4)] =
            new ParameterOverride { Value = 8.4, IsLocked = true };

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(25) },
            reuseAttachedSolutionInitialValues: false);

        var model = Assert.IsType<SequentialBindingSites>(context.SingleModel);
        Assert.Equal(4, model.SiteCount);
        Assert.Equal(9, model.Parameters.Table.Count);
        Assert.Equal(8.4, model.Parameters.Table[ParameterType.Affinity4].Value, 12);
        Assert.True(model.Parameters.Table[ParameterType.Affinity4].IsLocked);
    }

    [Fact]
    public void SequentialCountFourBuildsDistinctPerStepGlobalCoordinatesAndFamilies()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = AnalysisModel.SequentialBindingSites;
        session.IsGlobal = true;
        session.Global.ModelOptions[AttributeKey.SequentialSiteCount] =
            ExperimentAttribute.Int(AttributeKey.SequentialSiteCount, "Steps", 4);
        session.Global.SetSequentialConstraintFamily(
            ParameterType.Affinity1,
            VariableConstraint.SameForAll,
            4);
        session.Global.SetSequentialConstraintFamily(
            ParameterType.Enthalpy1,
            VariableConstraint.SameForAll,
            4);

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(20), CreateExperiment(35) },
            reuseAttachedSolutionInitialValues: false);

        Assert.All(context.GlobalModel.Models, model =>
        {
            var sequential = Assert.IsType<SequentialBindingSites>(model);
            Assert.Equal(4, sequential.SiteCount);
            Assert.Equal(9, sequential.Parameters.Table.Count);
        });
        Assert.All(ThermodynamicParameterSlots.Active(4), slot =>
        {
            Assert.True(context.GlobalModelParameters.GlobalTable.ContainsKey(slot.Affinity));
            Assert.True(context.GlobalModelParameters.GlobalTable.ContainsKey(slot.Enthalpy));
            Assert.False(context.GlobalModelParameters.GlobalTable.ContainsKey(slot.Gibbs));
        });
        Assert.Equal(4, context.GlobalModelParameters.GlobalTable.Keys
            .Count(key => ThermodynamicParameterSlots.TryResolve(key, out _, out var family)
                && family == ThermodynamicParameterFamily.Affinity));
        Assert.Equal(4, context.ExposedConstraintFamilies
            .Single(family => family.Key == ParameterType.Affinity1)
            .MemberKeys.Count);
        Assert.Equal(4, context.ExposedConstraintFamilies
            .Single(family => family.Key == ParameterType.Enthalpy1)
            .MemberKeys.Count);
    }

    [Fact]
    public void LegacySingleFactoryAppliesCountImmediatelyAndRestoresStepFourOverrideAfterRefresh()
    {
        var experiment = CreateExperiment(25);
        var factory = new SingleModelFactory(AnalysisModel.SequentialBindingSites);
        factory.InitializeModel(experiment);

        factory.SetModelOption(ExperimentAttribute.Int(
            AttributeKey.SequentialSiteCount,
            "Steps",
            4));

        Assert.Contains(factory.GetExposedParameters(), parameter => parameter.Key == ParameterType.Affinity4);
        factory.SetCustomParameterValue(ParameterType.Affinity4, 8.75, locked: true);

        factory.InitializeModel(experiment);

        Assert.Equal(4, ((SequentialBindingSites)factory.Model).SiteCount);
        Assert.Equal(8.75, factory.Model.Parameters.Table[ParameterType.Affinity4].Value, 12);
        Assert.True(factory.Model.Parameters.Table[ParameterType.Affinity4].IsLocked);
    }

    [Fact]
    public void LegacyGlobalFactoryRebuildsCountRowsAndDoesNotResurrectDiscardedCoordinates()
    {
        GlobalModelFactory.ClearPreviousParameters();
        var factory = new GlobalModelFactory(AnalysisModel.SequentialBindingSites);
        factory.InitializeModel(new List<ExperimentData>
        {
            CreateExperiment(20),
            CreateExperiment(35),
        });
        // Legacy factories intentionally restore prior options across refreshes, so
        // establish the precondition explicitly instead of depending on test order.
        factory.SetModelOption(ExperimentAttribute.Int(
            AttributeKey.SequentialSiteCount,
            "Steps",
            2));
        factory.GlobalModelParameters.SetSequentialConstraintFamily(
            ParameterType.Affinity1,
            VariableConstraint.SameForAll,
            2);
        factory.InitializeGlobalParameters();

        factory.SetModelOption(ExperimentAttribute.Int(
            AttributeKey.SequentialSiteCount,
            "Steps",
            4));

        Assert.Contains(ParameterType.Affinity4, factory.GetExposedConstraints().Keys);
        Assert.Equal(VariableConstraint.SameForAll,
            factory.GlobalModelParameters.GetConstraintForParameter(ParameterType.Affinity4));
        Assert.Contains(ParameterType.Affinity4, factory.GlobalModelParameters.GlobalTable.Keys);
        factory.SetCustomParameterValue(ParameterType.Affinity3, 9.9, locked: false);

        factory.SetModelOption(ExperimentAttribute.Int(
            AttributeKey.SequentialSiteCount,
            "Steps",
            2));
        Assert.DoesNotContain(ParameterType.Affinity3, factory.GlobalModelParameters.GlobalTable.Keys);
        Assert.DoesNotContain(ParameterType.Affinity3, factory.GlobalModelParameters.Constraints.Keys);

        factory.SetModelOption(ExperimentAttribute.Int(
            AttributeKey.SequentialSiteCount,
            "Steps",
            4));

        Assert.Equal(VariableConstraint.SameForAll,
            factory.GlobalModelParameters.GetConstraintForParameter(ParameterType.Affinity4));
        Assert.NotEqual(9.9,
            factory.GlobalModelParameters.GlobalTable[ParameterType.Affinity3].Value);
    }


    [Fact]
    public void SameForAllAffinityUsesSharedLog10AssociationCoordinate()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Affinity1] = VariableConstraint.SameForAll;
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.OneSetOfSites, ParameterType.Affinity1)] =
            new ParameterOverride { Value = 7.25, IsLocked = false };

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(20), CreateExperiment(35) },
            reuseAttachedSolutionInitialValues: false);

        Assert.Contains(VariableConstraint.SameForAll,
            context.ExposedConstraintOptions[ParameterType.Affinity1]);
        Assert.True(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Affinity1));
        Assert.False(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Gibbs1));
        Assert.Equal(7, context.FittingVariableCount);

        context.FinalizeForSolver();

        Assert.All(context.GlobalModel.Models, model =>
        {
            var affinity = model.Parameters.Table[ParameterType.Affinity1];
            Assert.Equal(7.25, affinity.Value, 12);
            Assert.True(affinity.IsGloballyDetermined);
            Assert.False(affinity.IsFitted);
        });
        Assert.Equal(context.FittingVariableCount,
            context.GlobalModelParameters.TotalFittingParameters);
    }

    [Fact]
    public void TemperatureDependentAffinityUsesSharedGibbsCoordinateAtEachTemperature()
    {
        const double gibbs = -25000;
        var session = AnalysisSessionState.CreateDefault();
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Affinity1] = VariableConstraint.TemperatureDependent;
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.OneSetOfSites, ParameterType.Gibbs1)] =
            new ParameterOverride { Value = gibbs, IsLocked = false };

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(20), CreateExperiment(35) },
            reuseAttachedSolutionInitialValues: false);

        Assert.True(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Gibbs1));
        Assert.False(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Affinity1));

        context.FinalizeForSolver();

        var affinities = context.GlobalModel.Models.Select(model =>
        {
            var expected = GlobalConstraintSemantics.Log10AffinityFromGibbs(
                gibbs,
                model.Data.MeasuredTemperatureKelvin);
            var actual = model.Parameters.Table[ParameterType.Affinity1];
            Assert.Equal(expected, actual.Value, 12);
            Assert.True(actual.IsGloballyDetermined);
            return actual.Value;
        }).ToArray();
        Assert.NotEqual(affinities[0], affinities[1]);
    }

    [Fact]
    public void TwoSetsSharedAffinityPreservesDistinctCoordinatesAndSlotInitialValues()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = AnalysisModel.TwoSetsOfSites;
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Affinity1] = VariableConstraint.SameForAll;
        session.Global.Constraints[ParameterType.Affinity2] = VariableConstraint.SameForAll;

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(20), CreateExperiment(35) },
            reuseAttachedSolutionInitialValues: false);

        Assert.Contains(VariableConstraint.SameForAll,
            context.ExposedConstraintOptions[ParameterType.Affinity1]);
        Assert.Contains(VariableConstraint.SameForAll,
            context.ExposedConstraintOptions[ParameterType.Affinity2]);
        var shared1 = context.GlobalModelParameters.GlobalTable[ParameterType.Affinity1].Value;
        var shared2 = context.GlobalModelParameters.GlobalTable[ParameterType.Affinity2].Value;
        Assert.Equal(context.GlobalModel.Models.Average(model =>
            model.Parameters.Table[ParameterType.Affinity1].Value), shared1, 12);
        Assert.Equal(context.GlobalModel.Models.Average(model =>
            model.Parameters.Table[ParameterType.Affinity2].Value), shared2, 12);
        Assert.NotEqual(shared1, shared2);
        Assert.False(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Gibbs1));
        Assert.False(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Gibbs2));

        context.FinalizeForSolver();

        Assert.All(context.GlobalModel.Models, model =>
        {
            Assert.Equal(shared1, model.Parameters.Table[ParameterType.Affinity1].Value, 12);
            Assert.Equal(shared2, model.Parameters.Table[ParameterType.Affinity2].Value, 12);
        });
    }

    [Fact]
    public void LegacyFactoryUsesAffinityCoordinateForSameForAll()
    {
        GlobalModelFactory.ClearPreviousParameters();
        var factory = new GlobalModelFactory(AnalysisModel.OneSetOfSites);
        factory.InitializeModel(new System.Collections.Generic.List<ExperimentData>
        {
            CreateExperiment(20),
            CreateExperiment(35),
        });

        Assert.Contains(VariableConstraint.SameForAll,
            factory.GetExposedConstraints()[ParameterType.Affinity1]);
        factory.GlobalModelParameters.SetConstraintForParameter(
            ParameterType.Affinity1,
            VariableConstraint.SameForAll);
        factory.InitializeGlobalParameters();

        Assert.True(factory.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Affinity1));
        Assert.False(factory.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Gibbs1));

        factory.BuildModel();
        var shared = factory.GlobalModelParameters.GlobalTable[ParameterType.Affinity1].Value;
        Assert.All(factory.Model.Models, model =>
            Assert.Equal(shared, model.Parameters.Table[ParameterType.Affinity1].Value, 12));
    }

    [Fact]
    public void TwoSetsCanMixSharedAffinityAndTemperatureDependentGibbsBySlot()
    {
        const double affinity1 = 6.5;
        const double gibbs2 = -18000;
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = AnalysisModel.TwoSetsOfSites;
        session.IsGlobal = true;
        session.Global.Constraints[ParameterType.Affinity1] = VariableConstraint.SameForAll;
        session.Global.Constraints[ParameterType.Affinity2] = VariableConstraint.TemperatureDependent;
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.TwoSetsOfSites, ParameterType.Affinity1)] =
            new ParameterOverride { Value = affinity1 };
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.TwoSetsOfSites, ParameterType.Gibbs2)] =
            new ParameterOverride { Value = gibbs2 };

        var context = AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(20), CreateExperiment(35) },
            reuseAttachedSolutionInitialValues: false);

        Assert.True(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Affinity1));
        Assert.True(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Gibbs2));
        Assert.False(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Gibbs1));
        Assert.False(context.GlobalModelParameters.GlobalTable.ContainsKey(ParameterType.Affinity2));

        context.FinalizeForSolver();

        Assert.All(context.GlobalModel.Models, model =>
        {
            Assert.Equal(affinity1, model.Parameters.Table[ParameterType.Affinity1].Value, 12);
            Assert.Equal(
                GlobalConstraintSemantics.Log10AffinityFromGibbs(
                    gibbs2,
                    model.Data.MeasuredTemperatureKelvin),
                model.Parameters.Table[ParameterType.Affinity2].Value,
                12);
        });
    }

    [Fact]
    public void SequentialFamilyStateAppliesAtomicallyAndDropsInactiveStepState()
    {
        var state = new AnalysisState();
        state.SetSequentialConstraintFamily(
            ParameterType.Affinity1,
            VariableConstraint.SameForAll,
            activeStepCount: 4);
        state.SetSequentialConstraintFamily(
            ParameterType.Enthalpy1,
            VariableConstraint.TemperatureDependent,
            activeStepCount: 4);
        state.ParameterOverrides[new ParameterOverrideKey(
            AnalysisModel.SequentialBindingSites,
            ParameterType.Gibbs3)] = new ParameterOverride { Value = -30000 };
        state.ParameterOverrides[new ParameterOverrideKey(
            AnalysisModel.SequentialBindingSites,
            ParameterType.HeatCapacity4)] = new ParameterOverride { Value = -500 };

        Assert.All(ThermodynamicParameterSlots.Active(4), slot =>
        {
            Assert.Equal(VariableConstraint.SameForAll, state.Constraints[slot.Affinity]);
            Assert.Equal(VariableConstraint.TemperatureDependent, state.Constraints[slot.Enthalpy]);
        });

        state.ResizeSequentialSteps(4, 2);

        Assert.DoesNotContain(ParameterType.Affinity3, state.Constraints.Keys);
        Assert.DoesNotContain(ParameterType.Affinity4, state.Constraints.Keys);
        Assert.DoesNotContain(ParameterType.Enthalpy3, state.Constraints.Keys);
        Assert.DoesNotContain(ParameterType.Enthalpy4, state.Constraints.Keys);
        Assert.DoesNotContain(state.ParameterOverrides.Keys, key => key.Key == ParameterType.Gibbs3);
        Assert.DoesNotContain(state.ParameterOverrides.Keys, key => key.Key == ParameterType.HeatCapacity4);
        Assert.Equal(VariableConstraint.SameForAll, state.Constraints[ParameterType.Affinity1]);
        Assert.Equal(VariableConstraint.SameForAll, state.Constraints[ParameterType.Affinity2]);

        state.ResizeSequentialSteps(2, 4);

        Assert.All(ThermodynamicParameterSlots.Active(4), slot =>
        {
            Assert.Equal(VariableConstraint.SameForAll, state.Constraints[slot.Affinity]);
            Assert.Equal(VariableConstraint.TemperatureDependent, state.Constraints[slot.Enthalpy]);
        });
        Assert.DoesNotContain(state.ParameterOverrides.Keys, key => key.Key == ParameterType.Gibbs3);
        Assert.DoesNotContain(state.ParameterOverrides.Keys, key => key.Key == ParameterType.HeatCapacity4);
    }

    [Fact]
    public void SequentialBuildRejectsMixedOrInactiveFamilyConstraintState()
    {
        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = AnalysisModel.SequentialBindingSites;
        session.IsGlobal = true;
        session.Global.ModelOptions[AttributeKey.SequentialSiteCount] =
            ExperimentAttribute.Int(AttributeKey.SequentialSiteCount, "Steps", 4);
        session.Global.Constraints[ParameterType.Affinity1] = VariableConstraint.SameForAll;
        session.Global.Constraints[ParameterType.Affinity2] = VariableConstraint.SameForAll;

        var inconsistent = Assert.Throws<InvalidOperationException>(() => AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(20), CreateExperiment(35) }));
        Assert.Contains("family-wide style", inconsistent.Message);

        session.Global.SetSequentialConstraintFamily(
            ParameterType.Affinity1,
            VariableConstraint.SameForAll,
            activeStepCount: 4);
        session.Global.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = 2;

        var inactive = Assert.Throws<InvalidOperationException>(() => AnalysisBuilder.Build(
            session,
            new[] { CreateExperiment(20), CreateExperiment(35) }));
        Assert.Contains("inactive", inactive.Message);
    }

    [Fact]
    public void GlobalModelTreatsMissingSyringeCorrectionOptionAsDisabled()
    {
        var experiment = CreateExperiment(25);
        var member = new Dissociation(experiment);
        member.InitializeParameters(experiment);
        var global = new GlobalModel();
        global.AddModel(member);

        Assert.False(global.UseSyringeCorrectionMode);
    }

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
