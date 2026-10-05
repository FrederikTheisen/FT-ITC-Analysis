// Native renderer regression fixture using the actual macOS graph implementation.
// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using AppKit;
using CoreGraphics;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.Drawing;

static class AnalysisNullGraphTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static int Main(string[] args)
    {
        NSApplication.Init();
        try
        {
            NullLineUsesPlotOriginAndClipping(false, 81);
            NullLineUsesPlotOriginAndClipping(true, 94);
            NullPredictionUpdatesAxisRange();
            SavedPredictionUsesCapturedCoordinatesAndBindingOffset();
            NonconstantNullModelUsesModelEvaluationAndInterpolation();
            ToolbarMenuTracksAvailablePrediction();
            var controller = File.ReadAllText(Path.Combine(args[0],
                "AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs"));
            Check(!controller.Contains("nullPredictionToggle")
                && !controller.Contains("EnsureNullPredictionToggle"),
                "Analysis view still creates a floating null checkbox");
            Console.WriteLine("PASS: native null line origin, clipping, dash isolation, residual layout, axes, saved values and toolbar state");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    static ExperimentData Experiment()
    {
        var data = new ExperimentData("null-graph.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            TargetTemperature = 25,
        };
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 2e-9, include: true)
            {
                Ratio = index,
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = (index + 1) * 5e-6,
            };
            injection.SetPeakArea(new FloatWithError(2e-8));
            data.Injections.Add(injection);
        }
        return data;
    }

    static NullModelComparison Comparison(ExperimentData data, double enthalpy)
    {
        var comparison = new NullModelComparison();
        var member = new NullModelComparisonMember { ExperimentId = data.UniqueID };
        // Extend beyond both X limits to exercise the graph layer's clipping.
        foreach (var ratio in new[] { -0.5, 2.5 })
            member.Points.Add(new NullModelComparisonPoint
            {
                Ratio = ratio,
                InjectionMass = 2e-9,
                PredictedHeatJoules = enthalpy * 2e-9,
            });
        comparison.Members.Add(member);
        return comparison;
    }

    static void NullLineUsesPlotOriginAndClipping(bool residuals, int expectedY)
    {
        var data = Experiment();
        var graph = new DataFittingGraph(data, null);
        graph.NullComparison = Comparison(data, 50);
        graph.ShowNullPrediction = true;
        graph.ResidualDisplayOptions.ShowResidualGraph = residuals;
        graph.ResidualDisplayOptions.ResidualFraction = 0.25f;
        graph.SetFrame(new CGSize(200, 100), new CGPoint(47, 31));
        graph.XAxis.Min = 0;
        graph.XAxis.Max = 2;
        graph.YAxis.Min = 0;
        graph.YAxis.Max = 100;
        graph.SetupAxisScalingUnits();

        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 320, 240, 8, 1280,
            space, CGImageAlphaInfo.PremultipliedLast))
        {
            DrawNullFit(graph, data, bitmap);
            var pixels = new byte[320 * 240 * 4];
            Marshal.Copy(bitmap.Data, pixels, 0, pixels.Length);
            var painted = 0;
            var gaps = 0;
            for (var y = 0; y < 240; y++)
                for (var x = 0; x < 320; x++)
                    if (pixels[(y * 320 + x) * 4 + 3] != 0)
                    {
                        Check(x >= 47 && x < 247 && Math.Abs(239 - y - expectedY) <= 2,
                            "Null line escaped the fit plot or missed its origin (" + x + ", " + y + ")");
                        painted++;
                    }
            for (var x = 52; x < 242; x++)
                if (pixels[((239 - expectedY) * 320 + x) * 4 + 3] == 0) gaps++;
            Check(painted > 150 && gaps > 30, "Null line is missing or no longer dashed");

            // A subsequent ordinary fit line must remain solid.
            using (var black = new CGColor(0, 0, 0, 1))
                graph.DrawSpline(bitmap, new[] { new CGPoint(5, 10), new CGPoint(195, 10) },
                    2, black, GraphBase.LineSmoothness.Linear);
            Marshal.Copy(bitmap.Data, pixels, 0, pixels.Length);
            var solidY = residuals ? 66 : 41;
            for (var x = 54; x < 240; x++)
                Check(pixels[((239 - solidY) * 320 + x) * 4 + 3] != 0,
                    "Null dashes leaked into the ordinary fit line");
        }
    }

    static void DrawNullFit(DataFittingGraph graph, ExperimentData data, CGContext context)
    {
        var predictions = typeof(DataFittingGraph).GetMethod("NullPredictionPointsFor", PrivateInstance)
            .Invoke(graph, new object[] { data });
        typeof(DataFittingGraph).GetMethod("DrawFit", PrivateInstance)
            .Invoke(graph, new object[] { context, predictions, 1.6f, NSColor.SystemPurple.CGColor,
                new nfloat[] { 5, 3 } });
    }

    static void NonconstantNullModelUsesModelEvaluationAndInterpolation()
    {
        var data = Experiment();
        var model = new NonconstantModel(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        var comparison = Comparison(data, 999);
        comparison.NullSolutions.Add(model.Solution);
        var graph = new DataFittingGraph(data, null) { NullComparison = comparison };
        var predictions = ((IEnumerable)typeof(DataFittingGraph).GetMethod("NullPredictionPointsFor", PrivateInstance)
            .Invoke(graph, new object[] { data })).Cast<object>().ToArray();
        var expected = new[] { 100.0, 300.0, 100.0 };
        Check(predictions.Length == 3 && model.EvaluationCount >= 3,
            "Null curve did not evaluate the supplied model");
        for (var index = 0; index < 3; index++)
            Check(Math.Abs((double)predictions[index].GetType().GetField("Item2").GetValue(predictions[index])
                - expected[index]) < 1e-10, "Nonconstant null model was replaced with captured or Offset values");

        graph.ResidualDisplayOptions.ShowResidualGraph = false;
        graph.SetFrame(new CGSize(200, 100), new CGPoint(47, 31));
        graph.XAxis.Min = 0;
        graph.XAxis.Max = 2;
        graph.YAxis.Min = 0;
        graph.YAxis.Max = 400;
        graph.SetupAxisScalingUnits();
        using (var space = CGColorSpace.CreateDeviceRGB())
        {
            foreach (var smoothness in new[] { GraphBase.LineSmoothness.Linear, GraphBase.LineSmoothness.Smooth })
            {
                graph.FitLineSmoothnessSetting = smoothness;
                using (var bitmap = new CGBitmapContext(IntPtr.Zero, 320, 240, 8, 1280,
                    space, CGImageAlphaInfo.PremultipliedLast))
                {
                    DrawNullFit(graph, data, bitmap);
                    var pixels = new byte[320 * 240 * 4];
                    Marshal.Copy(bitmap.Data, pixels, 0, pixels.Length);
                    var maxY = 0;
                    for (var y = 0; y < 240; y++)
                        for (var x = 48; x < 246; x++)
                            if (pixels[(y * 320 + x) * 4 + 3] != 0) maxY = Math.Max(maxY, 239 - y);
                    // Linear reaches 300 J/mol; the quadratic midpoint curve peaks at 250 J/mol.
                    var expectedY = smoothness == GraphBase.LineSmoothness.Linear ? 106 : 94;
                    Check(Math.Abs(maxY - expectedY) <= 2, "Null curve ignored the model interpolation setting");
                }
            }
        }
    }

    sealed class NonconstantModel : OneSetOfSites
    {
        public NonconstantModel(ExperimentData data) : base(data) { }
        public int EvaluationCount { get; private set; }
        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            EvaluationCount++;
            return (injectionindex == 1 ? 300.0 : 100.0) * Data.Injections[injectionindex].InjectionMass;
        }
    }

    static void NullPredictionUpdatesAxisRange()
    {
        var data = Experiment();
        var graph = new DataFittingGraph(data, null);
        graph.NullComparison = Comparison(data, 1000);
        Check(graph.YAxis.Max < 100, "Hidden null prediction changed the axis range");
        graph.ShowNullPrediction = true;
        Check(graph.YAxis.Max >= 1000, "Enabling null prediction did not expand the axes");
        graph.NullComparison = Comparison(data, -2000);
        Check(graph.YAxis.Min <= -2000 && graph.YAxis.Max < 500,
            "Replacing the comparison left stale axis limits");
        graph.ShowNullPrediction = false;
        Check(graph.YAxis.Min > -100, "Disabling null prediction left stale axis limits");
        graph.ShowNullPrediction = true;
        graph.NullComparison = null;
        Check(graph.YAxis.Min > -100, "Clearing the comparison left stale axis limits");
    }

    static void SavedPredictionUsesCapturedCoordinatesAndBindingOffset()
    {
        var data = Experiment();
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.Table[ParameterType.Offset].Update(125);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
        var graph = new DataFittingGraph(data, null);
        var comparison = Comparison(data, 100);
        comparison.Members[0].Points.Add(new NullModelComparisonPoint
        {
            Ratio = 1, InjectionMass = double.PositiveInfinity, PredictedHeatJoules = 1,
        });
        graph.NullComparison = comparison;
        graph.DrawWithOffset = false;
        var method = typeof(DataFittingGraph).GetMethod("NullPredictionPointsFor", PrivateInstance);
        var points = ((IEnumerable)method.Invoke(graph, new object[] { data })).Cast<object>().ToArray();
        Check(points.Length == 2, "Non-finite captured mass was included");
        Check(Math.Abs((double)points[0].GetType().GetField("Item1").GetValue(points[0]) + 0.5) < 1e-12,
            "Null prediction did not use the captured ratio");
        Check(Math.Abs((double)points[0].GetType().GetField("Item2").GetValue(points[0]) + 25) < 1e-10,
            "Null heat was not converted to J/mol with the binding offset subtracted");
        graph.DrawWithOffset = true;
        points = ((IEnumerable)method.Invoke(graph, new object[] { data })).Cast<object>().ToArray();
        Check(Math.Abs((double)points[0].GetType().GetField("Item2").GetValue(points[0]) - 100) < 1e-10,
            "Raw null enthalpy changed with the display offset enabled");
        Check(!((IEnumerable)method.Invoke(graph, new object[] { Experiment() })).Cast<object>().Any(),
            "A different experiment's null prediction was used");

        var otherData = Experiment();
        var otherModel = new OneSetOfSites(otherData);
        otherModel.InitializeParameters(otherData);
        otherModel.Parameters.Table[ParameterType.Offset].Update(75);
        otherModel.Solution = SolutionInterface.FromModel(otherModel,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        otherData.Model = otherModel;
        comparison.Members.Add(Comparison(otherData, 100).Members[0]);
        graph.DrawWithOffset = false;
        points = ((IEnumerable)method.Invoke(graph, new object[] { otherData })).Cast<object>().ToArray();
        Check(Math.Abs((double)points[0].GetType().GetField("Item2").GetValue(points[0]) - 25) < 1e-10,
            "Shared-axis null values did not subtract each experiment's binding offset");
    }

    static void ToolbarMenuTracksAvailablePrediction()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var data = Experiment();
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
        DataManager.AddData(new[] { data });
        DataManager.SelectIndex(0);
        var controller = (MainWindowController)FormatterServices.GetUninitializedObject(typeof(MainWindowController));
        var populate = typeof(MainWindowController).GetMethod("PopulateAnalysisToolbarMenu", PrivateInstance);
        foreach (var available in new[] { false, true })
        {
            typeof(SolutionInterface).GetProperty("NullComparison")
                .SetValue(model.Solution, available ? Comparison(data, 50) : null);
            AnalysisGraphView.ShowNullPrediction = available;
            using (var menu = new NSMenu())
            {
                populate.Invoke(controller, new object[] { menu });
                var item = menu.Items.Single(candidate => candidate.Identifier == "analysisshownullprediction");
                Check(item.Enabled == available && (item.State == NSCellStateValue.On) == available,
                    "Toolbar null option has the wrong availability or checkmark");
            }
        }
        typeof(MainWindowController).GetField("stopableProcessRunning", PrivateInstance).SetValue(controller, true);
        using (var menu = new NSMenu())
        {
            populate.Invoke(controller, new object[] { menu });
            Check(!menu.Items.Single(candidate => candidate.Identifier == "analysisshownullprediction").Enabled,
                "Null prediction option remained enabled during a fit");
        }
        AnalysisGraphView.ShowNullPrediction = false;
        DataManager.Clear(DataClearMode.ResetSession);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
