using System;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;

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
        try
        {
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
            }
            Console.WriteLine("PASS: native uncertainty phase starts, bootstrap/leave-one-out/profile/independent counters, and project-name restoration after success/cancellation/failure");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally
        {
            StatusBarManager.ClearAppStatus();
            StatusBarManager.SetDefaultSecondaryStatus("");
            SolverUpdate.Step = savedStep;
            SolverUpdate.TotalSteps = savedTotal;
        }
    }

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
