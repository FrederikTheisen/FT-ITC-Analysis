// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh SaltDependenceGraphTests
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using ObjCRuntime;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.Drawing;

static class SaltDependenceGraphTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        var failures = new List<string>();
        foreach (var savedPolicy in new[] { false, true })
        {
            try { CheckSavedPolicy(savedPolicy); }
            catch (Exception error) { failures.Add("saved policy=" + savedPolicy + ": " + error.Message); }
        }
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        if (failures.Count > 0) return 1;
        Console.WriteLine("PASS: 2 native salt-plot cases preserve completed buffer policy independently of captured run policy");
        return 0;
    }

    static void CheckSavedPolicy(bool savedPolicy)
    {
        var previous = AppSettings.IncludeBufferInIonicStrengthCalc;
        try
        {
            AppSettings.IncludeBufferInIonicStrengthCalc = !savedPolicy;
            var salts = new[] { .04, .16, .36 };
            var result = CreateResult(salts, new[] { 6.0, 5.5, 7.0 }, false);
            foreach (var member in result.Solution.Solutions)
            {
                var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
                buffer.IntValue = (int)AnalysisITC.Core.Data.Buffer.Hepes;
                buffer.DoubleValue = 7.4; buffer.ParameterValue = new FloatWithError(.01);
                member.Data.Attributes.Add(buffer);
            }
            result.UpdateSolution(result.Solution);
            var analysis = result.ElectrostaticsAnalysis;
            var handle = Send(Send(new Class(typeof(ResultGraphView)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
            using (var view = Runtime.GetNSObject<ResultGraphView>(handle))
            {
                view.Frame = new CGRect(0, 0, 640, 480);
                view.Setup(analysis, ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel);
                CheckPoints(view, !savedPolicy);
                typeof(ElectrostaticsAnalysis).GetMethod("RestoreResult", Instance).Invoke(analysis, new object[] {
                    new IonicStrengthDependenceFit(new FloatWithError(1e-6), new FloatWithError(2), new FloatWithError(0)),
                    null, 100, 0, DateTime.UtcNow, ErrorEstimationMethod.None, savedPolicy });
                view.Setup(analysis, ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel);
                CheckPoints(view, savedPolicy);
            }
        }
        finally { AppSettings.IncludeBufferInIonicStrengthCalc = previous; }
    }

    static void CheckPoints(ResultGraphView view, bool policy)
    {
        var graph = (ParameterDependenceGraph)typeof(ResultGraphView).GetProperty("Graph", Instance).GetValue(view);
        var salts = new[] { .04, .16, .36 };
        Check(graph.XValues.Length == 3, "all saved observations retained");
        for (var i = 0; i < salts.Length; i++)
            Near(graph.XValues[i].Value, Math.Sqrt(salts[i] + (policy ? .0037005197012182495 : 0)), "saved condition coordinate");
    }

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

    static void Near(double actual, double expected, string message)
    {
        Check(Math.Abs(actual - expected) <= 1e-12 * Math.Max(1, Math.Abs(expected)),
            message + ": expected " + expected + ", got " + actual);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
