// Loads the actual compiled Process-view nib; no code-created buttons or app bundle.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using AnalysisITC.Platform.MacOS;

static class ProcessingCopyButtonsTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> failures = new List<string>();
    static readonly List<string> errors = new List<string>();
    static int passed;

    static int Main(string[] args)
    {
        NSApplication.Init();
        var dispatcher = PlatformServices.MainThreadDispatcher;
        var context = SynchronizationContext.Current;
        var messages = AppEventHandler.ShowAppMessage;
        try
        {
            PlatformServices.RegisterMainThreadDispatcher(new MacMainThreadDispatcher());
            if (SynchronizationContext.Current == null)
            {
                var cocoa = typeof(NSApplication).Assembly.GetType("AppKit.AppKitSynchronizationContext", true);
                SynchronizationContext.SetSynchronizationContext((SynchronizationContext)Activator.CreateInstance(cocoa, true));
            }
            AppEventHandler.ShowAppMessage = (_, error) => errors.Add(error.Message);
            Run("baseline zoom presets and corrected display", () => BaselineZoomPresets(args[1]));
            foreach (var scope in new[] { "All", "Active", "New" })
                foreach (var locked in new[] { false, true })
                    Run(scope + ", source locked=" + locked, () => CopiesToScope(args[1], scope, locked));
            foreach (var scope in new[] { "Active", "New" })
                Run(scope + " menu", () => CopiesToScope(args[1], scope, false, menu: true));
            foreach (var selection in new[] { "no selection", "empty project", "result", "heat-only", "no baseline" })
                Run(selection, () => UnavailableSource(args[1], selection));
            foreach (var loseSelection in new[] { false, true })
                Run("busy, lose selection=" + loseSelection, () => BusyOperation(args[1], loseSelection));
            Run("no destinations", () => NoDestinations(args[1]));
            foreach (var keyboard in new[] { false, true })
                foreach (var zoomed in new[] { false, true })
                    foreach (var copyStart in new[] { false, true })
                        Run("next peak, keyboard=" + keyboard + ", zoomed=" + zoomed + ", copy start=" + copyStart,
                            () => CopyToNextPeak(args[1], keyboard, zoomed, copyStart));
            foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
            if (failures.Count != 0) return 1;
            Console.WriteLine("PASS: " + passed + " archived native processing-copy cases");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            DataManager.StopProcessCopying = false;
            StatusBarManager.ClearAppStatus();
            AppEventHandler.ShowAppMessage = messages;
            PlatformServices.RegisterMainThreadDispatcher(dispatcher);
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    static void Run(string name, Action test)
    {
        errors.Clear();
        try
        {
            test();
            Check(errors.Count == 0, string.Join("; ", errors));
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        catch (Exception error) { failures.Add(name + ": " + error); }
    }

    static void CopiesToScope(string storyboard, string scope, bool lockedSource, bool menu = false)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var source = RawExperiment("source");
        Process(source);
        if (lockedSource) source.Processor.Lock();
        var included = RawExperiment("included, processed");
        var excluded = RawExperiment("excluded, processed");
        excluded.Include = false;
        var locked = RawExperiment("locked destination");
        foreach (var data in new[] { included, excluded, locked })
        {
            data.Injections[0].SetIntegrationLengthByTime(3);
            Process(data);
            Check(Math.Abs(data.Injections[0].RawPeakArea.Value - 12) > 1,
                "destination fixture must initially differ from the expected copied heat");
        }
        locked.Processor.Lock();
        var fresh = RawExperiment("included, no baseline");
        var excludedFresh = RawExperiment("excluded, no baseline");
        fresh.Processor.Interpolator = excludedFresh.Processor.Interpolator = null;
        excludedFresh.Include = false;
        var heatOnly = HeatOnlyExperiment();
        var destinations = new[] { included, excluded, fresh, excludedFresh, locked, heatOnly };
        DataManager.AddData(destinations.Concat(new[] { source }).ToArray());
        var before = destinations.Concat(new[] { source }).ToDictionary(data => data, data => new Snapshot(data));
        using (var view = new ArchivedProcessView(storyboard))
        {
            Select(source);
            var button = CopyButton(view.Controller, scope);
            Check(button.Enabled, "a usable source should enable copying, including when locked");
            var starts = 0;
            var statuses = new List<string>();
            EventHandler activityChanged = (sender, args) => { if (ProcessingActivity.IsBusy) starts++; };
            EventHandler<string> statusChanged = (_, status) => statuses.Add(status);
            ProcessingActivity.Changed += activityChanged;
            StatusBarManager.StatusUpdated += statusChanged;
            try
            {
                if (menu)
                {
                    var options = Outlet<NSPopUpButton>(view.Controller, "ProcessingOptionsMenuButton");
                    var item = options.Menu.Items.Single(candidate => candidate.Identifier ==
                        (scope == "Active" ? "copytoactive" : "copytononprocessed"));
                    typeof(DataProcessingViewControllet).GetMethod("ProcessingOptionsMenuItemActivated", Instance)
                        .Invoke(view.Controller, new object[] { item, EventArgs.Empty });
                }
                else button.PerformClick(button);
                DrainActivity();
            }
            finally
            {
                ProcessingActivity.Changed -= activityChanged;
                StatusBarManager.StatusUpdated -= statusChanged;
            }
            Check(starts == 1, "one click should start exactly one operation, got " + starts);
            before[source].CheckUnchanged(source);
            foreach (var data in destinations)
            {
                var shouldCopy = data != locked && data != heatOnly && (scope == "All"
                    || scope == "Active" && data.Include
                    || scope == "New" && before[data].Interpolator == null);
                if (!shouldCopy) { before[data].CheckUnchanged(data); continue; }
                Check(!ReferenceEquals(before[data].Processor, data.Processor), data.Name + ": processor was not replaced");
                Check(data.Processor.BaselineCompleted && data.Processor.IntegrationCompleted,
                    data.Name + ": calculated outputs are incomplete");
                // Six one-second periods at 2 W above a 1 W baseline give exactly 12 J.
                Check(Math.Abs(data.Injections[0].RawPeakArea.Value - 12) < 1e-10, data.Name + ": expected 12 J");
                Check(data.Injections[0].IntegrationEndOffset == 6, data.Name + ": end boundary was not copied");
                Check(data.Processor.IsLocked == lockedSource, data.Name + ": copied source lock is incorrect");
            }
            Check(statuses.Any(status => status.Contains("skipped 1 experiment without a thermogram")),
                "the existing skipped-thermogram feedback was not reported");
        }
    }

    static void BaselineZoomPresets(string storyboard)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var data = new ExperimentData("baseline-zoom.itc");
        var powers = new[] { 8, 10, 100, 100, 100, 10, 12, 10 };
        for (var i = 0; i < powers.Length; i++) data.DataPoints.Add(new DataPoint(i, powers[i] * 1e-6f));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 2, 1e-6, 5, 1, 25);
        data.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(2);
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        data.Processor.DiscardIntegratedPoints = true;
        data.Processor.Interpolator.Baseline = new[] { 9, 9, 9, 10, 10, 11, 11, 11 }
            .Select(value => new AnalysisITC.Core.Units.Energy(value * 1e-6)).ToList();
        data.Processor.SubtractBaseline();
        DataManager.AddData(data);
        using (var view = new ArchivedProcessView(storyboard))
        {
            Select(data);
            var graph = Outlet<DataProcessingGraphView>(view.Controller, "BaselineGraphView");
            var control = (NSSegmentedControl)typeof(DataProcessingViewControllet)
                .GetField("baselineZoomControl", Instance).GetValue(view.Controller);
            Check(control != null && control.SegmentCount == 4 && control.SelectedSegment == 1, "default baseline preset missing");
            view.Controller.View.LayoutSubtreeIfNeeded();
            Check(control.Frame.Width >= 160 && control.Frame.Height > 0, "baseline presets are not laid out");
            Check(control.ToolTip == ProcessingZoomPresentation.BaselineZoomTooltip, "shared tooltip missing");
            var before = graph.Graph.YAxis.Min;
            control.SelectedSegment = 3;
            Check(NSApplication.SharedApplication.SendAction(control.Action, control.Target, control), "preset did not dispatch");
            Check(graph.Graph.YAxis.Min == before && graph.CurrentVerticalZoomMode == DataProcessingGraphView.VerticalZoomMode.AllData,
                "changing a preset changed the all-data view");
            graph.Graph.SetXAxisRange(0, 7);
            graph.ZoomBaseline();
            CheckZoomRange(graph, 7.5, 12.5);
            for (var i = 0; i < 4; i++)
            {
                control.SelectedSegment = i;
                Check(NSApplication.SharedApplication.SendAction(control.Action, control.Target, control), "preset did not dispatch");
                var margin = new[] { 12.0, 6.0, 3.0, 1.5 }[i];
                CheckZoomRange(graph, 9 - margin, 11 + margin);
                graph.SetFeatureVisibility(true, true, true, false);
                CheckZoomRange(graph, -margin, margin);
                graph.SetFeatureVisibility(true, true, false, false);
            }
            graph.Initialize(data);
            Check(graph.BaselineZoomLevel == 4, "initializing a graph lost the view-local zoom level");
        }
    }

    static void CheckZoomRange(DataProcessingGraphView graph, double minimum, double maximum)
    {
        Check(Math.Abs(graph.Graph.YAxis.Min - minimum * 1e-6) < 1e-10, "unexpected baseline minimum: " + graph.Graph.YAxis.Min);
        Check(Math.Abs(graph.Graph.YAxis.Max - maximum * 1e-6) < 1e-10, "unexpected baseline maximum: " + graph.Graph.YAxis.Max);
        Check(graph.CurrentVerticalZoomMode == DataProcessingGraphView.VerticalZoomMode.Baseline, "baseline mode lost");
    }

    static void UnavailableSource(string storyboard, string selection)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var source = RawExperiment("source");
        Process(source);
        var target = RawExperiment("target");
        var unavailable = selection == "heat-only" ? HeatOnlyExperiment() : RawExperiment("no baseline");
        unavailable.Processor.Interpolator = null;
        DataManager.AddData(new[] { target, unavailable, source });
        var before = new Snapshot(target);
        using (var view = new ArchivedProcessView(storyboard))
        {
            Select(source);
            switch (selection)
            {
                case "no selection": DataManager.SelectIndex(-1); break;
                case "empty project": DataManager.Clear(DataClearMode.ResetSession); break;
                case "result":
                    DataManager.AddData(new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(
                        new Solver { Model = HeatModel(source) })));
                    break;
                default: Select(unavailable); break;
            }
            foreach (var scope in new[] { "All", "Active", "New" })
            {
                var button = CopyButton(view.Controller, scope);
                Check(!button.Enabled, selection + ": button remains enabled");
                ForceDispatch(button);
            }
            Check(!ProcessingActivity.IsBusy, "unavailable source started an operation");
            before.CheckUnchanged(target);
        }
    }

    static void BusyOperation(string storyboard, bool loseSelection)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var source = RawExperiment("source");
        Process(source);
        var target = RawExperiment("target");
        DataManager.AddData(new[] { target, source });
        var before = new Snapshot(target);
        using (var view = new ArchivedProcessView(storyboard))
        {
            Select(source);
            var buttons = new[] { "All", "Active", "New" }.Select(scope => CopyButton(view.Controller, scope)).ToArray();
            var tips = buttons.Select(button => button.ToolTip).ToArray();
            using (var activity = ProcessingActivity.TryBegin())
            using (activity.Enter())
            {
                foreach (var button in buttons)
                {
                    Check(!button.Enabled && button.ToolTip == FittingStatusPresentation.BusyTooltip,
                        "busy button remained available or lost its explanation");
                    ForceDispatch(button);
                }
                if (loseSelection) DataManager.SelectIndex(-1);
                typeof(DataProcessingViewControllet).GetMethod("UpdateUI", Instance).Invoke(view.Controller, null);
                Check(buttons.All(button => !button.Enabled), "a refresh enabled copying during an operation");
                before.CheckUnchanged(target);
            }
            DrainActivity();
            Check(buttons.All(button => button.Enabled == !loseSelection), "idle availability was restored incorrectly");
            Check(buttons.Select(button => button.ToolTip).SequenceEqual(tips), "idle tooltips were not restored");
            before.CheckUnchanged(target);
            Select(source);
            Check(buttons.All(button => button.Enabled), "selecting a valid source did not restore availability");
        }
    }

    static void NoDestinations(string storyboard)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var source = RawExperiment("source");
        Process(source);
        DataManager.AddData(source);
        using (var view = new ArchivedProcessView(storyboard))
        {
            Select(source);
            var before = new Snapshot(source);
            var statuses = new List<string>();
            EventHandler<string> changed = (_, status) => statuses.Add(status);
            StatusBarManager.StatusUpdated += changed;
            try
            {
                foreach (var scope in new[] { "All", "Active", "New" })
                {
                    statuses.Clear();
                    var button = CopyButton(view.Controller, scope);
                    Check(button.Enabled, "a valid source with no destinations should remain available");
                    button.PerformClick(button);
                    DrainActivity();
                    Check(statuses.Contains("No experiments available for processing copy"), "no-destination feedback is missing");
                    before.CheckUnchanged(source);
                }
            }
            finally { StatusBarManager.StatusUpdated -= changed; }
        }
    }

    static void ForceDispatch(NSButton button) =>
        Check(NSApplication.SharedApplication.SendAction(button.Action, button.Target, button), "archived selector did not dispatch");

    static void CopyToNextPeak(string storyboard, bool keyboard, bool zoomed, bool copyStart)
    {
        var savedCopyStart = AppSettings.IntegrationRegionCopyIncludesStart;
        var savedState = StateManager.CurrentState;
        try
        {
            AppSettings.IntegrationRegionCopyIncludesStart = copyStart;
            DataManager.Clear(DataClearMode.ResetSession);
            var data = RawExperiment("two peaks");
            var next = InjectionData.FromPEAQFile(data, 1, true, 30, 2e-6, 10, 2, 25);
            next.SetIntegrationLengthByTime(5);
            next.SetIntegrationStartTime(2);
            data.Injections.Add(next);
            data.Injections[0].SetIntegrationStartTime(1);
            Process(data);
            DataManager.AddData(data);
            StateManager.SetProgramState(ProgramState.Process);
            using (var view = new ArchivedProcessView(storyboard))
            {
                Select(data);
                var graph = Outlet<DataProcessingGraphView>(view.Controller, "BaselineGraphView");
                var button = Outlet<NSButton>(view.Controller, "CopyToNextButton");
                graph.SelectedPeak = 0;
                if (zoomed) graph.FocusPeak();
                else graph.UnfocusPeak();
                Check(button.Enabled, "ordinary nonfinal selection must enable copying");
                Check(graph.IsInjectionZoomed == zoomed, "fixture zoom state is incorrect");
                Check(next.IntegrationStartDelay == 2 && next.IntegrationEndOffset == 5,
                    "destination fixture boundaries are incorrect");
                DispatchNextPeak(view.Controller, graph, keyboard);
                DrainActivity();
                Check(graph.SelectedPeak == 1, "copy did not advance to the next peak");
                Check(next.IntegrationEndOffset == 6, "selected end boundary was not copied");
                Check(next.IntegrationStartDelay == (copyStart ? 1 : 2), "start boundary option was not respected");
                Check(data.Injections[0].IntegrationStartDelay == 1 && data.Injections[0].IntegrationEndOffset == 6,
                    "copy changed the source boundaries");
                Check(graph.IsInjectionZoomed, "copy should retain its existing focus-on-next behavior");
                Check(!button.Enabled, "copy must be disabled at the final peak");
                var revision = Revision(data);
                DispatchNextPeak(view.Controller, graph, keyboard);
                DrainActivity();
                Check(graph.SelectedPeak == 1 && Revision(data) == revision, "final peak must not copy onto itself");
                graph.SelectedPeak = -1;
                DispatchNextPeak(view.Controller, graph, keyboard);
                DrainActivity();
                Check(graph.SelectedPeak == -1 && Revision(data) == revision, "copy without a selection must do nothing");
            }
        }
        finally
        {
            AppSettings.IntegrationRegionCopyIncludesStart = savedCopyStart;
            StateManager.SetProgramState(savedState);
        }
    }

    static void DispatchNextPeak(DataProcessingViewControllet controller, DataProcessingGraphView graph, bool keyboard)
    {
        if (!keyboard) ForceDispatch(Outlet<NSButton>(controller, "CopyToNextButton"));
        else
            using (var input = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, (NSEventModifierMask)0,
                0, 0, null, " ", " ", false, (ushort)NSKey.Space))
                typeof(DataProcessingGraphView).GetMethod("KeyDownEventHandler", Instance).Invoke(graph, new object[] { input });
    }

    static NSButton CopyButton(DataProcessingViewControllet controller, string scope)
    {
        var button = Buttons(controller).Single(control => control.Title == scope);
        Check(ReferenceEquals(button, Outlet<NSButton>(controller, "CopyProcessingTo" + scope + "Button")),
            scope + ": outlet does not reference the visible button");
        Check(ReferenceEquals(button.Target, controller) && button.Action != null, scope + ": archived action is missing");
        var expected = scope == "All" ? ProcessingCopyPresentation.AllTooltip
            : scope == "Active" ? ProcessingCopyPresentation.ActiveTooltip : ProcessingCopyPresentation.NewTooltip;
        Check(button.ToolTip == expected, scope + ": shared tooltip was not installed");
        return button;
    }

    static T Outlet<T>(DataProcessingViewControllet controller, string name) =>
        (T)typeof(DataProcessingViewControllet).GetProperty(name, Instance).GetValue(controller);

    static int Revision(ExperimentData data) =>
        (int)typeof(ExperimentData).GetProperty("ProcessingRevision", Instance).GetValue(data);

    static void Select(ExperimentData data) => DataManager.SelectIndex(DataManager.SourceItems.ToList().IndexOf(data));

    static void Process(ExperimentData data) =>
        Task.Run(() => data.Processor.ProcessData(showProgress: false)).GetAwaiter().GetResult();

    static ExperimentData HeatOnlyExperiment()
    {
        var data = new ExperimentData("heat-only.dh") { Include = true };
        var injection = new InjectionData(data, 2e-6f, 20, 0, 2);
        injection.InitializeIntegrationTimes();
        injection.SetPeakArea(new FloatWithError(-7e-6, 1e-8));
        data.Injections.Add(injection);
        data.UpdateSolution(HeatModel(data));
        return data;
    }

    static Offset HeatModel(ExperimentData data)
    {
        var model = new Offset(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(
            new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
        return model;
    }

    sealed class Snapshot
    {
        public readonly DataProcessor Processor;
        public readonly BaselineInterpolator Interpolator;
        readonly double heat;
        readonly bool integrated;
        readonly int revision;
        readonly object solution;
        readonly bool valid;
        readonly double end;
        readonly bool locked;
        public Snapshot(ExperimentData data)
        {
            Processor = data.Processor;
            Interpolator = Processor.Interpolator;
            heat = data.Injections[0].RawPeakArea.Value;
            integrated = data.Injections[0].IsIntegrated;
            revision = Revision(data);
            solution = data.Solution;
            valid = data.Solution != null && data.Solution.IsValid;
            end = data.Injections[0].IntegrationEndOffset;
            locked = Processor.IsLocked;
        }
        public void CheckUnchanged(ExperimentData data)
        {
            Check(ReferenceEquals(Processor, data.Processor) && ReferenceEquals(Interpolator, data.Processor.Interpolator)
                && heat.Equals(data.Injections[0].RawPeakArea.Value) && integrated == data.Injections[0].IsIntegrated
                && revision == Revision(data) && ReferenceEquals(solution, data.Solution)
                && valid == (data.Solution != null && data.Solution.IsValid)
                && end == data.Injections[0].IntegrationEndOffset && locked == data.Processor.IsLocked,
                data.Name + ": copying changed a protected, out-of-scope or source experiment");
        }
    }

    static IEnumerable<NSView> Descendants(NSView view)
    {
        foreach (var child in view.Subviews)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    static IEnumerable<NSButton> Buttons(DataProcessingViewControllet controller) =>
        Descendants(controller.View).OfType<NSButton>();

    static ExperimentData RawExperiment(string name)
    {
        var data = new ExperimentData(name + ".itc") { Name = name, Include = true,
            CellConcentration = new FloatWithError(30e-6),
            SyringeConcentration = new FloatWithError(400e-6), CellVolume = 1.4e-3 };
        for (var time = 0; time <= 40; time++)
            data.DataPoints.Add(new DataPoint(time, time > 10 && time <= 16 ? 3 : 1));
        var injection = InjectionData.FromPEAQFile(data, 0, true, 10, 2e-6, 20, 2, 25);
        injection.SetIntegrationLengthByTime(6);
        data.Injections.Add(injection);
        data.Processor.DiscardIntegratedPoints = true;
        data.Processor.Interpolator = new PolynomialLeastSquaresInterpolator(data.Processor) { Degree = 0 };
        return data;
    }

    static void DrainActivity()
    {
        var timer = Stopwatch.StartNew();
        while (ProcessingActivity.IsBusy && timer.Elapsed < TimeSpan.FromSeconds(20))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.005));
        Check(!ProcessingActivity.IsBusy, "processing did not finish within 20 seconds");
    }

    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    sealed class ArchivedProcessView : IDisposable
    {
        public readonly DataProcessingViewControllet Controller;
        readonly NSStoryboard storyboard;
        readonly NSBundle resources;

        public ArchivedProcessView(string storyboardPath)
        {
            new Class(typeof(DataProcessingGraphView));
            new Class(typeof(IntegrationRangeSlider));
            new Class(typeof(DataProcessingViewControllet));
            resources = NSBundle.FromPath(Path.GetDirectoryName(storyboardPath));
            Check(resources != null, "could not locate compiled storyboard resources");
            storyboard = NSStoryboard.FromName("Main", resources);
            Controller = (DataProcessingViewControllet)storyboard.InstantiateControllerWithIdentifier("DataProcessingViewControllet");
            Check(Controller.View != null, "archived controller view is missing");
        }

        public void Dispose()
        {
            // These older controller subscriptions are not all detached by Dispose.
            Detach(typeof(AppDelegate), "StartPrintOperation", "AppDelegate_StartPrintOperation");
            Detach(typeof(DataManager), "SelectionDidChange", "OnSelectionChanged");
            Detach(typeof(DataProcessor), "BaselineInterpolationCompleted", "OnInterpolationCompleted");
            Detach(typeof(DataProcessor), "ProcessingCompleted", "OnProcessingCompleted");
            Detach(typeof(BaselineOptionsPopoverViewController), "Updated", "BaselineOptionsPopoverViewController_Updated");
            Controller.Dispose();
            storyboard.Dispose();
            resources.Dispose();
        }

        void Detach(Type type, string eventName, string methodName)
        {
            var eventInfo = type.GetEvent(eventName, BindingFlags.Static | BindingFlags.Public);
            var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, Controller,
                typeof(DataProcessingViewControllet).GetMethod(methodName, Instance));
            eventInfo.RemoveEventHandler(null, handler);
        }
    }
}
