using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using AnalysisITC.Platform.MacOS;

static class UpdateResultStatusTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var savedStep = SolverUpdate.Step;
        var savedTotal = SolverUpdate.TotalSteps;
        var savedDispatcher = PlatformServices.MainThreadDispatcher;
        try
        {
            PlatformServices.RegisterMainThreadDispatcher(new MacMainThreadDispatcher());
            using (var status = CreateController<StatusBarViewController>())
            using (var analysis = CreateController<DataAnalysisViewController>())
            {
                var primary = Outlet<NSTextField>(status, "StatusLabel");
                var secondary = Outlet<NSTextField>(status, "DocumentStatusLabel");
                var progress = Outlet<NSProgressIndicator>(status, "ProgressIndicator");

                CheckProgress(primary, secondary, progress);
                CheckMemberNames(primary);
                CheckAdmission(analysis, primary);
                CheckStaleProgress(primary, secondary);
                CheckToolControls();

                CheckResultFeedback(primary);
            }
            Console.WriteLine("PASS: native progress and both Update Result entry points report accepted, retained and failed updates");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            StatusBarManager.ClearAppStatus();
            StatusBarManager.SetDefaultSecondaryStatus("");
            SolverUpdate.Step = savedStep;
            SolverUpdate.TotalSteps = savedTotal;
            PlatformServices.RegisterMainThreadDispatcher(savedDispatcher);
        }
    }

    static void CheckProgress(NSTextField primary, NSTextField secondary, NSProgressIndicator indicator)
    {
        foreach (var method in new[] { ErrorEstimationMethod.BootstrapResiduals,
            ErrorEstimationMethod.LeaveOneOut, ErrorEstimationMethod.ProfileLikelihood })
            foreach (var termination in new[] { SolverTermination.Converged,
                SolverTermination.Cancelled, SolverTermination.Failed })
                using (var activity = ProcessingActivity.TryBegin())
                using (activity.Enter())
                {
                    StatusBarManager.ClearAppStatus();
                    StatusBarManager.SetDefaultSecondaryStatus("Example project");
                    var solver = new SolverInterface { ErrorEstimationMethod = method, BootstrapIterations = 100 };
                    solver.Analyze();
                    Pump();
                    Check(primary.StringValue == "Fitting…", "The fitting phase was not presented");
                    var initialFraction = StatusBarManager.Progress;
                    solver.ReportAnalysisStepFinished();
                    Pump();
                    Check(primary.StringValue == "Fitting…" && StatusBarManager.Progress == initialFraction,
                        "A primary-fit completion invented a phase or changed the native indicator");
                    Publish(solver, FittingPhase.AssessingBinding, null, null, null);
                    Pump();
                    Check(primary.StringValue == "Assessing binding…" && StatusBarManager.Progress == initialFraction,
                        "Assessment text reset the native indicator");
                    Publish(solver, FittingPhase.EstimatingUncertainty, null, null, null);
                    var counter = method == ErrorEstimationMethod.BootstrapResiduals ? "Bootstrap 40/100"
                        : method == ErrorEstimationMethod.LeaveOneOut ? "Leave-one-out 2/5" : "Profile searches 3/8";
                    if (method == ErrorEstimationMethod.BootstrapResiduals) solver.ReportBootstrapProgress(40);
                    else if (method == ErrorEstimationMethod.LeaveOneOut) solver.ReportLeaveOneOutProgress(2, 5);
                    else Publish(solver, FittingPhase.EstimatingUncertainty, 3, 8, 0.375f);
                    Pump();
                    Check(primary.StringValue == "Estimating uncertainty…" && secondary.StringValue == counter,
                        method + " phase or counter missing");
                    Check(Math.Abs(indicator.DoubleValue - (method == ErrorEstimationMethod.ProfileLikelihood ? 37.5 : 40)) < 0.00001,
                        method + " progress fraction changed");
                    StatusBarManager.SetDefaultSecondaryStatus("Example project [M]");
                    Check(secondary.StringValue == counter, "Project name replaced the active counter");
                    solver.ReportAnalysisFinished(Convergence(termination));
                    Pump();
                    Check(secondary.StringValue == "Example project [M]" && indicator.Hidden,
                        "Completion did not restore the project status and clear progress");
                    Check(!primary.StringValue.Contains("RMSD") && !primary.StringValue.Contains("iterations"),
                        "Routine completion contains fit diagnostics");
                }

        using (var activity = ProcessingActivity.TryBegin())
        using (activity.Enter())
        {
            var solver = new SolverInterface();
            solver.Analyze();
            solver.ReportAnalysisFinished(null);
            Pump();
            Check(indicator.Hidden && primary.StringValue.StartsWith("Analysis failed:"),
                "Missing completion did not produce a descriptive failure");
        }
    }

    static void Publish(SolverInterface solver, FittingPhase phase, int? completed, int? total, float? fraction)
    {
        typeof(SolverInterface).GetMethod("PublishProgress", Instance).Invoke(solver,
            new object[] { phase, completed, total, fraction, 0, null });
    }

    static void CheckAdmission(DataAnalysisViewController analysis, NSTextField primary)
    {
        var button = Outlet<NSButton>(analysis, "FitSimplexButton");
        var draft = Outlet<NSPopUpButton>(analysis, "ModelTypeControl");
        var label = new NSTextField { StringValue = "Analysis label", ToolTip = "Keep this tooltip", Enabled = true };
        Outlet<NSTabView>(analysis, "AnalysisInspectorTabView").AddSubview(label);
        var draftEnabled = draft.Enabled;
        var draftTooltip = draft.ToolTip;
        using (var activity = ProcessingActivity.TryBegin())
        using (activity.Enter())
        {
            Pump();
            Check(!button.Enabled && button.ToolTip == FittingStatusPresentation.BusyTooltip,
                "Run Fit was not disabled with the shared busy tooltip");
            Check(draft.Enabled == draftEnabled && draft.ToolTip == draftTooltip
                && label.Enabled && label.ToolTip == "Keep this tooltip",
                "unrelated work changed analysis drafts or label availability");
            var before = primary.StringValue;
            typeof(DataAnalysisViewController).GetMethod("RunFit", Instance).Invoke(analysis, null);
            Check(primary.StringValue == before, "A refused fit changed the current operation's status");
            Check(ProcessingActivity.TryBegin() == null, "A second native start was admitted");
        }
        Pump();
        Check(button.ToolTip != FittingStatusPresentation.BusyTooltip, "The idle tooltip was not restored");
    }

    static void CheckToolControls()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var first = CreateResult().Solution.Solutions[0].Data;
        var second = CreateResult().Solution.Solutions[0].Data;
        first.Instrument = second.Instrument = AppSettings.DefaultDesignerInstrument;
        first.DataPoints.Add(new DataPoint(0, 0));
        first.DataPoints.Add(new DataPoint(1, 0));
        second.DataPoints.Add(new DataPoint(0, 0));
        second.DataPoints.Add(new DataPoint(1, 0));
        first.Processor.InitializeBaseline(AnalysisITC.Core.Processing.BaselineInterpolatorTypes.Polynomial);
        DataManager.AddData(new[] { first, second });
        DataManager.SelectIndex(0);
        using (var merger = CreateController<ExperimentMergerViewController>())
        using (var popover = CreateController<BaselineOptionsPopoverViewController>())
        {
            var cancel = new NSButton { Title = "Cancel", Action = new Selector("dismissController:"),
                Target = merger, Enabled = true, ToolTip = "Dismiss this tool" };
            merger.View.AddSubview(cancel);
            var label = Outlet<NSTextField>(merger, "DeadVolLabel");
            var draft = Outlet<NSTextField>(merger, "DeadVolumeTextField");
            label.ToolTip = "Dead volume meaning";
            var labelEnabled = label.Enabled;
            var draftEnabled = draft.Enabled;
            var copyAll = new NSButton { Title = "Copy to All", Action = new Selector("CopyToAllAction:"), Target = popover,
                Enabled = true, ToolTip = "Copy processing" };
            var copyRemaining = new NSButton { Title = "Copy to Non-Processed", Action = new Selector("CopyToNonProcessed:"),
                Target = popover, Enabled = true, ToolTip = "Copy unprocessed data" };
            var popoverLabel = new NSTextField { StringValue = "Processing settings", Enabled = true, ToolTip = "Keep label tooltip" };
            popover.View.AddSubview(copyAll);
            popover.View.AddSubview(copyRemaining);
            popover.View.AddSubview(popoverLabel);
            popover.ViewDidAppear();
            Check(Outlet<NSButton>(popover, "ToSplineButton").Enabled, "Polynomial conversion should initially be available");
            using (var activity = ProcessingActivity.TryBegin())
            using (activity.Enter())
            {
                Pump();
                Check(!Outlet<NSButton>(merger, "MergeButtonControl").Enabled
                    && cancel.Enabled && cancel.ToolTip == "Dismiss this tool"
                    && label.Enabled == labelEnabled && label.ToolTip == "Dead volume meaning" && draft.Enabled == draftEnabled,
                    "unrelated work locked merger drafts, labels or Cancel");
                Check(!copyAll.Enabled && !copyRemaining.Enabled && copyAll.ToolTip == FittingStatusPresentation.BusyTooltip,
                    "popover copy starts remained available during another calculation");
                Check(popoverLabel.Enabled && popoverLabel.ToolTip == "Keep label tooltip",
                    "unrelated work changed a popover label");
                first.Processor.InitializeBaseline(AnalysisITC.Core.Processing.BaselineInterpolatorTypes.Spline);
            }
            Pump();
            Check(copyAll.Enabled && copyRemaining.Enabled && copyAll.ToolTip == "Copy processing",
                "popover starts did not restore idle tooltips");
            Check(!Outlet<NSButton>(popover, "ToSplineButton").Enabled,
                "popover restored stale conversion eligibility after processing changed");
        }
        DataManager.Clear(DataClearMode.ResetSession);
        DocumentDirtyTracker.MarkClean();
    }

    static void CheckMemberNames(NSTextField primary)
    {
        var previous = FittingStatusPresentation.ShowExperimentNames;
        using (var activity = ProcessingActivity.TryBegin())
        using (activity.Enter())
        {
            var solver = new SolverInterface();
            solver.Analyze();
            var handlers = (EventHandler<FittingProgress>)typeof(SolverInterface)
                .GetField("ProgressChanged", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var longName = "Long sample " + new string('x', 350);
            try
            {
                FittingStatusPresentation.ShowExperimentNames = true;
                handlers(solver, new FittingProgress(activity.Operation.Id, FittingPhase.Fitting,
                    FittingScope.Individual, 2, 3, longName));
                Pump();
                Check(primary.StringValue == "Fitting 2 of 3: " + longName,
                    "The native status did not keep action and original position before the long name");
                FittingStatusPresentation.ShowExperimentNames = false;
                handlers(solver, new FittingProgress(activity.Operation.Id, FittingPhase.Fitting,
                    FittingScope.Individual, 2, 3, longName));
                Pump();
                Check(primary.StringValue == "Fitting 2 of 3", "Disabling names left a suffix or colon");
                FittingStatusPresentation.ShowExperimentNames = true;
                handlers(solver, new FittingProgress(activity.Operation.Id, FittingPhase.Fitting,
                    FittingScope.Global, 2, 3, longName));
                Pump();
                Check(primary.StringValue == "Fitting 3 experiments globally…", "A global fit named one member");
                solver.ReportAnalysisFinished(Convergence(SolverTermination.Converged));
            }
            finally { FittingStatusPresentation.ShowExperimentNames = previous; }
        }
    }

    static void CheckStaleProgress(NSTextField primary, NSTextField secondary)
    {
        var dispatcher = PlatformServices.MainThreadDispatcher;
        var queued = new QueuedDispatcher();
        try
        {
            PlatformServices.RegisterMainThreadDispatcher(queued);
            using (var activity = ProcessingActivity.TryBegin())
            using (activity.Enter())
            {
                var old = new SolverInterface { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
                old.Analyze();
                old.ReportBootstrapProgress(40);
                old.ReportAnalysisFinished(Convergence(SolverTermination.Converged));
            }
            using (var activity = ProcessingActivity.TryBegin())
            using (activity.Enter())
            {
                var current = new SolverInterface();
                current.Analyze();
                queued.Drain();
                Pump();
                Check(primary.StringValue == "Fitting…", "A queued older operation replaced the current status");
                current.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
                current.ReportBootstrapProgress(40);
                SolverInterface.TerminateAnalysisFlag.Raise();
                Publish(current, FittingPhase.Stopping, null, null, null);
                queued.Drain();
                Pump();
                Check(primary.StringValue == FittingStatusPresentation.StoppingAnalysis,
                    "A queued pre-Stop snapshot restored an earlier phase");
                Check(!secondary.StringValue.StartsWith("Bootstrap "), "A queued pre-Stop counter was restored");
                Check(Math.Abs(StatusBarManager.Progress - 0.4) < 0.00001,
                    "Stop changed an existing numeric progress fraction");
                current.ReportAnalysisFinished(Convergence(SolverTermination.Converged));
                queued.Drain();
            }
        }
        finally { PlatformServices.RegisterMainThreadDispatcher(dispatcher); }
    }

    sealed class QueuedDispatcher : IMainThreadDispatcher
    {
        readonly Queue<Action> pending = new Queue<Action>();
        public void Invoke(Action action) { pending.Enqueue(action); }
        public void Drain() { while (pending.Count > 0) pending.Dequeue()(); }
    }

    static void CheckResultFeedback(NSTextField primary)
    {
        foreach (var entry in new[] { "results", "menu" })
            foreach (var outcome in new[] { "accepted", "cancelled", "failed", "preparation-failed" })
            {
                DataManager.Clear(DataClearMode.ResetSession);
                ClearPreviousParameters();
                StatusBarManager.ClearAppStatus();
                var result = CreateResult();
                if (outcome != "preparation-failed") DataManager.AddData(result.Solution.Solutions[0].Data);
                DataManager.AddData(result);
                if (outcome != "preparation-failed") DataManager.LoadResultSolutionsToExperiments(result);
                DataManager.SelectIndex(DataManager.SourceItems.ToList().IndexOf(result));
                DocumentDirtyTracker.MarkClean();
                var stamp = SaveStamp();
                var original = result.Solution;
                var messages = new List<string>();
                EventHandler<string> status = (_, message) => messages.Add(message);
                EventHandler<SolverConvergence> complete = (_, convergence) =>
                {
                    if (outcome == "cancelled")
                        typeof(SolverConvergence).GetMethod("SetErrorEstimationOutcome", Instance)
                            .Invoke(convergence, new object[] { ErrorEstimationOutcome.Cancelled });
                    if (outcome == "failed")
                        typeof(SolverConvergence).GetMethod("MarkInvalidValues", Instance)
                            .Invoke(convergence, new object[] { "Controlled primary failure" });
                };
                StatusBarManager.StatusUpdated += status;
                SolverInterface.AnalysisFinished += complete;
                try
                {
                    if (entry == "results")
                    {
                        using (var controller = CreateController<AnalysisResultTabViewController>())
                        {
                            var temperature = controller.GetType().GetField("evaluationTemperatureText", Instance);
                            temperature.SetValue(controller, "17");
                            RefuseConcurrentUpdate(controller, "UpdateResultAsync", result, primary);
                            Wait((Task)controller.GetType().GetMethod("UpdateResultAsync", Instance).Invoke(controller, null));
                            if (outcome != "accepted")
                                Check((string)temperature.GetValue(controller) == "17", "A retained/failed update reset the evaluation temperature");
                        }
                    }
                    else
                    {
                        using (var controller = (MainWindowController)Create(typeof(MainWindowController)))
                        {
                            RefuseConcurrentUpdate(controller, "UpdateSelectedResult", result, primary);
                            Wait((Task)controller.GetType().GetMethod("UpdateSelectedResult", Instance).Invoke(controller, null));
                        }
                    }

                    if (outcome == "accepted")
                    {
                        Check(!ReferenceEquals(original, result.Solution), entry + " did not replace the result");
                        Check(messages.Any(message => message.StartsWith("Result updated in ")),
                            entry + " did not report the accepted fit");
                    }
                    else
                    {
                        Check(ReferenceEquals(original, result.Solution), entry + " replaced a retained/failed result");
                        Check(stamp.Equals(SaveStamp()), entry + " changed retained-result state");
                        Check(messages.Any(message => outcome == "cancelled"
                            ? message.StartsWith("Profile result retained:") && message.Contains("cancelled")
                            : message.StartsWith("Result update failed:") && (outcome == "preparation-failed" || message.Contains("Controlled primary failure"))),
                            entry + " omitted the retention/failure status");
                        Check(primary.StringValue.StartsWith(outcome == "cancelled" ? "Profile result retained:" : "Result update failed:"),
                            entry + " hid the completion status behind an earlier message");
                        Check(!messages.Any(message => message.StartsWith("Result updated in ")),
                            entry + " announced a replacement for a retained/failed update");
                    }
                }
                finally
                {
                    StatusBarManager.StatusUpdated -= status;
                    SolverInterface.AnalysisFinished -= complete;
                    DataManager.Clear(DataClearMode.ResetSession);
                    ClearPreviousParameters();
                    DocumentDirtyTracker.MarkClean();
                    StatusBarManager.ClearAppStatus();
                }
            }
    }

    static void RefuseConcurrentUpdate(object controller, string method, AnalysisResult result, NSTextField primary)
    {
        var original = result.Solution;
        var stamp = SaveStamp();
        using (var activity = ProcessingActivity.TryBegin())
        using (activity.Enter())
        {
            Pump();
            if (controller is AnalysisResultTabViewController)
            {
                var button = controller.GetType().GetField("updateResultControl", Instance).GetValue(controller) as NSButton;
                Check(button != null && !button.Enabled && button.ToolTip == FittingStatusPresentation.BusyTooltip,
                    "Update Result did not explain its disabled busy state");
            }
            var status = primary.StringValue;
            var refused = (Task)controller.GetType().GetMethod(method, Instance).Invoke(controller, null);
            Check(refused.IsCompleted, "A competing update was queued implicitly");
            refused.GetAwaiter().GetResult();
            Check(ReferenceEquals(result.Solution, original) && stamp.Equals(SaveStamp()),
                "A refused update changed stored result state");
            Check(primary.StringValue == status, "A refused update replaced the active status");
        }
    }

    static AnalysisResult CreateResult()
    {
        var data = new ExperimentData("update-feedback.itc")
        {
            CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25,
        };
        for (var index = 0; index < 8; index++)
        {
            var injection = new InjectionData(data, volume: 1e-6) { IsIntegrated = true, Ratio = index + 1 };
            injection.SetPeakArea(new FloatWithError(2e-7 + 1e-8 * Math.Sin(index + 1), 1e-8));
            data.Injections.Add(injection);
        }
        var model = new Offset(data);
        model.InitializeParameters(data);
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood };
        model.Solution = SolutionInterface.FromModel(model, Convergence(SolverTermination.Converged));
        model.Solution.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(
            new Solver { Model = model, ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood }));
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        return result;
    }

    static void Wait(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(20)) Pump();
        Check(task.IsCompleted, "Update Result timed out");
        task.GetAwaiter().GetResult();
        Pump();
    }

    static object SaveStamp() => typeof(DocumentDirtyTracker)
        .GetMethod("CaptureSaveStamp", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    static void ClearPreviousParameters() => typeof(GlobalModelFactory)
        .GetMethod("ClearPreviousParameters", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    static T CreateController<T>() where T : NSViewController
    {
        var controller = (T)Create(typeof(T));
        controller.View = new NSView(new CGRect(0, 0, 800, 600));
        foreach (var outlet in typeof(T).GetProperties(Instance))
            if (outlet.GetCustomAttribute<OutletAttribute>() != null)
            {
                var control = (NSView)Create(outlet.PropertyType);
                outlet.SetValue(controller, control);
                controller.View.AddSubview(control);
                var segmented = control as NSSegmentedControl;
                if (segmented != null) { segmented.SegmentCount = 2; segmented.SelectedSegment = 0; }
                var tabs = control as NSTabView;
                if (tabs != null)
                    for (var index = 0; index < 4; index++) tabs.Add(new NSTabViewItem());
            }
        if (controller is ExperimentMergerViewController)
        {
            var slider = Outlet<NSSlider>(controller, "BackMixingSliderControl");
            slider.Action = new Selector("takeFloatValueFrom:");
            slider.Target = Outlet<NSTextField>(controller, "BackMixFracLabel");
        }
        controller.ViewDidLoad();
        return controller;
    }

    static NSObject Create(Type type) => Runtime.GetNSObject(
        Send(Send(new Class(type).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init")));

    static T Outlet<T>(NSViewController controller, string name) =>
        (T)controller.GetType().GetProperty(name, Instance).GetValue(controller);

    static SolverConvergence Convergence(SolverTermination termination) =>
        SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot { Termination = termination });

    static void Pump() => NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.15));

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
