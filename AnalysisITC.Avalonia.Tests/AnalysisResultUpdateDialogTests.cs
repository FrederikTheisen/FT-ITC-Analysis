using System;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Threading;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Platform;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisResultUpdateDialogTests
{
    public AnalysisResultUpdateDialogTests() => AvaloniaTestBootstrap.EnsureInitialized();

    [Fact]
    public void DialogDefaultsToStoredCountAndOffersOnlyLargerPresets()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedCount: 200);
            var dialog = new AnalysisResultUpdateDialogWindow(result);

            Assert.Equal(200, dialog.EffectiveStoredIterations);
            Assert.Equal(
                new int?[] { null, 500, 1_000, 2_000, 5_000, 10_000 },
                dialog.IterationValues);
            Assert.Equal(0, dialog.IterationCombo.SelectedIndex);
            Assert.Null(dialog.SelectedOptions().BootstrapIterationsOverride);

            dialog.IterationCombo.SelectedIndex = 2;
            Assert.Equal(1_000, dialog.SelectedOptions().BootstrapIterationsOverride);
        });
    }

    [Fact]
    public void WorkspaceUpdateStopsWhenBootstrapDialogIsCancelled()
    {
        var previous = PlatformServices.AnalysisResultUpdatePromptService;
        var prompt = new CancellingPromptService();
        try
        {
            PlatformServices.RegisterAnalysisResultUpdatePromptService(prompt);
            Dispatcher.UIThread.Invoke(() =>
            {
                var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedCount: 100);
                var originalSolution = result.Solution;
                var workspace = new AnalysisResultWorkspaceControl { Result = result };

                workspace.UpdateResultAsync().GetAwaiter().GetResult();

                Assert.Equal(1, prompt.CallCount);
                Assert.Same(originalSolution, result.Solution);
            });
        }
        finally
        {
            PlatformServices.RegisterAnalysisResultUpdatePromptService(previous);
        }
    }

    [Fact]
    public void SelectionUpdateUsesTheSameBootstrapDialog()
    {
        var previous = PlatformServices.AnalysisResultUpdatePromptService;
        var prompt = new CancellingPromptService();
        try
        {
            PlatformServices.RegisterAnalysisResultUpdatePromptService(prompt);
            Dispatcher.UIThread.Invoke(() =>
            {
                DataManager.Clear(DataClearMode.ResetSession);
                var result = CreateResult(ErrorEstimationMethod.BootstrapResiduals, retainedCount: 100);
                DataManager.AddData(result);
                var window = new MainWindow();
                window.Show();
                try
                {
                    window.CreateDataListItemMenu(window.DataListEntries[0]);
                    window.UpdateSelectedResultAsync().GetAwaiter().GetResult();
                    Assert.Equal(1, prompt.CallCount);
                }
                finally
                {
                    window.Close();
                    DataManager.Clear(DataClearMode.ResetSession);
                }
            });
        }
        finally
        {
            PlatformServices.RegisterAnalysisResultUpdatePromptService(previous);
        }
    }

    [Fact]
    public void NonBootstrapResultsDoNotAdvertiseIterationOverrides()
    {
        Assert.False(AnalysisResultUpdater.CanOverrideBootstrapIterations(
            CreateResult(ErrorEstimationMethod.None, retainedCount: 0)));
        Assert.False(AnalysisResultUpdater.CanOverrideBootstrapIterations(
            CreateResult(ErrorEstimationMethod.LeaveOneOut, retainedCount: 0)));
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.None)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut)]
    public void NonBootstrapWorkspaceUpdatesBypassThePrompt(ErrorEstimationMethod method)
    {
        var previous = PlatformServices.AnalysisResultUpdatePromptService;
        var prompt = new CancellingPromptService();
        try
        {
            PlatformServices.RegisterAnalysisResultUpdatePromptService(prompt);
            Dispatcher.UIThread.Invoke(() =>
            {
                DataManager.Clear(DataClearMode.ResetSession);
                var result = CreateResult(method, retainedCount: 0);
                var original = result.Solution;
                var workspace = new AnalysisResultWorkspaceControl { Result = result };

                // No current experiment is loaded, so the updater returns through its
                // existing handled-failure path without running a fit.
                workspace.UpdateResultAsync().GetAwaiter().GetResult();

                Assert.Equal(0, prompt.CallCount);
                Assert.Same(original, result.Solution);
            });
        }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            PlatformServices.RegisterAnalysisResultUpdatePromptService(previous);
        }
    }

    static AnalysisResult CreateResult(ErrorEstimationMethod method, int retainedCount)
    {
        var experiment = new ExperimentData("update-dialog.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
        };
        experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
        });

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = method };
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        model.Solution.ErrorMethod = method;

        var solver = new Solver { Model = model, ErrorEstimationMethod = method };
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(solver));
        for (var index = 0; index < retainedCount; index++)
            result.Solution.BootstrapSolutions.Add(result.Solution);
        return result;
    }

    sealed class CancellingPromptService : IAnalysisResultUpdatePromptService
    {
        public int CallCount { get; private set; }

        public Task<AnalysisResultUpdateOptions> ChooseOptionsAsync(AnalysisResult result)
        {
            CallCount++;
            return Task.FromResult<AnalysisResultUpdateOptions>(null!);
        }
    }
}
