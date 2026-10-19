// Managed AppKit checks; run with run-analysis-null-graph-tests.sh CompetitiveResultModelOptionPresentationTests.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
using AnalysisITC.Core.Units;

static class CompetitiveResultModelOptionPresentationTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main()
    {
        NSApplication.Init();
        var unit = AppSettings.DefaultConcentrationUnit;
        var family = AppSettings.EnergyUnitFamily;
        var style = AppSettings.UncertaintyDisplayStyle;
        var precision = AppSettings.NumberPrecision;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CheckModelPage(false);
            CheckModelPage(true);
            CheckStoredStoichiometry();
            Console.WriteLine("PASS: native saved competitor intervals, all display styles, manual/member rows, preference refresh and stored stoichiometry");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            AppSettings.DefaultConcentrationUnit = unit;
            AppSettings.EnergyUnitFamily = family;
            AppSettings.UncertaintyDisplayStyle = style;
            AppSettings.NumberPrecision = precision;
            CultureInfo.CurrentCulture = culture;
        }
    }

    static void CheckStoredStoichiometry()
    {
        var values = new[] { new[] { 0.0, 7.0 }, new[] { 0.2, double.NaN }, new[] { 0.25, 1.0 } };
        var expected = new[] { new[] { "0", "7" }, new[] { "0.2", "Unavailable" },
            new[] { "One to tetramer (1:4)", "One to one (1:1)" } };
        for (var index = 0; index < values.Length; index++)
        {
            var data = new ExperimentData("stored-stoichiometry.itc")
            {
                CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3, MeasuredTemperature = 25,
            };
            data.Injections.Add(new InjectionData(data, volume: 1e-6) { IsIntegrated = true, Ratio = 1 });
            var model = new TwoSetsOfSites(data);
            model.InitializeParameters(data);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            var first = model.ModelOptions[AttributeKey.NumberOfSites1];
            var second = model.ModelOptions[AttributeKey.NumberOfSites2];
            first.DoubleValue = values[index][0];
            second.DoubleValue = values[index][1];
            first.IntValue = second.IntValue = 7;
            model.Solution = SolutionInterface.FromModel(model, Convergence());
            var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
            using (var controller = Runtime.GetNSObject<AnalysisResultTabViewController>(
                Send(Send(new Class(typeof(AnalysisResultTabViewController)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"))))
            using (var stack = new NSStackView())
            {
                typeof(AnalysisResultTabViewController).GetField("modelStack", Instance).SetValue(controller, stack);
                typeof(AnalysisResultTabViewController).GetField("analysisResult", Instance).SetValue(controller, result);
                Refresh(controller);
                var text = Text(stack);
                Contains(text, expected[index][0]);
                Contains(text, expected[index][1]);
                if (index < 2) Check(!text.Contains("One to one (1:1)"), "Stored stoichiometry replaced with 1:1");
                Check(first.DoubleValue == values[index][0], "First stored stoichiometry changed");
                Check(second.DoubleValue.Equals(values[index][1]), "Second stored stoichiometry changed");
                Check(first.IntValue == 7 && second.IntValue == 7, "Stored integer fields changed");
            }
        }
    }

    static void CheckModelPage(bool fromAttributes)
    {
        AppSettings.DefaultConcentrationUnit = ConcentrationUnit.µM;
        AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
        AppSettings.NumberPrecision = NumberPrecision.SingleDecimal;
        var models = new List<Model> { CreateModel(0, fromAttributes), CreateModel(1, fromAttributes) };
        var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        var solution = new GlobalSolution(new GlobalSolver { Model = global },
            models.Select(model => model.Solution).ToList(), Convergence());
        global.Solution = solution;
        using (var controller = Runtime.GetNSObject<AnalysisResultTabViewController>(
            Send(Send(new Class(typeof(AnalysisResultTabViewController)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"))))
        using (var stack = new NSStackView())
        {
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 12;
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            typeof(AnalysisResultTabViewController).GetField("modelStack", Instance).SetValue(controller, stack);
            typeof(AnalysisResultTabViewController).GetField("analysisResult", Instance).SetValue(controller, new AnalysisResult(solution));
            var styles = new[] { UncertaintyDisplayStyle.ConfidenceInterval, UncertaintyDisplayStyle.Automatic,
                UncertaintyDisplayStyle.StandardDeviation, UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval, UncertaintyDisplayStyle.None };
            var affinities = new[] { "1.0 [0.1, 10.0] µM", "1.0 [0.1, 10.0] µM", "1.0 ± 0.2 µM", "1.0 ± 0.2 [0.1, 10.0] µM", "1.0 µM" };
            var enthalpies = new[] { "-32.0 [-35.0, -20.0] kJ/mol", "-32.0 [-35.0, -20.0] kJ/mol", "-32.0 ± 2.0 kJ/mol",
                "-32.0 ± 2.0 [-35.0, -20.0] kJ/mol", "-32.0 kJ/mol" };
            for (var index = 0; index < styles.Length; index++)
            {
                AppSettings.UncertaintyDisplayStyle = styles[index];
                Refresh(controller);
                var sections = stack.ArrangedSubviews.Select(Text).ToArray();
                Check(sections[0].Count(value => value == "From experiment attribute") == (fromAttributes ? 2 : 0), "Source indicator changed");
                Contains(sections[fromAttributes ? 1 : 0], affinities[index]);
                Contains(sections[fromAttributes ? 1 : 0], enthalpies[index]);
                if (fromAttributes && styles[index] == UncertaintyDisplayStyle.ConfidenceInterval)
                {
                    Contains(sections[2], "0.1 [0.0, 1.0] µM");
                    Contains(sections[2], "-16.0 [-18.0, -10.0] kJ/mol");
                    Render(stack);
                }
            }
            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.ConfidenceInterval;
            AppSettings.DefaultConcentrationUnit = ConcentrationUnit.nM;
            AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
            Refresh(controller);
            Contains(Text(stack), "1000 [100, 10000] nM");
            Check(models[0].ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue.Lower == 5, "Formatting changed saved evidence");
        }
    }

    static Model CreateModel(int index, bool fromAttributes)
    {
        var data = new ExperimentData("saved-" + index + ".itc")
        { CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6), CellVolume = 1.4e-3, MeasuredTemperature = 25 };
        data.Injections.Add(new InjectionData(data, volume: 1e-6) { IsIntegrated = true, Ratio = 1 });
        var model = new CompetitiveBinding(data);
        model.InitializeParameters(data);
        model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
        model.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = fromAttributes;
        model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue = new FloatWithError(6 + index, .1, 5 + index, 7 + index);
        model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue = fromAttributes;
        model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue = index == 0
            ? new FloatWithError(-32000, 2000, -35000, -20000) : new FloatWithError(-16000, 1000, -18000, -10000);
        model.Solution = SolutionInterface.FromModel(model, Convergence());
        return model;
    }

    static SolverConvergence Convergence() => SolverConvergence.FromSnapshot(
        new SolverConvergenceSnapshot { Termination = SolverTermination.Converged });

    static void Render(NSStackView stack)
    {
        using (var window = new NSWindow(new CGRect(100, 100, 340, 900), NSWindowStyle.Titled, NSBackingStore.Buffered, false))
        {
            var host = new BackgroundView(new CGRect(0, 0, 340, 900));
            host.AddSubview(stack);
            NSLayoutConstraint.ActivateConstraints(new[] { stack.TopAnchor.ConstraintEqualToAnchor(host.TopAnchor, 12),
                stack.LeadingAnchor.ConstraintEqualToAnchor(host.LeadingAnchor, 12), stack.TrailingAnchor.ConstraintEqualToAnchor(host.TrailingAnchor, -12) });
            window.ContentView = host;
            host.LayoutSubtreeIfNeeded();
            foreach (var field in Fields(stack).Where(field => field.StringValue.Contains("[")))
                Check(field.Frame.Width > 0 && field.Frame.Height >= field.Font.PointSize, "Interval row has no visible layout");
            var directory = Environment.GetEnvironmentVariable("FTITC_CR050_QA_DIRECTORY");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                using (var bitmap = host.BitmapImageRepForCachingDisplayInRect(host.Bounds))
                {
                    host.CacheDisplay(host.Bounds, bitmap);
                    bitmap.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png).Save(Path.Combine(directory, "native-model.png"), true);
                }
            }
            stack.RemoveFromSuperview();
        }
    }

    sealed class BackgroundView : NSView
    {
        public BackgroundView(CGRect frame) : base(frame) { }
        public override void DrawRect(CGRect rect) { NSColor.WindowBackground.SetFill(); NSBezierPath.FillRect(rect); }
    }

    static IEnumerable<NSTextField> Fields(NSView view)
    {
        if (view is NSTextField) yield return (NSTextField)view;
        foreach (var child in view.Subviews) foreach (var field in Fields(child)) yield return field;
    }
    static string[] Text(NSView view) => Fields(view).Select(field => field.StringValue).ToArray();
    static void Refresh(object controller) => controller.GetType().GetMethod("RefreshModel", Instance).Invoke(controller, null);
    static void Contains(string[] values, string expected) => Check(values.Contains(expected), "Missing " + expected + "; actual: " + string.Join(" | ", values));
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
