// Managed AppKit checks; run with run-analysis-null-graph-tests.sh UncertaintyPresentationTests.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using AppKit;
using CoreGraphics;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

static class UncertaintyPresentationTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int Main(string[] args)
    {
        NSApplication.Init();
        try
        {
            foreach (var method in new[] { ErrorEstimationMethod.None, ErrorEstimationMethod.BootstrapResiduals,
                ErrorEstimationMethod.LeaveOneOut, ErrorEstimationMethod.ProfileLikelihood })
                foreach (var outcome in new[] { ErrorEstimationOutcome.SkippedNoBinding, ErrorEstimationOutcome.Completed,
                    ErrorEstimationOutcome.Cancelled, ErrorEstimationOutcome.CompleteFailure })
                    CheckConsumers(Create(method, outcome));
            var mixed = Create(ErrorEstimationMethod.BootstrapResiduals, ErrorEstimationOutcome.Completed);
            var second = Create(ErrorEstimationMethod.BootstrapResiduals, ErrorEstimationOutcome.SkippedNoBinding);
            var models = new List<Model> { mixed.Model.Models[0], second.Model.Models[0] };
            var globalModel = new GlobalModel(models) { ModelCloneOptions = mixed.ModelCloneOptions, Parameters = new GlobalModelParameters() };
            foreach (var member in models) globalModel.Parameters.AddIndivdualParameter(member.Parameters);
            var solver = new GlobalSolver { Model = globalModel, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
            for (var index = 0; index < 100; index++) models[0].Solution.BootstrapSolutions.Add(models[0].Solution);
            mixed = new GlobalSolution(solver, models.Select(m => m.Solution).ToList(), mixed.Convergence, reconstructBootstrap: false);
            Check(UncertaintyEstimationPresentation.Section(mixed).Rows[0].Value == "Mixed", "Eligible/skipped members lost their mixed status");
            CheckConsumers(mixed);
            Console.WriteLine("PASS: native Results/details method sections, skipped metrics, mixed evidence, shared tooltips and narrow layouts");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }

    static GlobalSolution Create(ErrorEstimationMethod method, ErrorEstimationOutcome outcome)
    {
        var data = new ExperimentData("uncertainty-native.itc")
        { CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6), CellVolume = 1.4e-3, MeasuredTemperature = 25 };
        for (var index = 0; index < 8; index++)
            data.Injections.Add(new InjectionData(data, volume: 1e-6)
            {
                IsIntegrated = true,
                Ratio = index + 1,
                ActualCellConcentration = 10e-6,
                ActualTitrantConcentration = (index + 1) * 10e-6,
            });
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = method };
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.Converged, ErrorEstimationOutcome = outcome,
            RequestedBootstrapIterations = method == ErrorEstimationMethod.BootstrapResiduals ? (int?)100 : null });
        model.Solution = SolutionInterface.FromModel(model, convergence);
        model.Solution.ErrorMethod = method;
        return GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model, ErrorEstimationMethod = method });
    }

    static void CheckConsumers(GlobalSolution solution)
    {
        var section = UncertaintyEstimationPresentation.Section(solution);
        foreach (var type in new[] { typeof(AnalysisResultTabViewController), typeof(BindingAnalysisViewController) })
        {
            var method = type.GetMethod("BuildUncertaintySection", Flags);
            var instance = method.IsStatic ? null : FormatterServices.GetUninitializedObject(type);
            var view = (NSView)method.Invoke(instance, new object[] { solution });
            if (section == null) { Check(view == null, "None produced an empty method section"); continue; }
            using (view)
            using (var window = new NSWindow(new CGRect(100, 100, 340, 520), NSWindowStyle.Titled, NSBackingStore.Buffered, false))
            {
                var host = new BackgroundView(new CGRect(0, 0, 340, 520));
                host.AddSubview(view);
                NSLayoutConstraint.ActivateConstraints(new[] { view.TopAnchor.ConstraintEqualToAnchor(host.TopAnchor),
                    view.LeadingAnchor.ConstraintEqualToAnchor(host.LeadingAnchor), view.TrailingAnchor.ConstraintEqualToAnchor(host.TrailingAnchor) });
                window.ContentView = host;
                view.HeightAnchor.ConstraintGreaterThanOrEqualToConstant(150).Active = true;
                host.LayoutSubtreeIfNeeded();
                Check(view.Frame.Height > 0, "Section has no rendered height");
                var fields = Descendants(view).OfType<NSTextField>().ToArray();
                Check(fields.Any(field => field.StringValue == section.Title), "Method section heading missing");
                foreach (var row in section.Rows)
                {
                    Check(fields.Any(field => field.StringValue == row.Label), "Shared row missing: " + row.Label);
                    Check(fields.Any(field => field.StringValue == row.Value && field.ToolTip == row.Tooltip), "Shared value or tooltip missing: " + row.Label);
                }
                if (section.Rows[0].Value == SolverConvergence.SkippedForNoBindingDescription)
                {
                    Check(!fields.Any(field => field.StringValue == "Bootstraps" || field.StringValue == "95% CI endpoints" || field.StringValue == "Calculation time"), "Skipped calculation showed metrics");
                    var status = fields.First(field => field.StringValue == SolverConvergence.SkippedForNoBindingDescription);
                    Check(status.Frame.Width > 0 && status.Frame.Height > status.Font.PointSize, "Long skipped status did not wrap");
                }
                if (solution.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals && section.Rows[0].Value == SolverConvergence.SkippedForNoBindingDescription)
                {
                    using (var bitmap = host.BitmapImageRepForCachingDisplayInRect(host.Bounds))
                    {
                        host.CacheDisplay(host.Bounds, bitmap);
                        var png = bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
                        png.Save("/private/tmp/itc094-native-" + type.Name + ".png", true);
                    }
                }
            }
        }
    }
    sealed class BackgroundView : NSView
    {
        public BackgroundView(CGRect frame) : base(frame) { }
        public override void DrawRect(CGRect rect) { NSColor.WindowBackground.SetFill(); NSBezierPath.FillRect(rect); }
    }
    static IEnumerable<NSView> Descendants(NSView view)
    { yield return view; foreach (var child in view.Subviews) foreach (var descendant in Descendants(child)) yield return descendant; }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
