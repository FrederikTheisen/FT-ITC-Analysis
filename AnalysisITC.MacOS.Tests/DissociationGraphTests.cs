// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh DissociationGraphTests
using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.UI.MacOS.Drawing;

static class DissociationGraphTests
{
    static int Main(string[] args)
    {
        NSApplication.Init();
        var failures = 0;
        Run("Imported buffer-cell concentration axis", () => ImportedBufferCellHasXAxis(args[0]), ref failures);
        Run("Saved buffer-cell data with non-finite ratios", SavedConcentrationsWithUnusableRatios, ref failures);
        Run("Saved dissociation concentration coordinates", DissociationUsesConcentrationCoordinates, ref failures);
        Run("Model selection updates both axes", ModelSelectionUpdatesBothAxes, ref failures);
        Run("Unified concentration axes", UnifiedAxesUseMatchingCoordinates, ref failures);
        Run("Native final-figure concentration axis", FinalFigureUsesConcentrationAxis, ref failures);
        Run("Injection-number fallback", MissingConcentrationsUseInjectionNumber, ref failures);
        return failures == 0 ? 0 : 1;
    }

    static void ImportedBufferCellHasXAxis(string root)
    {
        var reader = typeof(RawDataReader).Assembly.GetType("AnalysisITC.Core.DataReaders.MicroCalITC200Reader");
        var data = (ExperimentData)reader.GetMethod("ReadPath").Invoke(null,
            new object[] { Path.Combine(root, "AnalysisITC.Tests/dissociation_data_test.itc") });
        foreach (var injection in data.Injections)
            injection.SetPeakArea(new FloatWithError(1e-6, 1e-8));
        AttachDissociation(data);
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = new DataFittingGraph(data, view);
            Draw(graph);
            CheckConcentrationAxis(graph);
            Check(graph.XAxis.GetValidTicks().Item1.Count > 2, "Fit concentration ticks are missing");
            Check(graph.ResidualGraph.XAxis.GetValidTicks().Item1.Count > 2, "Residual concentration ticks are missing");
            graph.ResidualDisplayOptions.ShowResidualGraph = false;
            Draw(graph);
            Check(!graph.XAxis.HideLabels, "Hiding residuals also hides the fit X-axis labels");
        }
    }

    static void SavedConcentrationsWithUnusableRatios()
    {
        var data = Experiment();
        data.CellConcentration = new FloatWithError(0);
        foreach (var injection in data.Injections)
        {
            injection.ActualCellConcentration = 0;
            injection.Ratio = double.PositiveInfinity;
        }
        AttachDissociation(data);
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = new DataFittingGraph(data, view);
            Draw(graph, "analysis-dissociation");
            CheckConcentrationAxis(graph);
            Check(graph.XAxis.Max * graph.XAxis.ValueFactor < 20, "Unusable ratios changed the concentration range");
            Check(graph.ResidualGraph.XAxis.GetValidTicks().Item1.Count > 2, "Unusable ratios erased concentration ticks");
            Check(data.Injections.All(injection => double.IsPositiveInfinity(injection.Ratio)), "Drawing rewrote saved ratios");
        }
    }

    static void DissociationUsesConcentrationCoordinates()
    {
        var data = Experiment();
        AttachDissociation(data);
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = new DataFittingGraph(data, view);
            Draw(graph);
            CheckConcentrationAxis(graph);
            // The saved post-injection concentrations are 5, 10 and 15 µM.
            Check(graph.XAxis.Max * graph.XAxis.ValueFactor < 20,
                "Concentration axis uses the molar-ratio field: maximum = " + graph.XAxis.Max * graph.XAxis.ValueFactor);
            foreach (var injection in data.Injections)
            {
                var point = graph.GetRelativePosition(injection.ActualTitrantConcentration, injection.Enthalpy);
                var hit = graph.CursorFeatureFromPos(point + new CGSize(graph.Origin));
                Check(hit.IsMouseOverFeature && hit.FeatureID == injection.ID,
                    "Injection #" + (injection.ID + 1) + " is not plotted at its saved concentration");
                Check(hit.ToolTip.Contains("[Titrant] (µM): " + (5 * (injection.ID + 1)).ToString("F2", CultureInfo.InvariantCulture)),
                    "Concentration tooltip reports a molar ratio");
                var residual = data.Model.Residual(injection) / injection.InjectionMass;
                var residualPoint = graph.ResidualGraph.GetRelativePosition(injection.ActualTitrantConcentration, residual);
                var residualHit = graph.ResidualGraph.CursorFeatureFromPos(residualPoint + new CGSize(graph.ResidualGraph.Origin));
                Check(residualHit.IsMouseOverFeature && residualHit.FeatureID == injection.ID,
                    "Residual is not plotted at the matching concentration");
            }
            var predictions = ((System.Collections.IEnumerable)typeof(DataFittingGraph)
                .GetMethod("FitPredictionPointsFor", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(graph, new object[] { data, data.Model, true, 0.0, null })).Cast<object>().ToArray();
            for (var index = 0; index < predictions.Length; index++)
            {
                var x = (double)predictions[index].GetType().GetField("Item1").GetValue(predictions[index]);
                Check(Math.Abs(x - (index + 1) * 5e-6) < 1e-15, "Fit prediction uses the molar ratio");
            }
        }
    }

    static void UnifiedAxesUseMatchingCoordinates()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        try
        {
            var data = Experiment();
            AttachDissociation(data);
            var otherConcentrationData = Experiment();
            otherConcentrationData.CellConcentration = new FloatWithError(0);
            foreach (var injection in otherConcentrationData.Injections)
                injection.ActualTitrantConcentration *= 2;
            var ratioData = Experiment();
            foreach (var injection in ratioData.Injections) injection.Ratio = 100;
            DataManager.AddData(new[] { data, otherConcentrationData, ratioData });
            using (var view = new NSView(new CGRect(0, 0, 800, 600)))
            {
                var graph = new DataFittingGraph(data, view) { UnifiedMolarRatioAxis = true };
                Draw(graph);
                Check(graph.XAxis.Max * graph.XAxis.ValueFactor > 30
                    && graph.XAxis.Max * graph.XAxis.ValueFactor < 40,
                    "Unified concentration range omitted another concentration plot or included a molar-ratio plot");
            }
        }
        finally { DataManager.Clear(DataClearMode.ResetSession); }
    }

    static void FinalFigureUsesConcentrationAxis()
    {
        var data = Experiment();
        AttachDissociation(data);
        FinalFigureGraphView.MolarRatioAxisTitle = "";
        var figure = FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.BindingModel);
        var graph = (DataFittingGraph)typeof(FinalFigure).GetField("IntegrationGraph",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(figure);
        graph.ShowParameterBox = false;
        graph.ShowParameterGuides = false;
        figure.DrawConfidence = false;
        figure.ShowResiduals = true;
        figure.FitXAxisMin = 2;
        figure.FitXAxisMax = 20;
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 800, 600, 8, 3200,
            space, CGImageAlphaInfo.PremultipliedLast))
        {
            bitmap.SetFillColor(NSColor.White.CGColor);
            bitmap.FillRect(new CGRect(0, 0, 800, 600));
            figure.Draw(bitmap, new CGPoint(400, 300));
            CheckConcentrationAxis(graph);
            Check(Math.Abs(graph.XAxis.Min - 2e-6) < 1e-12 && Math.Abs(graph.XAxis.Max - 20e-6) < 1e-12,
                "Final Figure did not interpret custom limits in µM");
            Check(graph.ResidualGraph.XAxis.GetValidTicks().Item1.Count > 2, "Final Figure concentration ticks are missing");
            SaveSnapshot(bitmap, "final-figure-dissociation");
            figure.MolarRatioAxisTitle = "Total concentration";
            figure.Draw(bitmap, new CGPoint(400, 300));
            Check(graph.ResidualGraph.XAxis.LegendTitle == "Total concentration", "Residual axis discarded the custom title");
        }
    }

    static void MissingConcentrationsUseInjectionNumber()
    {
        var data = Experiment();
        data.CellConcentration = data.SyringeConcentration = new FloatWithError(0);
        foreach (var injection in data.Injections) injection.Ratio = 0;
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = new DataFittingGraph(data, view);
            graph.ResidualDisplayOptions.ShowResidualGraph = false;
            Draw(graph);
            Check(graph.XAxis.Max > 3 && graph.XAxis.Max < 4, "Injection-number range is missing");
            Check(graph.XAxis.LegendTitle == "Injection Number" && graph.XAxis.ValueFactor == 1,
                "Missing concentrations did not select an injection-number axis");
            Check(graph.XAxis.GetValidTicks().Item1.Count > 2, "Injection-number ticks are missing");
        }
    }

    static void ModelSelectionUpdatesBothAxes()
    {
        var data = Experiment();
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        {
            var graph = new DataFittingGraph(data, view);
            Check(graph.XAxis.ValueFactor == 1, "Fixture should start on a molar-ratio axis");
            AttachDissociation(data);
            Draw(graph);
            CheckConcentrationAxis(graph);
        }
    }

    static ExperimentData Experiment()
    {
        var data = new ExperimentData("dissociation-axis.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            TargetTemperature = 25,
        };
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8.4e-10, include: true)
            {
                ActualCellConcentration = 35e-6,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) / 7.0,
            };
            injection.SetPeakArea(new FloatWithError(2e-6 - index * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        return data;
    }

    static void AttachDissociation(ExperimentData data)
    {
        var model = new Dissociation(data);
        model.InitializeParameters(data);
        model.Parameters.Table[ParameterType.Enthalpy1].Update(-18000);
        model.Parameters.Table[ParameterType.Affinity1].Update(Math.Log10(1.0 / 32e-6));
        model.Parameters.Table[ParameterType.Offset].Update(0);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
    }

    static void Draw(DataFittingGraph graph, string snapshotName = null)
    {
        graph.ShowParameterBox = false;
        graph.ShowParameterGuides = false;
        graph.ShowPeakInfo = false;
        graph.DrawOnWhite = true;
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 800, 600, 8, 3200,
            space, CGImageAlphaInfo.PremultipliedLast))
        {
            bitmap.SetFillColor(NSColor.White.CGColor);
            bitmap.FillRect(new CGRect(0, 0, 800, 600));
            graph.PrepareDraw(bitmap, new CGPoint(400, 300));
            var pixels = new byte[800 * 600 * 4];
            Marshal.Copy(bitmap.Data, pixels, 0, pixels.Length);
            var labels = 0;
            for (var y = 570; y < 600; y++)
                for (var x = 100; x < 700; x++)
                    if (pixels[(y * 800 + x) * 4] < 200) labels++;
            Check(labels > 50, "Rendered X-axis labels are missing");
            SaveSnapshot(bitmap, snapshotName);
        }
    }

    static void SaveSnapshot(CGBitmapContext bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FTITC_DISSOCIATION_GRAPH_QA_DIRECTORY");
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name)) return;
        Directory.CreateDirectory(directory);
        using (var image = bitmap.ToImage())
        using (var representation = new NSBitmapImageRep(image))
        using (var png = representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()))
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), png.ToArray());
    }

    static void CheckConcentrationAxis(DataFittingGraph graph)
    {
        Check(graph.XAxis.ValueFactor == 1000000, "Fit axis is not scaled to µM");
        Check(graph.XAxis.LegendTitle == "[Titrant] (µM)", "Fit concentration title is missing");
        Check(graph.ResidualGraph.XAxis.ValueFactor == 1000000, "Residual axis is not scaled to µM");
        Check(graph.ResidualGraph.XAxis.LegendTitle == "[Titrant] (µM)", "Residual concentration title is missing");
    }

    static void Run(string name, Action test, ref int failures)
    {
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + ": " + error.Message); }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
