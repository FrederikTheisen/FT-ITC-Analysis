using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Processing;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProcessingGraphControlTests
{
    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaTheory]
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

    [AvaloniaTheory]
    [InlineData("IntegrationEnd", 50f, InjectionData.IntegrationLengthMode.Time)]
    [InlineData("IntegrationStart", 10f, InjectionData.IntegrationLengthMode.Fit)]
    public void OnlyEndBoundaryDragReplacesFitPeaksRecord(string kind, float offset, InjectionData.IntegrationLengthMode expected)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateSplineExperiment();
            experiment.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Fit;
            var graph = new ProcessingGraphControl { Experiment = experiment };
            graph.Measure(new Size(600, 400));
            graph.Arrange(new Rect(0, 0, 600, 400));

            DragIntegrationMarker(graph, kind, 0, offset);

            Assert.Equal(expected, experiment.Processor.IntegrationLengthMode);
        });
    }

    [AvaloniaFact]
    public void CorrectedSplinePointRepeatedMovesKeepCapturedPower()
    {
        using var fixture = new SplineDragFixture(corrected: true, allowTimeDragging: false, EnergyUnitFamily.Joules);
        var point = fixture.Spline.SplinePoints[1];
        var start = fixture.ScreenPoint(50, 0);
        var moved = fixture.ScreenPoint(50, 0.1e-6);
        var completions = 0;
        fixture.Graph.SplineEditCompleted += (_, _) => completions++;

        fixture.Window.MouseDown(start, MouseButton.Left);
        fixture.Window.MouseMove(moved);
        Assert.Equal(5.1e-6, point.Power, tolerance: 1e-15);
        Assert.True(point.Locked);
        Assert.Equal(5.1e-6, fixture.Spline.Baseline[50].Value, tolerance: 1e-15);
        var firstPower = point.Power;

        for (var i = 0; i < 5; i++)
        {
            fixture.Window.MouseMove(moved);
            Assert.Equal(firstPower, point.Power);
            Assert.Equal(firstPower, fixture.Spline.Baseline[50].Value);
        }

        fixture.Window.MouseUp(moved, MouseButton.Left);
        Assert.Equal(firstPower, point.Power);
        Assert.Equal(1, completions);
    }

    [AvaloniaTheory]
    [InlineData(false, false, EnergyUnitFamily.Joules)]
    [InlineData(false, true, EnergyUnitFamily.Joules)]
    [InlineData(true, false, EnergyUnitFamily.Joules)]
    [InlineData(true, true, EnergyUnitFamily.Joules)]
    [InlineData(false, false, EnergyUnitFamily.Calories)]
    [InlineData(false, true, EnergyUnitFamily.Calories)]
    [InlineData(true, false, EnergyUnitFamily.Calories)]
    [InlineData(true, true, EnergyUnitFamily.Calories)]
    public void SplinePointDragPreservesClickOffsetAndUsesDisplacementFromStart(
        bool corrected, bool allowTimeDragging, EnergyUnitFamily family)
    {
        using var fixture = new SplineDragFixture(corrected, allowTimeDragging, family);
        var point = fixture.Spline.SplinePoints[1];
        var originalPower = point.Power;
        var displayedPower = corrected ? 0 : originalPower;
        var clickOffset = new Vector(0, 2);
        var start = fixture.ScreenPoint(50, displayedPower) + clickOffset;
        var up = fixture.ScreenPoint(60, displayedPower + 0.1e-6) + clickOffset;
        var down = fixture.ScreenPoint(40, displayedPower - 0.2e-6) + clickOffset;

        fixture.Window.MouseDown(start, MouseButton.Left);
        fixture.Window.MouseMove(start);
        Assert.Equal(originalPower, point.Power);

        fixture.Window.MouseMove(up);
        Assert.Equal(5.1e-6, point.Power, tolerance: 1e-15);
        Assert.Equal(allowTimeDragging ? 60 : 50, point.Time, tolerance: 1e-10);
        var firstPower = point.Power;
        fixture.Window.MouseMove(up);
        Assert.Equal(firstPower, point.Power);

        fixture.Window.MouseMove(down);
        Assert.Equal(4.8e-6, point.Power, tolerance: 1e-15);
        Assert.Equal(allowTimeDragging ? 40 : 50, point.Time, tolerance: 1e-10);

        fixture.Window.MouseMove(start);
        Assert.Equal(originalPower, point.Power);
        Assert.Equal(50, point.Time, tolerance: 1e-10);
        fixture.Window.MouseUp(start, MouseButton.Left);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SplinePointHorizontalDraggingPreservesTimeOrClampsToNeighborsAndDataRange(
        bool corrected, bool allowTimeDragging)
    {
        using var fixture = new SplineDragFixture(corrected, allowTimeDragging, EnergyUnitFamily.Joules);
        var displayedPower = corrected ? 0 : 5e-6;

        DragAndCheck(1, 50, -10, 21);
        DragAndCheck(1, 50, 110, 79);
        DragAndCheck(0, 20, -10, 0);
        DragAndCheck(2, 80, 110, 100);

        void DragAndCheck(int index, double originalTime, double cursorTime, double clampedTime)
        {
            var point = fixture.Spline.SplinePoints[index];
            var start = fixture.ScreenPoint(originalTime, displayedPower);
            var moved = fixture.ScreenPoint(cursorTime, displayedPower);
            fixture.Window.MouseDown(start, MouseButton.Left);
            fixture.Window.MouseMove(moved);
            Assert.Equal(allowTimeDragging ? clampedTime : originalTime, point.Time, tolerance: 1e-10);
            Assert.Equal(5e-6, point.Power);
            fixture.Window.MouseMove(start);
            fixture.Window.MouseUp(start, MouseButton.Left);
            Assert.Equal(originalTime, point.Time, tolerance: 1e-10);
        }
    }

    public static IEnumerable<object[]> SplineHandleCases()
    {
        foreach (var corrected in new[] { false, true })
        foreach (var family in new[] { EnergyUnitFamily.Joules, EnergyUnitFamily.Calories })
        foreach (var slope in new[] { -1e-8, 0, 1e-8 })
            yield return new object[] { corrected, family, slope };
    }

    [AvaloniaTheory]
    [MemberData(nameof(SplineHandleCases))]
    public void SplineHandlesAreDrawnAroundTheirDisplayedAnchor(
        bool corrected, EnergyUnitFamily family, double slope)
    {
        using var fixture = new SplineDragFixture(corrected, allowTimeDragging: false, family, slope);
        // The middle point is at 50 s and 5 µW; a 100 s delay gives 20 s handles.
        var displayedPower = corrected ? 0 : 5e-6;
        var anchor = fixture.ScreenPoint(50, displayedPower);
        var left = fixture.ScreenPoint(30, displayedPower - slope * 20);
        var right = fixture.ScreenPoint(70, displayedPower + slope * 20);
        var group = new DrawingGroup();
        using (var context = group.Open()) fixture.Graph.Render(context);
        var drawings = GeometryDrawings(group).ToArray();

        foreach (var expected in new[] { left, right })
        {
            var handle = Assert.Single(drawings, drawing => drawing.Geometry != null
                && drawing.Brush != null && drawing.Pen != null
                && Math.Abs(drawing.Geometry.Bounds.Width - 7) < 1e-6
                && Math.Abs(drawing.Geometry.Bounds.Height - 7) < 1e-6
                && Math.Abs(drawing.Geometry.Bounds.Center.X - expected.X) < 1e-6);
            AssertPointNear(expected, handle.Geometry!.Bounds.Center);
        }

        var connector = Assert.Single(drawings, drawing => drawing.Geometry != null
            && drawing.Brush == null && drawing.Pen != null
            && Math.Abs(drawing.Geometry.Bounds.Left - left.X) < 1e-6
            && Math.Abs(drawing.Geometry.Bounds.Right - right.X) < 1e-6);
        var bounds = connector.Geometry!.Bounds;
        AssertPointNear(new Point(left.X, Math.Min(left.Y, right.Y)), bounds.TopLeft);
        AssertPointNear(new Point(right.X, Math.Max(left.Y, right.Y)), bounds.BottomRight);
        AssertPointNear(anchor, bounds.Center);
    }

    [AvaloniaTheory]
    [MemberData(nameof(SplineHandleCases))]
    public void SplineHandlesCanBeDraggedAtTheirDisplayedPositions(
        bool corrected, EnergyUnitFamily family, double slope)
    {
        foreach (var left in new[] { true, false })
        {
            using var fixture = new SplineDragFixture(corrected, allowTimeDragging: false, family, slope);
            var point = fixture.Spline.SplinePoints[1];
            var originalTime = point.Time;
            var originalPower = point.Power;
            var displayedPower = corrected ? 0 : originalPower;
            var handleTime = left ? 30 : 70;
            var handlePower = displayedPower + (left ? -1 : 1) * slope * 20;
            var start = fixture.ScreenPoint(handleTime, handlePower);
            var up = fixture.ScreenPoint(handleTime, handlePower + 0.1e-6);
            var down = fixture.ScreenPoint(handleTime, handlePower - 0.1e-6);
            var completions = 0;
            fixture.Graph.SplineEditCompleted += (_, _) => completions++;

            fixture.Window.MouseDown(start, MouseButton.Left);
            fixture.Window.MouseMove(up);
            Assert.Equal(slope + (left ? -5e-9 : 5e-9), point.Slope, tolerance: 1e-18);
            Assert.True(point.Locked);
            Assert.True(point.SlopeLocked);
            var firstSlope = point.Slope;
            for (var i = 0; i < 5; i++)
            {
                fixture.Window.MouseMove(up);
                Assert.Equal(firstSlope, point.Slope);
            }

            fixture.Window.MouseMove(down);
            Assert.Equal(slope + (left ? 5e-9 : -5e-9), point.Slope, tolerance: 1e-18);
            Assert.Equal(originalTime, point.Time);
            Assert.Equal(originalPower, point.Power);
            Assert.Equal(slope, fixture.Spline.SplinePoints[0].Slope);
            Assert.Equal(slope, fixture.Spline.SplinePoints[2].Slope);
            Assert.Equal(0, completions);
            fixture.Window.MouseUp(down, MouseButton.Left);
            Assert.Equal(1, completions);
        }
    }

    static void AssertPointNear(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, tolerance: 1e-6);
        Assert.Equal(expected.Y, actual.Y, tolerance: 1e-6);
    }

    static IEnumerable<GeometryDrawing> GeometryDrawings(global::Avalonia.Media.Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry) yield return geometry;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var geometryChild in GeometryDrawings(child)) yield return geometryChild;
    }

    sealed class SplineDragFixture : IDisposable
    {
        readonly EnergyUnitFamily originalFamily = AppSettings.EnergyUnitFamily;
        readonly double powerScale;
        readonly object transform;
        readonly MethodInfo toScreen;

        public ProcessingGraphControl Graph { get; }
        public Window Window { get; }
        public SplineInterpolator Spline { get; }

        public SplineDragFixture(bool corrected, bool allowTimeDragging, EnergyUnitFamily family, double? handleSlope = null)
        {
            AppSettings.EnergyUnitFamily = family;
            // Internal power is W; one calorie is 4.184 J.
            powerScale = family == EnergyUnitFamily.Calories ? 1e6 / 4.184 : 1e6;
            var experiment = CreateSplineExperiment();
            for (var i = 0; i < experiment.DataPoints.Count; i++)
                experiment.DataPoints[i] = new DataPoint(i, i % 2 == 0 ? 4e-6f : 6e-6f);
            Spline = Assert.IsType<SplineInterpolator>(experiment.Processor.Interpolator);
            Spline.Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Linear;
            Spline.AllowPointTimeDragging = allowTimeDragging;
            Spline.SetSplinePoints(new List<SplineInterpolator.SplinePoint>
            {
                new(20, 5e-6, 0), new(50, 5e-6, 1), new(80, 5e-6, 2),
            });
            if (handleSlope.HasValue)
            {
                Spline.Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Smooth;
                Spline.ShowHandles = true;
                foreach (var point in Spline.SplinePoints)
                {
                    point.Slope = handleSlope.Value;
                    point.LockSlope();
                }
            }
            Spline.RefreshBaselineFromCurrentSplinePoints();
            experiment.Processor.SubtractBaseline();
            Graph = new ProcessingGraphControl
            {
                ShowBaselineCorrected = corrected,
                ShowIntegrationRegions = false,
                Experiment = experiment,
            };
            Window = new Window { Width = 600, Height = 400, Content = Graph };
            Window.Show();
            Window.UpdateLayout();

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var type = typeof(ProcessingGraphControl);
            var layoutType = type.GetNestedType("GraphLayout", BindingFlags.NonPublic)!;
            var layout = layoutType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)!
                .Invoke(null, new[] { Graph.Bounds, type.GetField("view", flags)!.GetValue(Graph), type.GetProperty("Power", flags)!.GetValue(Graph) })!;
            transform = layoutType.GetProperty("Transform", flags)!.GetValue(layout)!;
            toScreen = transform.GetType().GetMethod("ToScreen", flags, new[] { typeof(double), typeof(double) })!;
        }

        public Point ScreenPoint(double time, double powerWatts) =>
            (Point)toScreen.Invoke(transform, new object[] { time, powerWatts * powerScale })!;

        public void Dispose()
        {
            try { Window.Close(); }
            finally { AppSettings.EnergyUnitFamily = originalFamily; }
        }
    }

    static void DragIntegrationMarker(ProcessingGraphControl graph, string kind, int injectionIndex, float offset)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var type = typeof(ProcessingGraphControl);
        var hitKind = Enum.Parse(type.GetNestedType("HitKind", BindingFlags.NonPublic)!, kind);
        var target = Activator.CreateInstance(type.GetNestedType("HitTarget", BindingFlags.NonPublic)!, hitKind, injectionIndex);
        type.GetField("dragTarget", flags)!.SetValue(graph, target);
        var layoutType = type.GetNestedType("GraphLayout", BindingFlags.NonPublic)!;
        var layout = layoutType.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(null, new[] { graph.Bounds, type.GetField("view", flags)!.GetValue(graph), type.GetProperty("Power", flags)!.GetValue(graph) })!;
        var transform = layoutType.GetProperty("Transform", flags)!.GetValue(layout)!;
        var injection = graph.Experiment!.Injections[injectionIndex];
        var point = (Point)transform.GetType().GetMethod("ToScreen", flags, new[] { typeof(double), typeof(double) })!
            .Invoke(transform, new object[] { injection.Time + (double)offset, 0d })!;
        type.GetMethod("UpdateIntegrationMarker", flags)!.Invoke(graph, new[] { point, layout });
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
