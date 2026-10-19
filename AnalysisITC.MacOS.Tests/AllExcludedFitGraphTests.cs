using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using AppKit;
using CoreGraphics;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.Drawing;

// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh AllExcludedFitGraphTests
static class AllExcludedFitGraphTests
{
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    static int Main(string[] args)
    {
        NSApplication.Init();
        var previousTraceability = AppSettings.TraceabilityModeEnabled;
        var previousFitYAxisMin = FinalFigureGraphView.FitYAxisMin;
        var previousFitYAxisMax = FinalFigureGraphView.FitYAxisMax;
        var previousShowBadData = FinalFigureGraphView.ShowBadData;
        var previousAutoAxesIgnoresBadData = FinalFigureGraphView.AutoAxesIgnoresBadData;
        try
        {
            AppSettings.TraceabilityModeEnabled = false;
            DataManager.Clear(DataClearMode.ResetSession);
            AllExcludedGraphsKeepFiniteAxes(false);
            AllExcludedGraphsKeepFiniteAxes(true);
            ExcludedDataOnlyAffectsRangeWhenRequested();
            NullPredictionAndFiniteObservationRangesAreAggregated();
            EmptyInjectionSetKeepsFiniteAxes();
            UnifiedRangeUsesIncludedMembersOnly();
            AnalysisAndSelectedFitEntryPointsHandleExcludedData();
            FinalFigureManualLimitsTakePrecedence();
            Console.WriteLine("PASS: 8 native fit graph axis scenario groups");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally
        {
            AppSettings.TraceabilityModeEnabled = previousTraceability;
            FinalFigureGraphView.FitYAxisMin = previousFitYAxisMin;
            FinalFigureGraphView.FitYAxisMax = previousFitYAxisMax;
            FinalFigureGraphView.ShowBadData = previousShowBadData;
            FinalFigureGraphView.AutoAxesIgnoresBadData = previousAutoAxesIgnoresBadData;
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static void AllExcludedGraphsKeepFiniteAxes(bool attachFit)
    {
        var data = Experiment("all-excluded", (10, false), (30, false));
        if (attachFit) AttachFit(data, 20);
        DataFittingGraph graph;
        using (var view = new NSView(new CGRect(0, 0, 640, 480)))
        {
            graph = new DataFittingGraph(data, view);
            graph.DrawOnWhite = true;
            var expectedMin = attachFit ? -2 : -0.1;
            var expectedMax = attachFit ? 22 : 0.1;
            Check(Math.Abs(graph.YAxis.Min - expectedMin) < 1e-4
                && Math.Abs(graph.YAxis.Max - expectedMax) < 1e-4,
                "All-excluded graph did not use the finite fit-only or zero fallback bounds");
            Check(double.IsFinite(graph.YAxis.Min) && double.IsFinite(graph.YAxis.Max)
                && graph.YAxis.Max > graph.YAxis.Min,
                "All-excluded observations produced a non-finite or degenerate range");
            graph.SetFrame(new CGSize(320, 220), CGPoint.Empty);
            using (var space = CGColorSpace.CreateDeviceRGB())
            using (var bitmap = new CGBitmapContext(IntPtr.Zero, 640, 480, 8, 2560,
                space, CGImageAlphaInfo.PremultipliedLast))
                graph.PrepareDraw(bitmap, new CGPoint(320, 240));
        }

        var nonfinite = Experiment("nonfinite-included", (double.NaN, true), (double.PositiveInfinity, true));
        using (var view = new NSView(new CGRect(0, 0, 640, 480)))
        {
            graph = new DataFittingGraph(nonfinite, view);
            Check(double.IsFinite(graph.YAxis.Min) && double.IsFinite(graph.YAxis.Max)
                && graph.YAxis.Max > graph.YAxis.Min,
                "Non-finite-only eligible observations produced invalid axes");
        }
    }

    static void ExcludedDataOnlyAffectsRangeWhenRequested()
    {
        var data = Experiment("excluded-scaling", (12, true), (900, false));
        var includedOnly = new DataFittingGraph(data, null) { HideBadData = true };
        Check(Math.Abs(includedOnly.YAxis.Min + 1.2) < 1e-4
            && Math.Abs(includedOnly.YAxis.Max - 13.2) < 1e-4,
            "Included-only observations did not produce independently expected buffered bounds");

        var includeExcluded = new DataFittingGraph(data, null);
        includeExcluded.AutoAxesFocusesIncludedOnly = false;
        includeExcluded.HideBadData = false;
        Check(Math.Abs(includeExcluded.YAxis.Min + 90) < 1e-4
            && Math.Abs(includeExcluded.YAxis.Max - 990) < 1e-4,
            "Opted-in excluded observation did not produce independently expected buffered bounds");

        FinalFigureGraphView.ShowBadData = false;
        FinalFigureGraphView.AutoAxesIgnoresBadData = false;
        AppSettings.TraceabilityModeEnabled = false;
        var hiddenFigure = FinalFigureGraphView.SetupForExport(data);
        var hiddenGraph = FigureFitGraph(hiddenFigure);
        Check(Math.Abs(hiddenGraph.YAxis.Min + 1.2) < 1e-4
            && Math.Abs(hiddenGraph.YAxis.Max - 13.2) < 1e-4,
            "Final Figure hide option did not preserve included-only bounds");
        DisposeFigure(hiddenFigure);

        AppSettings.TraceabilityModeEnabled = true;
        var traceabilityFigure = FinalFigureGraphView.SetupForExport(data);
        var traceabilityGraph = FigureFitGraph(traceabilityFigure);
        Check(Math.Abs(traceabilityGraph.YAxis.Min + 90) < 1e-4
            && Math.Abs(traceabilityGraph.YAxis.Max - 990) < 1e-4,
            "Traceability Mode did not ignore hiding and scale excluded observations");
        DisposeFigure(traceabilityFigure);
        FinalFigureGraphView.AutoAxesIgnoresBadData = true;
        var includedOnlyFigure = FinalFigureGraphView.SetupForExport(data);
        var includedOnlyGraph = FigureFitGraph(includedOnlyFigure);
        Check(Math.Abs(includedOnlyGraph.YAxis.Min + 1.2) < 1e-4
            && Math.Abs(includedOnlyGraph.YAxis.Max - 13.2) < 1e-4,
            "Final Figure included-only option did not exclude the bad observation");
        DisposeFigure(includedOnlyFigure);
        AppSettings.TraceabilityModeEnabled = false;
    }

    static void NullPredictionAndFiniteObservationRangesAreAggregated()
    {
        var data = Experiment("null-range-all-excluded", (15, false), (30, false));
        var graph = new DataFittingGraph(data, null);
        graph.NullComparison = Comparison(data, -300, 500, double.NaN);
        graph.ShowNullPrediction = true;
        Check(Math.Abs(graph.YAxis.Min + 380) < 1e-4 && Math.Abs(graph.YAxis.Max - 580) < 1e-4,
            "Enabled finite Null predictions did not set exact bounds for all-excluded observations");
        graph.ShowNullPrediction = false;
        Check(Math.Abs(graph.YAxis.Min + 0.1) < 1e-4 && Math.Abs(graph.YAxis.Max - 0.1) < 1e-4,
            "Hidden Null predictions changed the neutral all-excluded range");

        var finiteObservation = Experiment("finite-and-nonfinite", (15, true), (double.PositiveInfinity, true));
        graph = new DataFittingGraph(finiteObservation, null);
        Check(Math.Abs(graph.YAxis.Min + 1.5) < 1e-4 && Math.Abs(graph.YAxis.Max - 16.5) < 1e-4,
            "A nonfinite included observation changed finite observation bounds");
        graph.NullComparison = Comparison(finiteObservation, double.NaN, double.PositiveInfinity);
        graph.ShowNullPrediction = true;
        Check(Math.Abs(graph.YAxis.Min + 1.5) < 1e-4 && Math.Abs(graph.YAxis.Max - 16.5) < 1e-4,
            "Nonfinite Null predictions changed finite observation bounds");
    }

    static void UnifiedRangeUsesIncludedMembersOnly()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var includedA = Experiment("unified-a", (20, true));
        var includedB = Experiment("unified-b", (-75, false));
        var selectedOutside = Experiment("outside", (1500, true));
        DataManager.AddData(includedA);
        DataManager.AddData(includedB);
        DataManager.AddData(selectedOutside);
        selectedOutside.Include = false;
        var graph = new DataFittingGraph(selectedOutside, null) { UnifiedEnthalpyAxis = true };
        Check(Math.Abs(graph.YAxis.Min + 2) < 1e-4 && Math.Abs(graph.YAxis.Max - 22) < 1e-4,
            "Unified axis included selected experiment outside IncludedData");

        DataManager.Clear(DataClearMode.ResetSession);
        var allExcludedMember = Experiment("unified-empty", (1500, false));
        DataManager.AddData(allExcludedMember);
        Check(DataManager.IncludedData.Count() == 1,
            "The all-excluded member must remain in IncludedData for the unified group check");
        graph = new DataFittingGraph(allExcludedMember, null) { UnifiedEnthalpyAxis = true };
        Check(double.IsFinite(graph.YAxis.Min) && double.IsFinite(graph.YAxis.Max)
            && Math.Abs(graph.YAxis.Min + 0.1) < 1e-4 && Math.Abs(graph.YAxis.Max - 0.1) < 1e-4,
            "All-excluded member in a nonempty unified group did not use neutral bounds");

        DataManager.Clear(DataClearMode.ResetSession);
        selectedOutside = Experiment("unified-empty-members", (1500, true));
        DataManager.AddData(selectedOutside);
        selectedOutside.Include = false;
        Check(DataManager.IncludedData.Count() == 0,
            "The empty unified group must have no included experiments");
        graph = new DataFittingGraph(selectedOutside, null) { UnifiedEnthalpyAxis = true };
        Check(Math.Abs(graph.YAxis.Min + 150) < 1e-4 && Math.Abs(graph.YAxis.Max - 1650) < 1e-4,
            "Empty IncludedData did not retain the existing selected-experiment fallback");
    }

    static void EmptyInjectionSetKeepsFiniteAxes()
    {
        var data = Experiment("empty-injections");
        var graph = new DataFittingGraph(data, null);
        Check(double.IsFinite(graph.YAxis.Min) && double.IsFinite(graph.YAxis.Max)
            && graph.YAxis.Max > graph.YAxis.Min,
            "An experiment with no injections produced invalid axes");
    }

    static void AnalysisAndSelectedFitEntryPointsHandleExcludedData()
    {
        var data = Experiment("entry-point", (10, false), (30, false));
        AttachFit(data, 70);
        var analysisHandle = Send(Send(new Class(typeof(AnalysisGraphView)).Handle,
            Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        using (var view = Runtime.GetNSObject<AnalysisGraphView>(analysisHandle))
        {
            view.Frame = new CGRect(0, 0, 640, 480);
            view.Initialize(data);
            var graph = (DataFittingGraph)view.Graph;
            Check(Math.Abs(graph.YAxis.Min + 7) < 1e-4 && Math.Abs(graph.YAxis.Max - 77) < 1e-4,
                "Analyze initialization omitted the independent 70-unit fit prediction");
            view.Initialize(data);
            graph = (DataFittingGraph)view.Graph;
            Check(double.IsFinite(graph.YAxis.Min) && double.IsFinite(graph.YAxis.Max),
                "Analyze reinitialization produced invalid axes");
        }

        var resultHandle = Send(Send(new Class(typeof(ResultGraphView)).Handle,
            Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        using (var resultView = Runtime.GetNSObject<ResultGraphView>(resultHandle))
        {
            resultView.Frame = new CGRect(0, 0, 640, 480);
            resultView.SetupSelectedFit(data.Solution);
            var graph = (DataFittingGraph)typeof(ResultGraphView).GetProperty("Graph",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(resultView);
            Check(double.IsFinite(graph.YAxis.Min) && double.IsFinite(graph.YAxis.Max),
                "Selected-fit initialization produced invalid axes");
            graph.ResidualDisplayOptions.ShowResidualGraph = true;
            graph.ResidualGraph.SetupAxisScalingUnits();
            Check(Math.Abs(graph.ResidualGraph.YAxis.Min + 1100) < 1e-9
                && Math.Abs(graph.ResidualGraph.YAxis.Max - 1100) < 1e-9,
                "Neutral residual axis changed from +/-1100");
        }
    }

    static void FinalFigureManualLimitsTakePrecedence()
    {
        var data = Experiment("final-figure", (10, false), (30, false));
        AttachFit(data, 70);
        FinalFigureGraphView.FitYAxisMin = -25;
        FinalFigureGraphView.FitYAxisMax = 45;
        var figure = FinalFigureGraphView.SetupForExport(data);
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 900, 700, 8, 3600,
            space, CGImageAlphaInfo.PremultipliedLast))
        {
            figure.Draw(bitmap, new CGPoint(450, 350));
            var graph = (DataFittingGraph)typeof(FinalFigure).GetField("IntegrationGraph",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(figure);
            Check(Math.Abs(graph.YAxis.Min * graph.YAxis.ValueFactor + 25) < 1e-4
                && Math.Abs(graph.YAxis.Max * graph.YAxis.ValueFactor - 45) < 1e-4,
                "Final Figure automatic range replaced the configured manual Y limits");
        }
        DisposeFigure(figure);
    }

    static DataFittingGraph FigureFitGraph(FinalFigure figure)
        => (DataFittingGraph)typeof(FinalFigure).GetField("IntegrationGraph",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(figure);

    static void DisposeFigure(FinalFigure figure)
        => ((NSView)typeof(FinalFigure).GetField("View", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(figure)).Dispose();

    static ExperimentData Experiment(string name, params (double enthalpy, bool included)[] injections)
    {
        var data = new ExperimentData(name + ".itc") { SyringeConcentration = new FloatWithError(1) };
        for (var i = 0; i < injections.Length; i++)
        {
            var item = new InjectionData(data, i, 1, 1, injections[i].included) { Ratio = i };
            item.SetPeakArea(new FloatWithError(injections[i].enthalpy));
            data.Injections.Add(item);
        }
        return data;
    }

    static void AttachFit(ExperimentData data, double prediction)
    {
        data.Injections[0].Include = true;
        var model = new FixedPredictionModel(data, prediction);
        model.InitializeParameters(data);
        model.Parameters.Table[ParameterType.Enthalpy1].Update(prediction);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
        data.Injections[0].Include = false;
    }

    sealed class FixedPredictionModel : OneSetOfSites
    {
        readonly double prediction;
        public FixedPredictionModel(ExperimentData data, double prediction) : base(data) => this.prediction = prediction;
        public override double Evaluate(int injectionindex, bool withoffset = true)
            => prediction * Data.Injections[injectionindex].InjectionMass;
    }

    static NullModelComparison Comparison(ExperimentData data, params double[] predictions)
    {
        var member = new NullModelComparisonMember { ExperimentId = data.UniqueID };
        foreach (var prediction in predictions)
            member.Points.Add(new NullModelComparisonPoint
            {
                Ratio = 0, InjectionMass = 1, PredictedHeatJoules = prediction,
            });
        return new NullModelComparison { Members = { member } };
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
