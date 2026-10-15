using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Platform;
using AnalysisITC.Platform.Avalonia;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ResultUpdateFeedbackTests
{
    [AvaloniaFact]
    public void MissingCompletionDoesNotCrashTheApplicationProgressHandler()
    {
        using var coordinator = new AnalysisProgressCoordinator();
        var solver = new SolverInterface();
        solver.Analyze();
        try
        {
            solver.ReportAnalysisFinished(null!);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(-1, StatusBarManager.Progress);
        }
        finally { StatusBarManager.ClearAppStatus(); }
    }

    [AvaloniaTheory]
    [InlineData("accepted")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    [InlineData("preparation-failed")]
    public async Task WorkspaceReportsWhetherTheProfileResultWasReplaced(string outcome)
    {
        var previousDispatcher = PlatformServices.MainThreadDispatcher;
        PlatformServices.RegisterMainThreadDispatcher(new AvaloniaMainThreadDispatcher());
        DataManager.Clear(DataClearMode.ResetSession);
        GlobalModelFactory.ClearPreviousParameters();
        StatusBarManager.ClearAppStatus();
        var result = CreateResult();
        if (outcome != "preparation-failed") DataManager.AddData(result.Solution.Solutions[0].Data);
        DataManager.AddData(result);
        if (outcome != "preparation-failed") DataManager.LoadResultSolutionsToExperiments(result);
        DocumentDirtyTracker.MarkClean();
        var stamp = DocumentDirtyTracker.CaptureSaveStamp();
        var original = result.Solution;
        var workspace = new AnalysisResultWorkspaceControl { Result = result };
        var replaced = 0;
        workspace.ResultUpdated += (_, _) => replaced++;
        var statuses = new List<string>();
        EventHandler<string> status = (_, message) => statuses.Add(message);
        EventHandler<SolverConvergence> complete = (_, convergence) =>
        {
            if (outcome == "cancelled") convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.Cancelled);
            if (outcome == "failed") convergence.MarkInvalidValues("Controlled primary failure");
        };
        using var coordinator = new AnalysisProgressCoordinator();
        StatusBarManager.StatusUpdated += status;
        SolverInterface.AnalysisFinished += complete;
        try
        {
            await workspace.UpdateResultAsync();
            Dispatcher.UIThread.RunJobs();

            if (outcome == "accepted")
            {
                Assert.NotSame(original, result.Solution);
                Assert.Equal(1, replaced);
                Assert.Contains(statuses, text => text.Contains("RMSD"));
            }
            else
            {
                Assert.Same(original, result.Solution);
                Assert.Equal(0, replaced);
                Assert.Equal(stamp, DocumentDirtyTracker.CaptureSaveStamp());
                Assert.Contains(statuses, text => outcome == "cancelled"
                    ? text.StartsWith("Profile result retained:") && text.Contains("cancelled")
                    : text.StartsWith("Result update failed:") && (outcome == "preparation-failed" || text.Contains("Controlled primary failure")));
                Assert.DoesNotContain(statuses, text => text.Contains("RMSD"));
            }
        }
        finally
        {
            StatusBarManager.StatusUpdated -= status;
            SolverInterface.AnalysisFinished -= complete;
            DataManager.Clear(DataClearMode.ResetSession);
            GlobalModelFactory.ClearPreviousParameters();
            DocumentDirtyTracker.MarkClean();
            StatusBarManager.ClearAppStatus();
            PlatformServices.RegisterMainThreadDispatcher(previousDispatcher);
        }
    }

    static AnalysisResult CreateResult()
    {
        var data = new ExperimentData("update-feedback.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };
        for (var index = 0; index < 8; index++)
        {
            var injection = new InjectionData(data, volume: 1e-6) { IsIntegrated = true, Ratio = index + 1 };
            injection.SetPeakArea(new FloatWithError(2e-7 + 1e-8 * Math.Sin(index + 1), 1e-8));
            data.Injections.Add(injection);
        }
        var model = new Offset(data);
        model.InitializeParameters(data);
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood };
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
        model.Solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(
            new Solver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood }));
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        return result;
    }
}
