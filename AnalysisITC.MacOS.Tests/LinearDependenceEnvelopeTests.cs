// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh LinearDependenceEnvelopeTests
// Optional: FTITC_LINEAR_ENVELOPE_QA_DIRECTORY=/private/tmp/cr047/before (or after).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using Foundation;
using CoreGraphics;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.Drawing;
using Buffer = AnalysisITC.Core.Data.Buffer;

static class LinearDependenceEnvelopeTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly DateTime CompletedAt = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main()
    {
        NSApplication.Init();
        var failures = 0;
        Run("two-buffer-positive-sd", () => Protonation("two-buffer-positive-sd", 2, true, false), ref failures);
        Run("two-buffer-zero-sd", () => Protonation("two-buffer-zero-sd", 2, false, false), ref failures);
        Run("literal-bounds", () => Protonation("literal-bounds", 3, true, false), ref failures);
        Run("unusable-bounds", () => Protonation("unusable-bounds", 3, true, true), ref failures);
        Run("counter-ion-three-activities", CounterIon, ref failures);
        Console.WriteLine("Native linear envelopes: " + (5 - failures) + " passed, " + failures + " failed, 5 total");
        return failures == 0 ? 0 : 1;
    }

    static void Protonation(string name, int count, bool uncertainty, bool unusableBounds)
    {
        var result = CreateResult(count, uncertainty, false);
        Check(result.IsProtonationAnalysisEnabled, "production two-buffer eligibility");
        var slope = uncertainty ? new FloatWithError(0.3, 0.05, 0.2, 0.4) : new FloatWithError(0.3);
        var intercept = uncertainty ? new FloatWithError(1000, 250, 500, 1500) : new FloatWithError(1000);
        if (unusableBounds)
        {
            slope = new FloatWithError(0.3, 0, double.NaN, double.NaN);
            intercept = new FloatWithError(1000, 0, double.NaN, double.NaN);
        }
        var analysis = result.ProtonationAnalysis;
        typeof(ProtonationAnalysis).GetMethod("RestoreResult", Instance).Invoke(analysis,
            new object[] { intercept, slope, 100, CompletedAt, ErrorEstimationMethod.BootstrapResiduals });
        var savedFit = (LinearFitWithError)analysis.Fit;
        Check(savedFit.ReferenceT == 0, "literal expectations require ReferenceT = 0");
        var observations = analysis.DataPoints.Select(point => point.Item2).ToArray();
        using (var view = CreateView())
        {
            view.Setup(analysis);
            var graph = ProductionGraph(view);
            Check(ReferenceEquals(graph.Fit, savedFit), "production setup uses the completed fit");
            Check(graph.XLabel.Contains("buffer,ionization"), "x axis identifies buffer ionization enthalpy");
            graph.SetXAxisRange(0, 30000);
            graph.SetYAxisRange(0, 15000);
            var recorded = RenderAndRecord(graph, name);
            AssertFinite(recorded, name);
            AssertMapped(recorded, 10000, 4000, "original-data center");
            if (uncertainty && !unusableBounds)
            {
                AssertMapped(recorded, 10000, 5500, "upper corner bound");
                AssertMapped(recorded, 10000, 2500, "lower corner bound");
            }
            else Check(recorded.FitCalls.Count == 301, "only the 301 center samples, with no band geometry");
            for (var i = 0; i < observations.Length; i++)
                Same(analysis.DataPoints[i].Item2, observations[i], "observation unchanged by rendering");
        }
        Same(savedFit.Slope, slope, "stored slope unchanged");
        Same(savedFit.Intercept, intercept, "stored intercept unchanged");
        Check(ReferenceEquals(analysis.Fit, savedFit) && analysis.CompletedAtUtc == CompletedAt
            && analysis.CompletedIterations == 100, "drawing does not replace or rerun the analysis");
    }

    static void CounterIon()
    {
        var result = CreateResult(3, true, true);
        var analysis = result.ElectrostaticsAnalysis;
        Check(analysis != null, "production counter-ion eligibility");
        var fit = new LinearFitWithError(new FloatWithError(0.5, 0.1, 0.4, 0.6),
            new FloatWithError(-10, 0.2, -10.4, -9.6), 0);
        typeof(ElectrostaticsAnalysis).GetMethod("RestoreResult", Instance).Invoke(analysis, new object[] {
            new IonicStrengthDependenceFit(new FloatWithError(1e-6), new FloatWithError(2), new FloatWithError(0)),
            fit, 100, 100, CompletedAt, ErrorEstimationMethod.BootstrapResiduals, false });
        var savedAffinities = result.Solution.Solutions.Select(member => member.Parameters[ParameterType.Affinity1]).ToArray();
        List<Coordinate> firstCalls = null;
        using (var view = CreateView())
        {
            for (var repeat = 0; repeat < 3; repeat++)
            {
                view.Setup(analysis, ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease);
                var graph = ProductionGraph(view);
                Check(graph.XValues.Length >= 3 && graph.XValues.Select(value => value.Value).Distinct().Count() >= 3,
                    "at least three distinct activities through production setup");
                Check(graph.XValues.All(value => value.Value < 0), "negative logarithmic activity coordinates");
                Check(ReferenceEquals(graph.Fit, fit), "production setup uses the stored counter-ion fit");
                graph.SetXAxisRange(-8, 0);
                graph.SetYAxisRange(-17, -9);
                var recorded = RenderAndRecord(graph, repeat == 0 ? "counter-ion-three-activities" : null);
                AssertFinite(recorded, "counter-ion repeat " + repeat);
                AssertMapped(recorded, -4, -12, "counter-ion center");
                AssertMapped(recorded, -4, -12.8, "negative-x lower corner bound");
                AssertMapped(recorded, -4, -11.2, "negative-x upper corner bound");
                if (firstCalls != null)
                {
                    Check(recorded.FitCalls.Count == firstCalls.Count, "repeat geometry count");
                    for (var i = 0; i < firstCalls.Count; i++)
                    {
                        Near(recorded.FitCalls[i].X, firstCalls[i].X, "repeat x");
                        Near(recorded.FitCalls[i].Y, firstCalls[i].Y, "repeat y");
                    }
                }
                firstCalls = recorded.FitCalls;
            }
        }
        for (var i = 0; i < savedAffinities.Length; i++)
            Same(result.Solution.Solutions[i].Parameters[ParameterType.Affinity1], savedAffinities[i], "stored affinity unchanged");
        Same(fit.Slope, new FloatWithError(0.5, 0.1, 0.4, 0.6), "stored counter-ion slope");
        Same(fit.Intercept, new FloatWithError(-10, 0.2, -10.4, -9.6), "stored counter-ion intercept");
        Check(ReferenceEquals(analysis.CounterIonReleaseFit, fit) && analysis.CompletedAtUtc == CompletedAt
            && analysis.CounterIonReleaseIterations == 100, "counter-ion analysis is not rerun");
    }

    static ResultGraphView CreateView()
    {
        var handle = Send(Send(new Class(typeof(ResultGraphView)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        var view = Runtime.GetNSObject<ResultGraphView>(handle);
        view.Frame = new CGRect(0, 0, 900, 650);
        return view;
    }

    static ParameterDependenceGraph ProductionGraph(ResultGraphView view) =>
        (ParameterDependenceGraph)typeof(ResultGraphView).GetProperty("Graph", Instance).GetValue(view);

    static RecordingGraph RenderAndRecord(ParameterDependenceGraph production, string name)
    {
        // Exercise the actual graph from ResultGraphView before using its recording copy.
        production.DrawOnWhite = true;
        Render(production, name);
        var recorded = new RecordingGraph(production);
        var fit = recorded.Fit;
        // Measure the data/tick calls on the same graph so they cannot be mistaken for fit samples.
        recorded.Fit = null;
        Render(recorded, null);
        var nonFitCount = recorded.Calls.Count;
        recorded.Calls.Clear();
        recorded.Fit = fit;
        Render(recorded, null);
        // ParameterDependenceGraph draws its fit before the observations and axes.
        recorded.FitCalls = recorded.Calls.Take(recorded.Calls.Count - nonFitCount).ToList();
        return recorded;
    }

    static void Render(ParameterDependenceGraph graph, string name)
    {
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 900, 650, 8, 3600,
            space, CGImageAlphaInfo.PremultipliedLast))
        {
            bitmap.SetFillColor(NSColor.White.CGColor);
            bitmap.FillRect(new CGRect(0, 0, 900, 650));
            graph.PrepareDraw(bitmap, new CGPoint(450, 325));
            var directory = Environment.GetEnvironmentVariable("FTITC_LINEAR_ENVELOPE_QA_DIRECTORY");
            if (name == null || string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            using (var image = bitmap.ToImage())
            using (var representation = new NSBitmapImageRep(image))
            using (var png = representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()))
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), png.ToArray());
        }
    }

    static void AssertFinite(RecordingGraph graph, string name)
    {
        var invalid = graph.FitCalls.Where(call => !Finite(call.X) || !Finite(call.Y)
            || !Finite(call.Mapped.X) || !Finite(call.Mapped.Y)).ToList();
        Console.WriteLine(name + ": " + graph.FitCalls.Count + " fit coordinates, " + invalid.Count
            + " nonfinite" + (invalid.Count == 0 ? "" : "; first y=" + invalid[0].Y));
        Check(invalid.Count == 0, "nonfinite native fit coordinates: " + (invalid.Count == 0 ? "" : invalid[0].Y.ToString()));
    }

    static void AssertMapped(RecordingGraph graph, double x, double y, string name)
    {
        var matches = graph.FitCalls.Where(call => call.X == x && Math.Abs(call.Y - y) < 1e-9).ToList();
        Check(matches.Count > 0, name + ": exact sampled coordinate (" + x + ", " + y + ") missing");
        // Independent axis mapping of the literal expected scientific value.
        var expectedX = (x - graph.XAxis.Min) / (graph.XAxis.Max - graph.XAxis.Min) * graph.PlotSize.Width;
        var expectedY = (y - graph.YAxis.Min) / (graph.YAxis.Max - graph.YAxis.Min) * graph.PlotSize.Height;
        foreach (var match in matches)
        {
            Near(match.Mapped.X, expectedX, name + " mapped x");
            Near(match.Mapped.Y, expectedY, name + " mapped y");
        }
    }

    sealed class RecordingGraph : ParameterDependenceGraph
    {
        public readonly List<Coordinate> Calls = new List<Coordinate>();
        public List<Coordinate> FitCalls;

        public RecordingGraph(ParameterDependenceGraph source) : base(source.View)
        {
            XValues = source.XValues;
            YValues = source.YValues;
            Fit = source.Fit;
            XLabel = source.XLabel;
            YLabel = source.YLabel;
            XScaleFactor = source.XScaleFactor;
            YScaleFactor = source.YScaleFactor;
            DrawOnWhite = source.DrawOnWhite;
            Setup();
            SetXAxisRange(source.XAxis.Min, source.XAxis.Max);
            SetYAxisRange(source.YAxis.Min, source.YAxis.Max);
        }

        public override CGPoint GetRelativePosition(double x, double y, GraphAxis axis = null)
        {
            var mapped = base.GetRelativePosition(x, y, axis);
            Calls.Add(new Coordinate { X = x, Y = y, Mapped = mapped });
            return mapped;
        }
    }

    sealed class Coordinate
    {
        public double X, Y;
        public CGPoint Mapped;
    }

    static AnalysisResult CreateResult(int count, bool uncertainty, bool saltSeries)
    {
        var buffers = new[] { Buffer.MES, Buffer.Hepes, Buffer.Tris };
        var salts = new[] { 0.04, 0.16, 0.36 };
        var models = new List<Model>();
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot());
        for (var index = 0; index < count; index++)
        {
            var data = new ExperimentData("envelope-" + index + ".itc")
            {
                MeasuredTemperature = 25, TargetTemperature = 25,
                CellConcentration = new FloatWithError(30e-6), SyringeConcentration = new FloatWithError(400e-6), CellVolume = 1.4e-3,
            };
            var attribute = ExperimentAttribute.FromKey(saltSeries ? AttributeKey.Salt : AttributeKey.Buffer);
            attribute.IntValue = saltSeries ? (int)Salt.NaCl : (int)buffers[index];
            attribute.DoubleValue = 7;
            attribute.ParameterValue = new FloatWithError(saltSeries ? salts[index] : 0.05);
            data.Attributes.Add(attribute);
            var enthalpy = saltSeries ? -25000 : 1000 + 0.3 * buffers[index].GetIonizationEnthalpy(25);
            var logKa = saltSeries ? (10 - 0.5 * Math.Log(salts[index] * salts[index])) / Math.Log(10) : 6;
            var model = new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, logKa);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, enthalpy);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            model.Solution = SolutionInterface.FromModel(model, convergence);
            if (uncertainty)
            {
                model.Solution.Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpy, 250);
                model.Solution.Parameters[ParameterType.Affinity1] = new FloatWithError(logKa, 0.08, logKa - 0.1, logKa + 0.3);
            }
            models.Add(model);
        }
        var global = new GlobalModel(models) { ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = global }, models.Select(model => model.Solution).ToList(), convergence);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }

    static void Run(string name, Action test, ref int failures)
    {
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + ": " + error.Message); }
    }

    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static void Same(FloatWithError actual, FloatWithError expected, string name)
    {
        Check(actual.Value.Equals(expected.Value) && actual.SD.Equals(expected.SD)
            && actual.Lower.Equals(expected.Lower) && actual.Upper.Equals(expected.Upper), name);
    }
    static void Near(double actual, double expected, string name) =>
        Check(Finite(actual) && Math.Abs(actual - expected) <= 1e-10 * Math.Max(1, Math.Abs(expected)), name);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
