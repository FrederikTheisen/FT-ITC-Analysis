using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(AnalysisBuilderConstraintCollectionDefinition.Name)]
public sealed class AnalysisWorkspaceSolutionRetentionTests
{
    [Fact]
    public void ErrorWeightedEligibilityRequiresEveryIncludedSdToBeFiniteAndPositive()
    {
        var experiment = CreateReadyExperiment("weighted-eligibility.itc", 25);

        Assert.True(AnalysisBuilder.CanUseErrorWeightedFitting(experiment));

        foreach (var invalidSd in new[]
        {
            0.0,
            -1.0,
            double.NaN,
            double.PositiveInfinity,
        })
        {
            experiment.Injections[0].SetPeakArea(PeakAreaWithSd(invalidSd));
            Assert.False(AnalysisBuilder.CanUseErrorWeightedFitting(experiment));
        }

        experiment.Injections[0].Include = false;
        Assert.True(AnalysisBuilder.CanUseErrorWeightedFitting(experiment));

        foreach (var injection in experiment.Injections)
            injection.Include = false;
        Assert.False(AnalysisBuilder.CanUseErrorWeightedFitting(experiment));
    }

    [Fact]
    public void ErrorWeightedEligibilityChecksEveryExperimentAndRejectsEmptyInput()
    {
        var first = CreateReadyExperiment("weighted-first.itc", 20);
        var second = CreateReadyExperiment("weighted-second.itc", 30);

        Assert.True(AnalysisBuilder.CanUseErrorWeightedFitting(new[] { first, second }));

        second.Injections[2].SetPeakArea(new FloatWithError(-3e-6));
        Assert.False(AnalysisBuilder.CanUseErrorWeightedFitting(new[] { first, second }));
        Assert.False(AnalysisBuilder.CanUseErrorWeightedFitting((ExperimentData)null));
        Assert.False(AnalysisBuilder.CanUseErrorWeightedFitting(
            (IEnumerable<ExperimentData>)null));
        Assert.False(AnalysisBuilder.CanUseErrorWeightedFitting(
            Array.Empty<ExperimentData>()));
    }

    [Fact]
    public void WeightedPreflightRejectsInvalidSdBeforeReplacingAttachedSolution()
    {
        DataManager.Init();
        try
        {
            var experiment = CreateReadyExperiment("weighted-preflight-invalid.itc", 25);
            experiment.Injections[0].SetPeakArea(new FloatWithError(-1e-6));
            var attachedModel = AttachFittedSolution(experiment);
            var attachedSolution = experiment.Solution;
            DataManager.AddData(experiment);
            var workspace = new AnalysisWorkspace();

            Assert.True(workspace.TryRebuild());
            var exception = Assert.Throws<HandledException>(() =>
                workspace.PrepareForSolve(true));

            Assert.Equal("Error-weighted fitting unavailable", exception.Title);
            Assert.Same(attachedModel, experiment.Model);
            Assert.Same(attachedSolution, experiment.Solution);

            var solver = workspace.PrepareForSolve(false);
            Assert.False(solver.UseErrorWeightedFitting);
        }
        finally
        {
            DataManager.Init();
        }
    }

    [Fact]
    public void WeightedPreflightConfiguresEligibleSolver()
    {
        DataManager.Init();
        try
        {
            var experiment = CreateReadyExperiment("weighted-preflight-valid.itc", 25);
            DataManager.AddData(experiment);
            var workspace = new AnalysisWorkspace();

            Assert.True(workspace.TryRebuild());
            var solver = workspace.PrepareForSolve(true);

            Assert.True(solver.UseErrorWeightedFitting);
        }
        finally
        {
            DataManager.Init();
        }
    }

    [Fact]
    public void EditableModelOptionsAreAvailableWithoutAContextAndUseCanonicalDefaults()
    {
        var workspace = new AnalysisWorkspace();
        workspace.SetModelType(AnalysisModel.CompetitiveBinding);

        var options = workspace.GetEditableModelOptions();
        Assert.Equal(5, options.Count);
        Assert.Equal(10e-6, options[AttributeKey.PreboundLigandConc].ParameterValue.Value, 12);
        Assert.False(options[AttributeKey.PreboundLigandConc].BoolValue);
        options[AttributeKey.PreboundLigandConc].ParameterValue = new FloatWithError(2e-6);
        Assert.Equal(10e-6, workspace.GetEditableModelOptions()[AttributeKey.PreboundLigandConc].ParameterValue.Value, 12);
    }

