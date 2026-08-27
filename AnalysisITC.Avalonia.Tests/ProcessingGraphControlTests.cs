using System.Collections.Generic;

using Avalonia;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Processing;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProcessingGraphControlTests
{
    public ProcessingGraphControlTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void RightClickInsideIntegrationRegionIsSplineInsertionTarget()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var graph = new ProcessingGraphControl { Experiment = CreateSplineExperiment() };
            graph.Measure(new Size(600, 400));
            graph.Arrange(new Rect(0, 0, 600, 400));

            Assert.True(graph.CanInsertSplinePointAt(new Point(300, 200)));
        });
    }

    static ExperimentData CreateSplineExperiment()
    {
        var experiment = new ExperimentData("spline-context-menu.itc")
        {
            DataPoints = new List<DataPoint>(),
        };
        for (var time = 0; time <= 100; time++)
            experiment.DataPoints.Add(new DataPoint(time, time));

        var injection = new InjectionData(experiment, 1e-6f, 100, 0, 1)
        {
            Time = 0,
        };
        experiment.Injections.Add(injection);
        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        return experiment;
    }
}
