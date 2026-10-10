using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Processing;
using AnalysisITC.UI.MacOS.Drawing;

// Exercises native mouse handlers without creating an app bundle or using a storyboard.
static class ProcessingGraphDragTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var application = NSApplication.SharedApplication;
        try
        {
            DragPublishesOnlyOnRelease(CreateExperiment(), start: false, discard: true);
            DragPublishesOnlyOnRelease(CreateExperiment(), start: true, discard: true);
            DragPublishesOnlyOnRelease(CreateExperiment(), start: false, discard: false);
            VerifyLocalY242AFixtures(args[0]);
            Console.WriteLine("PASS: native integration drags preview silently and finalize once");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    static void VerifyLocalY242AFixtures(string repository)
    {
        // Investigation artifacts are local evidence, not required test-suite inputs.
        var directory = Path.Combine(repository, "Artifacts/Investigations/Y242A-AICc-2026-10-09");
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.GetFiles(directory, "*.ftxtc").OrderBy(path => path))
        {
            using (var stream = File.OpenRead(path))
            {
                var read = Task.Run(() => FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict)).GetAwaiter().GetResult();
                Check(read.Issues.Count == 0, "Y242A fixture did not reopen strictly");
                var experiment = read.Containers.OfType<ExperimentData>().Single();
                DragPublishesOnlyOnRelease(experiment, start: false, discard: true, degree: 12);
            }
        }
    }

    static ExperimentData CreateExperiment()
    {
        var experiment = new ExperimentData("polynomial-drag.itc") { CellVolume = 1.4e-3 };
        for (int time = 0; time <= 200; time++)
            experiment.DataPoints.Add(new DataPoint(time, 2e-6f + (time > 40 && time <= 60 ? 4e-6f : 0), 25));
        var injection = InjectionData.FromPEAQFile(experiment, 0, true, 40, 2e-6, 160, 2, 25);
        experiment.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(20);
        return experiment;
    }

    static void DragPublishesOnlyOnRelease(ExperimentData experiment, bool start, bool discard, int degree = 0)
    {
        var processor = experiment.Processor;
        var polynomial = new CountingPolynomial(processor) { Degree = degree };
        processor.Interpolator = polynomial;
        processor.DiscardIntegratedPoints = discard;
        Task.Run(() => processor.ProcessData(showProgress: false)).GetAwaiter().GetResult();
        polynomial.Calls = 0;

        var graph = Create<DataProcessingGraphView>();
        graph.Frame = new CGRect(0, 0, 800, 500);
        graph.Initialize(experiment);
        graph.Graph.AutoSetFrame();
        var injection = experiment.Injections[0];
        var originalOffset = start ? injection.IntegrationStartDelay : injection.IntegrationEndOffset;
        var originalBaseline = polynomial.Baseline.ToArray();
        var updates = 0;
        var notifications = 0;
        var starts = 0;
        var stops = 0;
        EventHandler completed = (sender, _) =>
        {
            if (ReferenceEquals(sender, experiment)) Interlocked.Increment(ref notifications);
        };
        EventHandler updated = (_, __) => Interlocked.Increment(ref updates);
        EventHandler<ProgressIndicatorEventData> progress = (_, value) =>
        {
            if (value.Progress == -0.5) Interlocked.Increment(ref starts);
            if (value.Progress == -1) Interlocked.Increment(ref stops);
        };
        DataProcessor.ProcessingCompleted += completed;
        experiment.ProcessingUpdated += updated;
        StatusBarManager.ProgressUpdate += progress;
        try
        {
            var click = Position(graph, injection.Time + originalOffset);
            using (var down = MouseEvent(NSEventType.LeftMouseDown, click)) graph.MouseDown(down);
            // Select the marker directly; hit boxes are normally populated by DrawRect.
            var box = new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.IntegrationRangeMarker,
                new CGRect(click.X, click.Y, 1, 1), 0, CGPoint.Empty, start ? 0 : 1);
            typeof(DataProcessingGraphView).GetProperty("SelectedFeature", Instance).SetValue(graph,
                MouseOverFeatureEvent.BoundboxFeature(box, click));

            for (int step = 1; step <= 20; step++)
            {
                var offset = originalOffset + (start ? -1 : 1) * step * 0.25f;
                using (var drag = MouseEvent(NSEventType.LeftMouseDragged, Position(graph, injection.Time + offset)))
                    graph.MouseDragged(drag);
            }

            var previewNotifications = notifications;
            var previewUpdates = updates;
            Check(polynomial.Calls == 0, "baseline refitted during the drag");
            Check(polynomial.Baseline.SequenceEqual(originalBaseline), "drag changed the preview baseline");
            Check(processor.IntegrationCompleted, "drag did not calculate preview heats");
            var finalOffset = start ? injection.IntegrationStartDelay : injection.IntegrationEndOffset;
            Check(Math.Abs(finalOffset - (originalOffset + (start ? -5 : 5))) < 0.001,
                "marker did not follow the drag");

            using (var up = MouseEvent(NSEventType.LeftMouseUp, Position(graph, injection.Time + finalOffset)))
                graph.MouseUp(up);
            if (discard) PumpUntil(() => Volatile.Read(ref stops) == 1);
            var committedNotifications = notifications;
            var committedUpdates = updates;

            // A later mouse-up without a new mouse-down must not re-finalize the old drag.
            using (var up = MouseEvent(NSEventType.LeftMouseUp, Position(graph, injection.Time + finalOffset)))
                graph.MouseUp(up);
            if (starts > 1) PumpUntil(() => Volatile.Read(ref stops) == Volatile.Read(ref starts));

            Console.WriteLine(experiment.FileName + ": preview notifications=" + previewNotifications
                + ", preview updates=" + previewUpdates + ", refits=" + polynomial.Calls
                + ", progress starts=" + starts);
            Check(previewNotifications == 0 && previewUpdates == 0,
                "drag preview published completed processing on every mouse movement");
            Check(committedNotifications > 0 && committedUpdates > 0,
                "release did not publish the processing change");
            Check(notifications == committedNotifications && updates == committedUpdates,
                "a later mouse-up finalized the completed drag again");
            Check(polynomial.Calls == (discard ? 1 : 0), "release refitted the baseline more than once");
            Check(starts == (discard ? 1 : 0) && stops == starts, "progress did not start and finish once");
            Check(processor.BaselineCompleted && processor.IntegrationCompleted, "processing remained incomplete");
        }
        finally
        {
            DataProcessor.ProcessingCompleted -= completed;
            experiment.ProcessingUpdated -= updated;
            StatusBarManager.ProgressUpdate -= progress;
            graph.Dispose();
        }
    }

    static CGPoint Position(DataProcessingGraphView graph, double time)
    {
        var frame = graph.Graph.Frame;
        return new CGPoint(frame.X + frame.Width * (time - graph.Graph.XAxis.Min)
            / (graph.Graph.XAxis.Max - graph.Graph.XAxis.Min), frame.Y + frame.Height / 2);
    }

    static NSEvent MouseEvent(NSEventType type, CGPoint point) => NSEvent.MouseEvent(type, point,
        (NSEventModifierMask)0, 0, 0, null, 0, 1, 1);

    static T Create<T>() where T : NSObject
    {
        var handle = Send(Send(new Class(typeof(T)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        return Runtime.GetNSObject<T>(handle);
    }

    static void PumpUntil(Func<bool> complete)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!complete() && timer.Elapsed < TimeSpan.FromSeconds(30))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        Check(complete(), "processing did not finish within 30 seconds");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class CountingPolynomial : PolynomialLeastSquaresInterpolator
    {
        public int Calls;
        public CountingPolynomial(DataProcessor processor) : base(processor) { }
        public override Task Interpolate(CancellationToken token, bool replace = true)
        {
            Interlocked.Increment(ref Calls);
            return base.Interpolate(token, replace);
        }
    }
}
