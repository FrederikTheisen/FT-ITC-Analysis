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
    public ProcessingWorkspaceControlTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void PolynomialBaselineUsesDirectIntegerDegreeStepper()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment(BaselineInterpolatorTypes.Polynomial);
            var workspace = new ProcessingWorkspaceControl { Experiment = experiment };

            Assert.Equal(0m, workspace.DegreeStepper.Minimum);
            Assert.Equal(32m, workspace.DegreeStepper.Maximum);
            Assert.Equal(1m, workspace.DegreeStepper.Increment);
            Assert.Equal(12m, workspace.DegreeStepper.Value);
        });
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
            Assert.Equal(32m, workspace.DegreeStepper.Maximum);
        });
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
