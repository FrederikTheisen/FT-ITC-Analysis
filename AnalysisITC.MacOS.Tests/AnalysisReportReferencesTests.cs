// Run with run-analysis-null-graph-tests.sh; exercises the real AppKit report picker and renderer.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using AnalysisITC.Core.Processing;
using AnalysisITC.UI.MacOS.Drawing;

static class AnalysisReportReferencesTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type ReportControllerType = typeof(MainWindowController).Assembly.GetType("AnalysisITC.AnalysisReportViewController");

    static int Main(string[] args)
    {
        NSApplication.Init();
        var original = PreferencesState.FromSettings();
        try
        {
            CheckPicker(false);
            CheckPicker(true);
            CheckCoverSignOff();
            Console.WriteLine("PASS: native tandem source preselection, shared picker roles/tooltips, optional removal and PDF rendering");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            typeof(PreferencesState).GetMethod("ApplyToSettings", PrivateInstance).Invoke(original, null);
        }
    }

    static void CheckCoverSignOff()
    {
        var rendererType = typeof(MainWindowController).Assembly.GetType(
            "AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
        var renderer = Activator.CreateInstance(rendererType, true);
        var results = new[] { Result(Source("signoff-first")), Result(Source("signoff-second")) };
        foreach (var resultCount in new[] { 1, 2 })
        foreach (var traceability in new[] { false, true })
        {
            AppSettings.TraceabilityModeEnabled = traceability;
            var document = AnalysisReportBuilder.Build(results.Take(resultCount).ToList(), new AnalysisReportOptions
            {
                Title = "A long custom report title for cover layout inspection",
                DocumentLabel = "Subtitle for visual inspection with Unicode λ β and wrapped content",
                Author = "Research collaborator Zoë with a deliberately long display name",
                ReportId = "Study-β-" + new string('0', 120),
                ExtraTraceability = traceability,
            });
            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { document });
            var signOffs = plan.Pages.SelectMany(page => page.Fragments)
                .Where(fragment => fragment.Kind == AnalysisReportFragmentKind.SignOff).ToList();
            Check(signOffs.Count == (traceability ? 1 : 0), "Native signing block ignored traceability or was repeated");
            if (traceability)
            {
                var signOff = signOffs.Single();
                Check(plan.Pages[0].Fragments.Contains(signOff), "Native signing block moved off the front page");
                Check(Math.Abs(signOff.Bounds.Bottom - (plan.PageHeight - plan.MarginBottom - 18)) < 1e-6,
                    "Native signing block is not anchored above the footer");
                Check(plan.Pages[0].Fragments.Where(fragment => fragment != signOff)
                    .All(fragment => fragment.Bounds.Bottom <= signOff.Bounds.Y - 9 + 1e-6),
                    "Native cover content overlaps the signing block");
            }
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { document, plan }))
                Check(pdf.Length > 0, "Native signing block PDF is empty");
            var output = Environment.GetEnvironmentVariable("FTITC_SIGNOFF_QA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
                rendererType.GetMethod("WritePdf").Invoke(renderer, new object[] { document, plan,
                    Path.Combine(output, "macos-" + resultCount + (traceability ? "-on.pdf" : "-off.pdf")) });
        }
    }

    static void CheckPicker(bool automatic)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        AppSettings.AutoSelectReportReferenceExperiments = automatic;
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        var first = Source("first"); var second = Source("second");
        var tandem = TandemConcatenation.ConcatTandem(new List<ExperimentData> { first, second });
        tandem.SetBufferSubtraction(first, BufferSubtractionMethod.MatchedInjection, notify: false);
        var result = Result(tandem);
        DataManager.AddData(new ITCDataContainer[] { second, first, tandem, result });
        using (var controller = (NSViewController)Activator.CreateInstance(ReportControllerType, new object[] { result }))
        using (var window = new NSWindow(new CGRect(0, 0, 1120, 720), NSWindowStyle.Titled, NSBackingStore.Buffered, false))
        {
            window.ContentView = controller.View;
            var supporting = (List<ExperimentData>)Field(controller, "selectedSupportingExperiments");
            Check(supporting.Select(data => data.UniqueID).SequenceEqual(automatic ? new[] { "second", "first" } : new string[0]),
                "Native initial source selection ignored the preference or project order");
            ReportControllerType.GetMethod("OpenResultPopover", PrivateInstance).Invoke(controller, null);
            var rows = (IDictionary)Field(controller, "resultPickerButtons");
            var sourceButton = Row(rows, first);
            var coveredButton = Row(rows, tandem);
            Check(sourceButton.ToolTip == "Buffer and tandem source for selected results"
                && sourceButton.Title.Contains("Buffer reference; Tandem source"), "Native source row role/tooltip differs from Core");
            Check(!coveredButton.Enabled && coveredButton.Title.Contains("Included through Result 1")
                && coveredButton.ToolTip == "Already reported with a selected result", "Native covered source row is selectable");
            var resultButton = Row(rows, result);
            Check(resultButton.ToolTip == "Reports this fit and its experiments", "Native result row tooltip differs from Core");
            resultButton.PerformClick(controller);
            Check(sourceButton.ToolTip == "Adds saved data as a supporting experiment"
                && !sourceButton.Title.Contains("Tandem source"),
                "Native role text did not update with the checked results");
            sourceButton.State = NSCellStateValue.Off;
            Row(rows, second).State = NSCellStateValue.Off;
            resultButton.PerformClick(controller);
            Check((sourceButton.State == NSCellStateValue.On) == automatic, "Native result check did not honor reference preselection");
            sourceButton.State = NSCellStateValue.Off;
            var popover = (NSPopover)Field(controller, "resultPopover");
            Buttons(popover.ContentViewController.View).Single(button => button.Title == "Apply").PerformClick(controller);
            Check(!supporting.Contains(first), "Native Apply added a removed tandem source back");
            var report = (AnalysisReport)Field(controller, "report");
            var document = AnalysisReportBuilder.Build(report, _ => result,
                id => DataManager.Data.FirstOrDefault(data => data.UniqueID == id));
            Check(!document.SupportingExperiments.Any(item => item.Id == "first"), "Renderer added an unchecked source");
            Check(document.IsValid, "Report failed after removing an available tandem source");
            // A separate complete selection renders the actual appendix role and missing-source note.
            var pdfReport = new AnalysisReport();
            pdfReport.SetResultIds(new[] { result.UniqueID });
            pdfReport.SetSupportingExperimentIds(new[] { first.UniqueID });
            var pdfDocument = AnalysisReportBuilder.Build(pdfReport, _ => result, id => id == "first" ? first : null);
            Check(pdfDocument.IsValid, "Missing recorded source prevented native report generation");
            var renderer = Field(controller, "renderer");
            var rendererType = renderer.GetType();
            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { pdfDocument });
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { pdfDocument, plan }))
                Check(pdf.Length > 0, "Native tandem report PDF is empty");
            var output = Environment.GetEnvironmentVariable("FTITC_TANDEM_REPORT_QA_DIRECTORY");
            if (automatic && !string.IsNullOrEmpty(output)) rendererType.GetMethod("WritePdf").Invoke(renderer,
                new object[] { pdfDocument, plan, Path.Combine(output, "macos.pdf") });
            window.Close();
        }
    }

    static object Field(object instance, string name) => instance.GetType().GetField(name, PrivateInstance).GetValue(instance);
    static NSButton Row(IDictionary rows, object data) => rows.Keys.Cast<NSButton>().Single(button => ReferenceEquals(rows[button], data));
    static IEnumerable<NSButton> Buttons(NSView view)
    {
        if (view is NSButton) yield return (NSButton)view;
        foreach (var child in view.Subviews)
            foreach (var button in Buttons(child)) yield return button;
    }

    static ExperimentData Source(string id)
    {
        var data = new ExperimentData(id + ".itc")
            { CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6), CellVolume = 200e-6, TargetTemperature = 25 };
        data.SetID(id);
        data.DataPoints.Add(new DataPoint(0, 0, 25)); data.DataPoints.Add(new DataPoint(120, 0, 25));
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 2e-10, true) { Time = 30 + index * 30 };
            injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8)); data.Injections.Add(injection);
        }
        return data;
    }

    static AnalysisResult Result(ExperimentData data)
    {
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Algorithm = SolverAlgorithm.LevenbergMarquardt, Termination = SolverTermination.Converged, Loss = 1 });
        model.Solution = SolutionInterface.FromModel(model, convergence);
        var globalModel = new GlobalModel(new List<Model> { model })
            { Parameters = new GlobalModelParameters(), ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None } };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, new List<SolutionInterface> { model.Solution }, convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        return new AnalysisResult(global);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
