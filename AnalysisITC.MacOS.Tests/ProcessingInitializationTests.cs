using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using AnalysisITC.Platform.MacOS;

// Runs the real Process-view appearance callback with Cocoa continuations.
// Holding a foreign root without Enter is deliberate: this view does not own it.
static class ProcessingInitializationTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> failures = new List<string>();

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var dispatcher = PlatformServices.MainThreadDispatcher;
        var messages = AppEventHandler.ShowAppMessage;
        var context = SynchronizationContext.Current;
        try
        {
            PlatformServices.RegisterMainThreadDispatcher(new MacMainThreadDispatcher());
            if (SynchronizationContext.Current == null)
            {
                var cocoaContext = typeof(NSApplication).Assembly.GetType("AppKit.AppKitSynchronizationContext", true);
                SynchronizationContext.SetSynchronizationContext((SynchronizationContext)Activator.CreateInstance(cocoaContext, true));
            }
            Console.WriteLine("Native initialization context=" + SynchronizationContext.Current.GetType().FullName
                + ", mainThread=" + NSThread.IsMain);
            Check(NSThread.IsMain, "native initialization harness must run on the main thread");
            DocumentDirtyTracker.Initialize();
            InitializesAfterForeignOperation(true);
            InitializesAfterForeignOperation(false);
            InitializesAfterForeignOperation(true, actualFit: true);
            InitializesAfterForeignOperation(false, actualFit: true);
            InitializesAfterForeignOperation(true, appearances: 3);
            InitializesAfterForeignOperation(false, appearances: 3);
            foreach (StaleChange change in Enum.GetValues(typeof(StaleChange)))
                foreach (var discard in new[] { true, false })
                    PendingInitializationRejectsChangedTarget(discard, change);
            HandledInitializationFailureRetainsAdmissionThroughDialog();
            foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
            if (failures.Count != 0) return 1;
            Console.WriteLine("PASS: native Process-view initialization waits for roots and active fits, processes once, rejects stale targets, and retains admission through handled errors");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            AppEventHandler.ShowAppMessage = messages;
            PlatformServices.RegisterMainThreadDispatcher(dispatcher);
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    static void InitializesAfterForeignOperation(bool discard, bool actualFit = false, int appearances = 1)
    {
        var experiment = CreateExperiment(discard);
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(experiment);
        DataManager.SelectIndex(0);
        var controller = CreateController(experiment);
        DocumentDirtyTracker.MarkClean();
        var processor = experiment.Processor;
        var initialOffset = experiment.Injections[0].IntegrationEndOffset;
        var initialStart = experiment.Injections[0].IntegrationStartDelay;
        var stamp = CaptureRevision();
        var baselineCount = 0;
        var processingCount = 0;
        var starts = 0;
        var stops = 0;
        var dialogs = new List<string>();
        var completionOnMainThread = true;
        EventHandler baseline = (sender, _) =>
        {
            if (!ReferenceEquals(sender, processor)) return;
            baselineCount++;
            completionOnMainThread &= NSThread.IsMain;
        };
        EventHandler completed = (sender, _) =>
        {
            if (!ReferenceEquals(sender, experiment)) return;
            processingCount++;
            completionOnMainThread &= NSThread.IsMain;
        };
        EventHandler<ProgressIndicatorEventData> progress = (_, value) =>
        {
            if (value.Progress == -0.5) starts++;
            if (value.Progress == -1) stops++;
        };
        DataProcessor.BaselineInterpolationCompleted += baseline;
        DataProcessor.ProcessingCompleted += completed;
        StatusBarManager.ProgressUpdate += progress;
        AppEventHandler.ShowAppMessage = (_, error) => dialogs.Add(error.Message);
        var foreign = new ForeignOperation(actualFit);
        Exception appearanceError = null;
        try
        {
            try
            {
                for (var appearance = 0; appearance < appearances; appearance++)
                {
                    if (actualFit) Send(controller.Handle, Selector.GetHandle("viewWillAppear"));
                    else controller.ViewWillAppear();
                }
            }
            catch (Exception error) { appearanceError = error; }
            PumpFor(TimeSpan.FromMilliseconds(75));
            var pristine = processor.BaselineType == BaselineInterpolatorTypes.None
                && processor.Interpolator == null
                && experiment.Injections[0].IntegrationEndOffset == initialOffset
                && experiment.Injections[0].IntegrationStartDelay == initialStart
                && !processor.BaselineCompleted && !processor.IntegrationCompleted
                && !DocumentDirtyTracker.IsDirty && CaptureRevision() == stamp;
            var prefix = "discard=" + discard + ", actualFit=" + actualFit + ", appearances=" + appearances;
            Console.WriteLine(prefix + " held: baseline=" + processor.BaselineType
                + ", offset=" + experiment.Injections[0].IntegrationEndOffset
                + ", dialogs=" + dialogs.Count + ", exception=" + (appearanceError == null ? "none" : appearanceError.GetType().Name)
                + ", processing=" + processingCount);
            Expect(pristine, prefix + ": appearance changed live processing inputs before admission");
            Expect(appearanceError == null, prefix + ": appearance threw while waiting for admission");
            Expect(dialogs.Count == 0, prefix + ": appearance showed a handled error while waiting for admission");
            Expect(baselineCount == 0 && processingCount == 0 && starts == 0,
                prefix + ": initialization processed before admission");
            Expect(!Outlet<NSButton>(controller, "FitPeaksButton").Enabled,
                prefix + ": calculation eligibility remained enabled during foreign work");
            foreign.Dispose();
            foreign = null;
            DrainActivity();
            Console.WriteLine(prefix + " released: baselines=" + baselineCount + ", processing=" + processingCount
                + ", progress=" + starts + "/" + stops + ", mainThread=" + completionOnMainThread);
            Expect(processor.BaselineType == BaselineInterpolatorTypes.Spline
                && processor.BaselineCompleted && processor.IntegrationCompleted,
                prefix + ": queued initialization did not finish");
            Expect(experiment.Injections[0].IntegrationEndOffset == experiment.Injections[0].Delay / 2
                && experiment.Injections[0].IntegrationStartDelay == initialStart,
                prefix + ": initialization changed the established endpoint/start-delay behavior");
            Expect(baselineCount == 1 && processingCount == 2 && starts == 1 && stops == 1,
                prefix + ": appearance did not perform exactly one existing processing cycle");
            Expect(completionOnMainThread, prefix + ": processing publication left the native main thread");
            Expect(dialogs.Count == 0, prefix + ": initialization displayed an error");
            Expect(Outlet<NSButton>(controller, "FitPeaksButton").Enabled
                && Outlet<NSButton>(controller, "FitPeaksButton").ToolTip != FittingStatusPresentation.BusyTooltip,
                prefix + ": processing eligibility did not restore after completion");
        }
        finally
        {
            if (foreign != null) foreign.Dispose();
            DrainActivity();
            DataProcessor.BaselineInterpolationCompleted -= baseline;
            DataProcessor.ProcessingCompleted -= completed;
            StatusBarManager.ProgressUpdate -= progress;
            UnsubscribeController(controller);
            controller.Dispose();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    enum StaleChange { Selection, Removed, Document, Processor, Baseline, Locked, NoInjections }

    static void PendingInitializationRejectsChangedTarget(bool discard, StaleChange change)
    {
        var experiment = CreateExperiment(discard);
        var other = CreateExperiment(discard);
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(new[] { experiment, other });
        DataManager.SelectIndex(0);
        var controller = CreateController(experiment);
        var originalProcessor = experiment.Processor;
        var initialEnd = experiment.Injections[0].IntegrationEndOffset;
        var originalInjection = experiment.Injections[0];
        var completed = 0;
        var baselines = 0;
        var starts = 0;
        var dialogs = 0;
        EventHandler baseline = (_, value) => baselines++;
        EventHandler processing = (_, value) => completed++;
        EventHandler<ProgressIndicatorEventData> progress = (_, value) => { if (value.Progress == -0.5) starts++; };
        DataProcessor.BaselineInterpolationCompleted += baseline;
        DataProcessor.ProcessingCompleted += processing;
        StatusBarManager.ProgressUpdate += progress;
        AppEventHandler.ShowAppMessage = (_, error) => dialogs++;
        var foreign = new ForeignOperation(false);
        try
        {
            controller.ViewWillAppear();
            PumpFor(TimeSpan.FromMilliseconds(25));
            Expect(originalProcessor.BaselineType == BaselineInterpolatorTypes.None
                && originalInjection.IntegrationEndOffset == initialEnd,
                change + ": pending initialization changed live inputs");
            switch (change)
            {
                case StaleChange.Selection:
                    DataManager.SelectIndex(1);
                    break;
                case StaleChange.Removed:
                    DataManager.RemoveSourceItemAt(0);
                    break;
                case StaleChange.Document:
                    // Reusing the same object isolates document identity from selection/processor guards.
                    DataManager.Clear(DataClearMode.ResetSession);
                    DataManager.AddData(experiment);
                    DataManager.SelectIndex(0);
                    break;
                case StaleChange.Processor:
                    typeof(ExperimentData).GetProperty("Processor").SetValue(experiment, new DataProcessor(experiment));
                    DataManager.InvokeDataDidChange();
                    break;
                case StaleChange.Baseline:
                    originalProcessor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
                    DataManager.InvokeDataDidChange();
                    break;
                case StaleChange.Locked:
                    originalProcessor.Lock();
                    DataManager.InvokeDataDidChange();
                    break;
                case StaleChange.NoInjections:
                    experiment.Injections.Clear();
                    break;
            }
            DocumentDirtyTracker.MarkClean();
            var revision = CaptureRevision();
            var expectedInterpolator = originalProcessor.Interpolator;
            foreign.Dispose();
            foreign = null;
            DrainActivity();
            var prefix = change + ", discard=" + discard;
            Console.WriteLine(prefix + ": baselines=" + baselines + ", processing=" + completed
                + ", starts=" + starts + ", dialogs=" + dialogs);
            Expect(ReferenceEquals(originalProcessor.Interpolator, expectedInterpolator)
                && originalInjection.IntegrationEndOffset == initialEnd
                && !originalProcessor.BaselineCompleted && !originalInjection.IsIntegrated,
                prefix + ": stale initialization changed its captured processor/inputs");
            Expect(other.Processor.BaselineType == BaselineInterpolatorTypes.None
                && (change != StaleChange.Processor || experiment.Processor.BaselineType == BaselineInterpolatorTypes.None),
                prefix + ": stale initialization processed a replacement target");
            Expect(baselines == 0 && completed == 0 && starts == 0 && dialogs == 0,
                prefix + ": stale initialization processed or displayed an error");
            Expect(!DocumentDirtyTracker.IsDirty && CaptureRevision() == revision,
                prefix + ": rejected stale initialization marked current content dirty");
            Expect(!ProcessingActivity.IsBusy
                && Outlet<NSButton>(controller, "FitPeaksButton").ToolTip != FittingStatusPresentation.BusyTooltip,
                prefix + ": rejected stale initialization retained busy eligibility");
        }
        finally
        {
            if (foreign != null) foreign.Dispose();
            DrainActivity();
            DataProcessor.BaselineInterpolationCompleted -= baseline;
            DataProcessor.ProcessingCompleted -= processing;
            StatusBarManager.ProgressUpdate -= progress;
            UnsubscribeController(controller);
            controller.Dispose();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static void HandledInitializationFailureRetainsAdmissionThroughDialog()
    {
        var experiment = CreateExperiment(true);
        var following = CreateExperiment(false);
        following.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        ((PolynomialLeastSquaresInterpolator)following.Processor.Interpolator).Degree = 0;
        DataManager.Clear(DataClearMode.ResetSession);
        DataManager.AddData(new[] { experiment, following });
        DataManager.SelectIndex(0);
        var controller = CreateController(experiment);
        var targetCompletions = 0;
        var followingCompletions = 0;
        var starts = 0;
        var dialogs = 0;
        var startsBeforeDialogPump = -1;
        var startsAfterDialogPump = -1;
        ProcessingOperation initializationOperation = null;
        ProcessingOperation dialogOperation = null;
        Task followingTask = null;
        EventHandler completed = (sender, _) =>
        {
            if (ReferenceEquals(sender, following)) followingCompletions++;
            if (!ReferenceEquals(sender, experiment)) return;
            targetCompletions++;
            if (targetCompletions != 2) return;
            initializationOperation = ProcessingActivity.Current;
            // The second notification comes from DidProcessDataCore, outside
            // IntegratePeaksCore's own error handler.
            throw new InvalidOperationException("Injected native initialization publication failure");
        };
        EventHandler<ProgressIndicatorEventData> progress = (_, value) =>
        {
            if (value.Progress == -0.5) starts++;
        };
        DataProcessor.ProcessingCompleted += completed;
        StatusBarManager.ProgressUpdate += progress;
        AppEventHandler.ShowAppMessage = (sender, error) =>
        {
            dialogs++;
            dialogOperation = sender as ProcessingOperation;
            Expect(dialogOperation != null && ReferenceEquals(dialogOperation, initializationOperation),
                "handled initialization failure did not retain its operation as dialog sender");
            Expect(ReferenceEquals(ProcessingActivity.Current, initializationOperation) && ProcessingActivity.IsBusy,
                "handled initialization failure released ownership before its dialog");
            startsBeforeDialogPump = starts;
            Expect(followingTask != null && !followingTask.IsCompleted && starts == 1 && followingCompletions == 0,
                "following work started before the handled initialization dialog");
            // Native alerts run a nested event loop. A queued calculation must
            // remain queued even while its continuation could otherwise run.
            PumpFor(TimeSpan.FromMilliseconds(75));
            startsAfterDialogPump = starts;
            Expect(ReferenceEquals(ProcessingActivity.Current, initializationOperation) && ProcessingActivity.IsBusy,
                "handled initialization dialog lost ownership during its nested event loop");
            Expect(followingTask != null && !followingTask.IsCompleted && starts == 1 && followingCompletions == 0
                && !following.Processor.BaselineCompleted && !following.Processor.IntegrationCompleted,
                "following work ran inside the handled initialization dialog");
            Expect(!Outlet<NSButton>(controller, "FitPeaksButton").Enabled,
                "handled initialization dialog restored calculation eligibility too early");
        };
        var foreign = new ForeignOperation(false);
        try
        {
            controller.ViewWillAppear();
            followingTask = following.Processor.ProcessData();
            Check(!followingTask.IsCompleted, "following work did not queue behind pending initialization");
            foreign.Dispose();
            foreign = null;
            DrainActivity();
            PumpUntil(() => followingTask.IsCompleted, "following processing did not finish after the handled dialog");
            followingTask.GetAwaiter().GetResult();
            Console.WriteLine("Handled failure: target completions=" + targetCompletions + ", dialogs=" + dialogs
                + ", sender=" + (dialogOperation == null ? "null" : dialogOperation.Id.ToString())
                + ", nested-loop starts=" + startsBeforeDialogPump + "/" + startsAfterDialogPump
                + ", following completions=" + followingCompletions);
            Expect(dialogs == 1 && targetCompletions == 2,
                "initialization publication failure did not display exactly one handled error");
            Expect(followingCompletions == 2 && following.Processor.BaselineCompleted
                && following.Processor.IntegrationCompleted && starts == 2,
                "following work did not complete one processing cycle after the handled dialog");
            Expect(Outlet<NSButton>(controller, "FitPeaksButton").Enabled
                && Outlet<NSButton>(controller, "FitPeaksButton").ToolTip != FittingStatusPresentation.BusyTooltip,
                "handled initialization failure did not restore idle calculation eligibility");
        }
        finally
        {
            if (foreign != null) foreign.Dispose();
            DrainActivity();
            DataProcessor.ProcessingCompleted -= completed;
            StatusBarManager.ProgressUpdate -= progress;
            UnsubscribeController(controller);
            controller.Dispose();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static ExperimentData CreateExperiment(bool discard)
    {
        var experiment = new ExperimentData("initialization-" + discard + ".itc") { CellVolume = 1.4e-3, InitialDelay = 40 };
        for (int time = 0; time <= 200; time++)
            experiment.DataPoints.Add(new DataPoint(time, 2e-6f + (time > 40 && time <= 60 ? 4e-6f : 0), 25));
        var injection = InjectionData.FromPEAQFile(experiment, 0, true, 40, 2e-6, 160, 2, 25);
        experiment.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(20);
        experiment.Processor.DiscardIntegratedPoints = discard;
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
        Outlet<NSSlider>(controller, "IntegrationLengthControl").MaxValue = 160;
        Outlet<NSSlider>(controller, "IntegrationDelayControl").MinValue = -10;
        Outlet<NSSlider>(controller, "IntegrationDelayControl").MaxValue = 10;
        Outlet<NSPopUpButton>(controller, "ProcessingOptionsMenuButton").Menu.AddItem(
            new NSMenuItem("Lock processing") { Identifier = "lockprocessor" });
        controller.ViewDidLoad();
        return controller;
    }

    static long CaptureRevision()
    {
        var stamp = typeof(DocumentDirtyTracker).GetMethod("CaptureSaveStamp", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        return (long)stamp.GetType().GetProperty("Revision").GetValue(stamp);
    }

    static void DrainActivity()
    {
        var timer = Stopwatch.StartNew();
        while (ProcessingActivity.IsBusy && timer.Elapsed < TimeSpan.FromSeconds(20))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        Check(!ProcessingActivity.IsBusy, "queued initialization did not drain within 20 seconds");
        PumpFor(TimeSpan.FromMilliseconds(25));
    }

    static void PumpFor(TimeSpan duration)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < duration)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
    }

    static void PumpUntil(Func<bool> complete, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!complete() && timer.Elapsed < TimeSpan.FromSeconds(20))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        Check(complete(), message);
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

    static T Create<T>() where T : NSObject
    {
        var handle = Send(Send(new Class(typeof(T)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        return Runtime.GetNSObject<T>(handle);
    }

    static T Outlet<T>(DataProcessingViewControllet controller, string name) =>
        (T)typeof(DataProcessingViewControllet).GetProperty(name, Instance).GetValue(controller);

    static void Expect(bool condition, string message)
    {
        if (!condition) failures.Add(message);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class ForeignOperation : IDisposable
    {
        ProcessingActivity.ProcessingLease lease;
        readonly BlockingSolver solver;
        readonly Task worker;

        public ForeignOperation(bool actualFit)
        {
            if (actualFit)
            {
                solver = new BlockingSolver();
                worker = Task.Run(() => solver.Solve());
                PumpUntil(() => solver.Entered.Task.IsCompleted, "solver did not enter its actual Solve worker");
                Check(solver.Operation != null, "actual Solve worker did not acquire admission");
            }
            else
            {
                lease = ProcessingActivity.TryBegin();
                Check(lease != null, "fixture could not acquire unrelated operation");
            }
            Check(ProcessingActivity.Current == null && ProcessingActivity.IsBusy,
                "appearance context inherited the unrelated calculation owner");
        }

        public void Dispose()
        {
            if (lease != null) { lease.Dispose(); lease = null; }
            if (solver != null)
            {
                solver.Release.Set();
                PumpUntil(() => worker.IsCompleted, "actual Solve worker did not finish after release");
                worker.GetAwaiter().GetResult();
            }
        }
    }

    sealed class BlockingSolver : SolverInterface
    {
        public readonly TaskCompletionSource<bool> Entered = new TaskCompletionSource<bool>();
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
        public BlockingSolver() { Silent = true; ErrorEstimationMethod = ErrorEstimationMethod.None; }
        protected override SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            Entered.TrySetResult(true);
            if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("The native test did not release its solver.");
            return null;
        }
    }
}
