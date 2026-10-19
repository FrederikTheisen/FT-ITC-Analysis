using System;
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
using AnalysisITC.Platform;
using AnalysisITC.UI.MacOS;

static class ResultClipboardTests
{
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    static int Main()
    {
        NSApplication.Init();
        var savedClipboard = PlatformServices.ClipboardService;
        var savedStyle = AppSettings.UncertaintyDisplayStyle;
        try
        {
            PlatformServices.RegisterClipboardService(new MacClipboardService());
            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.None;
            var data = new ExperimentData("clipboard, sample ±.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3, MeasuredTemperature = 25,
            };
            var injection = new InjectionData(data, 0, 2e-6, 2e-10, true)
            {
                ActualCellConcentration = data.CellConcentration,
                ActualTitrantConcentration = 2e-6,
                Ratio = 1,
            };
            injection.SetPeakArea(new FloatWithError(-1e-6, 1e-8));
            data.Injections.Add(injection);
            var model = new OneSetOfSites(data);
            model.InitializeParameters(data);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(
                new SolverConvergenceSnapshot { Termination = SolverTermination.Converged }));
            var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
            var handle = Send(Send(new Class(typeof(BindingAnalysisViewController)).Handle,
                Selector.GetHandle("alloc")), Selector.GetHandle("init"));
            using (var controller = Runtime.GetNSObject<BindingAnalysisViewController>(handle))
            using (var status = NSTextField.CreateLabel(""))
            {
                controller.SetResult(result);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BindingAnalysisViewController).GetField("statusField", flags).SetValue(controller, status);
                typeof(BindingAnalysisViewController).GetMethod("CopyResultTable", flags).Invoke(controller, null);
                var table = NSPasteboard.GeneralPasteboard.GetStringForType("NSStringPboardType");
                var rows = table.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                    .Select(line => line.Split('\t')).ToArray();
                Check(rows.Length == 2, "Quick copy must contain one member row");
                Check(rows[0].Length == rows[1].Length, "Copied columns do not match headings");
                Check(rows[0].Contains("N_value") && !rows[0].Contains("N_sd"), "None must copy value only");
                Check(rows[1][Array.IndexOf(rows[0], "Experiment")] == data.Name, "Experiment name was split");
                Check(status.StringValue == "Result table copied.", "Copy action did not finish");
            }
            Console.WriteLine("PASS: native result-details action copies aligned TSV to the macOS pasteboard");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            AppSettings.UncertaintyDisplayStyle = savedStyle;
            PlatformServices.RegisterClipboardService(savedClipboard);
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
