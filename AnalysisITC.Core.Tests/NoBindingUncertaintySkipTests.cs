using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests;

// ITC-052: uncertainty estimation is skipped for fits assessed as no binding.
[Collection(SolverEventCollectionDefinition.Name)]
public sealed partial class NoBindingUncertaintySkipTests : IDisposable
{
    const int Iterations = 4;

    readonly bool previousSkip = AnalysisITC.Core.Application.AppSettings.SkipUncertaintyEstimationForNoBinding;
    readonly int previousParallelism = AnalysisITC.Core.Application.AppSettings.MaxDegreeOfParallelism;
    readonly int previousIterations = FittingOptionsController.BootstrapIterations;

    public NoBindingUncertaintySkipTests()
    {
        AnalysisITC.Core.Application.AppSettings.SkipUncertaintyEstimationForNoBinding = true;
        AnalysisITC.Core.Application.AppSettings.MaxDegreeOfParallelism = 1;
        FittingOptionsController.BootstrapIterations = Iterations;
    }

    public void Dispose()
    {
        AnalysisITC.Core.Application.AppSettings.SkipUncertaintyEstimationForNoBinding = previousSkip;
        AnalysisITC.Core.Application.AppSettings.MaxDegreeOfParallelism = previousParallelism;
        FittingOptionsController.BootstrapIterations = previousIterations;
        SolverInterface.TerminateAnalysisFlag.Lower();
    }

