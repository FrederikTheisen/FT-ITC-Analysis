// Exercises the native validity section with production source removal and Undo.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

static class AnalysisResultAvailabilityTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main()
    {
        NSApplication.Init();
        DataManager.Clear(DataClearMode.ResetSession);
        try
        {
            var data = new ExperimentData("availability.itc")
            {
                MeasuredTemperature = 25, CellVolume = 1.4e-3,
                CellConcentration = new FloatWithError(35e-6),
                SyringeConcentration = new FloatWithError(420e-6)
            };
            var injection = new InjectionData(data, 0, 2e-6, 8.4e-10, true)
            {
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = new FloatWithError(5e-6), Ratio = 1
            };
            injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8));
            data.Injections.Add(injection);
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None };
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(
                new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
            var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
            DataManager.AddData(new ITCDataContainer[] { data, result });
            var handle = Send(Send(new Class(typeof(AnalysisResultTabViewController)).Handle,
                Selector.GetHandle("alloc")), Selector.GetHandle("init"));
            using (var controller = Runtime.GetNSObject<AnalysisResultTabViewController>(handle))
            {
                typeof(AnalysisResultTabViewController).GetField("analysisResult", Instance).SetValue(controller, result);
                CheckSection(controller, "Analysis is Valid", false);
                DataManager.RemoveSourceItemAt(0);
                CheckSection(controller, "Invalid", true);
                DataManager.UndoDeleteData();
                CheckSection(controller, "Analysis is Valid", false);
            }
            Console.WriteLine("PASS: native validity status and missing-source reason after removal and Undo");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { DataManager.Clear(DataClearMode.ResetSession); }
    }

    static void CheckSection(AnalysisResultTabViewController controller, string status, bool missing)
    {
        using (var section = (NSView)typeof(AnalysisResultTabViewController)
            .GetMethod("BuildValiditySection", Instance).Invoke(controller, null))
        {
            var text = Text(section).ToArray();
            if (!text.Contains(status)) throw new InvalidOperationException("Native validity status: " + string.Join("; ", text));
            if (text.Any(value => value.Contains("Experiment missing: availability.")) != missing)
                throw new InvalidOperationException("Native missing-source reason: " + string.Join("; ", text));
        }
    }

    static IEnumerable<string> Text(NSView view)
    {
        var field = view as NSTextField;
        if (field != null) yield return field.StringValue;
        foreach (var child in view.Subviews)
            foreach (var text in Text(child)) yield return text;
    }
}
