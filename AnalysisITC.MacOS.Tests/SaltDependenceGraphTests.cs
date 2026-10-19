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
        foreach (var mode in new[] { ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease,
            ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel })
        foreach (var repeatedSalt in new[] { false, true })
        foreach (var uncertainty in new[] { false, true })
        {
            var name = mode + ", repeated salt=" + repeatedSalt + ", uncertainty=" + uncertainty;
            try { CheckGraph(mode, repeatedSalt, uncertainty); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); }
        }
        foreach (var savedPolicy in new[] { false, true })
        {
            try { CheckSavedPolicy(savedPolicy); }
            catch (Exception error) { failures.Add("saved policy=" + savedPolicy + ": " + error.Message); }
        }
        foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
        if (failures.Count > 0) return 1;
        Console.WriteLine("PASS: 10 native salt-plot cases preserve individual fitted centers, transformed intervals and stored fits across mode switches");
        return 0;
    }

    static void CheckGraph(ElectrostaticsAnalysis.DissocFitMode mode, bool repeatedSalt, bool uncertainty)
    {
        var salts = repeatedSalt ? new[] { 0.04, 0.04, 0.16, 0.36 } : new[] { 0.04, 0.16, 0.36 };
        var logAffinities = new[] { 6.0, 5.5, 7.0, 6.5 };
        var result = CreateResult(salts, logAffinities, uncertainty);
        var analysis = result.ElectrostaticsAnalysis;
        var counterIonFit = new LinearFitWithError(new FloatWithError(0.5, 0.1), new FloatWithError(-10, 0.2), 0);
        var debyeFit = new IonicStrengthDependenceFit(new FloatWithError(1e-6, 1e-7), new FloatWithError(2, 0.1), new FloatWithError(0));
        var completedAt = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        typeof(ElectrostaticsAnalysis).GetMethod("RestoreResult", Instance).Invoke(analysis, new object[] {
            debyeFit, counterIonFit, 100, 100, completedAt, ErrorEstimationMethod.BootstrapResiduals, false });
        var savedAffinities = result.Solution.Solutions.Select(solution => solution.ReportParameters[ParameterType.Affinity1]).ToArray();
        var handle = Send(Send(new Class(typeof(ResultGraphView)).Handle, Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        using (var view = Runtime.GetNSObject<ResultGraphView>(handle))
        {
            view.Frame = new CGRect(0, 0, 640, 480);
            FloatWithError[] firstX = null;
            FloatWithError[] firstY = null;
            for (var refresh = 0; refresh < 4; refresh++)
            {
                view.Setup(analysis, mode);
                var graph = (ParameterDependenceGraph)typeof(ResultGraphView).GetProperty("Graph", Instance).GetValue(view);
                Check(graph.XValues.Length == salts.Length && graph.YValues.Length == salts.Length,
                    "each experiment must retain its own point, including repeated conditions");
                Check(ReferenceEquals(graph.Fit, mode == ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease
                    ? (object)counterIonFit : debyeFit), "graph uses the stored advanced fit");
                for (var index = 0; index < salts.Length; index++)
                {
                    var logK = logAffinities[index];
                    var counterIon = mode == ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease;
                    var factor = counterIon ? Math.Log(10) : 1;
                    Near(graph.XValues[index].Value, counterIon ? Math.Log(salts[index] * salts[index]) : Math.Sqrt(salts[index]), "condition coordinate");
                    Near(graph.YValues[index].Value, -logK * factor, "original fitted center");
                    Near(graph.YValues[index].Lower, -(logK + (uncertainty ? 0.3 : 0)) * factor, "transformed lower interval");
                    Near(graph.YValues[index].Upper, -(logK - (uncertainty ? 0.1 : 0)) * factor, "transformed upper interval");
                    Check(graph.XValues[index].SD == 0, "condition coordinate has no invented uncertainty");
                    Check(uncertainty ? graph.YValues[index].SD > 0 : graph.YValues[index].SD == 0,
                        "preserves presence or absence of uncertainty");
                    if (refresh > 0)
                    {
                        Same(graph.XValues[index], firstX[index], "condition unchanged by rebuilding");
                        Same(graph.YValues[index], firstY[index], "center and interval unchanged by rebuilding");
                    }
                    Same(result.Solution.Solutions[index].ReportParameters[ParameterType.Affinity1], savedAffinities[index],
                        "viewing preserves the member estimate and interval");
                }
                firstX = graph.XValues;
                firstY = graph.YValues;
                view.Setup(analysis, ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt);
                view.Setup(analysis, mode == ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease
                    ? ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel : ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease);
            }
        }
        Check(ReferenceEquals(analysis.CounterIonReleaseFit, counterIonFit)
            && ReferenceEquals(analysis.IonicStrengthDependenceFit, debyeFit), "viewing preserves stored advanced fits");
        Check(analysis.CompletedAtUtc == completedAt && analysis.CompletedIterations == 100
            && analysis.CounterIonReleaseIterations == 100, "viewing does not rerun advanced analysis");
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

    static void Same(FloatWithError actual, FloatWithError expected, string message)
    {
        Check(actual.Value == expected.Value && actual.SD == expected.SD
            && actual.Lower == expected.Lower && actual.Upper == expected.Upper, message);
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
