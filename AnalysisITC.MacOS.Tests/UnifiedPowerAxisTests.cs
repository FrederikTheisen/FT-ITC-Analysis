// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh UnifiedPowerAxisTests
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using AppKit;
using CoreGraphics;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using AnalysisITC.UI.MacOS.Drawing;

static class UnifiedPowerAxisTests
{
    const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static int failures;

    static int Main(string[] args)
    {
        NSApplication.Init();
        SynchronizationContext.SetSynchronizationContext(null);
        Run("Processed fixture with DH import renders preview and PDF", () => FixtureFigures(args[0]));
        Run("Exothermal shared bounds ignore heat-only direction", () => DirectionBounds(false, false, -11, 1));
        Run("Endothermal shared bounds ignore heat-only direction", () => DirectionBounds(true, false, -1, 11));
        Run("Mixed shared bounds retain both directions", () => DirectionBounds(false, true, -5.4, 11.4));
        Run("Raw/corrected switching retains shared bounds", ModeSwitches);
        Run("Empty corrected traces are skipped; null retains raw fallback", EmptyCorrected);
        Run("Empty shared domain falls back to selected trace", SelectedFallback);
        Run("No available samples retain initialized axes", NoSamples);
        Run("Local raw/corrected scaling is unchanged", LocalScaling);
        Console.WriteLine("Unified power axis checks: " + (9 - failures) + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static void Run(string name, Action test)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        FinalFigureGraphView.UnifiedPowerAxis = false;
        FinalFigureGraphView.UnifiedEnthalpyAxis = false;
        FinalFigureGraphView.UseUnifiedMolarRatioAxis = false;
        FinalFigureGraphView.DrawBaselineCorrected = true;
        FinalFigureGraphView.DataYAxisMin = null;
        FinalFigureGraphView.DataYAxisMax = null;
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL: " + name + "\n" + error); }
        finally { DataManager.Clear(DataClearMode.ResetSession); }
    }

    static ExperimentData Trace(string name, float[] raw, float[] corrected, PeakHeatDirection direction)
    {
        var data = new ExperimentData(name) { AverageHeatDirection = direction };
        data.DataPoints = Points(raw);
        data.BaseLineCorrectedDataPoints = Points(corrected);
        return data;
    }

    static List<DataPoint> Points(float[] powers)
        => powers == null ? null : powers.Select((power, index) => new DataPoint(index, power, 25)).ToList();

    static ExperimentData Exothermal(string name = "selected")
        => Trace(name, new float[] { -8, -4 }, new float[] { -4, -2 }, PeakHeatDirection.Exothermal);

    static ExperimentData Peer()
        => Trace("peer", new float[] { -20, -10 }, new float[] { -10, -5 }, PeakHeatDirection.Exothermal);

    static ExperimentData HeatOnly()
        => Trace("integrated-heats", new float[0], new float[0], PeakHeatDirection.Unknown);

    static DataGraph Graph(ExperimentData data, NSView view)
    {
        var graph = new DataGraph(data, view) { ShowBaselineCorrected = true };
        graph.YAxis.Buffer = 0.1;
        graph.UseUnifiedAxes = true;
        return graph;
    }

    static void DirectionBounds(bool endothermal, bool mixed, double min, double max)
    {
        var selected = endothermal
            ? Trace("selected", new float[] { 8, 4 }, new float[] { 4, 2 }, PeakHeatDirection.Endothermal)
            : Exothermal();
        var peer = endothermal || mixed
            ? Trace("peer", new float[] { 20, 10 }, new float[] { 10, 5 }, PeakHeatDirection.Endothermal)
            : Peer();
        DataManager.AddData(new ITCDataContainer[] { selected, peer, HeatOnly() });
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
            Bounds(Graph(selected, view).YAxis, min, max);
    }

    static void ModeSwitches()
    {
        var selected = Exothermal();
        DataManager.AddData(new ITCDataContainer[] { selected, Peer() });
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = Graph(selected, view);
            Bounds(graph.YAxis, -11, 1);
            graph.ShowBaselineCorrected = false;
            Bounds(graph.YAxis, -21.6, -2.4);
            graph.ShowBaselineCorrected = true;
            Bounds(graph.YAxis, -11, 1);
            graph.UseUnifiedAxes = false;
            Bounds(graph.YAxis, -4.4, 0.4);
        }
    }