    [Fact]
    public void MissingEnabledAttributesDoNotBreakRebuildAndAreReportedTogetherAtFitLaunch()
    {
        DataManager.Init();
        try
        {
            var first = CreateReadyExperiment("missing-attributes-first.itc", 25);
            var second = CreateReadyExperiment("missing-attributes-second.itc", 25);
            var firstAttachedModel = AttachFittedSolution(first);
            var secondAttachedModel = AttachFittedSolution(second);
            var firstAttachedSolution = first.Solution;
            var secondAttachedSolution = second.Solution;
            DataManager.AddData(first);
            DataManager.AddData(second);
            var workspace = new AnalysisWorkspace();
            workspace.SetModelType(AnalysisModel.CompetitiveBinding);
            workspace.SetGlobalMode(true);
            Assert.True(workspace.TryRebuild());

            var options = workspace.GetEditableModelOptions();
            options[AttributeKey.PreboundLigandConc].BoolValue = true;
            workspace.ReplaceModelOptions(options);
            Assert.True(workspace.IsReady);

            var exception = Assert.Throws<MissingModelOptionAttributesException>(() => workspace.PrepareForSolve(false));
            Assert.Contains(first.Name, exception.Message);
            Assert.Contains(second.Name, exception.Message);
            Assert.Contains(AttributeKey.PreboundLigandConc.GetProperties().Name, exception.Message);
            Assert.Same(firstAttachedModel, first.Model);
            Assert.Same(firstAttachedSolution, first.Solution);
            Assert.Same(secondAttachedModel, second.Model);
            Assert.Same(secondAttachedSolution, second.Solution);
        }
        finally
        {
            DataManager.Init();
        }
    }

    [Fact]
    public void GlobalFitUsesEachExperimentAttributeAfterSharedOptionCopyWithoutChangingEditableTemplate()
    {
        DataManager.Init();
        try
        {
            var first = CreateReadyExperiment("member-attribute-first.itc", 25);
            var second = CreateReadyExperiment("member-attribute-second.itc", 25);
            first.Attributes.Add(ExperimentAttribute.Concentration(AttributeKey.PreboundLigandConc, "", new FloatWithError(12e-6, 1e-6)));
            second.Attributes.Add(ExperimentAttribute.Concentration(AttributeKey.PreboundLigandConc, "", new FloatWithError(28e-6, 2e-6)));
            DataManager.AddData(first);
            DataManager.AddData(second);
            var workspace = new AnalysisWorkspace();
            workspace.SetModelType(AnalysisModel.CompetitiveBinding);
            workspace.SetGlobalMode(true);
            Assert.True(workspace.TryRebuild());

            var options = workspace.GetEditableModelOptions();
            options[AttributeKey.PreboundLigandConc].BoolValue = true;
            options[AttributeKey.PreboundLigandConc].ParameterValue = new FloatWithError(3e-6);
            workspace.ReplaceModelOptions(options);
            workspace.PrepareForSolve(false);

            var members = workspace.Context.GlobalModel.Models.ToDictionary(model => model.Data, model => model);
            Assert.Equal(12e-6, members[first].ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue.Value, 12);
            Assert.Equal(28e-6, members[second].ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue.Value, 12);
            Assert.Equal(3e-6, workspace.GetEditableModelOptions()[AttributeKey.PreboundLigandConc].ParameterValue.Value, 12);
        }
        finally
        {
            DataManager.Init();
        }
    }

