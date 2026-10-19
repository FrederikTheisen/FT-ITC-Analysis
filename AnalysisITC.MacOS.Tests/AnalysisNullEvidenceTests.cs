using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AppKit;
using Foundation;
using CoreGraphics;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.Drawing;

static class AnalysisNullEvidenceTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        DataAnalysisViewController controller = null;
        Task notification = null;
        var previousShowNullPrediction = AnalysisGraphView.ShowNullPrediction;
        try
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var experiment = new ExperimentData("null-evidence.DH");
            for (var index = 0; index < 3; index++)
            {
                var injection = new InjectionData(experiment, index, 2e-6, 2e-9, include: true)
                {
                    Ratio = index,
                    ActualCellConcentration = new FloatWithError(35e-6),
                    ActualTitrantConcentration = (index + 1) * 5e-6
                };
                injection.SetPeakArea(new FloatWithError(2e-8));
                experiment.Injections.Add(injection);
            }
            experiment.CellConcentration = new FloatWithError(35e-6);
            experiment.SyringeConcentration = new FloatWithError(420e-6);
            var model = new OneSetOfSites(experiment);
            model.InitializeParameters(experiment);
            model.Solution = SolutionInterface.FromModel(model,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            var comparison = new NullModelComparison
            {
                NullModelId = "offset",
                BindingFitSucceeded = true,
                NullFitSucceeded = true,
                DeltaAicc = 12,
                BindingInformationCriteria = Criteria(100, 1, 1),
                NullInformationCriteria = Criteria(112, 2, 2),
                Members = { Member(experiment, 40) }
            };
            typeof(SolutionInterface).GetProperty("NullComparison").GetSetMethod(true)
                .Invoke(model.Solution, new object[] { comparison });
            experiment.Model = model;
            DataManager.AddData(experiment);
            DataManager.SelectIndex(0);

            controller = CreateController();
            controller.ViewWillAppear();
            Check(ReferenceEquals(Field(controller, "currentNullComparison"), comparison),
                "View entry did not load saved Null evidence");
            Check(ReferenceEquals(Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison, comparison),
                "Graph did not receive saved Null evidence");
            var stack = Outlet<NSView>(controller, "FitSummaryView").Superview as NSStackView;
            Check(stack != null, "Fit summary is not hosted by an inspector stack");
            var rendered = Rendered(stack);
            Check(rendered.Contains("Null hypothesis test") && rendered.Contains("Model")
                && rendered.Contains("Null RMSD") && rendered.Contains("ΔAICc")
                && rendered.Contains("Conclusion") && rendered.Contains("Offset")
                && rendered.Contains("+12"), "Saved Null evidence values were not rendered");

            AnalysisGraphView.ShowNullPrediction = true;
            experiment.RemoveModel(notifyDataChange: false);
            Check(Field(controller, "currentNullComparison") == null, "Controller retained cleared Null evidence");
            Check(Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison == null,
                "Graph retained cleared Null evidence");
            Check(AnalysisGraphView.ShowNullPrediction, "Clearing a fit reset the prediction preference");
            rendered = Rendered(stack);
            Check(rendered.Contains("Unknown") && rendered.Contains("Unavailable")
                && rendered.Contains("Not calculated") && rendered.Contains("Not assessed")
                && rendered.Contains("No fit result for the selected experiment."),
                "Cleared Null evidence rows did not show their unavailable states");
            var predictionMethod = typeof(DataFittingGraph).GetMethod("NullPredictionPointsFor", BindingFlags.Instance | BindingFlags.NonPublic);
            var graph = Outlet<AnalysisGraphView>(controller, "GraphView").DataFittingGraph;
            var noPrediction = (System.Collections.IEnumerable)predictionMethod.Invoke(graph, new object[] { experiment });
            Check(!noPrediction.Cast<object>().Any(), "Cleared fit retained prediction overlay points");

            AttachSolution(experiment, Comparison(experiment, 9));
            var reattached = experiment.Solution.NullComparison;
            Check(ReferenceEquals(Field(controller, "currentNullComparison"), reattached)
                && ReferenceEquals(Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison, reattached),
                "Replacing a solution did not refresh its Null comparison");
            Check(Rendered(stack).Contains("+9"), "Replacement comparison rows did not render their values");
            Check(((System.Collections.IEnumerable)predictionMethod.Invoke(graph, new object[] { experiment }))
                .Cast<object>().Any(), "Reattached comparison did not restore prediction points");

            notification = Task.Run(() => experiment.RemoveModel(notifyDataChange: false));
            Check(notification.Wait(3000), "Worker RemoveModel blocked on the main run loop");
            var removalDeadline = DateTime.UtcNow.AddSeconds(3);
            while (Field(controller, "currentNullComparison") != null && DateTime.UtcNow < removalDeadline)
                NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
            Check(Field(controller, "currentNullComparison") == null
                && Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison == null,
                "Worker RemoveModel retained selected Null evidence");

            AttachSolution(experiment, Comparison(experiment, 13));
            AttachSolution(experiment, null);
            Check(Field(controller, "currentNullComparison") == null
                && Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison == null,
                "Replacing a solution without a comparison retained prior evidence");
            Check(Rendered(stack).Contains("Not calculated"),
                "Replacement without a comparison did not clear rendered evidence");

            AttachSolution(experiment, Comparison(experiment, 11));
            typeof(DataAnalysisViewController).GetField("currentNullComparison", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, null);
            Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison = null;
            typeof(DataAnalysisViewController).GetMethod("RefreshNullHypothesisTestPresentation", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            typeof(DataAnalysisViewController).GetField("isFitting", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, true);
            var fittingUpdate = Task.Run(() => AttachSolution(experiment, Comparison(experiment, 22)));
            notification = fittingUpdate;
            Check(fittingUpdate.Wait(3000), "Worker solution update during fitting blocked");
            var fittingDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < fittingDeadline)
                NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
            Check(Field(controller, "currentNullComparison") == null
                && Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison == null
                && Rendered(stack).Contains("Not assessed"),
                "Worker solution notification repopulated suppressed Null evidence while fitting");
            typeof(DataAnalysisViewController).GetField("isFitting", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, false);
            AttachSolution(experiment, null);

            var globalMember = new OneSetOfSites(experiment);
            globalMember.InitializeParameters(experiment);
            globalMember.Solution = SolutionInterface.FromModel(globalMember,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            experiment.Model = globalMember;
            var global = GlobalSolution.FromSingleExperimentSolver(new Solver { Model = globalMember });
            var pooled = Comparison(experiment, 31);
            typeof(GlobalSolution).GetProperty("NullComparison").GetSetMethod(true)
                .Invoke(global, new object[] { pooled });
            typeof(SolutionInterface).GetProperty("NullComparison").GetSetMethod(true)
                .Invoke(globalMember.Solution, new object[] { null });
            experiment.UpdateSolution(globalMember);
            Check(ReferenceEquals(Field(controller, "currentNullComparison"), pooled),
                "Global parent Null evidence was not shown for its member");
            experiment.RemoveModel(notifyDataChange: false);
            Check(Field(controller, "currentNullComparison") == null
                && Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison == null,
                "Removing a global-fit member retained its parent's Null evidence");

            var other = CreateExperiment("other-null-evidence.DH", 25);
            DataManager.AddData(other);
            DataManager.SelectIndex(0);
            var delayedModel = new OneSetOfSites(experiment);
            delayedModel.InitializeParameters(experiment);
            delayedModel.Solution = SolutionInterface.FromModel(delayedModel,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            var delayedComparison = Comparison(experiment, 17);
            typeof(SolutionInterface).GetProperty("NullComparison").GetSetMethod(true)
                .Invoke(delayedModel.Solution, new object[] { delayedComparison });
            notification = Task.Run(() => experiment.UpdateSolution(delayedModel));
            Check(notification.Wait(3000), "Worker solution notification blocked on the main run loop");
            DataManager.SelectIndex(1);
            var otherComparison = other.Solution.NullComparison;
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.1));
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!ReferenceEquals(Field(controller, "currentNullComparison"), otherComparison)
                && DateTime.UtcNow < deadline)
                NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
            Check(ReferenceEquals(Field(controller, "currentNullComparison"), otherComparison)
                && ReferenceEquals(Outlet<AnalysisGraphView>(controller, "GraphView").NullComparison, otherComparison),
                "Delayed solution notification replaced the newly selected experiment's Null evidence");
            Check(Rendered(stack).Contains("+25"),
                "Delayed solution notification changed the selected experiment's rendered evidence");
            Check(AnalysisGraphView.ShowNullPrediction, "New solution cleared the saved prediction preference");
            var predictions = typeof(DataFittingGraph).GetMethod("NullPredictionPointsFor", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(Outlet<AnalysisGraphView>(controller, "GraphView").DataFittingGraph, new object[] { other }) as System.Collections.IEnumerable;
            Check(predictions != null && predictions.Cast<object>().Any(), "New solution did not restore the prediction overlay data");
            Console.WriteLine("PASS: native saved Null rows and solution removal clear controller and graph evidence");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally
        {
            if (notification != null && !notification.IsCompleted)
                notification.Wait(3000);
            var drainDeadline = DateTime.UtcNow.AddMilliseconds(250);
            while (DateTime.UtcNow < drainDeadline)
                NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
            if (controller != null) controller.Dispose();
            DataManager.Clear(DataClearMode.ResetSession);
            AnalysisGraphView.ShowNullPrediction = previousShowNullPrediction;
        }
    }

    static DataAnalysisViewController CreateController()
    {
        var controller = (DataAnalysisViewController)Create(typeof(DataAnalysisViewController));
        controller.View = new NSView(new CGRect(0, 0, 800, 600));
        foreach (var outlet in typeof(DataAnalysisViewController).GetProperties(Instance))
        {
            if (outlet.GetCustomAttribute<OutletAttribute>() == null) continue;
            var control = Create(outlet.PropertyType);
            outlet.SetValue(controller, control);
            if (control is NSView view) controller.View.AddSubview(view);
            if (control is NSPopUpButton popup) popup.Menu = new NSMenu();
            if (control is NSSegmentedControl segmented) { segmented.SegmentCount = 2; segmented.SelectedSegment = 0; }
            if (control is NSTabView tabs) for (var i = 0; i < 4; i++) tabs.Add(new NSTabViewItem());
        }
        Outlet<NSPopUpButton>(controller, "ErrorMethodControl").AddItems(new[] { "None", "Bootstrap residuals", "Leave-one-out", "Profile likelihood" });
        Outlet<NSPopUpButton>(controller, "SolverAlgorithmControl").AddItems(new[] { "Nelder-Mead", "Levenberg-Marquardt" });
        Outlet<NSSlider>(controller, "ErrorIterationsControl").MaxValue = 4;
        var summary = Outlet<NSView>(controller, "FitSummaryView");
        summary.RemoveFromSuperview();
        var inspector = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical };
        controller.View.AddSubview(inspector);
        inspector.AddArrangedSubview(summary);
        controller.ViewDidLoad();
        return controller;
    }

    static ExperimentData CreateExperiment(string name, double delta)
    {
        var experiment = new ExperimentData(name);
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(experiment, index, 2e-6, 2e-9, include: true)
            {
                Ratio = index,
                ActualCellConcentration = new FloatWithError(35e-6),
                ActualTitrantConcentration = (index + 1) * 5e-6
            };
            injection.SetPeakArea(new FloatWithError(2e-8));
            experiment.Injections.Add(injection);
        }
        experiment.CellConcentration = new FloatWithError(35e-6);
        experiment.SyringeConcentration = new FloatWithError(420e-6);
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        typeof(SolutionInterface).GetProperty("NullComparison").GetSetMethod(true)
            .Invoke(model.Solution, new object[] { Comparison(experiment, delta) });
        experiment.Model = model;
        return experiment;
    }

    static NullModelComparison Comparison(ExperimentData experiment, double delta)
        => new NullModelComparison
        {
            NullModelId = "offset",
            BindingFitSucceeded = true,
            NullFitSucceeded = true,
            DeltaAicc = delta,
            BindingInformationCriteria = Criteria(100, 1, 1),
            NullInformationCriteria = Criteria(100 + delta, 2, 2),
            Members = { Member(experiment, 40) }
        };

    static void AttachSolution(ExperimentData experiment, NullModelComparison comparison)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        typeof(SolutionInterface).GetProperty("NullComparison").GetSetMethod(true)
            .Invoke(model.Solution, new object[] { comparison });
        experiment.UpdateSolution(model);
    }

    static NullModelComparisonMember Member(ExperimentData experiment, double heat)
    {
        var member = new NullModelComparisonMember { ExperimentId = experiment.UniqueID };
        foreach (var ratio in new[] { 0.0, 1.0, 2.0 })
            member.Points.Add(new NullModelComparisonPoint
            {
                Ratio = ratio,
                InjectionMass = 2e-9,
                PredictedHeatJoules = heat * 2e-9
            });
        return member;
    }

    static FitInformationCriteria Criteria(double aicc, int parameterCount, double rmsd)
        => (FitInformationCriteria)typeof(FitInformationCriteria).GetMethod("Restore", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { 20, parameterCount, parameterCount + 1,
                GaussianLikelihoodMode.EstimatedCommonVariance, 90.0, aicc - 4, aicc,
                true, true, string.Empty, string.Empty, 0.0, rmsd, 0.0, 0.0 });

    static string Rendered(NSView view)
        => string.Join("\n", Descendants(view).OfType<NSTextField>().Select(field => field.StringValue));

    static NSObject Create(Type type) => Runtime.GetNSObject(
        Send(Send(new Class(type).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init")));
    static T Outlet<T>(DataAnalysisViewController controller, string name)
        => (T)typeof(DataAnalysisViewController).GetProperty(name, Instance).GetValue(controller);
    static object Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    static System.Collections.Generic.IEnumerable<NSView> Descendants(NSView view)
    {
        yield return view;
        foreach (var child in view.Subviews)
            foreach (var nested in Descendants(child)) yield return nested;
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
