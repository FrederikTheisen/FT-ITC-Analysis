// Managed native figure/control regression checks; no bundle or signing targets.
using System;
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
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.UI.MacOS.Drawing;

static class FinalFigureModelSelectionTests
{
    const BindingFlags InternalStatic = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly string QaDirectory = Environment.GetEnvironmentVariable("FTITC_FINAL_FIGURE_QA_DIRECTORY");

    static int Main(string[] args)
    {
        NSApplication.Init();
        var original = FinalFigureModelPresentation.Selection;
        var directory = Path.Combine(Path.GetTempPath(), "native-itc012-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FinalFigureModelPresentation.Selection = PublicationFigureModelSelection.BindingModel;
            Controls();
            FinalFigureGraphView.DrawFitOffsetCorrected = true;
            FinalFigureGraphView.DrawConfidence = true;
            FinalFigureGraphView.DrawParameterGuides = true;
            FinalFigureGraphView.ShowResiduals = true;
            FinalFigureGraphView.DrawExpDetails = true;
            FinalFigureGraphView.DrawFitParameters = true;
            FinalFigureGraphView.DrawModelInfo = true;
            FinalFigureGraphView.UnifiedEnthalpyAxis = false;
            FinalFigureGraphView.UseUnifiedMolarRatioAxis = false;
            AppSettings.FinalFigureParameterDisplay = FinalFigureDisplayParameters.Fitted;

            Check(typeof(FinalFigureGraphView).GetMethod("BuildFinalFigureCanvas", InternalStatic) == null,
                "Final Figure still has a second rendering engine");

            var data = Experiment();
            var attached = data.Solution;
            var savedNull = attached.NullComparison.NullSolutions.Single();

            // Experiment entry: Binding draws the attached fit; Null draws the saved Null fit.
            var binding = Graph(FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.BindingModel));
            Check(ReferenceEquals(ActiveSolution(binding), attached), "Binding did not draw the attached fit");
            Check(!binding.DrawWithOffset && binding.DrawConfidenceBands && binding.ShowParameterGuides,
                "Binding lost its offset, confidence or guide settings");
            Check(binding.ParameterBoxModelLabel == null && binding.UnavailableNotice == null, "Binding gained Null labels");

            var nullGraph = Graph(FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.NullModel));
            Check(ReferenceEquals(ActiveSolution(nullGraph), savedNull), "Null did not draw the saved Null fit");
            Check(nullGraph.DrawWithOffset && !nullGraph.DrawConfidenceBands && !nullGraph.ShowParameterGuides,
                "Null drew offset-corrected, confidence or binding guides");
            Check(nullGraph.ParameterBoxModelLabel == "Null (Offset)", "Null header label is wrong");
            Check(FinalFigureGraphView.DrawFitOffsetCorrected && FinalFigureGraphView.DrawConfidence
                && FinalFigureGraphView.DrawParameterGuides, "Null changed the stored Binding settings");

