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

                // Update Result starts a solver outside the Analysis workspace.
                // Publish its real events without relying on a numerical fit's timing.
                foreach (var method in new[] { ErrorEstimationMethod.BootstrapResiduals,
                    ErrorEstimationMethod.LeaveOneOut, ErrorEstimationMethod.ProfileLikelihood })
                    foreach (var termination in new[] { SolverTermination.Converged,
                        SolverTermination.Cancelled, SolverTermination.Failed })
                    {
                        StatusBarManager.ClearAppStatus();
                        StatusBarManager.SetDefaultSecondaryStatus("Example project");
                        var solver = new SolverInterface { ErrorEstimationMethod = method, BootstrapIterations = 100 };
                        solver.Analyze();
                        solver.ReportAnalysisStepFinished();
                        Pump();
                        Check(primary.StringValue == "Estimating uncertainty...",
                            method + " did not announce uncertainty before the first counter");

                        var expectedCounter = "";
                        if (method == ErrorEstimationMethod.BootstrapResiduals)
                        {
                            solver.ReportBootstrapProgress(40);
                            expectedCounter = "40/100";
                        }
                        else if (method == ErrorEstimationMethod.LeaveOneOut)
                        {
                            solver.ReportLeaveOneOutProgress(2, 5);
                            expectedCounter = "2/5";
                        }
                        else
                        {
                            solver.ReportSolverUpdate(new SolverUpdate(2, 8)
                            {
                                Message = "Profile likelihood: endpoints found 2/8", Progress = 0.25f,
                            });
                            expectedCounter = "2/8";
                        }
                        Pump();
                        Check(secondary.StringValue == expectedCounter, method + " counter missing");
                        Check(primary.StringValue == (method == ErrorEstimationMethod.ProfileLikelihood
                            ? "Profile likelihood: endpoints found 2/8" : "Estimating uncertainty..."),
                            method + " operation status differs");
                        Check(Math.Abs(progress.DoubleValue - (method == ErrorEstimationMethod.ProfileLikelihood ? 25 : 40)) < 0.00001,
                            method + " progress differs");

                        StatusBarManager.SetDefaultSecondaryStatus("Example project [M]");
                        Check(secondary.StringValue == expectedCounter, "Project name replaced an active counter");
                        solver.ReportAnalysisFinished(Convergence(termination));
                        Pump();
                        Check(secondary.StringValue == "Example project [M]", "Project name not restored after " + termination);
                        Check(progress.Hidden, "Progress not hidden after " + termination);
                    }

                StatusBarManager.ClearAppStatus();
                var noUncertainty = new SolverInterface { ErrorEstimationMethod = ErrorEstimationMethod.None };
                noUncertainty.Analyze();
                Pump();
                var fittingMessage = primary.StringValue;
                noUncertainty.ReportAnalysisStepFinished();
                Pump();
                Check(primary.StringValue == fittingMessage, "A fit with no uncertainty announced uncertainty estimation");
                noUncertainty.ReportAnalysisFinished(Convergence(SolverTermination.Converged));
                Pump();

                StatusBarManager.ClearAppStatus();
                var independent = new SolverInterface();
                independent.Analyze();
                independent.ReportSolverUpdate(new SolverUpdate(1, 3)
                {
                    Message = "Estimating uncertainty...", Progress = 1f / 3,
                });
                Pump();
                Check(primary.StringValue == "Estimating uncertainty..." && secondary.StringValue == "1/3",
                    "Independent-fit operation status or counter missing");
                independent.ReportAnalysisFinished(Convergence(SolverTermination.Converged));
                Pump();
                Check(secondary.StringValue == "Example project [M]", "Independent completion did not restore the project name");

                var missingCompletion = new SolverInterface();
                missingCompletion.Analyze();
                missingCompletion.ReportAnalysisFinished(null);
                Pump();
                Check(progress.Hidden && secondary.StringValue == "Example project [M]",
                    "Missing completion crashed progress handling or left it active");

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
                            Wait((Task)controller.GetType().GetMethod("UpdateResultAsync", Instance).Invoke(controller, null));
                            if (outcome != "accepted")
                                Check((string)temperature.GetValue(controller) == "17", "A retained/failed update reset the evaluation temperature");
                        }
                    }
                    else
                    {
                        using (var controller = (MainWindowController)Create(typeof(MainWindowController)))
                            Wait((Task)controller.GetType().GetMethod("UpdateSelectedResult", Instance).Invoke(controller, null));
                    }

                    if (outcome == "accepted")
                    {
                        Check(!ReferenceEquals(original, result.Solution), entry + " did not replace the result");
                        Check(messages.Any(message => message.Contains("RMSD") || message == "Analysis result updated"),
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
                        Check(!messages.Any(message => message.Contains("RMSD") || message == "Analysis result updated"),
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