    [Fact]
    public void ExplicitSingleExperimentRebuildDoesNotDependOnCurrentSelection()
    {
        DataManager.Init();
        try
        {
            var experiment = CreateReadyExperiment("explicit-workspace-data.itc", 25);
            DataManager.AddData(experiment);
            var workspace = new AnalysisWorkspace();

            Assert.True(workspace.TryRebuild());

            DataManager.SelectIndex(-1);

            Assert.Null(DataManager.Current);
            Assert.True(workspace.TryRebuild(new[] { experiment }));
            Assert.True(workspace.IsReady);
            Assert.Same(experiment, workspace.Context.SingleModel.Data);
            Assert.True(AnalysisBuilder.IsModelAvailable(
                workspace.Session.ModelType,
                isGlobal: false,
                experiments: new[] { experiment }));
        }
        finally
        {
            DataManager.Init();
        }
    }

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
    public void ClampedPreflightPreservesAttachedSolutionUntilCandidateAttach()
    {
        var previousLimits = AppSettings.ParameterLimitSetting;
        DataManager.Init();
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            var experiment = CreateReadyExperiment("failed-preflight.itc", 25);
            var attachedModel = AttachFittedSolution(experiment);
            attachedModel.Parameters.Table[ParameterType.Offset].Update(50001);
            attachedModel.Solution = SolutionInterface.FromModel(
                attachedModel,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            var attachedSolution = experiment.Solution;
            DataManager.AddData(experiment);
            var workspace = new AnalysisWorkspace();

            Assert.True(workspace.TryRebuild());
            var solver = workspace.PrepareForSolve(false);
            Assert.Equal(50000, Assert.IsType<Solver>(solver).Model.Parameters.Table[ParameterType.Offset].Value);

            Assert.NotSame(attachedModel, experiment.Model);
            Assert.Equal(50000, experiment.Model.Parameters.Table[ParameterType.Offset].Value);
            Assert.Equal(50001, attachedModel.Parameters.Table[ParameterType.Offset].Value);
            Assert.NotNull(attachedSolution);
        }
        finally
        {
            DataManager.Init();
            AppSettings.ParameterLimitSetting = previousLimits;
        }
    }

