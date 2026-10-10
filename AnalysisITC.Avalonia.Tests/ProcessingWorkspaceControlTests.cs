using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Threading;

using Xunit;

using AnalysisITC.Avalonia.Processing;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProcessingWorkspaceControlTests
{
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
