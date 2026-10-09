// Exercises native structuring output without a storyboard or app bundle.
// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh SpolarSavedTemperatureViewTests
using System;
using System.Collections.Generic;
using System.Globalization;
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
using AnalysisITC.Core.Units;

static class SpolarSavedTemperatureViewTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main(string[] args)
    {
        NSApplication.Init();
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var previousPrecision = AppSettings.NumberPrecision;
        var previousStyle = AppSettings.UncertaintyDisplayStyle;
        var previousReference = AppSettings.ReferenceTemperature;
        var previousFamily = AppSettings.EnergyUnitFamily;
        var kelvinProperty = typeof(AnalysisResultTabViewController).GetProperty("CurrentUseKelvin");
        var previousKelvin = AnalysisResultTabViewController.CurrentUseKelvin;
        try
        {
            AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
            foreach (var hasUncertainty in new[] { false, true })
            {
                var result = CreateResult();
                typeof(FTSRMethod).GetMethod("RestoreResult", Instance).Invoke(result.SpolarRecordAnalysis, new object[] {
                    FTSRMethod.SRFoldedMode.Glob, FTSRMethod.SRTempMode.ReferenceTemperature,
                    new FTSRMethod.SROutput(new FloatWithError(-2), new FloatWithError(3), new FloatWithError(1),
                        hasUncertainty ? new FloatWithError(25, 1, 23, 29) : new FloatWithError(25)),
                    4, DateTime.UtcNow });
                foreach (var useKelvin in new[] { false, true })
                foreach (var style in new[] { UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
                    UncertaintyDisplayStyle.StandardDeviation })
                {
                    kelvinProperty.SetValue(null, useKelvin);
                    AppSettings.UncertaintyDisplayStyle = style;
                    var central = useKelvin ? "298.15" : "25";
                    var unit = useKelvin ? " K" : " °C";
                    var interval = useKelvin ? " [296.15, 302.15]" : " [23, 29]";
                    var expected = central + (hasUncertainty ? " ± 1" +
                        (style == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval ? interval : "") : "") + unit;
                    AppSettings.ReferenceTemperature = 25;
                    var original = Output(result);
                    Check(Value(original, "Evaluated at") == expected, "saved temperature with units and uncertainty");
                    AppSettings.ReferenceTemperature = 37;
                    var after = Output(result);
                    Check(Value(after, "Evaluated at") == expected, "reference preference leaves saved temperature unchanged");
                    Check(Value(after, "−TΔS_HE") == Value(original, "−TΔS_HE"), "hydration output unchanged");
                    Check(Value(after, "−TΔS_conf") == Value(original, "−TΔS_conf"), "conformational output unchanged");
                    Check(result.SpolarRecordAnalysis.Result.ReferenceTemperature.Value == 25, "saved Celsius value unchanged");
                }
            }
            Console.WriteLine("PASS: native structuring temperature retains saved values and uncertainty in Celsius/Kelvin");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            AppSettings.NumberPrecision = previousPrecision;
            AppSettings.UncertaintyDisplayStyle = previousStyle;
            AppSettings.ReferenceTemperature = previousReference;
            AppSettings.EnergyUnitFamily = previousFamily;
            kelvinProperty.SetValue(null, previousKelvin);
        }
    }

    static NSStackView Output(AnalysisResult result)
    {
        var handle = Send(Send(new Class(typeof(AnalysisResultTabViewController)).Handle,
            Selector.GetHandle("alloc")), Selector.GetHandle("init"));
        var controller = Runtime.GetNSObject<AnalysisResultTabViewController>(handle);
        var stack = new NSStackView();
        typeof(AnalysisResultTabViewController).GetField("analysisResult", Instance).SetValue(controller, result);
        typeof(AnalysisResultTabViewController).GetField("analysisStack", Instance).SetValue(controller, stack);
        typeof(AnalysisResultTabViewController).GetMethod("AddTemperatureAnalysisSections", Instance).Invoke(controller, null);
        return stack.ArrangedSubviews.OfType<NSStackView>().Single(section =>
            section.ArrangedSubviews.OfType<NSTextField>().Any(field => field.StringValue == "Output"));
    }

    static string Value(NSStackView output, string label)
    {
        var row = output.ArrangedSubviews.OfType<NSStackView>().Single(pair =>
            pair.ArrangedSubviews.OfType<NSTextField>().Any(field => field.StringValue == label));
        return row.ArrangedSubviews.OfType<NSTextField>().Last().StringValue;
    }

    static AnalysisResult CreateResult()
    {
        var models = new List<Model>();
        var members = new List<SolutionInterface>();
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = SolverAlgorithm.LevenbergMarquardt,
            Termination = SolverTermination.Converged,
            Loss = 0.1,
        });
        foreach (var temperature in new[] { 10.0, 25.0, 45.0 })
        {
            var data = new ExperimentData("structuring-view.itc")
            {
                CellConcentration = new FloatWithError(30e-6),
                SyringeConcentration = new FloatWithError(400e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = temperature,
                TargetTemperature = temperature,
            };
            var model = new OneSetOfSites(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
            var member = SolutionInterface.FromModel(model, convergence);
            model.Solution = member;
            models.Add(model);
            members.Add(member);
        }
        var global = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
        var solution = new GlobalSolution(new GlobalSolver
        {
            Model = global,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
        }, members, convergence);
        global.Solution = solution;
        return new AnalysisResult(solution);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Failed: " + message);
    }
}
