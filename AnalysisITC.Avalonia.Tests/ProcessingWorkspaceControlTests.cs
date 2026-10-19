using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.LogicalTree;

using Xunit;

using AnalysisITC.Avalonia.Processing;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProcessingWorkspaceControlTests : IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(WaitForIdle());
    public ValueTask DisposeAsync() => new(WaitForIdle());
    static async Task WaitForIdle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (ProcessingActivity.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(1);
        Assert.False(ProcessingActivity.IsBusy);
    }

    [AvaloniaFact]
    public void PolynomialBaselineUsesDirectIntegerDegreeStepper()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment(BaselineInterpolatorTypes.Polynomial);
            var workspace = new ProcessingWorkspaceControl { Experiment = experiment };

            Assert.Equal(0m, workspace.DegreeStepper.Minimum);
            Assert.Equal(24m, workspace.DegreeStepper.Maximum);
            Assert.Equal(1m, workspace.DegreeStepper.Increment);
            Assert.Equal(12m, workspace.DegreeStepper.Value);
        });
    }

    [AvaloniaFact]
    public void PolynomialStepperAcceptsArbitraryIntegerDegree()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment(BaselineInterpolatorTypes.Polynomial);
            var workspace = new ProcessingWorkspaceControl { Experiment = experiment };

            workspace.DegreeStepper.Value = 7;

            var polynomial = Assert.IsType<PolynomialLeastSquaresInterpolator>(experiment.Processor.Interpolator);
            Assert.Equal(7, polynomial.Degree);
        });
    }

    [AvaloniaFact]
    public void SegmentedBaselineUsesSameStepperWithItsDegreeRange()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var workspace = new ProcessingWorkspaceControl
            {
                Experiment = CreateExperiment(BaselineInterpolatorTypes.Polynomial)
            };
            var stepper = workspace.DegreeStepper;

            workspace.Experiment = CreateExperiment(BaselineInterpolatorTypes.Segmented);

            Assert.Same(stepper, workspace.DegreeStepper);
            Assert.Equal((decimal)SegmentedBaselineInterpolator.MinimumDegree, stepper.Minimum);
            Assert.Equal((decimal)SegmentedBaselineInterpolator.MaximumDegree, stepper.Maximum);
            Assert.Equal(1m, stepper.Value);
        });
    }

    [AvaloniaFact]
    public void ControlSynchronizationDoesNotStartProcessing()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var statuses = new List<string>();
            var workspace = new ProcessingWorkspaceControl();
            workspace.StatusChanged += (_, status) => statuses.Add(status);

            workspace.Experiment = CreateExperiment(BaselineInterpolatorTypes.Polynomial);
            workspace.Experiment = CreateExperiment(BaselineInterpolatorTypes.Segmented);

            Assert.DoesNotContain("Processing data...", statuses);
        });
    }

    [AvaloniaFact]
    public void LegacyPolynomialDegreeOutsideCuratedStopsIsPreserved()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment(BaselineInterpolatorTypes.Polynomial);
            var polynomial = Assert.IsType<PolynomialLeastSquaresInterpolator>(experiment.Processor.Interpolator);
            polynomial.Degree = 5;

            var workspace = new ProcessingWorkspaceControl { Experiment = experiment };

            Assert.Equal(5, polynomial.Degree);
            Assert.Equal(5m, workspace.DegreeStepper.Value);
            Assert.Equal(24m, workspace.DegreeStepper.Maximum);
        });
    }

    [AvaloniaFact]
    public void ProcessingNotificationsResumeAfterWorkspaceIsReattached()
    {
        var experiment = CreateExperiment(BaselineInterpolatorTypes.Polynomial);
        typeof(DataProcessor)
            .GetProperty(nameof(DataProcessor.BaselineCompleted))!
            .SetValue(experiment.Processor, true);

        Dispatcher.UIThread.Invoke(() =>
        {
            var workspace = new ProcessingWorkspaceControl { Experiment = experiment };
            var window = new Window { Content = workspace };
            var notificationCount = 0;
            workspace.ProcessingChanged += (_, _) => notificationCount++;
            window.Show();

            try
            {
                experiment.UpdateProcessing(invalidate: false);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, notificationCount);

                window.Content = new Border();
                Dispatcher.UIThread.RunJobs();
                window.Content = workspace;
                Dispatcher.UIThread.RunJobs();

                experiment.UpdateProcessing(invalidate: false);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, notificationCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [AvaloniaFact]
    public void CopyToNextReplacesFitPeaksRecord()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateTwoInjectionExperiment();
            var workspace = new ProcessingWorkspaceControl { Experiment = experiment };
            experiment.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Fit;
            experiment.Injections[0].SetIntegrationLengthByTime(40);
            Graph(workspace).SelectedInjectionIndex = 0;

            _ = Invoke(workspace, "CopySelectedIntegrationToNextAsync");

            Assert.Equal(40f, experiment.Injections[1].IntegrationEndOffset);
            Assert.Equal(InjectionData.IntegrationLengthMode.Time, experiment.Processor.IntegrationLengthMode);
        });
    }

    [AvaloniaTheory]
    [InlineData("ChangeIntegrationLengthAsync", InjectionData.IntegrationLengthMode.Time)]
    [InlineData("ChangeIntegrationStartAsync", InjectionData.IntegrationLengthMode.Fit)]
    public void OnlyLengthSliderReplacesFitPeaksRecord(string handler, InjectionData.IntegrationLengthMode expected)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateTwoInjectionExperiment();
            var workspace = new ProcessingWorkspaceControl { Experiment = experiment };
            experiment.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Fit;

            _ = Invoke(workspace, handler);

            Assert.Equal(expected, experiment.Processor.IntegrationLengthMode);
        });
    }

    [AvaloniaFact]
    public void ProcessingControlsExplainBusyStateAndRestoreTheirEligibility()
    {
        var experiment = CreateExperiment(BaselineInterpolatorTypes.Polynomial);
        var workspace = new ProcessingWorkspaceControl { Experiment = experiment };
        var window = new Window { Content = workspace };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var copyButtons = workspace.GetLogicalDescendants().OfType<Button>()
                .Where(button => Equals(button.Content, "Active") || Equals(button.Content, "New")).ToArray();
            Assert.Equal(2, copyButtons.Length);
            Assert.Equal(ProcessingCopyPresentation.ActiveTooltip,
                ToolTip.GetTip(copyButtons.Single(button => Equals(button.Content, "Active"))));
            Assert.Equal(ProcessingCopyPresentation.NewTooltip,
                ToolTip.GetTip(copyButtons.Single(button => Equals(button.Content, "New"))));
            var copyTooltips = copyButtons.Select(ToolTip.GetTip).ToArray();
            using (var lease = ProcessingActivity.TryBegin()!)
            {
                Dispatcher.UIThread.RunJobs();
                Assert.False(workspace.DegreeStepper.IsEffectivelyEnabled);
                Assert.Equal(FittingStatusPresentation.BusyTooltip, ToolTip.GetTip(workspace.DegreeStepper));
                Assert.All(copyButtons, button =>
                {
                    Assert.False(button.IsEffectivelyEnabled);
                    Assert.Equal(FittingStatusPresentation.BusyTooltip, ToolTip.GetTip(button));
                });
                workspace.DegreeStepper.Value = 7;
                Assert.Equal(12, ((PolynomialLeastSquaresInterpolator)experiment.Processor.Interpolator).Degree);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(workspace.DegreeStepper.IsEffectivelyEnabled);
            Assert.Equal(12m, workspace.DegreeStepper.Value);
            Assert.NotEqual(FittingStatusPresentation.BusyTooltip, ToolTip.GetTip(workspace.DegreeStepper));
            Assert.All(copyButtons, button => Assert.True(button.IsEffectivelyEnabled));
            Assert.Equal(copyTooltips, copyButtons.Select(ToolTip.GetTip).ToArray());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UnrelatedCalculationKeepsViewAndFutureCopyOptionsAvailable()
    {
        var previousCopyStart = AppSettings.IntegrationRegionCopyIncludesStart;
        var experiment = CreateTwoInjectionExperiment();
        var spline = (SplineInterpolator)experiment.Processor.Interpolator;
        spline.Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Smooth;
        var workspace = new ProcessingWorkspaceControl { Experiment = experiment };
        var window = new Window { Content = workspace };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var checks = workspace.GetLogicalDescendants().OfType<CheckBox>().ToArray();
            var handles = checks.Single(check => Equals(check.Content, "Show spline handles"));
            var timeDragging = checks.Single(check => Equals(check.Content, "Move spline points in time"));
            var copyStart = checks.Single(check => Equals(check.Content, "Copy start time to next"));
            var discard = checks.Single(check => Equals(check.Content, "Discard integrated regions"));
            var options = new[] { handles, timeDragging, copyStart };
            var tips = options.Select(ToolTip.GetTip).ToArray();
            var oldHandles = spline.ShowHandles;
            var oldTimeDragging = spline.AllowPointTimeDragging;
            var oldDiscard = experiment.Processor.DiscardIntegratedPoints;
            using (var lease = ProcessingActivity.TryBegin()!)
            {
                Dispatcher.UIThread.RunJobs();
                for (var index = 0; index < options.Length; index++)
                {
                    Assert.True(options[index].IsEffectivelyEnabled);
                    Assert.Equal(tips[index], ToolTip.GetTip(options[index]));
                    Assert.NotEqual(FittingStatusPresentation.BusyTooltip, ToolTip.GetTip(options[index]));
                }
                handles.IsChecked = !oldHandles;
                timeDragging.IsChecked = !oldTimeDragging;
                copyStart.IsChecked = !previousCopyStart;
                Assert.Equal(!oldHandles, spline.ShowHandles);
                Assert.Equal(!oldTimeDragging, spline.AllowPointTimeDragging);
                Assert.Equal(!previousCopyStart, AppSettings.IntegrationRegionCopyIncludesStart);
                Assert.False(discard.IsEffectivelyEnabled);
                Assert.False(Graph(workspace).IsEditingEnabled);
                discard.IsChecked = !oldDiscard;
                Assert.Equal(oldDiscard, experiment.Processor.DiscardIntegratedPoints);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(discard.IsEffectivelyEnabled);
            Assert.True(Graph(workspace).IsEditingEnabled);
        }
        finally
        {
            window.Close();
            AppSettings.IntegrationRegionCopyIncludesStart = previousCopyStart;
            AppSettings.Save();
        }
    }

    static ProcessingGraphControl Graph(ProcessingWorkspaceControl workspace) =>
        (ProcessingGraphControl)typeof(ProcessingWorkspaceControl)
            .GetField("graph", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(workspace)!;

    static object? Invoke(ProcessingWorkspaceControl workspace, string method) =>
        typeof(ProcessingWorkspaceControl)
            .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(workspace, null);

    static ExperimentData CreateTwoInjectionExperiment()
    {
        var experiment = new ExperimentData("fit-peaks-record.itc") { DataPoints = new List<DataPoint>() };
        for (var time = 0; time <= 200; time++)
            experiment.DataPoints.Add(new DataPoint(time, 0));
        experiment.Injections.Add(new InjectionData(experiment, 1e-6f, 100, 0, 1) { Time = 0 });
        experiment.Injections.Add(new InjectionData(experiment, 1e-6f, 100, 1, 1) { Time = 100 });
        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        return experiment;
    }

    static ExperimentData CreateExperiment(BaselineInterpolatorTypes baselineType)
    {
        var experiment = new ExperimentData("degree-stepper-test.itc")
        {
            DataPoints = new List<DataPoint>
            {
                new(0, 1),
                new(1, 2),
                new(2, 3),
            }
        };
        experiment.Processor.InitializeBaseline(baselineType);
        return experiment;
    }
}