            // Result entry: the exact member, and its saved comparison, even after No binding detected.
            var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = data.Model }));
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
            var member = result.Solution.Solutions.Single();
            var associatedBinding = Graph(FinalFigureGraphView.SetupForExport(data, result, member, PublicationFigureModelSelection.BindingModel));
            Check(ReferenceEquals(ActiveSolution(associatedBinding), member), "Associated Binding did not draw the exact member");
            var associatedNull = Graph(FinalFigureGraphView.SetupForExport(data, result, member, PublicationFigureModelSelection.NullModel));
            Check(ActiveSolution(associatedNull) != null && !ReferenceEquals(ActiveSolution(associatedNull), member),
                "Associated Null did not resolve the saved comparison");

            foreach (var selection in new[] { PublicationFigureModelSelection.BindingModel, PublicationFigureModelSelection.NullModel })
            {
                var name = selection == PublicationFigureModelSelection.BindingModel ? "native-binding" : "native-null";
                // Change the session choice to prove exports use the captured one.
                FinalFigureModelPresentation.Selection = selection == PublicationFigureModelSelection.NullModel
                    ? PublicationFigureModelSelection.BindingModel : PublicationFigureModelSelection.NullModel;
                WritePdf(data, directory, name, null, null, selection);
                SavePreview(data, null, null, selection, name);
            }
            WritePdf(data, directory, "native-associated-binding", result, member, PublicationFigureModelSelection.BindingModel);
            WritePdf(data, directory, "native-associated-null", result, member, PublicationFigureModelSelection.NullModel);
            Check(ReferenceEquals(attached, data.Solution), "Export reattached a solution");

            // Unavailable Null: observations only, never the Binding fit.
            SetComparison(data.Solution, null);
            var missing = Graph(FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.NullModel));
            Check(ActiveSolution(missing) == null, "Unavailable Null fell back to the Binding fit");
            Check(missing.UnavailableNotice == "Null (model unknown) fit unavailable", "Unavailable notice is wrong: " + missing.UnavailableNotice);
            WritePdf(data, directory, "native-unavailable-null", null, null, PublicationFigureModelSelection.NullModel);
            SavePreview(data, null, null, PublicationFigureModelSelection.NullModel, "native-unavailable-null");
            SetComparison(data.Solution, Comparison(data, savedNull));

            var unfitted = Experiment();
            unfitted.Name = "Unfitted";
            unfitted.Model = null;
            DataManager.AddData(data);
            DataManager.AddData(unfitted);
            Check((int)typeof(FinalFigureGraphView).GetMethod("GetExportFigureCount", InternalStatic)
                .Invoke(null, new object[] { ExportDataSelection.AllData }) == 2, "All export omitted unfitted observations");
            var batchDirectory = Path.Combine(directory, "batch");
            Directory.CreateDirectory(batchDirectory);
            Check((int)typeof(FinalFigureGraphView).GetMethod("ExportFiguresWithoutOverwriting", InternalStatic)
                .Invoke(null, new object[] { new[] { data, unfitted }, batchDirectory, PublicationFigureModelSelection.NullModel }) == 2,
                "Unavailable Null batch export failed");

            // Unified enthalpy axis resolves other experiments with the selected model.
            var reference = Experiment();
            reference.Solution.NullComparison.NullSolutions.Single().Model.Parameters.Table[ParameterType.Offset].Update(250000);
            DataManager.AddData(reference);
            FinalFigureGraphView.UnifiedEnthalpyAxis = true;
            var sharedNull = Graph(FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.NullModel));
            Check(sharedNull.YAxis.Max > 250000, "Shared axes ignored the other experiment's Null prediction");
            var sharedBinding = Graph(FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.BindingModel));
            Check(sharedBinding.YAxis.Max < 250000, "Shared Binding axes used another experiment's Null prediction");
            FinalFigureGraphView.UnifiedEnthalpyAxis = false;

            Console.WriteLine("PASS: native Plotted model controls, macOS renderer for Binding/Null/unavailable and associated figures, captured exports and shared axes");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
        finally { FinalFigureModelPresentation.Selection = original; Directory.Delete(directory, true); }
    }

    static void Controls()
    {
        var controller = typeof(FinalFigureGraphView).Assembly.GetType("AnalysisITC.FinalFigureOptionsController", true);
        var fitControlsType = controller.GetNestedType("FitControls", BindingFlags.NonPublic);
        var offset = new NSSwitch { State = 1 };
        var confidence = new NSSwitch { State = 1 };
        var selector = new NSSegmentedControl { SegmentCount = 2 };
        var assessment = NSTextField.CreateLabel("");
        var controls = Activator.CreateInstance(fitControlsType);
        fitControlsType.GetProperty("DrawOffsetCorrected").SetValue(controls, offset);
        fitControlsType.GetProperty("DrawConfidence").SetValue(controls, confidence);
        fitControlsType.GetProperty("PlottedModelControl").SetValue(controls, selector);
        fitControlsType.GetProperty("ModelAssessmentLabel").SetValue(controls, assessment);
        var sync = controller.GetMethod("SyncFit", InternalStatic);
        var apply = controller.GetMethod("ApplyFit", InternalStatic);

        FinalFigureGraphView.DrawConfidence = true;
        FinalFigureGraphView.DrawFitOffsetCorrected = true;
        sync.Invoke(null, new[] { controls });
        Check(selector.SelectedSegment == 0 && selector.ToolTip == FinalFigureModelPresentation.SelectorTooltip,
            "Binding default or selector tooltip missing");
        Check(assessment.StringValue == FinalFigureModelPresentation.NoAssessmentText
            && assessment.ToolTip == FinalFigureModelPresentation.AssessmentTooltip, "No assessment line missing");
        Check(offset.Enabled && confidence.Enabled, "Binding disabled its switches");

        selector.SelectedSegment = 1;
        apply.Invoke(null, new[] { controls });
        Check(FinalFigureModelPresentation.Selection == PublicationFigureModelSelection.NullModel, "Selector did not choose Null");
        Check(!offset.Enabled && !confidence.Enabled && offset.State == 1 && confidence.State == 1,
            "Null did not disable and retain Binding switches");
        Check(FinalFigureGraphView.DrawConfidence && FinalFigureGraphView.DrawFitOffsetCorrected, "Null cleared Binding settings");

        selector.SelectedSegment = 0;
        sync.Invoke(null, new[] { controls });
        Check(selector.SelectedSegment == 1, "Sync did not restore the session selection");
        selector.SelectedSegment = 0;
        apply.Invoke(null, new[] { controls });
        Check(offset.Enabled && confidence.Enabled && offset.State == 1 && confidence.State == 1,
            "Binding settings were not restored");
    }

    static DataFittingGraph Graph(FinalFigure figure)
        => (DataFittingGraph)typeof(FinalFigure).GetField("IntegrationGraph", AnyInstance).GetValue(figure);

    static SolutionInterface ActiveSolution(DataFittingGraph graph)
        => (SolutionInterface)typeof(DataFittingGraph).GetProperty("ActiveSolution", AnyInstance).GetValue(graph);

    static void WritePdf(ExperimentData data, string directory, string name, AnalysisResult owner, SolutionInterface member,
        PublicationFigureModelSelection selection)
    {
        var path = Path.Combine(directory, name + ".pdf");
        typeof(FinalFigureGraphView).GetMethod("WriteFigurePdf", InternalStatic)
            .Invoke(null, new object[] { data, path, owner, member, (PublicationFigureModelSelection?)selection });
        Check(new FileInfo(path).Length > 500, "PDF is empty");
        if (!String.IsNullOrEmpty(QaDirectory))
        {
            Directory.CreateDirectory(QaDirectory);
            File.Copy(path, Path.Combine(QaDirectory, name + ".pdf"), true);
        }
    }

    /// <summary>Draws the same macOS renderer figure used by the preview.</summary>
    static void SavePreview(ExperimentData data, AnalysisResult owner, SolutionInterface member,
        PublicationFigureModelSelection selection, string name)
    {
        using (var space = CGColorSpace.CreateDeviceRGB())
        using (var bitmap = new CGBitmapContext(IntPtr.Zero, 1000, 1100, 8, 4000, space, CGImageAlphaInfo.PremultipliedLast))
        {
            bitmap.SetFillColor(NSColor.White.CGColor);
            bitmap.FillRect(new CGRect(0, 0, 1000, 1100));
            var graph = FinalFigureGraphView.SetupForExport(data, owner, member, selection);
            var scale = Math.Min(950 / graph.PrintBox.Width, 1050 / graph.PrintBox.Height);
            bitmap.ScaleCTM((nfloat)scale, (nfloat)scale);
            graph.Draw(bitmap, new CGPoint(500 / scale, 550 / scale));
            if (!String.IsNullOrEmpty(QaDirectory))
            {
                using (var image = bitmap.ToImage())
                using (var representation = new NSBitmapImageRep(image))
                using (var png = representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()))
                    File.WriteAllBytes(Path.Combine(QaDirectory, name + ".png"), png.ToArray());
            }
        }
    }

    static ExperimentData Experiment()
    {
        var data = new ExperimentData("model-selection.itc")
        {
            CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25,
        };
        for (var index = 0; index < 18; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8.4e-10, true)
            {
                ActualCellConcentration = 35e-6, ActualTitrantConcentration = (index + 1) * 5e-6, Ratio = (index + 1) / 7d,
            };
            injection.SetPeakArea(new FloatWithError((-32000 / (1 + Math.Exp(index - 7)) + 1500) * 8.4e-10, 4e-7));
            data.Injections.Add(injection);
        }
        var binding = new OneSetOfSites(data);
        binding.InitializeParameters(data);
        binding.Parameters.Table[ParameterType.Enthalpy1].Update(-35000);
        binding.Parameters.Table[ParameterType.Affinity1].Update(6);
        binding.Parameters.Table[ParameterType.Nvalue1].Update(1);
        binding.Parameters.Table[ParameterType.Offset].Update(2000);
        binding.Solution = SolutionInterface.FromModel(binding, Converged());
        data.Model = binding;
        var nullModel = new Offset(data) { ReuseAttachedSolutionInitialValues = false };
        nullModel.InitializeParameters(data);
        nullModel.Parameters.Table[ParameterType.Offset].Update(1500);
        nullModel.Solution = SolutionInterface.FromModel(nullModel, Converged());
        SetComparison(binding.Solution, Comparison(data, nullModel.Solution));
        return data;
    }

    static NullModelComparison Comparison(ExperimentData data, SolutionInterface nullSolution)
    {
        var comparison = new NullModelComparison { NullModelId = "offset", NullFitSucceeded = true, BindingFitSucceeded = true };
        comparison.NullSolutions.Add(nullSolution);
        comparison.Members.Add(new NullModelComparisonMember { ExperimentId = data.UniqueID });
        return comparison;
    }

    static SolverConvergence Converged() => SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.Converged, Loss = 1e-6 });
    static void SetComparison(SolutionInterface solution, NullModelComparison comparison)
        => typeof(SolutionInterface).GetProperty("NullComparison").GetSetMethod(true).Invoke(solution, new object[] { comparison });
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