    [Fact]
    public async Task SingleFitAssessedAsNoBindingSkipsBootstrap()
    {
        var model = FlatModel();

        var convergence = await Run(new Solver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals, BootstrapIterations = Iterations, CanCreateAnalysisResult = false });

        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected,
            BindingAssessmentState.FromComparison(model.Solution.NullComparison).AutomaticOutcome);
        Assert.Equal(ErrorEstimationOutcome.SkippedNoBinding, convergence.ErrorEstimationOutcome);
        Assert.Equal("Not estimated (no binding detected)", convergence.ErrorEstimationSummary);
        Assert.Empty(model.Solution.BootstrapSolutions);
        Assert.Equal(ErrorEstimationMethod.BootstrapResiduals, model.Solution.ErrorMethod);
    }

    [Fact]
    public async Task SkippingCanBeTurnedOff()
    {
        var model = FlatModel();

        var convergence = await Run(new Solver
        {
            Model = model, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals, BootstrapIterations = Iterations,
            CanCreateAnalysisResult = false, SkipNoBindingUncertainty = false,
        });

        Assert.NotEqual(ErrorEstimationOutcome.SkippedNoBinding, convergence.ErrorEstimationOutcome);
        Assert.NotEmpty(model.Solution.BootstrapSolutions);
        Assert.NotNull(model.Solution.NullComparison);
    }

    [Fact]
    public async Task BindingFitIsStillEstimated()
    {
        var model = BindingModel();

        var convergence = await Run(new Solver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals, BootstrapIterations = Iterations, CanCreateAnalysisResult = false });

        Assert.Equal(BindingAssessmentOutcome.BindingDetected,
            BindingAssessmentState.FromComparison(model.Solution.NullComparison).AutomaticOutcome);
        Assert.NotEqual(ErrorEstimationOutcome.SkippedNoBinding, convergence.ErrorEstimationOutcome);
        Assert.NotEmpty(model.Solution.BootstrapSolutions);
    }

    [Fact]
    public async Task IndependentCollectionSkipsOnlyMembersAssessedAsNoBinding()
    {
        var binding = BindingModel();
        var flat = FlatModel();
        var global = Independent(binding, flat);

        var convergence = await Run(new GlobalSolver { Model = global, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals, BootstrapIterations = Iterations, CanCreateAnalysisResult = false });

        Assert.NotEmpty(binding.Solution.BootstrapSolutions);
        Assert.Empty(flat.Solution.BootstrapSolutions);
        Assert.Equal(ErrorEstimationOutcome.SkippedNoBinding, flat.Solution.Convergence.ErrorEstimationOutcome);
        Assert.NotEqual(ErrorEstimationOutcome.SkippedNoBinding, binding.Solution.Convergence.ErrorEstimationOutcome);
        Assert.NotEqual(ErrorEstimationOutcome.SkippedNoBinding, convergence.ErrorEstimationOutcome);
        // The comparison calculated before estimation is kept on the final solution.
        Assert.NotNull(global.Solution.NullComparison);

        var result = new AnalysisResult(global.Solution);
        Assert.True(result.IsIndependentAssessmentCollection);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected,
            result.GetMemberBindingAssessment(flat.Solution).EffectiveOutcome);
        Assert.Equal(BindingAssessmentOutcome.BindingDetected,
            result.GetMemberBindingAssessment(binding.Solution).EffectiveOutcome);
        Assert.DoesNotContain(result.ValidityReport.Reasons, reason => reason.Contains(AnalysisResult.SkippedUncertaintyReason));
        Assert.Equal(Iterations.ToString(), UncertaintyEstimationPresentation.BootstrapIterations(global.Solution));
    }

    [Fact]
    public async Task ManualBindingAssessmentEstimatesAMemberThatWouldBeSkipped()
    {
        var binding = BindingModel();
        var flat = FlatModel();
        var global = Independent(binding, flat);

        await Run(new GlobalSolver
        {
            Model = global, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals, BootstrapIterations = Iterations, CanCreateAnalysisResult = false,
            ManualMemberAssessmentOverrides = new Dictionary<string, BindingAssessmentOutcome>
            {
                [flat.Data.UniqueID] = BindingAssessmentOutcome.BindingDetected,
            },
        });

        Assert.NotEmpty(flat.Solution.BootstrapSolutions);
        Assert.NotEqual(ErrorEstimationOutcome.SkippedNoBinding, flat.Solution.Convergence.ErrorEstimationOutcome);
    }

    [Fact]
    public async Task ManualBindingOnASkippedMemberMakesTheResultStale()
    {
        var binding = BindingModel();
        var flat = FlatModel();
        var global = Independent(binding, flat);
        await Run(new GlobalSolver { Model = global, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals, BootstrapIterations = Iterations, CanCreateAnalysisResult = false });
        var result = new AnalysisResult(global.Solution);
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);

        result.SetMemberBindingAssessmentOverride(flat.Solution.Guid, BindingAssessmentOutcome.BindingDetected);

        Assert.Equal(AnalysisResultValidity.PartialInvalid, result.ValidityReport.Status);
        Assert.Equal(flat.Data.Name + ": " + AnalysisResult.SkippedUncertaintyReason, Assert.Single(result.ValidityReport.Reasons));

        result.SetMemberBindingAssessmentOverride(flat.Solution.Guid, BindingAssessmentOutcome.NoBindingDetected);

        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
    }

    [Fact]
    public async Task SkippedOutcomeRoundTripsThroughTheProjectFile()
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc"));
        var containers = await FTITCReader.ReadStream(source);
        var result = Assert.Single(containers.OfType<AnalysisResult>());
        var member = result.Solution.Solutions[0];
        member.Convergence.MarkErrorEstimationSkippedForNoBinding();
        // The legacy fixture holds a separate copy of the member as the experiment's attached solution.
        foreach (var attached in containers.OfType<ExperimentData>().Select(experiment => experiment.Solution)
                     .Where(solution => solution?.Guid == member.Guid))
            attached.Convergence.MarkErrorEstimationSkippedForNoBinding();

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), new[] { result });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());

        var restoredMember = restored.Solution.Solutions.Single(item => item.Data.UniqueID == member.Data.UniqueID);
        Assert.Equal(ErrorEstimationOutcome.SkippedNoBinding, restoredMember.Convergence.ErrorEstimationOutcome);
        Assert.Equal("Not estimated (no binding detected)", restoredMember.Convergence.ErrorEstimationSummary);
    }

    [Theory]
    [InlineData(ErrorEstimationOutcome.SkippedNoBinding, ErrorEstimationOutcome.SkippedNoBinding, ErrorEstimationOutcome.SkippedNoBinding)]
    [InlineData(ErrorEstimationOutcome.SkippedNoBinding, ErrorEstimationOutcome.Completed, ErrorEstimationOutcome.Completed)]
    [InlineData(ErrorEstimationOutcome.SkippedNoBinding, ErrorEstimationOutcome.NotRun, ErrorEstimationOutcome.SkippedNoBinding)]
    [InlineData(ErrorEstimationOutcome.SkippedNoBinding, ErrorEstimationOutcome.CompleteFailure, ErrorEstimationOutcome.PartialFailure)]
    public void MultiExperimentOutcomeTreatsSkippedMembersAsNotEstimated(
        ErrorEstimationOutcome first, ErrorEstimationOutcome second, ErrorEstimationOutcome expected)
    {
        var convergence = SolverConvergence.FromMultiExperimentAnalysis(new List<SolverConvergence>
        {
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { ErrorEstimationOutcome = first }),
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { ErrorEstimationOutcome = second }),
        });

        Assert.Equal(expected, convergence.ErrorEstimationOutcome);
    }

    static Model BindingModel() => InjectionProcessingMethodTests.FittedModel();

    /// <summary>The binding reference with every heat replaced by a nearly constant value.</summary>
    static Model FlatModel()
    {
        var model = InjectionProcessingMethodTests.FittedModel();
        var mean = model.Data.Injections.Average(injection => injection.PeakArea.Value);
        foreach (var injection in model.Data.Injections)
            injection.SetPeakArea(new FloatWithError(mean * (1 + 0.01 * Math.Sin(injection.ID)), injection.PeakArea.SD));
        model.Data.SetID("flat-" + model.Data.UniqueID);
        model.Data.Name = "Flat";
        return model;
    }

    static GlobalModel Independent(params Model[] members)
    {
        var global = new GlobalModel(members.ToList())
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        foreach (var member in members) global.Parameters.AddIndivdualParameter(member.Parameters);
        return global;
    }

    static async Task<SolverConvergence> Run(SolverInterface solver)
    {
        var finished = new TaskCompletionSource<SolverConvergence>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<SolverConvergence> handler = (sender, value) =>
        {
            if (ReferenceEquals(sender, solver)) finished.TrySetResult(value);
        };
        SolverInterface.AnalysisFinished += handler;
        try
        {
            SolverInterface.TerminateAnalysisFlag.Lower();
            solver.Analyze();
            return await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            SolverInterface.AnalysisFinished -= handler;
            SolverInterface.TerminateAnalysisFlag.Lower();
        }
    }
}