    static void EmptyCorrected()
    {
        var selected = Exothermal();
        var peer = Peer();
        peer.BaseLineCorrectedDataPoints.Clear();
        DataManager.AddData(new ITCDataContainer[] { selected, peer, HeatOnly() });
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = Graph(selected, view);
            Bounds(graph.YAxis, -4.4, 0.4);
            peer.BaseLineCorrectedDataPoints = null;
            graph.UseUnifiedAxes = true;
            Bounds(graph.YAxis, -22, 2);
            selected.BaseLineCorrectedDataPoints.Clear();
            Bounds(Graph(selected, view).YAxis, -22, 2);
        }
    }

    static void SelectedFallback()
    {
        var selected = Exothermal();
        selected.Include = false;
        DataManager.AddData(new ITCDataContainer[] { selected, HeatOnly() });
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = Graph(selected, view);
            Bounds(graph.YAxis, -4.4, 0.4);
            DataManager.Clear(DataClearMode.ResetSession);
            graph.UseUnifiedAxes = true;
            Bounds(graph.YAxis, -4.4, 0.4);
        }
    }

    static void NoSamples()
    {
        var heat = HeatOnly();
        DataManager.AddData(heat);
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = new DataGraph(heat, view);
            graph.ShowBaselineCorrected = true;
            graph.UseUnifiedAxes = true;
            graph.ShowBaselineCorrected = false;
            Bounds(graph.YAxis, -1, 1);
            Bounds(graph.XAxis, 0, 1);

            var selected = Exothermal();
            selected.BaseLineCorrectedDataPoints.Clear();
            graph = new DataGraph(selected, view);
            var min = graph.YAxis.Min;
            var max = graph.YAxis.Max;
            graph.ShowBaselineCorrected = true;
            graph.UseUnifiedAxes = true;
            Bounds(graph.YAxis, min, max);
        }
    }

    static void LocalScaling()
    {
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = new DataGraph(Exothermal(), view);
            graph.ShowBaselineCorrected = true;
            Bounds(graph.YAxis, -4.07, -1.93);
            graph.ShowBaselineCorrected = false;
            Bounds(graph.YAxis, -8.14, -3.86);
        }
    }

    static void FixtureFigures(string root)
    {
        ExperimentData raw;
        using (var stream = File.OpenRead(Path.Combine(root, "AnalysisITC.Tests/SolutionIdentityTracingTest/processed.ftxtc")))
        {
            var read = FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict, false).GetAwaiter().GetResult();
            raw = read.Containers.OfType<ExperimentData>().First(data => data.HasThermogram
                && data.BaseLineCorrectedDataPoints != null && data.BaseLineCorrectedDataPoints.Count > 1
                && data.Injections.Count > 0 && data.Injections.All(injection => injection.IsIntegrated));
        }
        var originalPrompt = PlatformServices.ImportPromptService;
        ExperimentData heat;
        IntegratedHeatReader.BeginImportQueue();
        try
        {
            PlatformServices.RegisterImportPromptService(new UnitPrompt());
            heat = IntegratedHeatReader.ReadFile(Path.Combine(root, "AnalysisITC.Tests/BufferSubtraction/hepes-01.DH"));
        }
        finally
        {
            IntegratedHeatReader.EndImportQueue();
            PlatformServices.RegisterImportPromptService(originalPrompt);
        }
        raw.Include = true;
        heat.Include = true;
        DataManager.AddData(new ITCDataContainer[] { raw, heat });
        DataManager.SelectIndex(0);
        Check(DataManager.IncludedData.Count() == 2 && !heat.HasThermogram, "Fixture must contain two active members");
        var local = PowerGraph(Figure(raw));
        var min = local.YAxis.Min;
        var max = local.YAxis.Max;
        SetUnifiedControl(true);
        var figure = Figure(raw);
        Bounds(PowerGraph(figure).YAxis, min, max);
        Draw(figure);
        using (var output = new MemoryStream())
        {
            typeof(FinalFigureGraphView).GetMethod("WriteFigurePdf", HiddenStatic, null,
                new[] { typeof(ExperimentData), typeof(Stream), typeof(AnalysisResult),
                    typeof(AnalysisITC.Core.Analysis.Models.SolutionInterface), typeof(PublicationFigureModelSelection?) }, null)
                .Invoke(null, new object[] { raw, output, null, null, (PublicationFigureModelSelection?)PublicationFigureModelSelection.None });
            Check(output.Length > 500 && System.Text.Encoding.ASCII.GetString(output.ToArray(), 0, 5) == "%PDF-", "PDF rendering failed");
        }
        heat.Include = false;
        Bounds(PowerGraph(Figure(raw)).YAxis, min, max);
        heat.Include = true;
        Check(PowerGraph(Figure(heat)) == null, "Heat-only member gained a thermogram panel");
        figure = Figure(raw);
        figure.DataYAxisMin = -30;
        figure.DataYAxisMax = 10;
        Draw(figure);
        var axis = PowerGraph(figure).YAxis;
        Near(axis.Min * axis.ValueFactor, -30);
        Near(axis.Max * axis.ValueFactor, 10);
    }

    static FinalFigure Figure(ExperimentData data)
        => FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.None);

    static DataGraph PowerGraph(FinalFigure figure)
        => (DataGraph)typeof(FinalFigure).GetField("DataGraph", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(figure);

    static void Draw(FinalFigure figure)
    {
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 1000, 1100, 8, 4000, space, CGImageAlphaInfo.PremultipliedLast))
            figure.Draw(bitmap, new CGPoint(500, 550));
    }

    static void SetUnifiedControl(bool on)
    {
        var controller = typeof(FinalFigureGraphView).Assembly.GetType("AnalysisITC.FinalFigureOptionsController", true);
        var type = controller.GetNestedType("DataControls", BindingFlags.Public | BindingFlags.NonPublic);
        var controls = Activator.CreateInstance(type, true);
        using (var toggle = new NSSwitch())
        {
            toggle.State = on ? 1 : 0;
            type.GetProperty("UnifiedPowerAxis").SetValue(controls, toggle, null);
            controller.GetMethod("ApplyData", HiddenStatic).Invoke(null, new object[] { controls });
        }
        Check(FinalFigureGraphView.UnifiedPowerAxis == on, "Shared power control did not apply");
    }

    sealed class UnitPrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
            => new EnergyUnitPromptResult(EnergyUnit.MicroCal, false, false);
    }

    static void Bounds(GraphAxis axis, double min, double max) { Near(axis.Min, min); Near(axis.Max, max); }
    static void Near(double actual, double expected)
        => Check(Math.Abs(actual - expected) <= 2e-6 * Math.Max(1e-6, Math.Abs(expected)), "Expected " + expected + ", got " + actual);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