    [Fact]
    public void ClampedPreflightPreservesHistoricalBootstrapAndProfileEvidence()
    {
        var previousLimits = AppSettings.ParameterLimitSetting;
        DataManager.Init();
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            var experiment = CreateOffsetExperiment("historical-evidence.itc", 2000);
            var model = new Offset(experiment);
            model.InitializeParameters(experiment);
            model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None };
            model.Parameters.Table[ParameterType.Offset].Update(60000);
            model.Solution = SolutionInterface.FromModel(model,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));

            var sampleModel = (Offset)model.GenerateSyntheticModel();
            sampleModel.Parameters.Table[ParameterType.Offset].Update(60001.25);
            var sample = SolutionInterface.FromModel(sampleModel,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            model.Solution.SetBootstrapSolutions(new List<SolutionInterface> { sample });
            var profile = new ProfileLikelihoodRunResult(.95,
                ProfileLikelihoodCalibration.UnweightedFCalibratedRss,
                10, 1, 1, 9, 1, 1, SolverAlgorithm.NelderMead, false,
                0, 100, 10, 10, TimeSpan.FromSeconds(1), ErrorEstimationOutcome.Completed,
                Array.Empty<ProfileCoordinateResult>());
            model.Solution.ProfileLikelihoodRun = profile;
            experiment.Model = model;
            var oldSampleValue = sample.Parameters[ParameterType.Offset].Value;
            var oldEstimate = model.Solution.Parameters[ParameterType.Offset].Value;
            DataManager.AddData(experiment);

            var workspace = new AnalysisWorkspace();
            workspace.SetModelType(AnalysisModel.Offset);
            Assert.True(workspace.TryRebuild(new[] { experiment }));
            var solver = workspace.PrepareForSolve(false);
            Assert.Equal(50000, Assert.IsType<Solver>(solver).Model.Parameters.Table[ParameterType.Offset].Value);

            Assert.Same(model, model.Solution.Model);
            Assert.Same(profile, model.Solution.ProfileLikelihoodRun);
            Assert.Same(sample, Assert.Single(model.Solution.BootstrapSolutions));
            Assert.Equal(oldSampleValue, sample.Parameters[ParameterType.Offset].Value);
            Assert.Equal(oldEstimate, model.Solution.Parameters[ParameterType.Offset].Value);
        }
        finally
        {
            DataManager.Init();
            AppSettings.ParameterLimitSetting = previousLimits;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalWorkspaceClampsIndependentOrSharedStartsBeforeAttachment(bool shareOffset)
    {
        var previousLimits = AppSettings.ParameterLimitSetting;
        DataManager.Init();
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            var first = CreateReadyExperiment("workspace-clamp-first.itc", 25);
            var second = CreateReadyExperiment("workspace-clamp-second.itc", 25);
            var firstAttached = AttachFittedSolution(first);
            var secondAttached = AttachFittedSolution(second);
            var firstStart = shareOffset ? 60000 : -60000;
            firstAttached.Parameters.Table[ParameterType.Offset].Update(firstStart);
            firstAttached.Solution.Model.Parameters.Table[ParameterType.Offset].Update(firstStart);
            firstAttached.Solution.Parameters[ParameterType.Offset] = new FloatWithError(firstStart);
            secondAttached.Parameters.Table[ParameterType.Offset].Update(60000);
            secondAttached.Solution.Model.Parameters.Table[ParameterType.Offset].Update(60000);
            secondAttached.Solution.Parameters[ParameterType.Offset] = new FloatWithError(60000);
            DataManager.AddData(first);
            DataManager.AddData(second);
            var workspace = new AnalysisWorkspace();
            workspace.SetGlobalMode(true);
            if (shareOffset)
            {
                workspace.SetConstraint(ParameterType.Offset, VariableConstraint.SameForAll);
                workspace.SetParameterOverride(ParameterType.Offset, 60000, isLocked: false);
            }
            Assert.True(workspace.TryRebuild());

            var solver = Assert.IsType<GlobalSolver>(workspace.PrepareForSolve(false));
            Assert.Equal(shareOffset ? 50000 : -50000,
                solver.Model.Models[0].Parameters.Table[ParameterType.Offset].Value);
            Assert.Equal(50000, solver.Model.Models[1].Parameters.Table[ParameterType.Offset].Value);
            Assert.NotSame(firstAttached, first.Model);
            Assert.NotSame(secondAttached, second.Model);
        }
        finally
        {
            DataManager.Init();
            AppSettings.ParameterLimitSetting = previousLimits;
        }
    }

    [Theory]
    [InlineData(ParameterLimitSetting.Extended, ParameterLimitSetting.Standard, 60000d, 50000d)]
    [InlineData(ParameterLimitSetting.NoLimit, ParameterLimitSetting.Standard, 60000d, 50000d)]
    [InlineData(ParameterLimitSetting.NoLimit, ParameterLimitSetting.Extended, 1200000d, 1000000d)]
    public async System.Threading.Tasks.Task CompletedFitsNormalizeAfterPresetTightening(
        ParameterLimitSetting oldLimit, ParameterLimitSetting newLimit, double start, double bound)
    {
        var previousLimits = AppSettings.ParameterLimitSetting;
        DataManager.Init();
        try
        {
            foreach (var algorithm in new[] { SolverAlgorithm.NelderMead, SolverAlgorithm.LevenbergMarquardt })
            foreach (var mode in new[] { "single", "independent", "shared" })
            foreach (var sign in new[] { -1, 1 })
            {
                AppSettings.ParameterLimitSetting = oldLimit;
                var experiments = Enumerable.Range(0, mode == "single" ? 1 : 2)
                    .Select(i => CreateOffsetExperiment($"preset-{mode}-{i}-{sign}.itc", sign * start)).ToArray();
                foreach (var experiment in experiments)
                {
                    var source = new Offset(experiment);
                    source.InitializeParameters(experiment);
                    source.Parameters.Table[ParameterType.Offset].Update(sign * start);
                    var originalFit = SolverInterface.Initialize(source);
                    originalFit.SolverAlgorithm = algorithm;
                    originalFit.ErrorEstimationMethod = ErrorEstimationMethod.None;
                    originalFit.Silent = true;
                    Assert.True(originalFit.Solve().Success);
                    experiment.Model = source;
                    DataManager.AddData(experiment);
                }
                var oldModels = experiments.Select(e => e.Model).ToArray();
                var oldSolutions = experiments.Select(e => e.Solution).ToArray();
                var oldEstimates = oldSolutions.Select(solution =>
                    solution.Parameters[ParameterType.Offset].Value).ToArray();
                var workspace = new AnalysisWorkspace();
                workspace.SetModelType(AnalysisModel.Offset);
                workspace.SetGlobalMode(mode != "single");
                if (mode == "shared")
                {
                    workspace.SetConstraint(ParameterType.Offset, VariableConstraint.SameForAll);
                    workspace.SetParameterOverride(ParameterType.Offset, sign * start, isLocked: false);
                }
                Assert.True(workspace.TryRebuild(experiments));
                workspace.PersistFittedParameters();
                AppSettings.ParameterLimitSetting = newLimit;
                Assert.True(workspace.TryRebuild(experiments));
                Assert.Equal(mode == "independent" ? 2 : 1, workspace.Context.DetectInitialParameterLimitViolations().Count);
                var solver = workspace.PrepareForSolve(false);
                solver.SolverAlgorithm = algorithm;
                solver.ErrorEstimationMethod = ErrorEstimationMethod.None;
                solver.Silent = true;
                if (mode == "independent")
                {
                    var completion = new System.Threading.Tasks.TaskCompletionSource<SolverConvergence>();
                    EventHandler<SolverConvergence> handler = (sender, result) =>
                    {
                        if (ReferenceEquals(sender, solver)) completion.TrySetResult(result);
                    };
                    SolverInterface.AnalysisFinished += handler;
                    try
                    {
                        solver.Silent = false;
                        solver.Analyze();
                        var completed = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
                        Assert.True(completed.Success);
                    }
                    finally { SolverInterface.AnalysisFinished -= handler; }
                }
                else Assert.True(solver.Solve().Success);

                var members = solver is Solver single ? new[] { single.Model } : ((GlobalSolver)solver).Model.Models.ToArray();
                Assert.All(members, model => Assert.Equal(sign * bound,
                    model.Parameters.Table[ParameterType.Offset].Value, 3));
                for (var i = 0; i < experiments.Length; i++)
                {
                    Assert.Equal(sign * start, oldModels[i].Parameters.Table[ParameterType.Offset].Value, 3);
                    Assert.Equal(oldEstimates[i], oldSolutions[i].Parameters[ParameterType.Offset].Value, 3);
                    Assert.Same(oldSolutions[i], oldModels[i].Solution);
                }
                DataManager.Init();
            }
        }
        finally
        {
            DataManager.Init();
            AppSettings.ParameterLimitSetting = previousLimits;
        }
    }

    static ExperimentData CreateOffsetExperiment(string fileName, double offset)
    {
        var experiment = new ExperimentData(fileName)
        {
            CellConcentration = new FloatWithError(1e-3), SyringeConcentration = new FloatWithError(10e-3),
            CellVolume = 1e-3, MeasuredTemperature = 25, TargetTemperature = 25,
        };
        for (var index = 0; index < 10; index++)
        {
            var injection = new InjectionData(experiment, index, 1e-6,
                experiment.SyringeConcentration * 1e-6, include: true)
            {
                ActualCellConcentration = experiment.CellConcentration,
                ActualTitrantConcentration = 0.25 * (index + 1) * experiment.CellConcentration,
                Ratio = 0.25 * (index + 1), Temperature = 25,
            };
            injection.SetPeakArea(new FloatWithError(offset * injection.InjectionMass, 1e-8));
            experiment.Injections.Add(injection);
        }
        return experiment;
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

            var solver = Assert.IsType<GlobalSolver>(workspace.PrepareForSolve(false));

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

    static FloatWithError PeakAreaWithSd(double sd)
    {
        object boxed = new FloatWithError(-1e-6, 1e-8);
        typeof(FloatWithError)
            .GetProperty(nameof(FloatWithError.SD), BindingFlags.Instance | BindingFlags.Public)
            .SetValue(boxed, sd);
        return (FloatWithError)boxed;
    }
}
