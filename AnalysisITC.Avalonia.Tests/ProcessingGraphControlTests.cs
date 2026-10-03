using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

    [Fact]
    public void UnlockMenuReleasesConvertedSmoothSlopeAndKeepsConvertedPoints()
    {
        var experiment = Task.Run(async () =>
        {
            var result = CreateSplineExperiment();
            result.Processor.InitializeBaseline(BaselineInterpolatorTypes.Segmented);
            await result.Processor.ProcessData(showProgress: false);
            var conversionTarget = SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm;
            SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Smooth;
            try
            {
                await result.Processor.Interpolator.ConvertToSplineAsync(4, showProgress: false);
            }
            finally
            {
                SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm = conversionTarget;
            }
            return result;
        }).GetAwaiter().GetResult();
        var spline = Assert.IsType<SplineInterpolator>(experiment.Processor.Interpolator);
        Assert.True(experiment.Processor.IsLocked);
        Assert.All(spline.SplinePoints, point => Assert.True(point.SlopeLocked));
        experiment.Processor.Unlock();
        var pointsBefore = spline.SplinePoints.Select(point => (point.ID, point.Time, point.Power)).ToArray();
        var referencesBefore = spline.SplinePoints.ToArray();
        var pointIndex = spline.SplinePoints.Count / 2;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DataProcessor.ProcessingCompleted += OnCompleted;
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                var graph = new ProcessingGraphControl { Experiment = experiment };
                var menu = (ContextMenu)typeof(ProcessingGraphControl)
                    .GetMethod("CreateSplinePointContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(graph, new object[] { spline, pointIndex })!;
                var unlock = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString() == "Unlock");
                unlock.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, unlock));
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (!completion.Task.IsCompleted && timeout.Elapsed < System.TimeSpan.FromSeconds(30))
                {
                    Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(5);
                }
            });

            Assert.True(completion.Task.IsCompleted, "Processing did not complete after invoking Unlock.");
        }
        finally
        {
            DataProcessor.ProcessingCompleted -= OnCompleted;
        }

        Assert.Equal(pointsBefore.Length, spline.SplinePoints.Count);
        for (var i = 0; i < pointsBefore.Length; i++)
        {
            Assert.Same(referencesBefore[i], spline.SplinePoints[i]);
            Assert.Equal(pointsBefore[i].ID, spline.SplinePoints[i].ID);
            Assert.Equal(pointsBefore[i].Time, spline.SplinePoints[i].Time);
            Assert.Equal(pointsBefore[i].Power, spline.SplinePoints[i].Power);
        }
        Assert.False(spline.SplinePoints[pointIndex].Locked);
        Assert.False(spline.SplinePoints[pointIndex].SlopeLocked);
        Assert.NotEmpty(spline.Baseline);
        Assert.True(experiment.Processor.IntegrationCompleted);
        return;

        void OnCompleted(object? sender, System.EventArgs args)
        {
            if (ReferenceEquals(sender, experiment)) completion.TrySetResult(true);
        }
    }

    [Fact]
    public void UnlockMenuIsShownForSlopeOnlyAndCannotEditProcessingLockedExperiment()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateSplineExperiment();
            var spline = Assert.IsType<SplineInterpolator>(experiment.Processor.Interpolator);
            spline.SetSplinePoints(new List<SplineInterpolator.SplinePoint>
            {
                new(0, 0, 0), new(50, 50, 1), new(100, 100, 2),
            });
            var point = spline.SplinePoints[0];
            point.LockSlope();
            Assert.False(point.Locked);
            Assert.True(point.SlopeLocked);
            var graph = new ProcessingGraphControl { Experiment = experiment };
            var method = typeof(ProcessingGraphControl).GetMethod("CreateSplinePointContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var menu = (ContextMenu)method.Invoke(graph, new object[] { spline, 0 })!;
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString() == "Unlock");

            experiment.Processor.Lock();
            menu = (ContextMenu)method.Invoke(graph, new object[] { spline, 0 })!;
            menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Unlock")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(point.SlopeLocked);
            Assert.True(experiment.Processor.IsLocked);
        });
    }

    [Theory]
    [InlineData(false, false, "Lock")]
    [InlineData(true, false, "Unlock")]
    [InlineData(false, true, "Unlock")]
    [InlineData(true, true, "Unlock")]
    public void PointMenuChoosesLockOrUnlockFromEitherLock(bool locked, bool slopeLocked, string expected)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateSplineExperiment();
            var spline = Assert.IsType<SplineInterpolator>(experiment.Processor.Interpolator);
            spline.SetSplinePoints(new List<SplineInterpolator.SplinePoint> { new(0, 0, 0), new(100, 100, 1) });
            spline.SplinePoints[0].Locked = locked;
            spline.SplinePoints[0].SlopeLocked = slopeLocked;
            var graph = new ProcessingGraphControl { Experiment = experiment };
            var menu = (ContextMenu)typeof(ProcessingGraphControl)
                .GetMethod("CreateSplinePointContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(graph, new object[] { spline, 0 })!;
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString() == expected);
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
