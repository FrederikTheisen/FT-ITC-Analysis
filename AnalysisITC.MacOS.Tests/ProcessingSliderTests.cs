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

// Exercises native MouseDown/slider-cell tracking and the controller's actual
// subscriptions, without building an app bundle or relying on physical input.
static class ProcessingSliderTests
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
            SliderPublishesOnlyOnRelease(CreateExperiment(), start: false, discard: true);
            SliderPublishesOnlyOnRelease(CreateExperiment(), start: true, discard: true);
            SliderPublishesOnlyOnRelease(CreateExperiment(), start: false, discard: false);
            SliderPublishesOnlyOnRelease(CreateExperiment(), start: false, discard: true, keyboard: true);
            SliderPublishesOnlyOnRelease(CreateExperiment(), start: false, discard: true, staleEvent: true);
            SliderPublishesOnlyOnRelease(CreateExperiment(), start: true, discard: true, staleEvent: true);
            SliderPublishesOnlyOnRelease(CreateExperiment(), start: false, discard: true, unchanged: true);
            var directory = Path.Combine(args[0], "Artifacts/Investigations/Y242A-AICc-2026-10-09");
            if (Directory.Exists(directory))
                foreach (var path in Directory.GetFiles(directory, "*.ftxtc").OrderBy(path => path))
                    using (var stream = File.OpenRead(path))
                    {
                        var read = Task.Run(() => FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict)).GetAwaiter().GetResult();
                        Check(read.Issues.Count == 0, "Y242A fixture did not reopen strictly");
                        SliderPublishesOnlyOnRelease(read.Containers.OfType<ExperimentData>().Single(), false, true, 12, staleEvent: true);
                    }
            Console.WriteLine("PASS: native slider tracking processes once after release, including stale current events and keyboard edits");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    static void SliderPublishesOnlyOnRelease(ExperimentData experiment, bool start, bool discard, int degree = 0, bool keyboard = false, bool staleEvent = false, bool unchanged = false)
    {
        var processor = experiment.Processor;
        var polynomial = new BlockingPolynomial(processor) { Degree = degree };
        processor.Interpolator = polynomial;
        processor.DiscardIntegratedPoints = discard;
        Task.Run(() => processor.ProcessData(showProgress: false)).GetAwaiter().GetResult();
        polynomial.Calls = 0;
        var releaseWorker = new TaskCompletionSource<bool>();
        polynomial.Barrier = releaseWorker.Task;
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(new[] { experiment });
        DataManager.SelectIndex(0);
        var controller = CreateController(experiment);
        var slider = Outlet<NSSlider>(controller, start ? "IntegrationDelayControl" : "IntegrationLengthControl");
        slider.Target = controller;
        slider.Action = new Selector(start ? "IntegrationStartTimeSliderChanged:" : "IntegrationLengthSliderChanged:");
        var injection = experiment.Injections[0];
        var original = start ? injection.IntegrationStartDelay : injection.IntegrationEndOffset;
        slider.FloatValue = original;
        var notifications = 0;
        var starts = 0;
        var stops = 0;
        EventHandler completed = (sender, _) =>
        {
            if (ReferenceEquals(sender, experiment)) Interlocked.Increment(ref notifications);
        };
        EventHandler<ProgressIndicatorEventData> progress = (_, value) =>
        {
            if (value.Progress == -0.5) Interlocked.Increment(ref starts);
            if (value.Progress == -1) Interlocked.Increment(ref stops);
        };
        DataProcessor.ProcessingCompleted += completed;
        StatusBarManager.ProgressUpdate += progress;
        var previewNotifications = 0;
        var previewStarts = 0;
        var previewRefits = 0;
        try
        {
            if (!keyboard)
            {
                TrackSlider(slider, () =>
                {
                    DispatchAction(slider, NSEventType.LeftMouseDown);
                    slider.FloatValue = original + (unchanged ? 0 : 0.25f);
                    DispatchAction(slider, NSEventType.LeftMouseDragged);
                    for (int step = 2; step <= 20; step++)
                    {
                        slider.FloatValue = original + (unchanged ? 0 : step * 0.25f);
                        DispatchAction(slider, staleEvent ? NSEventType.LeftMouseUp : NSEventType.Periodic);
                    }
                    // A native release action still runs inside MouseDown's tracking loop.
                    DispatchAction(slider, NSEventType.LeftMouseUp);
                    DispatchAction(slider, NSEventType.LeftMouseUp);
                    previewNotifications = notifications;
                    previewStarts = starts;
                    previewRefits = polynomial.Calls;
                });
            }
            else
            {
                slider.FloatValue = original + 5;
                DispatchAction(slider, NSEventType.KeyDown);
            }
            var finalValue = start ? injection.IntegrationStartDelay : injection.IntegrationEndOffset;
            releaseWorker.TrySetResult(true);
            DrainProcessor(processor);
            Console.WriteLine(experiment.FileName + ": preview notifications=" + previewNotifications
                + ", preview starts=" + previewStarts + ", refits=" + polynomial.Calls
                + ", progress cycles=" + stops);
            Check(previewStarts == 0 && previewRefits == 0 && previewNotifications == 0,
                "slider-cell tracking queued baseline refits or published completed processing before release");
            Check(Math.Abs(finalValue - (original + (unchanged ? 0 : 5))) < 0.001, "slider did not update the integration region");
            Check(polynomial.Calls == (discard && !unchanged ? 1 : 0), "slider edit did not refit exactly once");
            Check(starts == (discard && !unchanged ? 1 : 0) && stops == starts, "slider queued repeated progress cycles");
            Check((unchanged ? notifications == 0 : notifications > 0) && processor.BaselineCompleted && processor.IntegrationCompleted,
                "slider edit did not publish completed processing");
            Check(Math.Abs((start ? injection.IntegrationStartDelay : injection.IntegrationEndOffset) - finalValue) < 0.001,
                "processing changed the chosen integration region");
        }
        finally
        {
            releaseWorker.TrySetResult(true);
            DrainProcessor(processor);
            DataProcessor.ProcessingCompleted -= completed;
            StatusBarManager.ProgressUpdate -= progress;
            UnsubscribeController(controller);
            controller.Dispose();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static ExperimentData CreateExperiment()
    {
        var experiment = new ExperimentData("polynomial-slider.itc") { CellVolume = 1.4e-3 };
        for (int time = 0; time <= 200; time++)
            experiment.DataPoints.Add(new DataPoint(time, 2e-6f + (time > 40 && time <= 60 ? 4e-6f : 0), 25));
        var injection = InjectionData.FromPEAQFile(experiment, 0, true, 40, 2e-6, 160, 2, 25);
        experiment.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(20);
        return experiment;
    }

    static DataProcessingViewControllet CreateController(ExperimentData experiment)
    {
        var controller = Create<DataProcessingViewControllet>();
        controller.View = new NSView(new CGRect(0, 0, 800, 600));
        foreach (var outlet in typeof(DataProcessingViewControllet).GetProperties(Instance)
            .Where(property => property.GetCustomAttribute<OutletAttribute>() != null))
        {
            var value = outlet.PropertyType == typeof(DataProcessingGraphView)
                ? Create<DataProcessingGraphView>()
                : outlet.PropertyType == typeof(IntegrationRangeSlider)
                    ? (NSObject)new IntegrationRangeSlider(new CGRect(20, 20, 200, 20))
                    : (NSObject)Activator.CreateInstance(outlet.PropertyType);
            if (value is NSSegmentedControl) ((NSSegmentedControl)value).SegmentCount = 6;
            outlet.SetValue(controller, value);
        }
        typeof(DataProcessingViewControllet).GetProperty("DiscardIntegratedPoints", Instance)
            .SetValue(controller, experiment.Processor.DiscardIntegratedPoints);
        Outlet<DataProcessingGraphView>(controller, "BaselineGraphView").Initialize(experiment);
        var length = Outlet<NSSlider>(controller, "IntegrationLengthControl");
        length.Continuous = true;
        length.MaxValue = 160;
        length.FloatValue = experiment.Injections[0].IntegrationEndOffset;
        var start = Outlet<NSSlider>(controller, "IntegrationDelayControl");
        start.Continuous = true;
        start.MinValue = -10;
        start.MaxValue = 10;
        Outlet<NSPopUpButton>(controller, "ProcessingOptionsMenuButton").Menu.AddItem(
            new NSMenuItem("Lock processing") { Identifier = "lockprocessor" });
        controller.ViewDidLoad();
        return controller;
    }

    static void TrackSlider(NSSlider slider, Action actions)
    {
        using (var window = new NSWindow(new CGRect(0, 0, 300, 100), NSWindowStyle.Borderless, NSBackingStore.Buffered, false))
        using (var cell = new TrackingSliderCell())
        {
            var originalCell = slider.Cell;
            var action = slider.Action;
            var target = slider.Target;
            cell.MinValue = slider.MinValue;
            cell.MaxValue = slider.MaxValue;
            cell.DoubleValue = slider.DoubleValue;
            cell.Actions = actions;
            slider.Cell = cell;
            slider.Action = action;
            slider.Target = target;
            slider.Continuous = true;
            window.ContentView.AddSubview(slider);
            window.Display();
            var knob = cell.KnobRectFlipped(false);
            var location = slider.ConvertPointToView(new CGPoint(knob.X + knob.Width / 2, knob.Y + knob.Height / 2), null);
            try
            {
                using (var down = NSEvent.MouseEvent(NSEventType.LeftMouseDown, location,
                    (NSEventModifierMask)0, 0, window.WindowNumber, null, 0, 1, 1))
                    slider.MouseDown(down);
                Check(cell.TrackingCalls == 1, "native MouseDown did not enter slider-cell tracking");
            }
            finally
            {
                var value = slider.DoubleValue;
                slider.Cell = originalCell;
                slider.DoubleValue = value;
                slider.RemoveFromSuperview();
            }
        }
    }

    static void UnsubscribeController(DataProcessingViewControllet controller)
    {
        Detach(typeof(AppDelegate), "StartPrintOperation", "AppDelegate_StartPrintOperation", controller);
        Detach(typeof(DataManager), "SelectionDidChange", "OnSelectionChanged", controller);
        Detach(typeof(DataManager), "DataDidChange", "OnProcessingDataChanged", controller);
        Detach(typeof(DataProcessor), "BaselineInterpolationCompleted", "OnInterpolationCompleted", controller);
        Detach(typeof(DataProcessor), "ProcessingCompleted", "OnProcessingCompleted", controller);
        Detach(typeof(BaselineOptionsPopoverViewController), "Updated", "BaselineOptionsPopoverViewController_Updated", controller);
    }

    static void Detach(Type type, string eventName, string methodName, object target)
    {
        var eventInfo = type.GetEvent(eventName, BindingFlags.Static | BindingFlags.Public);
        var method = target.GetType().GetMethod(methodName, Instance);
        if (eventInfo != null && method != null)
            eventInfo.RemoveEventHandler(null, Delegate.CreateDelegate(eventInfo.EventHandlerType, target, method));
    }

    static void DispatchAction(NSSlider slider, NSEventType type)
    {
        NSEvent input;
        if (type == NSEventType.Periodic)
            input = NSEvent.OtherEvent(type, CGPoint.Empty, (NSEventModifierMask)0, 0, 0, null, 0, 0, 0);
        else if (type == NSEventType.KeyDown)
            input = NSEvent.KeyEvent(type, CGPoint.Empty, (NSEventModifierMask)0, 0, 0, null, "", "", false, 124);
        else
            input = NSEvent.MouseEvent(type, CGPoint.Empty, (NSEventModifierMask)0, 0, 0, null, 0, 1, 1);
        using (input)
        {
            NSApplication.SharedApplication.PostEvent(input, true);
            using (var current = NSApplication.SharedApplication.NextEvent((NSEventMask)(1UL << (int)type),
                NSDate.DistantFuture, NSRunLoopMode.Default, true))
            {
                Check(current.Type == type, "wrong current event for slider action");
                Check(slider.SendAction(slider.Action, slider.Target), "native slider action was not dispatched");
            }
        }
    }

    static void DrainProcessor(DataProcessor processor)
    {
        // Join the end of the existing queue without requesting another processing pass.
        var gate = (SemaphoreSlim)typeof(DataProcessor).GetField("processingGate", Instance).GetValue(processor);
        var drained = gate.WaitAsync();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!drained.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(30))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        Check(drained.IsCompleted, "processing queue did not finish within 30 seconds");
        gate.Release();
    }

    static T Create<T>() where T : NSObject
    {
        var handle = Send(Send(new Class(typeof(T)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        return Runtime.GetNSObject<T>(handle);
    }

    static T Outlet<T>(DataProcessingViewControllet controller, string name) =>
        (T)typeof(DataProcessingViewControllet).GetProperty(name, Instance).GetValue(controller);

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class BlockingPolynomial : PolynomialLeastSquaresInterpolator
    {
        public int Calls;
        public Task Barrier = Task.CompletedTask;
        public BlockingPolynomial(DataProcessor processor) : base(processor) { }
        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            Interlocked.Increment(ref Calls);
            await Barrier;
            await base.Interpolate(token, replace);
        }
    }
}

[Register("ProcessingTestTrackingSliderCell")]
public sealed class TrackingSliderCell : NSSliderCell
{
    public Action Actions;
    public int TrackingCalls;
    public TrackingSliderCell() : base() { }
    public TrackingSliderCell(IntPtr handle) : base(handle) { }
    public override bool TrackMouse(NSEvent input, CGRect rect, NSView view, bool untilMouseUp)
    {
        TrackingCalls++;
        Actions();
        return true;
    }
}
