// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh ConstantAxisTests
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.Drawing;

static class ConstantAxisTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    static int failures, total;

    static int Main()
    {
        NSApplication.Init();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        foreach (var value in new[] { 0.0, 12.0, -12.0, 1e-9, 1e12 })
        foreach (var buffer in new[] { 0.035, 0.1 })
        {
            var v = value; var b = buffer;
            Run("factory constant " + v + " buffer " + b, () => Axis(v, b, true));
            Run("setter constant " + v + " buffer " + b, () => Axis(v, b, false));
        }
        Run("ordinary ranges unchanged", Ordinary);
        Run("tick labels keep invariant decimal precision", InvariantTickLabels);
        Run("constant X and Y render", ConstantGraph);
        Run("constant affinity without uncertainty", () => SaltGraph(false));
        Run("constant affinity with uncertainty", () => SaltGraph(true));
        Console.WriteLine("Constant axes: " + (total - failures) + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static void Run(string name, Action action)
    {
        total++;
        try { action(); Console.WriteLine("PASS: " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL: " + name + ": " + e.Message); }
    }

    static void Axis(double value, double buffer, bool factory)
    {
        var graph = new ParameterDependenceGraph(null);
        var axis = factory ? GraphAxis.WithBuffer(graph, value, value, buffer, AxisPosition.Left)
            : new GraphAxis(graph, 0, 1, AxisPosition.Left);
        if (!factory) axis.SetWithBuffer(value, value, buffer);
        if (factory) Check(axis.Buffer == buffer, "constant buffer recorded");
        Valid(axis);
        var basis = Math.Max(1, Math.Abs(value));
        Check(axis.Min == (float)(value - basis * buffer), "lower padding");
        Check(axis.Max == (float)(value + basis * buffer), "upper padding");
        Check(axis.Position == AxisPosition.Left, "position retained");
    }

    static void Ordinary()
    {
        foreach (var limits in new[] { new[] { -20.0, 10.0 }, new[] { 1e-9, 2e-9 } })
        {
            var graph = new ParameterDependenceGraph(null);
            var axis = GraphAxis.WithBuffer(graph, limits[0], limits[1], .1);
            var setter = new GraphAxis(graph, 0, 1);
            setter.SetMaxTicks(5);
            setter.SetWithBuffer(limits[0], limits[1], .1);
            var delta = limits[1] - limits[0];
            // The factory historically reapplies Buffer after constructing float bounds.
            var legacy = new GraphAxis(graph, limits[0] - delta * .1, limits[1] + delta * .1) { Buffer = .1 };
            Check(axis.Min == legacy.Min && axis.Max == legacy.Max, "ordinary factory bounds unchanged");
            Check(setter.Min == (float)(limits[0] - delta * .1)
                && setter.Max == (float)(limits[1] + delta * .1), "ordinary setter bounds unchanged");
            Check(setter.TickScale.MaxTicks == 5, "tick count retained");
        }
    }

    static void InvariantTickLabels()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                var graph = new ParameterDependenceGraph(null);
                var axis = new GraphAxis(graph, 0, 1, AxisPosition.Left);
                axis.Set(.12, .88);
                var format = typeof(GraphAxis).GetMethod("FormatTickLabel", Instance);
                var tick = axis.TickScale.Ticks().First(value => value != 0);
                var label = (string)format.Invoke(axis, new object[] { tick });
                Check(label.Contains(".") && !label.Contains(","), cultureName + " tick uses period");
                Check(axis.DecimalPoints > 0, cultureName + " fractional tick precision retained");
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    static void Valid(GraphAxis axis)
    {
        Check(Finite(axis.Min) && Finite(axis.Max) && axis.Min < axis.Max, "finite ordered bounds");
        var ticks = axis.TickScale.Ticks();
        Check(ticks.Count > 0 && ticks.All(Finite), "finite populated ticks");
    }

    static void ConstantGraph()
    {
        using (var view = new NSView(new CGRect(0, 0, 900, 650)))
        {
            var graph = new ParameterDependenceGraph(view) {
                XValues = new[] { new FloatWithError(2), new FloatWithError(2) },
                YValues = new[] { new FloatWithError(0), new FloatWithError(0) } };
            graph.Setup();
            Render(graph, "constant-xy");
        }
    }

    static void SaltGraph(bool uncertainty)
    {
        var result = CreateResult(new[] { .04, .16, .36 }, new[] { 6.0, 6.0, 6.0 }, uncertainty);
        var saved = result.Solution.Solutions.Select(s => s.Parameters[ParameterType.Affinity1]).ToArray();
        var handle = Send(Send(new Class(typeof(ResultGraphView)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        using (var view = Runtime.GetNSObject<ResultGraphView>(handle))
        {
            view.Frame = new CGRect(0, 0, 900, 650);
            view.Setup(result.ElectrostaticsAnalysis, ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt);
            var graph = (ParameterDependenceGraph)typeof(ResultGraphView).GetProperty("Graph", Instance).GetValue(view);
            Check(graph.YValues.Select(v => v.Value).Distinct().Count() == 1, "constant fitted values");
            if (uncertainty)
                Check(graph.YValues.All(v => v.SD > 0 && v.Lower < v.Value && v.Value < v.Upper),
                    "uncertainty present around each fitted value");
            var before = graph.YValues.ToArray();
            Render(graph, uncertainty ? "constant-affinity-sd" : "constant-affinity-zero-sd");
            for (var i = 0; i < saved.Length; i++)
            {
                Same(result.Solution.Solutions[i].Parameters[ParameterType.Affinity1], saved[i]);
                Same(graph.YValues[i], before[i]);
            }
        }
    }

    static void Render(ParameterDependenceGraph graph, string name)
    {
        Valid(graph.XAxis); Valid(graph.YAxis);
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 900, 650, 8, 3600, space, CGImageAlphaInfo.PremultipliedLast))
        {
            bitmap.SetFillColor(NSColor.White.CGColor);
            bitmap.FillRect(new CGRect(0, 0, 900, 650));
            graph.DrawOnWhite = true;
            graph.PrepareDraw(bitmap, new CGPoint(450, 325));
            foreach (var x in graph.XValues)
            foreach (var y in graph.YValues)
            foreach (var ordinate in new[] { y.Value, y.Lower, y.Upper })
            {
                var point = graph.GetRelativePosition(x.Value, ordinate);
                Check(Finite(point.X) && Finite(point.Y), "finite point and error-bar mapping");
            }
            var directory = Environment.GetEnvironmentVariable("FTITC_CONSTANT_AXIS_QA_DIRECTORY");
            if (string.IsNullOrEmpty(directory)) return;
            System.IO.Directory.CreateDirectory(directory);
            using (var image = bitmap.ToImage())
            using (var representation = new NSBitmapImageRep(image))
            using (var png = representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new Foundation.NSDictionary()))
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), png.ToArray());
        }
    }

    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Same(FloatWithError actual, FloatWithError expected) => Check(actual.Value == expected.Value
        && actual.SD == expected.SD && actual.Lower == expected.Lower && actual.Upper == expected.Upper, "saved values and intervals retained");
    static AnalysisResult CreateResult(double[] salts, double[] logAffinities, bool uncertainty)
    {
        var models = new List<Model>();
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        for (var index = 0; index < salts.Length; index++)
        {
            var data = new ExperimentData("salt-" + index + ".itc")
            {
                MeasuredTemperature = 25, TargetTemperature = 25,
                CellConcentration = new FloatWithError(30e-6),
                SyringeConcentration = new FloatWithError(400e-6), CellVolume = 1.4e-3,
            };
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
            salt.IntValue = (int)Salt.NaCl;
            salt.ParameterValue = new FloatWithError(salts[index]);
            data.Attributes.Add(salt);
            var model = new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, logAffinities[index]);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence);
            if (uncertainty)
                model.Solution.Parameters[ParameterType.Affinity1] = new FloatWithError(logAffinities[index], 0.08,
                    logAffinities[index] - 0.1, logAffinities[index] + 0.3);
            models.Add(model);
        }
        var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(model => model.Solution).ToList(), convergence);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }

}
