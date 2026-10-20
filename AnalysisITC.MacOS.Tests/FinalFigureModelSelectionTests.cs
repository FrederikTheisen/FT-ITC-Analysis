// Managed native figure/control regression checks; no bundle or signing targets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
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
            TraceabilityControls();
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
            CultureInvariantFinalFigureMetadata(data, directory);
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

            var noneGraph = Graph(FinalFigureGraphView.SetupForExport(data, null, null, PublicationFigureModelSelection.None));
            Check(ActiveSolution(noneGraph) == null && noneGraph.UnavailableNotice == null, "None resolved a fit or warning");
            Check(!noneGraph.DrawConfidenceBands && !noneGraph.ShowParameterBox && !noneGraph.ShowParameterGuides
                && !noneGraph.ResidualDisplayOptions.ShowResidualGraph && noneGraph.DrawWithOffset,
                "None retained model output");
            FinalFigureGraphView.FitLineWidth = 3.25f;
            Check(Graph(FinalFigureGraphView.SetupForExport(data)).FitLineWidth == 3.25f, "Line width did not reach renderer");
            // Result entry: the exact member, and its saved comparison, even after No binding detected.
            var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = data.Model }));
            result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
            var member = result.Solution.Solutions.Single();
            var associatedBinding = Graph(FinalFigureGraphView.SetupForExport(data, result, member, PublicationFigureModelSelection.BindingModel));
            Check(ReferenceEquals(ActiveSolution(associatedBinding), member), "Associated Binding did not draw the exact member");
            var associatedNull = Graph(FinalFigureGraphView.SetupForExport(data, result, member, PublicationFigureModelSelection.NullModel));
            Check(ActiveSolution(associatedNull) != null && !ReferenceEquals(ActiveSolution(associatedNull), member),
                "Associated Null did not resolve the saved comparison");

            foreach (var selection in new[] { PublicationFigureModelSelection.BindingModel, PublicationFigureModelSelection.NullModel, PublicationFigureModelSelection.None })
            {
                var name = "native-" + (selection == PublicationFigureModelSelection.BindingModel ? "binding" : selection == PublicationFigureModelSelection.NullModel ? "null" : "none");
                // Change the session choice to prove exports use the captured one.
                FinalFigureModelPresentation.Selection = selection == PublicationFigureModelSelection.NullModel
                    ? PublicationFigureModelSelection.BindingModel : PublicationFigureModelSelection.NullModel;
                WritePdf(data, directory, name, null, null, selection);
                SavePreview(data, null, null, selection, name);
            }
            WritePdf(data, directory, "native-associated-binding", result, member, PublicationFigureModelSelection.BindingModel);
            WritePdf(data, directory, "native-associated-null", result, member, PublicationFigureModelSelection.NullModel);
            WritePdf(data, directory, "native-associated-none", result, member, PublicationFigureModelSelection.None);
            var associatedDirectory = Path.Combine(directory, "associated-batch");
            Directory.CreateDirectory(associatedDirectory);
            var associatedTargets = FigureExportPlanner.CreateTargets(new[] { data }, associatedDirectory);
            var associatedWriter = typeof(FinalFigureGraphView).GetMethod("WriteAssociatedFigureBatch", InternalStatic);
            var associatedOutcome = (ExportOutcome)associatedWriter.Invoke(null, new object[]
            {
                associatedTargets,
                ExportBatchWriter.CaptureExistingPaths(associatedTargets.Select(target => target.Path).ToList()),
                result,
                result.Solution.Solutions.ToList(),
                PublicationFigureModelSelection.BindingModel,
                null
            });
            Check(associatedOutcome.IsSuccess && File.Exists(associatedTargets[0].Path),
                "Associated batch did not render the saved result-member fit");
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
            var targets = FigureExportPlanner.CreateTargets(new[] { data, unfitted }, batchDirectory);
            var writeBatch = typeof(FinalFigureGraphView).GetMethod("WriteFigureBatch", InternalStatic);
            var firstOutcome = (ExportOutcome)writeBatch.Invoke(null, new object[]
            {
                targets, ExportBatchWriter.CaptureExistingPaths(targets.Select(target => target.Path).ToList()),
                PublicationFigureModelSelection.NullModel, null
            });
            Check(firstOutcome.IsSuccess && firstOutcome.CompletedCount == 2,
                "Unavailable Null batch export failed");
            Check(targets.All(target => File.Exists(target.Path)), "Planned native batch paths were not written");

            File.WriteAllText(targets[0].Path, "previous export");
            var approved = ExportBatchWriter.CaptureExistingPaths(targets.Select(target => target.Path).ToList());
            var secondOutcome = (ExportOutcome)writeBatch.Invoke(null, new object[]
            {
                targets, approved, PublicationFigureModelSelection.NullModel, null
            });
            Check(secondOutcome.IsSuccess && secondOutcome.ReplacedCount == 2,
                "Approved repeat export did not replace the planned PDFs");
            Check(!Directory.GetFiles(batchDirectory).Any(path => Path.GetFileName(path).EndsWith(" (2).pdf", StringComparison.OrdinalIgnoreCase)),
                "Repeat export created a suffix file instead of replacing the approved path");

            var partialTargets = new[]
            {
                targets[0],
                new FigureExportTarget(unfitted, Path.Combine(batchDirectory, "missing", "later.pdf"))
            };
            var partial = (ExportOutcome)writeBatch.Invoke(null, new object[]
            {
                partialTargets, ExportBatchWriter.CaptureExistingPaths(partialTargets.Select(target => target.Path).ToList()),
                PublicationFigureModelSelection.NullModel, null
            });
            Check(!partial.IsSuccess && partial.PlannedCount == 2 && partial.CompletedCount == 1
                && partial.ReplacedCount == 1 && partial.FailedPath == partialTargets[1].Path,
                "Partial native batch did not report its committed file and failed destination");
            Check(!File.Exists(partialTargets[1].Path), "Failed output destination was created");
            Check(ExportFailurePresentation.FormatMessage("Final figures", partial).Contains("mix of old and new exports"),
                "Partial native export omitted the mixed-folder explanation");

            NativeOverwriteConfirmationAndFailureReporting(data, directory);

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

    static void CultureInvariantFinalFigureMetadata(ExperimentData data, string directory)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            data.Solution.Convergence.SetUnweightedRmsd(0.8199);
            data.Solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(0.8199, 0.002, 0.8156, 0.8236);
            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                var resolution = typeof(FinalFigureGraphView).GetMethod("ResolveFigure", InternalStatic)
                    .Invoke(null, new object[] { data, null, null, PublicationFigureModelSelection.BindingModel });
                var method = typeof(FinalFigureGraphView).GetMethod("BuildPdfMetadataKeywords", InternalStatic);
                var keywords = (string[])method.Invoke(null, new[] { (object)data, resolution });
                Check(keywords.Any(value => value.Contains("=")) && keywords.All(value => !value.Contains(","))
                    && keywords.Any(value => value == "Loss: 0.82")
                    && keywords.Any(value => value == "N = 0.820 ± 0.002 [0.816.. 0.824]"),
                    cultureName + " numeric metadata is period-decimal and keyword-safe");
                var path = Path.Combine(directory, "culture-" + cultureName + ".pdf");
                typeof(FinalFigureGraphView).GetMethod("WriteFigurePdf", InternalStatic, null,
                        new[] { typeof(ExperimentData), typeof(string), typeof(AnalysisResult), typeof(SolutionInterface), typeof(PublicationFigureModelSelection?) }, null)
                    .Invoke(null, new object[] { data, path, null, null, (PublicationFigureModelSelection?)PublicationFigureModelSelection.BindingModel });
                Check(new FileInfo(path).Length > 500, cultureName + " Final Figure PDF written");
                if (!String.IsNullOrEmpty(QaDirectory))
                {
                    Directory.CreateDirectory(QaDirectory);
                    File.Copy(path, Path.Combine(QaDirectory, "culture-" + cultureName + ".pdf"), true);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    static void Controls()
    {
        var controller = typeof(FinalFigureGraphView).Assembly.GetType("AnalysisITC.FinalFigureOptionsController", true);
        var fitControlsType = controller.GetNestedType("FitControls", BindingFlags.NonPublic);
        var offset = new NSSwitch { State = 1 };
        var confidence = new NSSwitch { State = 1 };
        var selector = new NSSegmentedControl { SegmentCount = 3 };
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

        var width = new NSStepper { MinValue = 0.25, MaxValue = 8, Increment = 0.25, DoubleValue = 3.25 };
        var residuals = new NSSwitch { State = 1 };
        var smoothness = new NSSegmentedControl { SegmentCount = 3 };
        fitControlsType.GetProperty("FitLineWidthControl").SetValue(controls, width);
        fitControlsType.GetProperty("ShowResiduals").SetValue(controls, residuals);
        fitControlsType.GetProperty("SplineInterpolationControl").SetValue(controls, smoothness);
        selector.SelectedSegment = 2;
        apply.Invoke(null, new[] { controls });
        Check(FinalFigureModelPresentation.Selection == PublicationFigureModelSelection.None, "Selector did not choose None");
        Check(!width.Enabled && !residuals.Enabled && !smoothness.Enabled && !offset.Enabled && !confidence.Enabled,
            "None did not disable model controls");
        Check(FinalFigureGraphView.FitLineWidth == 3.25f && offset.State == 1 && confidence.State == 1,
            "None discarded appearance settings");
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

    static void TraceabilityControls()
    {
        var originalMode = AppSettings.TraceabilityModeEnabled;
        var originalVisibility = FinalFigureGraphView.ShowBadData;
        try
        {
            AppSettings.TraceabilityModeEnabled = false;
            var controller = typeof(FinalFigureGraphView).Assembly.GetType("AnalysisITC.FinalFigureOptionsController", true);
            var controlsType = controller.GetNestedType("FitControls", BindingFlags.NonPublic);
            var controls = Activator.CreateInstance(controlsType);
            var visibility = new NSSwitch { State = 0 };
            controlsType.GetProperty("HideBadData").SetValue(controls, visibility);
            var sync = controller.GetMethod("SyncFit", InternalStatic);
            var apply = controller.GetMethod("ApplyFit", InternalStatic);
            apply.Invoke(null, new[] { controls });
            Check(!FinalFigureGraphView.ShowBadData && visibility.Enabled, "Normal mode cannot hide excluded injections");

            var graph = Graph(FinalFigureGraphView.SetupForExport(Experiment()));
            Check(graph.HideBadData, "Normal figure did not hide excluded injections");
            AppSettings.TraceabilityModeEnabled = true;
            sync.Invoke(null, new[] { controls });
            Check(!visibility.Enabled && visibility.State == 1, "Traceability did not disable visibility control");
            Check(visibility.ToolTip == ExcludedInjectionPresentation.VisibilityTooltip, "Traceability tooltip missing");
            Check(!graph.HideBadData, "Existing graph hid excluded injections in Traceability Mode");
            visibility.State = 0;
            apply.Invoke(null, new[] { controls });
            Check(visibility.State == 1 && FinalFigureGraphView.ShowBadData, "Traceability accepted hidden injections");
            Check(!Graph(FinalFigureGraphView.SetupForExport(Experiment())).HideBadData, "Traceability export hid excluded injections");

            AppSettings.TraceabilityModeEnabled = false;
            sync.Invoke(null, new[] { controls });
            Check(visibility.Enabled && visibility.State == 0 && !FinalFigureGraphView.ShowBadData,
                "Normal mode did not restore excluded injection visibility choice");
        }
        finally
        {
            AppSettings.TraceabilityModeEnabled = originalMode;
            FinalFigureGraphView.ShowBadData = originalVisibility;
        }
    }

    static DataFittingGraph Graph(FinalFigure figure)
        => (DataFittingGraph)typeof(FinalFigure).GetField("IntegrationGraph", AnyInstance).GetValue(figure);

    static void NativeOverwriteConfirmationAndFailureReporting(ExperimentData data, string directory)
    {
        var outputDirectory = Path.Combine(directory, "confirmed-batch");
        Directory.CreateDirectory(outputDirectory);
        var targets = FigureExportPlanner.CreateTargets(new[] { data }, outputDirectory);
        var initial = RunConfirmedBatch(targets, existing => true);
        Check(initial != null && initial.IsSuccess, "Initial planned export failed");
        var original = File.ReadAllBytes(targets[0].Path);

        var cancelled = RunConfirmedBatch(targets, existing => ShowOverwritePrompt(existing,
            (IntPtr)(long)NSAlertButtonReturn.Second));
        Check(cancelled == null && Equal(original, File.ReadAllBytes(targets[0].Path)),
            "Cancel changed the existing PDF");

        var unexpectedResponse = RunConfirmedBatch(targets, existing => ShowOverwritePrompt(existing, IntPtr.Zero));
        Check(unexpectedResponse == null && Equal(original, File.ReadAllBytes(targets[0].Path)),
            "An unexpected modal response changed the existing PDF");

        var overwritten = RunConfirmedBatch(targets, existing => ShowOverwritePrompt(existing,
            (IntPtr)(long)NSAlertButtonReturn.First));
        Check(overwritten != null && overwritten.IsSuccess && overwritten.ReplacedCount == 1,
            "Overwrite did not commit to the approved planned path");
        Check(!Directory.GetFiles(outputDirectory).Any(path => Path.GetFileName(path).EndsWith(" (2).pdf", StringComparison.OrdinalIgnoreCase)),
            "Approved repeat export created a suffix file");

        var missingTarget = new FigureExportTarget(data, Path.Combine(outputDirectory, "missing", "later.pdf"));
        var partialTargets = new[] { targets[0], missingTarget };
        var messageCount = 0;
        HandledException shown = null;
        EventHandler<HandledException> capture = (sender, message) =>
        {
            if (message.Title != "Export Failed") return;
            messageCount++;
            shown = message;
        };
        var previousHandler = AppEventHandler.ShowAppMessage;
        AppEventHandler.ShowAppMessage = capture;
        try
        {
            var outcome = RunConfirmedBatch(partialTargets, existing => ShowOverwritePrompt(existing,
                (IntPtr)(long)NSAlertButtonReturn.First));
            Check(outcome != null && !outcome.IsSuccess && outcome.CompletedCount == 1 && outcome.ReplacedCount == 1,
                "Mixed native batch did not expose the committed replacement count");
            Check(messageCount == 1 && shown != null && shown.Message.Contains("mix of old and new exports"),
                "Mixed native failure did not show one shared handled-error notification");
        }
        finally { AppEventHandler.ShowAppMessage = previousHandler; }
    }

    static ExportOutcome RunConfirmedBatch(IReadOnlyList<FigureExportTarget> targets,
        Func<ISet<string>, bool> confirm)
    {
        var method = typeof(FinalFigureGraphView).GetMethod("RunFigureBatchAfterConfirmation", InternalStatic);
        return (ExportOutcome)method.Invoke(null, new object[]
        {
            targets, PublicationFigureModelSelection.NullModel, "Final figures", confirm, null
        });
    }

    static bool ShowOverwritePrompt(ISet<string> existing, IntPtr response)
    {
        var showModal = new Func<NSAlert, NSWindow, IntPtr>((alert, parent) =>
        {
            Check(alert.AlertStyle == NSAlertStyle.Warning && alert.Buttons.Length == 2,
                "Native overwrite alert style or buttons are incorrect");
            Check(alert.Buttons[0].Title == "Overwrite" && alert.Buttons[1].Title == "Cancel",
                "Native overwrite alert button labels or order are incorrect");
            Check(alert.InformativeText.Contains("This export will overwrite"),
                "Native overwrite alert did not identify the existing export");
            return response;
        });
        var method = typeof(FinalFigureGraphView).GetMethod("ConfirmOverwriteIfNeeded", InternalStatic, null,
            new[] { typeof(NSWindow), typeof(ISet<string>), typeof(Func<NSAlert, NSWindow, IntPtr>) }, null);
        return (bool)method.Invoke(null, new object[] { null, existing, showModal });
    }

    static bool Equal(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;
        for (var index = 0; index < first.Length; index++)
            if (first[index] != second[index]) return false;
        return true;
    }

    static SolutionInterface ActiveSolution(DataFittingGraph graph)
        => (SolutionInterface)typeof(DataFittingGraph).GetProperty("ActiveSolution", AnyInstance).GetValue(graph);

    static void WritePdf(ExperimentData data, string directory, string name, AnalysisResult owner, SolutionInterface member,
        PublicationFigureModelSelection selection)
    {
        var path = Path.Combine(directory, name + ".pdf");
        typeof(FinalFigureGraphView).GetMethod("WriteFigurePdf", InternalStatic, null,
                new[] { typeof(ExperimentData), typeof(string), typeof(AnalysisResult), typeof(SolutionInterface), typeof(PublicationFigureModelSelection?) }, null)
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
