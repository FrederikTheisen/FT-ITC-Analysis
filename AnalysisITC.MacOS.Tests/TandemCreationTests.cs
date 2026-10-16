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
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Platform;
using AnalysisITC.Platform.MacOS;

// Drives the native action and real sheet with Cocoa continuations, without an app bundle.
static class TandemCreationTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> failures = new List<string>();
    static readonly List<Exception> escaped = new List<Exception>();
    static readonly Notifications notifications = new Notifications();

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var dispatcher = PlatformServices.MainThreadDispatcher;
        var messages = AppEventHandler.ShowAppMessage;
        var notificationService = PlatformServices.AppNotificationService;
        var context = SynchronizationContext.Current;
        try
        {
            PlatformServices.RegisterMainThreadDispatcher(new MacMainThreadDispatcher());
            PlatformServices.RegisterAppNotificationService(notifications);
            var cocoaContext = context ?? (SynchronizationContext)Activator.CreateInstance(
                typeof(NSApplication).Assembly.GetType("AppKit.AppKitSynchronizationContext", true), true);
            SynchronizationContext.SetSynchronizationContext(new CapturingContext(cocoaContext));
            Check(NSThread.IsMain, "harness must run on the main thread");
            DocumentDirtyTracker.Initialize();
            CreationFailure(mode: 2, criterion: 0);
            CreationFailure(mode: 2, criterion: 1);
            CreationFailure(mode: 0, criterion: 0);
            CreationFailure(mode: 1, criterion: 0);
            SuccessfulCreation(0);
            SuccessfulCreation(1);
            foreach (var error in escaped) failures.Add("Exception escaped native action: " + error);
            foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
            if (failures.Count != 0) return 1;
            Console.WriteLine("PASS: native tandem validation/processing failures retain admission through handled errors, preserve drafts and allow retry; simple/fixed creation publishes, notifies and dismisses");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            AppEventHandler.ShowAppMessage = messages;
            PlatformServices.RegisterMainThreadDispatcher(dispatcher);
            PlatformServices.RegisterAppNotificationService(notificationService);
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    static void CreationFailure(int mode, int criterion)
    {
        using (var fixture = new Fixture(mode, criterion))
        {
            var controller = fixture.Controller;
            var draft = Draft(controller);
            var revision = Revision();
            var controls = controller.View.Subviews.OfType<NSControl>().ToDictionary(c => c, c => c.Enabled);
            var automatic = mode == 2;
            var prefix = "mode=" + mode + ", criterion=" + criterion;
            var processingCompletions = 0;
            EventHandler completed = (sender, _) =>
            {
                var data = sender as ExperimentData;
                if (automatic || data == null || !data.IsTandemExperiment) return;
                // IntegratePeaksCore and DidProcessDataCore each publish. Throw
                // from the latter, after the awaited baseline computation.
                if (++processingCompletions % 2 == 0)
                    throw new InvalidOperationException("Injected tandem processing publication failure");
            };
            DataProcessor.ProcessingCompleted += completed;
            try
            {
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    var dialogs = 0;
                    var following = Experiment("following");
                    following.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
                    ((PolynomialLeastSquaresInterpolator)following.Processor.Interpolator).Degree = 0;
                    Task followingTask = null;
                    var errorsBefore = escaped.Count;
                    AppEventHandler.ShowAppMessage = (sender, error) =>
                    {
                        dialogs++;
                        var operation = sender as ProcessingOperation;
                        Expect(NSThread.IsMain, prefix + ": error presentation left the main thread");
                        Expect(operation != null && ReferenceEquals(operation, ProcessingActivity.Current)
                            && ProcessingActivity.IsCurrentOperation(operation.Id) && ProcessingActivity.IsBusy,
                            prefix + ": error presentation lost operation ownership");
                        Expect(IsCreating(controller) && !Outlet<NSButton>(controller, "MergeButtonControl").Enabled,
                            prefix + ": creation controls restored before error presentation");
                        if (automatic)
                            Expect(controls.Keys.All(c => !c.Enabled), prefix + ": automatic scan controls restored too early");
                        Expect(error.Message.Contains(automatic
                            ? "All included injections must be integrated"
                            : "Injected tandem processing publication failure"), prefix + ": wrong handled error");
                        Expect(ReferenceEquals(controller.PresentingViewController, fixture.Presenter),
                            prefix + ": merger closed on failure");
                        Expect(followingTask != null && !followingTask.IsCompleted,
                            prefix + ": following work was not queued before presentation");
                        PumpFor(TimeSpan.FromMilliseconds(75));
                        Expect(ReferenceEquals(operation, ProcessingActivity.Current) && ProcessingActivity.IsBusy
                            && followingTask != null && !followingTask.IsCompleted
                            && !following.Processor.BaselineCompleted && !following.Processor.IntegrationCompleted,
                            prefix + ": following work ran inside the error's nested event loop");
                        Expect(IsCreating(controller) && !Outlet<NSButton>(controller, "MergeButtonControl").Enabled,
                            prefix + ": nested event loop restored creation eligibility");
                    };

                    fixture.Create();
                    // This call is outside the async action's execution context,
                    // so it queues as an unrelated operation instead of joining.
                    Check(ProcessingActivity.Current == null && ProcessingActivity.IsBusy,
                        prefix + ": fixture did not start an independent action");
                    followingTask = following.Processor.ProcessData();
                    Check(!followingTask.IsCompleted, prefix + ": following work did not wait for creation");
                    PumpUntil(() => !ProcessingActivity.IsBusy && followingTask.IsCompleted, prefix + ": work did not drain");
                    followingTask.GetAwaiter().GetResult();
                    PumpFor(TimeSpan.FromMilliseconds(25));
                    Expect(dialogs == 1 && escaped.Count == errorsBefore,
                        prefix + ": expected exactly one handled error without an escaping exception, attempt=" + attempt);
                    Expect(!IsCreating(controller) && controls.All(c => c.Key.Enabled == c.Value),
                        prefix + ": original control availability was not restored");
                    Expect(Draft(controller) == draft, prefix + ": failure changed selections or settings");
                    Expect(DataManager.Data.SequenceEqual(fixture.Sources)
                        && !DocumentDirtyTracker.IsDirty && Revision() == revision,
                        prefix + ": failed creation published data or changed document dirty state");
                    Expect(fixture.Sources.All(s => !s.Processor.BaselineCompleted && s.Injections.All(i => !i.IsIntegrated)),
                        prefix + ": creation processed its raw sources");
                    Expect(ReferenceEquals(controller.PresentingViewController, fixture.Presenter)
                        && notifications.Count == fixture.InitialNotifications,
                        prefix + ": failed creation dismissed the sheet or sent success notification");
                    Expect(following.Processor.BaselineCompleted && following.Processor.IntegrationCompleted,
                        prefix + ": queued work did not finish after error presentation");
                    Console.WriteLine(prefix + ", attempt=" + attempt + ": dialogs=" + dialogs
                        + ", escaped=" + (escaped.Count - errorsBefore) + ", queued processing completed");
                }
                if (!automatic)
                    Expect(processingCompletions == 4, prefix + ": processing failure did not reach both awaited completion publications");
            }
            finally { DataProcessor.ProcessingCompleted -= completed; }
        }
    }

    static void SuccessfulCreation(int mode)
    {
        using (var fixture = new Fixture(mode, 0))
        {
            var dialogs = 0;
            AppEventHandler.ShowAppMessage = (_, error) => { dialogs++; failures.Add("Unexpected success-path error: " + error.Message); };
            fixture.Create();
            PumpUntil(() => !ProcessingActivity.IsBusy && !IsCreating(fixture.Controller), "successful creation did not finish");
            PumpUntil(() => fixture.Controller.PresentingViewController == null, "successful creation did not dismiss sheet");
            var merged = DataManager.Data.Except(fixture.Sources).Single();
            Expect(dialogs == 0 && merged.IsTandemExperiment && merged.Processor.BaselineCompleted
                && merged.Processor.IntegrationCompleted && DocumentDirtyTracker.IsDirty,
                "mode=" + mode + ": successful tandem was not processed and published");
            Expect(merged.TandemSourceExperimentIds.SequenceEqual(fixture.Sources.Select(s => s.UniqueID)),
                "mode=" + mode + ": successful tandem lost source order");
            Expect(notifications.Count == fixture.InitialNotifications + 1
                && notifications.Title == "Tandem mixing finished" && notifications.Message == "The merged experiment is ready.",
                "mode=" + mode + ": successful tandem did not send its completion notification");
            Console.WriteLine("mode=" + mode + ": success processed, published, notified and dismissed");
        }
    }

    sealed class Fixture : IDisposable
    {
        public readonly ExperimentData[] Sources;
        public readonly ExperimentMergerViewController Controller;
        public readonly NSViewController Presenter;
        public readonly NSWindow Window;
        public readonly int InitialNotifications = notifications.Count;

        public Fixture(int mode, int criterion)
        {
            DataManager.Clear(DataClearMode.ResetSession);
            Sources = new[] { Experiment("source A"), Experiment("source B") };
            DataManager.AddData(Sources);
            var handle = Send(Send(new Class(typeof(ExperimentMergerViewController)).Handle,
                Selector.GetHandle("alloc")), Selector.GetHandle("init"));
            Controller = Runtime.GetNSObject<ExperimentMergerViewController>(handle);
            Controller.View = new NSView(new CGRect(0, 0, 700, 500));
            foreach (var outlet in typeof(ExperimentMergerViewController).GetProperties(Instance)
                .Where(p => p.GetCustomAttribute<OutletAttribute>() != null))
            {
                var value = (NSView)Activator.CreateInstance(outlet.PropertyType);
                if (value is NSSegmentedControl) ((NSSegmentedControl)value).SegmentCount = 3;
                if (value is NSSlider) ((NSSlider)value).MaxValue = 1;
                outlet.SetValue(Controller, value);
                Controller.View.AddSubview(value);
            }
            // Matches the storyboard's slider-to-value-label connection.
            Outlet<NSSlider>(Controller, "BackMixingSliderControl").Action = new Selector("takeFloatValueFrom:");
            Outlet<NSSlider>(Controller, "BackMixingSliderControl").Target = Outlet<NSTextField>(Controller, "BackMixFracLabel");
            Controller.ViewDidLoad();
            Outlet<NSTableView>(Controller, "MergeTableView").SelectRows(NSIndexSet.FromNSRange(new NSRange(0, 2)), false);
            Outlet<NSSegmentedControl>(Controller, "MergeMethodControl").SelectedSegment = mode;
            Outlet<NSSegmentedControl>(Controller, "AutoCriterionControl").SelectedSegment = criterion;
            Outlet<NSSlider>(Controller, "BackMixingSliderControl").DoubleValue = 0.23;
            Outlet<NSTextField>(Controller, "DeadVolumeTextField").DoubleValue = 80;
            Outlet<NSButton>(Controller, "RemovedTitratedAfterExperimentControl").State = NSCellStateValue.On;
            typeof(ExperimentMergerViewController).GetMethod("MergeMethodControlAction", Instance)
                .Invoke(Controller, new object[] { Outlet<NSSegmentedControl>(Controller, "MergeMethodControl") });
            Check(Outlet<NSButton>(Controller, "MergeButtonControl").Enabled, "raw sources were not admitted by merger");
            Presenter = new NSViewController { View = new NSView(new CGRect(0, 0, 800, 600)) };
            Window = new NSWindow(new CGRect(100, 100, 800, 600), NSWindowStyle.Titled, NSBackingStore.Buffered, false);
            Window.ContentViewController = Presenter;
            Presenter.PresentViewControllerAsSheet(Controller);
            PumpUntil(() => ReferenceEquals(Controller.PresentingViewController, Presenter), "merger sheet was not presented");
            DocumentDirtyTracker.MarkClean();
        }

        public void Create() => Check(NSApplication.SharedApplication.SendAction(
            new Selector("CreateNewMergedExperimentAction:"), Controller, null), "native creation selector did not dispatch");

        public void Dispose()
        {
            if (Controller.PresentingViewController != null)
            {
                Presenter.DismissViewController(Controller);
                PumpUntil(() => Controller.PresentingViewController == null, "fixture sheet did not dismiss");
            }
            Controller.Dispose();
            Window.OrderOut(null);
            Window.Dispose();
            Presenter.Dispose();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static ExperimentData Experiment(string name)
    {
        var data = new ExperimentData(name + ".itc")
        {
            CellVolume = 1.4e-3, InitialDelay = 40,
            CellConcentration = new FloatWithError(20e-6), SyringeConcentration = new FloatWithError(200e-6)
        };
        for (int time = 0; time <= 200; time++)
            data.DataPoints.Add(new DataPoint(time, 2e-6f + (time > 40 && time <= 60 ? 4e-6f : 0), 25));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 40, 2e-6, 160, 2, 25);
        data.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(20);
        return data;
    }

    static string Draft(ExperimentMergerViewController controller) => string.Join("|", new object[]
    {
        string.Join(",", Outlet<NSTableView>(controller, "MergeTableView").SelectedRows.ToArray()),
        Outlet<NSSegmentedControl>(controller, "MergeMethodControl").SelectedSegment,
        Outlet<NSSegmentedControl>(controller, "AutoCriterionControl").SelectedSegment,
        Outlet<NSTextField>(controller, "DeadVolumeTextField").StringValue,
        Outlet<NSSlider>(controller, "BackMixingSliderControl").DoubleValue,
        Outlet<NSButton>(controller, "RemovedTitratedAfterExperimentControl").State,
    });

    static bool IsCreating(ExperimentMergerViewController controller) =>
        (bool)typeof(ExperimentMergerViewController).GetField("IsCreatingMergedExperiment", Instance).GetValue(controller);
    static T Outlet<T>(ExperimentMergerViewController controller, string name) =>
        (T)typeof(ExperimentMergerViewController).GetProperty(name, Instance).GetValue(controller);
    static long Revision()
    {
        var stamp = typeof(DocumentDirtyTracker).GetMethod("CaptureSaveStamp", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        return (long)stamp.GetType().GetProperty("Revision").GetValue(stamp);
    }
    static void PumpFor(TimeSpan duration)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < duration) NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
    }
    static void PumpUntil(Func<bool> complete, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!complete() && timer.Elapsed < TimeSpan.FromSeconds(20))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        Check(complete(), message);
    }
    static void Expect(bool condition, string message) { if (!condition) failures.Add(message); }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    // Observe async-void exceptions as test failures instead of terminating the
    // harness before it can report the regression. All callbacks still use Cocoa.
    sealed class CapturingContext : SynchronizationContext
    {
        readonly SynchronizationContext inner;
        public CapturingContext(SynchronizationContext inner) { this.inner = inner; }
        public override void Post(SendOrPostCallback callback, object state) => inner.Post(_ =>
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { callback(state); }
            catch (Exception error) { escaped.Add(error); }
            finally { SetSynchronizationContext(previous); }
        }, null);
    }

    sealed class Notifications : IAppNotificationService
    {
        public int Count;
        public string Title, Message;
        public void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string actionUrl = null) =>
            failures.Add("Unexpected info alert: " + message);
        public void ShowSystemNotification(string title, string message) => failures.Add("Unexpected unconditional notification: " + message);
        public void ShowSystemNotificationIfBackground(string title, string message) { Count++; Title = title; Message = message; }
    }
}
