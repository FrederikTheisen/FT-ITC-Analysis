using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
using AnalysisITC.UI.MacOS.CustomViews;

static class SaltAnalysisPresentationTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main()
    {
        NSApplication.Init();
        var span = AppSettings.MinimumIonSpanForFitting;
        var iterations = ResultAnalysisController.CalculationIterations;
        try
        {
            AppSettings.MinimumIonSpanForFitting = .03;
            ResultAnalysisController.CalculationIterations = 8;
            CheckDetailsAndRunButton();
            foreach (var ionic in new[] { false, true })
            foreach (var counter in new[] { false, true })
            foreach (var missing in new[] { false, true }) CheckOutput(ionic, counter, missing);
            var result = Create(.04, .16, .36);
            AppSettings.MinimumIonSpanForFitting = 1;
            result = new AnalysisResult(result.Solution);
            Check(!result.IsElectrostaticsAnalysisDependenceEnabled, "spacing fails");
            var ensure = typeof(AnalysisResult).GetMethod("EnsureElectrostaticsAnalysisForRestore", Instance);
            Check((bool)ensure.Invoke(result, null), "saved output restore bypasses spacing");
            Restore(result.ElectrostaticsAnalysis, true, true);
            using (var controller = Controller(result))
            {
                typeof(AnalysisResultTabViewController).GetMethod("RefreshAvailableGraphTypes", Instance).Invoke(controller, null);
                var modes = (IEnumerable)typeof(AnalysisResultTabViewController).GetField("availableGraphTypes", Instance).GetValue(controller);
                Check(!modes.Cast<object>().Any(mode => mode.ToString() == "IonicStrengthDependence"), "restored Salt remains hidden");
                var section = (NSView)typeof(AnalysisResultTabViewController).GetMethod("BuildAvailabilitySection", Instance).Invoke(controller, null);
                var reason = Descendants(section).OfType<NSTextField>().Single(field => field.StringValue.Contains(result.ElectrostaticsAnalysisUnavailableReason));
                Check(reason.Cell.Wraps && !reason.UsesSingleLineMode, "eligibility reason wraps");
            }
            Console.WriteLine("PASS: native salt output availability, zero counter-ion result, shared tooltips, and hidden restored ineligible output");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            AppSettings.MinimumIonSpanForFitting = span;
            ResultAnalysisController.CalculationIterations = iterations;
        }
    }

    sealed class DetailsController : ExperimentDetailsPopoverController
    {
        public bool Dismissed;
        public override void DismissViewController(NSViewController controller) { Dismissed = true; }
    }

    static void CheckDetailsAndRunButton()
    {
        var result = Create(.025, .05, .1, .2);
        for (var i = 0; i < 4; i++)
            result.Solution.Solutions[i].Parameters[ParameterType.Affinity1] = new FloatWithError(-Math.Log10(1e-7 * Math.Pow(16, i)));
        result.UpdateSolution(result.Solution);
        var initial = result.ElectrostaticsAnalysis.PerformAnalysisAsync();
        var initialDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!initial.IsCompleted && DateTime.UtcNow < initialDeadline)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
        Check(initial.IsCompleted && initial.GetAwaiter().GetResult(), "initial salt calculation");
        Check(Math.Abs(result.ElectrostaticsAnalysis.CounterIonRelease.Value - 4) < 1e-10, "initial slope 4");
        for (var i = 0; i < 4; i++)
        {
            ExperimentDetailsPopoverController.Data = result.Solution.Solutions[i].Data;
            using (var details = new DetailsController { View = new NSView(new CGRect(0, 0, 720, 600)) })
            {
                details.ViewDidLoad();
                var rows = (List<ExperimentAttributeView>)typeof(ExperimentDetailsPopoverController).GetField("attributeViews", Instance).GetValue(details);
                ((NSTextField)typeof(ExperimentAttributeView).GetField("parameterValueField", Instance).GetValue(rows.Single())).StringValue
                    = (25 * Math.Pow(4, i)).ToString("F0");
                // AppKit editing notifications are how the control records a user edit.
                NSNotificationCenter.DefaultCenter.PostNotificationName(NSControl.TextDidChangeNotification,
                    (NSTextField)typeof(ExperimentAttributeView).GetField("parameterValueField", Instance).GetValue(rows.Single()));
                var apply = Descendants(details.View).OfType<NSButton>().Single(button => button.Title == "Apply");
                apply.PerformClick(apply);
                Check(details.Dismissed, "details Apply completed");
            }
        }
        using (var controller = Controller(result))
        using (var stack = new NSStackView())
        {
            controller.View = new NSView(new CGRect(0, 0, 720, 600));
            typeof(AnalysisResultTabViewController).GetField("analysisStack", Instance).SetValue(controller, stack);
            typeof(AnalysisResultTabViewController).GetMethod("AddSaltAnalysisSections", Instance).Invoke(controller, null);
            Check(Descendants(stack).OfType<NSTextField>().Any(field => field.StringValue == ElectrostaticsAnalysisAvailability.InputsChangedReason), "changed-input message");
            Check(!result.ElectrostaticsAnalysis.HasCounterIonResult, "old fit unavailable");
            var run = Descendants(stack).OfType<NSButton>().Single(button => button.Title == "Run Analysis");
            run.PerformClick(run);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((ProcessingActivity.IsBusy || !result.ElectrostaticsAnalysis.HasCounterIonResult) && DateTime.UtcNow < deadline)
                NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
            Check(!ProcessingActivity.IsBusy && Math.Abs(result.ElectrostaticsAnalysis.CounterIonRelease.Value - 2) < 1e-10, "Run Analysis replaces slope with 2");
        }
        Console.WriteLine("PASS: native details Apply -> unavailable -> Run Analysis -> slope 2");
    }

    static void CheckOutput(bool ionic, bool counter, bool missing)
    {
        var result = Create(.04, .16, .36);
        Restore(result.ElectrostaticsAnalysis, ionic, counter, missing);
        using (var controller = Controller(result))
        using (var stack = new NSStackView())
        {
            typeof(AnalysisResultTabViewController).GetField("analysisStack", Instance).SetValue(controller, stack);
            typeof(AnalysisResultTabViewController).GetMethod("AddSaltAnalysisSections", Instance).Invoke(controller, null);
            var rows = Descendants(stack).OfType<NSStackView>().ToList();
            CheckRow(rows, "Kd0", ionic, ionic ? ElectrostaticsAnalysisAvailability.Kd0ToolTip : result.ElectrostaticsAnalysis.IonicStrengthUnavailableReason);
            CheckRow(rows, "Salt-dependence slope", counter, counter ? ElectrostaticsAnalysisAvailability.SaltDependenceToolTip : result.ElectrostaticsAnalysis.CounterIonUnavailableReason);
        }
    }

    static void CheckRow(List<NSStackView> rows, string label, bool available, string tooltip)
    {
        var row = rows.Single(view => view.ArrangedSubviews.OfType<NSTextField>().Any(field => field.StringValue == label));
        var value = row.ArrangedSubviews.OfType<NSTextField>().Last();
        Check((value.StringValue == "Unavailable") == !available, label + " availability");
        if (tooltip != null) Check(value.ToolTip == tooltip, label + " shared tooltip");
    }

    static void Restore(ElectrostaticsAnalysis analysis, bool ionic, bool counter, bool missing = false)
    {
        typeof(ElectrostaticsAnalysis).GetMethod("RestoreResult", Instance).Invoke(analysis, new object[] {
            !ionic && missing ? null : new IonicStrengthDependenceFit(new FloatWithError(ionic ? 2e-6 : double.PositiveInfinity), new FloatWithError(2), FloatWithError.NaN, false),
            !counter && missing ? null : new LinearFitWithError(new FloatWithError(counter ? 0 : double.NaN), new FloatWithError(-12), 0),
            8, 7, DateTime.UtcNow, ErrorEstimationMethod.None, false });
    }

    static AnalysisResultTabViewController Controller(AnalysisResult result)
    {
        var handle = Send(Send(new Class(typeof(AnalysisResultTabViewController)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        var controller = Runtime.GetNSObject<AnalysisResultTabViewController>(handle);
        typeof(AnalysisResultTabViewController).GetField("analysisResult", Instance).SetValue(controller, result);
        return controller;
    }

    static IEnumerable<NSView> Descendants(NSView view)
    {
        foreach (var child in view.Subviews)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    static AnalysisResult Create(params double[] strengths)
    {
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        var models = strengths.Select(strength => {
            var data = new ExperimentData("salt.itc") { MeasuredTemperature = 25, CellVolume = 1.4e-3,
                CellConcentration = new FloatWithError(30e-6), SyringeConcentration = new FloatWithError(400e-6) };
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt); salt.IntValue = (int)Salt.NaCl;
            salt.ParameterValue = new FloatWithError(strength); data.Attributes.Add(salt);
            var model = new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence); return (Model)model;
        }).ToList();
        var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        global.Solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(model => model.Solution).ToList(), convergence);
        return new AnalysisResult(global.Solution);
    }
}
