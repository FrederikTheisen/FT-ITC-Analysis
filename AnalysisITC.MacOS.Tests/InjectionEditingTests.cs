// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh InjectionEditingTests
using System;
using System.Reflection;
using AppKit;
using CoreGraphics;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.Drawing;

static class InjectionEditingTests
{
    static int Main()
    {
        NSApplication.Init();
        try
        {
            VerifyGraph(false);
            VerifyGraph(true);
            Console.WriteLine("PASS: native analysis and buffer injection edits wait for processing completion");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    static void VerifyGraph(bool buffer)
    {
        var data = new ExperimentData("inclusion.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            TargetTemperature = 25,
        };
        for (var i = 0; i < 3; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 8.4e-10, include: true)
            {
                Ratio = i + 1,
                ActualCellConcentration = 35e-6,
                ActualTitrantConcentration = (i + 1) * 5e-6,
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + i * 1e-7, 1e-8));
            data.Injections.Add(injection);
        }
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
        using (var view = new NSView(new CGRect(0, 0, 800, 600)))
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 800, 600, 8, 3200,
            space, CGImageAlphaInfo.PremultipliedLast))
        {
            CGGraph graph = buffer
                ? (CGGraph)new BufferSubtractionGraph(data, new ExperimentData[0], null, view)
                : new DataFittingGraph(data, view);
            graph.DrawOnWhite = true;
            var fitting = graph as DataFittingGraph;
            if (fitting != null)
            {
                fitting.ShowParameterBox = false;
                fitting.ShowParameterGuides = false;
                fitting.ResidualDisplayOptions.ShowResidualGraph = false;
            }
            graph.PrepareDraw(bitmap, new CGPoint(400, 300));
            var injection = data.Injections[0];
            var point = graph.GetRelativePosition(buffer ? 1 : injection.Ratio,
                buffer ? injection.RawPeakArea.Value : injection.Enthalpy) + new CGSize(graph.Origin);
            var changes = 0;
            var bufferChanges = 0;
            data.InjectionIncludeChanged += (_, __) => changes++;
            var bufferGraph = graph as BufferSubtractionGraph;
            if (bufferGraph != null) bufferGraph.BufferPointIncludeChanged += (_, __) => bufferChanges++;

            using (BeginActivity())
            {
                Click(graph, point);
                Check(injection.Include && changes == 0 && bufferChanges == 0,
                    "Active processing changed injection inclusion (buffer=" + buffer + ")");
                // A press during processing must not arm a later mouse-up.
                graph.CursorFeatureFromPos(point, isclick: true);
            }
            graph.CursorFeatureFromPos(point, ismouseup: true);
            Check(injection.Include && changes == 0 && bufferChanges == 0,
                "Rejected press was deferred until after processing");

            Click(graph, point);
            Check(!injection.Include && changes == 1 && bufferChanges == (buffer ? 1 : 0),
                "Normal inclusion editing did not resume");
        }
    }

    static IDisposable BeginActivity() => (IDisposable)typeof(ProcessingActivity)
        .GetMethod("Begin", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

    static void Click(CGGraph graph, CGPoint point)
    {
        var hit = graph.CursorFeatureFromPos(point, isclick: true);
        Check(hit.IsMouseOverFeature, "Test point missed the rendered injection");
        graph.CursorFeatureFromPos(point, ismouseup: true);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
