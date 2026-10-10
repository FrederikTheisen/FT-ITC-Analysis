using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnalysisITC.Avalonia.Drawing;
using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Avalonia.Controls;
using Avalonia.Threading;
using SkiaSharp;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class FinalFigureModelSelectionTests
{
    [Fact]
    public void ControlsRetainSettingsAndSelectionAndRenderSelectedPrintAndBatchModels()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
        Dispatcher.UIThread.Invoke(() =>
        {
            var originalSelection = FinalFigureModelPresentation.Selection;
            var directory = Path.Combine(Path.GetTempPath(), "itc012-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                DataManager.Clear(DataClearMode.ResetSession);
                FinalFigureModelPresentation.Selection = PublicationFigureModelSelection.BindingModel;
                var data = Experiment("Binding");
                var other = Experiment("Other");
                DataManager.AddData(data);
                DataManager.AddData(other);
                var workspace = new FinalFigureWorkspaceControl { SelectedItem = data };
                var attached = data.Solution;
                var initialKey = workspace.PreviewCacheKeyForTesting;
                Assert.Equal(0, workspace.PlottedModelSelectorForTesting.SelectedIndex);
                Assert.Equal(FinalFigureModelPresentation.SelectorTooltip,
                    ToolTip.GetTip(workspace.PlottedModelSelectorForTesting));
                workspace.OffsetCorrectionForTesting.IsChecked = true;
                workspace.ConfidenceForTesting.IsChecked = true;
                SaveQa(workspace.BuildPreviewDocumentForTesting(), "avalonia-binding");

                workspace.LineWidthForTesting.Value = 3.25m;
                Assert.Equal(3.25, workspace.BuildPreviewDocumentForTesting().Options.FitLineWidth);
                workspace.PlottedModelSelectorForTesting.SelectedIndex = 2;
                Assert.NotEqual(initialKey, workspace.PreviewCacheKeyForTesting);
                Assert.False(workspace.LineWidthForTesting.IsEnabled);
                Assert.False(workspace.SmoothnessForTesting.IsEnabled);
                Assert.False(workspace.ResidualsForTesting.IsEnabled);
                Assert.False(workspace.ModelInfoForTesting.IsEnabled);
                Assert.False(workspace.OffsetCorrectionForTesting.IsEnabled);
                Assert.False(workspace.ConfidenceForTesting.IsEnabled);
                var none = workspace.BuildPreviewDocumentForTesting();
                Assert.Empty(none.FitPanel.Series);
                Assert.Empty(none.FitPanel.Bands);
                Assert.Empty(none.FitPanel.AnnotationBoxes);
                Assert.Null(none.ResidualPanel);
                Assert.Equal(data.Injections[0].Enthalpy * EnergyScale(none), none.FitPanel.Points[0].Y, 8);
                Assert.True(workspace.TryGetPrintTarget(out var noneTarget));
                workspace.PlottedModelSelectorForTesting.SelectedIndex = 0;
                Assert.True(workspace.LineWidthForTesting.IsEnabled);
                Assert.True(workspace.ModelInfoForTesting.IsEnabled);
                Assert.Equal(3.25m, workspace.LineWidthForTesting.Value);
                using (var capturedNone = noneTarget!.CaptureAsync().GetAwaiter().GetResult())
                {
                    Assert.True(capturedNone.Pdf.Length > 500);
                    SaveQaBytes(capturedNone.Pdf, "avalonia-none-print.pdf");
                }
                SaveQa(none, "avalonia-none");
                workspace.ExportExperimentFigure(data, Path.Combine(directory, "none.pdf"), PublicationFigureModelSelection.None);
                workspace.PlottedModelSelectorForTesting.SelectedIndex = 1;
                Assert.NotEqual(initialKey, workspace.PreviewCacheKeyForTesting);
                Assert.False(workspace.OffsetCorrectionForTesting.IsEnabled);
                Assert.False(workspace.ConfidenceForTesting.IsEnabled);
                Assert.True(workspace.OffsetCorrectionForTesting.IsChecked);
                Assert.True(workspace.ConfidenceForTesting.IsChecked);
                var nullFigure = workspace.BuildPreviewDocumentForTesting();
                Assert.Empty(nullFigure.FitPanel.Bands);
                Assert.Equal(data.Injections[0].Enthalpy * EnergyScale(nullFigure), nullFigure.FitPanel.Points[0].Y, 8);
                SaveQa(nullFigure, "avalonia-null");

                other.Solution.NullComparison.NullSolutions.Single().Model.Parameters.Table[ParameterType.Offset].Update(250000);
                workspace.SharedEnthalpyAxisForTesting.IsChecked = true;
                Assert.True(workspace.BuildPreviewDocumentForTesting().FitPanel.YAxis.Maximum > 250);
                workspace.SharedEnthalpyAxisForTesting.IsChecked = false;

                Assert.True(workspace.TryGetPrintTarget(out var target));
                workspace.SelectedItem = other;
                Assert.Equal(1, workspace.PlottedModelSelectorForTesting.SelectedIndex);
                var secondWorkspace = new FinalFigureWorkspaceControl();
                Assert.Equal(1, secondWorkspace.PlottedModelSelectorForTesting.SelectedIndex);
                workspace.PlottedModelSelectorForTesting.SelectedIndex = 0;
                Assert.True(workspace.OffsetCorrectionForTesting.IsEnabled);
                Assert.True(workspace.ConfidenceForTesting.IsEnabled);
                Assert.True(workspace.OffsetCorrectionForTesting.IsChecked);
                Assert.True(workspace.ConfidenceForTesting.IsChecked);
                using (var captured = target!.CaptureAsync().GetAwaiter().GetResult())
                {
                    Assert.True(captured.PreservePdf);
                    Assert.True(captured.Pdf.Length > 500);
                    SaveQaBytes(captured.Pdf, "avalonia-null-print.pdf");
                }

                // Exports use the choice captured at invocation even after the session choice changes.
                workspace.ExportExperimentFigure(data, Path.Combine(directory, "selected.pdf"), PublicationFigureModelSelection.NullModel);
                var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = data.Model }));
                result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
                workspace.ExportExperimentFigure(data, Path.Combine(directory, "associated-binding.pdf"),
                    PublicationFigureModelSelection.BindingModel, result, result.Solution.Solutions.Single());
                var source = FinalFigureWorkspaceControl.CreateFigureSource(data, PublicationFigureModelSelection.BindingModel, result);
                Assert.Same(result.Solution.Solutions.Single(), PublicationFigureBuilder.ResolveModel(source).Solution);
                Assert.Equal(PublicationFigureModelSelection.BindingModel, source.ModelSelection);

                workspace.SelectedItem = data;
                workspace.PlottedModelSelectorForTesting.SelectedIndex = 1;
                var oldNullKey = workspace.PreviewCacheKeyForTesting;
                SetComparison(data.Solution, null);
                workspace.InvalidatePreview();
                Assert.NotEqual(oldNullKey, workspace.PreviewCacheKeyForTesting);
                Assert.Equal(FinalFigureModelPresentation.NoAssessmentText, workspace.AssessmentTextForTesting);
                var missing = workspace.BuildPreviewDocumentForTesting();
                Assert.Empty(missing.FitPanel.Series);
                Assert.Null(missing.ResidualPanel);
                Assert.Contains(missing.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines), line => line.EndsWith("fit unavailable"));
                SaveQa(missing, "avalonia-unavailable-null");
                var batchDirectory = Path.Combine(directory, "batch");
                Directory.CreateDirectory(batchDirectory);
                Assert.Equal(2, workspace.ExportFiguresWithoutOverwriting(new[] { data, other }, batchDirectory,
                    PublicationFigureModelSelection.NullModel));
                Assert.Equal(2, Directory.GetFiles(batchDirectory, "*.pdf").Length);
                Assert.All(Directory.GetFiles(directory, "*.pdf"), path => Assert.True(new FileInfo(path).Length > 500));
                Assert.Same(attached, data.Solution);
                // An appearance snapshot cannot transfer the session choice into supporting/report sources.
                Assert.Equal(PublicationFigureModelSelection.AssessmentPolicy,
                    new PublicationFigureSource(data, data.Solution, result).ModelSelection);
            }
            finally
            {
                FinalFigureModelPresentation.Selection = originalSelection;
                DataManager.Clear(DataClearMode.ResetSession);
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void SharedEnthalpyAxisUsesOneUnitAcrossFiguresWithDifferentAutomaticUnits()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
        Dispatcher.UIThread.Invoke(() =>
        {
            var originalSelection = FinalFigureModelPresentation.Selection;
            var originalFamily = FinalFigureModelPresentation.EnergyUnitFamily;
            try
            {
                DataManager.Clear(DataClearMode.ResetSession);
                FinalFigureModelPresentation.Selection = PublicationFigureModelSelection.BindingModel;
                FinalFigureModelPresentation.EnergyUnitFamily = EnergyUnitFamily.Joules;
                var binding = Experiment("Binding");
                // Every observed heat is 50 J/mol, so this figure alone resolves to J.
                var weak = new ExperimentData("Weak.itc")
                {
                    CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
                    CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25,
                };
                for (var index = 0; index < 18; index++)
                {
                    var injection = new InjectionData(weak, index, 2e-6, 8.4e-10, true)
                    {
                        ActualCellConcentration = 35e-6, ActualTitrantConcentration = (index + 1) * 5e-6, Ratio = (index + 1) / 7d,
                    };
                    injection.SetPeakArea(new FloatWithError(50 * 8.4e-10, 1e-12));
                    weak.Injections.Add(injection);
                }
                DataManager.AddData(binding);
                DataManager.AddData(weak);

                // Unshared figures: the binding figure resolves to kJ, the weak one to J. In J the weak
                // maximum exceeds the kJ binding maximum, so mixing units would widen the shared axis.
                var workspace = new FinalFigureWorkspaceControl { SelectedItem = binding };
                var bindingAlone = workspace.BuildPreviewDocumentForTesting();
                workspace.SelectedItem = weak;
                var weakAlone = workspace.BuildPreviewDocumentForTesting();
                Assert.Equal(EnergyUnit.KiloJoule, bindingAlone.ResolvedEnergyUnit);
                Assert.Equal(EnergyUnit.Joule, weakAlone.ResolvedEnergyUnit);
                Assert.True(weakAlone.FitPanel.YAxis.Maximum > bindingAlone.FitPanel.YAxis.Maximum);

                workspace.SharedEnthalpyAxisForTesting.IsChecked = true;
                var weakFigure = workspace.BuildPreviewDocumentForTesting();
                workspace.SelectedItem = binding;
                var bindingFigure = workspace.BuildPreviewDocumentForTesting();
                foreach (var figure in new[] { weakFigure, bindingFigure })
                {
                    Assert.Equal(EnergyUnit.KiloJoule, figure.ResolvedEnergyUnit);
                    // In kJ the weak heats (0.05 kJ/mol) lie inside the binding range.
                    Assert.Equal(bindingAlone.FitPanel.YAxis.Minimum, figure.FitPanel.YAxis.Minimum, 9);
                    Assert.Equal(bindingAlone.FitPanel.YAxis.Maximum, figure.FitPanel.YAxis.Maximum, 9);
                }
            }
            finally
            {
                FinalFigureModelPresentation.Selection = originalSelection;
                FinalFigureModelPresentation.EnergyUnitFamily = originalFamily;
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    static double EnergyScale(PublicationFigureDocument figure)
        => AnalysisITC.Core.Units.Energy.ScaleFactor(figure.ResolvedEnergyUnit);

    static ExperimentData Experiment(string name)
    {
        var data = new ExperimentData(name + ".itc")
        {
            CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25,
        };
        for (var index = 0; index < 18; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8.4e-10, true)
            {
                ActualCellConcentration = 35e-6, ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) / 7d,
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
        SetComparison(binding.Solution, new NullModelComparison
        {
            NullModelId = "offset", NullFitSucceeded = true, BindingFitSucceeded = true,
            NullSolutions = { nullModel.Solution },
            Members = { new NullModelComparisonMember { ExperimentId = data.UniqueID } },
        });
        return data;
    }

    static SolverConvergence Converged() => SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.Converged, Loss = 1e-6 });

    static void SetComparison(SolutionInterface solution, NullModelComparison? comparison)
        => typeof(SolutionInterface).GetProperty(nameof(SolutionInterface.NullComparison))!.GetSetMethod(true)!
            .Invoke(solution, new object?[] { comparison });

    static void SaveQa(PublicationFigureDocument figure, string name)
    {
        var renderer = new SkiaFigureRenderer();
        using var bitmap = renderer.RenderBitmap(figure, 1000);
        Assert.True(bitmap.Width > 300 && bitmap.Height > 300);
        var directory = Environment.GetEnvironmentVariable("FTITC_FINAL_FIGURE_QA_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        renderer.WritePdf(figure, Path.Combine(directory, name + ".pdf"));
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), png.ToArray());
    }

    static void SaveQaBytes(byte[] bytes, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FTITC_FINAL_FIGURE_QA_DIRECTORY");
        if (!string.IsNullOrEmpty(directory)) File.WriteAllBytes(Path.Combine(directory, name), bytes);
    }
}
