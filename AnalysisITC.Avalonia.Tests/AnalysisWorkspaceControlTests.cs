using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using CoreAnalysisWorkspace = AnalysisITC.Core.Analysis.AnalysisWorkspace;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisWorkspaceControlTests
{
    [AvaloniaFact]
    public void ModelOptionsRemainAvailableWithoutAnExperiment()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var workspace = new AnalysisWorkspaceControl();
            var window = new Window { Content = workspace };
            window.Show();
            try
            {
                var competitive = Assert.Single(workspace.ModelComboForTesting.Items.OfType<ComboBoxItem>(),
                    item => item.Tag is AnalysisModel model && model == AnalysisModel.CompetitiveBinding);
                Assert.True(competitive.IsEnabled);
                workspace.ModelComboForTesting.SelectedItem = competitive;
                Dispatcher.UIThread.RunJobs();

                Assert.Null(workspace.ContextForTesting);
                Assert.False(workspace.CanRunFit);
                Assert.Equal(5, workspace.OptionPanelForTesting.Children.OfType<Border>().Count());

                workspace.ModeSelectorForTesting.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                Assert.True(workspace.IsGlobalMode);
                Assert.Equal(5, workspace.OptionPanelForTesting.Children.OfType<Border>().Count());
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaFact]
    public void NullHypothesisInspectorShowsSharedAutomaticRowsForSelectedAndRestoredEvidence()
    {
        var previousFamily = AppSettings.EnergyUnitFamily;
        try
        {
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
            AppSettings.Save();
            Dispatcher.UIThread.Invoke(() =>
            {
                DataManager.Clear(DataClearMode.ResetSession);
                var first = CreateNullComparisonExperiment("first-private-id", 10, 4.184);
                var second = CreateNullComparisonExperiment("second-private-id", -2, 8.368);
                var workspace = new AnalysisWorkspaceControl { Experiment = first };
                var window = new Window { Content = workspace };
                window.Show();
                try
                {
                    Assert.Equal("Offset", workspace.NullTestValueForTesting("Model").Text);
                    Assert.Equal(4.184.ToString("G4", CultureInfo.CurrentCulture), workspace.NullTestValueForTesting("Null RMSD").Text);
                    Assert.Equal("+10", workspace.NullTestValueForTesting("ΔAICc").Text);
                    Assert.Equal("Inconclusive", workspace.NullTestValueForTesting("Conclusion").Text);
                    Assert.Equal(NullModelComparisonPresentation.AnalysisEvidenceTooltip(
                        first.Solution!.NullComparison!, EnergyUnitFamily.Joules),
                        ToolTip.GetTip(workspace.NullTestValueForTesting("ΔAICc")));
                    Assert.Equal(ToolTip.GetTip(workspace.NullTestValueForTesting("ΔAICc")),
                        ToolTip.GetTip(workspace.NullTestValueForTesting("Null RMSD")));
                    Assert.Null(ToolTip.GetTip(workspace.NullTestValueForTesting("Conclusion")));
                    Assert.DoesNotContain("private-id", ToolTip.GetTip(workspace.NullTestValueForTesting("ΔAICc"))?.ToString());

                    var visibleText = string.Join("\n", workspace.GetLogicalDescendants()
                        .OfType<TextBlock>().Select(block => block.Text));
                    Assert.Contains("Null hypothesis test", visibleText);
                    Assert.Contains("Model", visibleText);
                    Assert.Contains("Null RMSD", visibleText);
                    Assert.Contains("ΔAICc", visibleText);
                    Assert.DoesNotContain("RMSD / ΔAICc", visibleText);
                    Assert.Contains("Conclusion", visibleText);
                    Assert.DoesNotContain("Binding fit:", visibleText);
                    Assert.DoesNotContain("ExperimentId", visibleText);
                    Assert.DoesNotContain("Modify assessment", visibleText);

                    workspace.Experiment = second;
                    Assert.Equal(8.368.ToString("G4", CultureInfo.CurrentCulture), workspace.NullTestValueForTesting("Null RMSD").Text);
                    Assert.Equal("-2", workspace.NullTestValueForTesting("ΔAICc").Text);
                    Assert.Equal("No binding detected", workspace.NullTestValueForTesting("Conclusion").Text);

                    var restored = CreateNullComparisonExperiment("restored-private-id", -2, 8.368);
                    workspace.Experiment = restored;
                    Assert.Equal(8.368.ToString("G4", CultureInfo.CurrentCulture), workspace.NullTestValueForTesting("Null RMSD").Text);
                    Assert.Equal("-2", workspace.NullTestValueForTesting("ΔAICc").Text);
                    Assert.Equal("No binding detected", workspace.NullTestValueForTesting("Conclusion").Text);

                    var pooled = CreateNullComparisonExperiment("pooled-private-id", 15, 4.184, pooledGlobal: true);
                    workspace.Experiment = pooled;
                    Assert.Equal(4.184.ToString("G4", CultureInfo.CurrentCulture), workspace.NullTestValueForTesting("Null RMSD").Text);
                    Assert.Equal("+15", workspace.NullTestValueForTesting("ΔAICc").Text);
                    Assert.Equal("Binding detected", workspace.NullTestValueForTesting("Conclusion").Text);

                    workspace.Experiment = restored;
                    AppSettings.EnergyUnitFamily = EnergyUnitFamily.Calories;
                    AppSettings.Save();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal("2", workspace.NullTestValueForTesting("Null RMSD").Text);
                    Assert.Equal("-2", workspace.NullTestValueForTesting("ΔAICc").Text);

                    var inconclusive = CreateNullComparisonExperiment("inconclusive-private-id", 8, 4.184);
                    workspace.Experiment = inconclusive;
                    Assert.Equal("Inconclusive", workspace.NullTestValueForTesting("Conclusion").Text);

                    var failed = CreateNullComparisonExperiment("failed-private-id", 10, 4.184);
                    failed.Solution!.NullComparison!.NullFitSucceeded = false;
                    failed.Solution.NullComparison.NullFitReason = "Offset fit did not converge.";
                    failed.Solution.NullComparison.DeltaAicc = null;
                    failed.Solution.NullComparison.NullInformationCriteria = null;
                    failed.Solution.NullComparison.ComparisonUnavailableReason = "Offset fit did not converge.";
                    workspace.Experiment = failed;
                    Assert.Equal("Offset", workspace.NullTestValueForTesting("Model").Text);
                    Assert.Equal("Unavailable", workspace.NullTestValueForTesting("Null RMSD").Text);
                    Assert.Equal("Unavailable", workspace.NullTestValueForTesting("ΔAICc").Text);
                    Assert.Equal("Not assessed", workspace.NullTestValueForTesting("Conclusion").Text);
                    Assert.Contains("Offset fit did not converge.",
                        ToolTip.GetTip(workspace.NullTestValueForTesting("ΔAICc"))?.ToString());

                    var unavailable = CreateReadyExperiment("unavailable-private-id");
                    var unavailableModel = AttachFittedSolution(unavailable);
                    unavailableModel.Solution!.NullComparison = new NullModelComparison
                    {
                        BindingFitSucceeded = true,
                        NullFitSucceeded = true,
                        ComparisonUnavailableReason = "AICc could not be calculated."
                    };
                    workspace.Experiment = unavailable;
                    Assert.Equal("Unavailable", workspace.NullTestValueForTesting("Null RMSD").Text);
                    Assert.Equal("Unavailable", workspace.NullTestValueForTesting("ΔAICc").Text);
                    Assert.Equal("Not assessed", workspace.NullTestValueForTesting("Conclusion").Text);
                    Assert.Contains("AICc could not be calculated.",
                        ToolTip.GetTip(workspace.NullTestValueForTesting("ΔAICc"))?.ToString());

                    workspace.Experiment = CreateReadyExperiment("missing-private-id");
                    Assert.Equal("Unknown", workspace.NullTestValueForTesting("Model").Text);
                    Assert.Equal("Unavailable", workspace.NullTestValueForTesting("Null RMSD").Text);
                    Assert.Equal("Not calculated", workspace.NullTestValueForTesting("ΔAICc").Text);
                    Assert.Equal("Not assessed", workspace.NullTestValueForTesting("Conclusion").Text);
                }
                finally
                {
                    window.Close();
                    DataManager.Clear(DataClearMode.ResetSession);
                }
            });
        }
        finally
        {
            AppSettings.EnergyUnitFamily = previousFamily;
            AppSettings.Save();
        }
    }

    [AvaloniaFact]
    public void MissingAttributeDoesNotRemoveGlobalModelOptionControls()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var first = CreateReadyExperiment("missing-attribute-first.itc");
            var second = CreateReadyExperiment("missing-attribute-second.itc");
            DataManager.AddData(new[] { first, second });

            var workspace = new AnalysisWorkspaceControl { Experiment = first };
            var window = new Window { Content = workspace };
            window.Show();
            try
            {
                var competitive = Assert.Single(workspace.ModelComboForTesting.Items.OfType<ComboBoxItem>(),
                    item => item.Tag is AnalysisModel model && model == AnalysisModel.CompetitiveBinding);
                workspace.ModelComboForTesting.SelectedItem = competitive;
                workspace.ModeSelectorForTesting.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                var fromAttributes = workspace.OptionPanelForTesting.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .First(check => check.Content?.ToString() == "From attributes");
                fromAttributes.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.True(workspace.ContextForTesting?.IsMultiExperiment);
                Assert.Equal(5, workspace.OptionPanelForTesting.Children.OfType<Border>().Count());

                workspace.RunFit();
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.IsAssignableFrom<Window>(workspace.MissingAttributeDialogForTesting);
                Assert.Equal("Attribute missing", dialog.Title);
                var dialogText = string.Join("\n", dialog.GetVisualDescendants()
                    .OfType<TextBlock>().Select(text => text.Text));
                Assert.Contains(first.Name, dialogText);
                Assert.Contains(second.Name, dialogText);
                Assert.Equal("Error: attribute missing", workspace.FitStatusForTesting);
                workspace.RunFit();
                Assert.Same(dialog, workspace.MissingAttributeDialogForTesting);
                Assert.Equal(5, workspace.OptionPanelForTesting.Children.OfType<Border>().Count());
                dialog.Close();
            }
            finally
            {
                workspace.MissingAttributeDialogForTesting?.Close();
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaFact]
    public void DisplayOptionsUnifyBothAxesAndRememberLargeParameterText()
    {
        var previousLargeText = AppSettings.UseLargeAnalysisParameterText;
        try
        {
            AppSettings.UseLargeAnalysisParameterText = false;
            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();

                workspace.UnifiedAxesCheckForTesting.IsChecked = true;
                Assert.True(workspace.GraphForTesting.UnifiedXAxis);
                Assert.True(workspace.GraphForTesting.UnifiedYAxis);

                workspace.LargeParameterTextCheckForTesting.IsChecked = true;
                Assert.True(AppSettings.UseLargeAnalysisParameterText);
                Assert.True(workspace.GraphForTesting.UseLargeParameterText);

                var reopenedWorkspace = new AnalysisWorkspaceControl();
                Assert.True(reopenedWorkspace.LargeParameterTextCheckForTesting.IsChecked);
                Assert.True(reopenedWorkspace.GraphForTesting.UseLargeParameterText);
            });
        }
        finally
        {
            AppSettings.UseLargeAnalysisParameterText = previousLargeText;
            AppSettings.Save();
        }
    }

    [AvaloniaFact]
    public void FitSummaryStaysVisibleAndCompleteWhenGraphParameterOverlaysChange()
    {
        var previousDisplay = AppSettings.AnalysisParameterDisplay;
        try
        {
            AppSettings.AnalysisParameterDisplay = FinalFigureDisplayParameters.AnalysisView;
            Dispatcher.UIThread.Invoke(() =>
            {
                DataManager.Clear(DataClearMode.ResetSession);
                var experiment = CreateReadyExperiment("fit-summary-ui.itc");
                var model = AttachFittedSolution(experiment);
                var workspace = new AnalysisWorkspaceControl { Experiment = experiment };
                var window = new Window { Content = workspace };
                window.Show();
                try
                {
                    var graph = workspace.GraphForTesting;
                    var expected = AnalysisParameterSummaryPresentation.BuildInspectorSummary(model.Solution);
                    var expectedLabels = expected.Rows.Select(row => PlainText(row.Label)).ToList();

                    Assert.Equal(model.ModelName, FitSummaryHeaderText(workspace));
                    Assert.Equal(expectedLabels, FitSummaryLabels(workspace));
                    Assert.Equal("RMSD", expectedLabels[0]);
                    Assert.Contains(PlainText(MarkdownStrings.GibbsFreeEnergy), expectedLabels);
                    Assert.True(graph.ShowParameterGuides);
                    Assert.True(graph.ShowParameterBox);

                    workspace.ParameterBoxCheckForTesting.IsChecked = false;
                    Assert.False(graph.ShowParameterBox);
                    Assert.True(graph.ShowParameterGuides);
                    Assert.Equal(expectedLabels, FitSummaryLabels(workspace));

                    workspace.ParameterGuidesCheckForTesting.IsChecked = false;
                    workspace.ParameterBoxCheckForTesting.IsChecked = true;
                    Assert.False(graph.ShowParameterGuides);
                    Assert.True(graph.ShowParameterBox);

                    workspace.DisplayDerivedCheckForTesting.IsChecked = false;
                    Assert.False(AppSettings.AnalysisParameterDisplay.HasFlag(FinalFigureDisplayParameters.Derived));
                    Assert.Equal(expectedLabels, FitSummaryLabels(workspace));
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            AppSettings.AnalysisParameterDisplay = previousDisplay;
            AppSettings.Save();
        }
    }

    [AvaloniaFact]
    public void FitSummaryReportsMissingFitForExperimentWithoutSolution()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var workspace = new AnalysisWorkspaceControl { Experiment = CreateReadyExperiment("no-fit-ui.itc") };

            var message = Assert.IsType<TextBlock>(Assert.Single(workspace.FitSummaryRowsForTesting.Children));
            Assert.Equal(AnalysisParameterSummaryPresentation.NoFitText, message.Text);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FitSummaryTracksHeatOnlySolutionChanges(bool fromWorkerThread)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var experiment = CreateReadyExperiment("heat-only-summary.DH");
        var workspace = new AnalysisWorkspaceControl { Experiment = experiment };
        var window = new Window { Content = workspace };
        window.Show();
        try
        {
            Assert.False(experiment.HasThermogram);
            Assert.True(experiment.CanBeAnalyzed);
            Assert.False(experiment.Processor.BaselineCompleted);
            AssertNoFitSummary(workspace);

            foreach (var stoichiometry in new[] { 2.0, 3.0 })
            {
                var model = new OneSetOfSites(experiment);
                model.InitializeParameters(experiment);
                model.Parameters.Table[ParameterType.Nvalue1].Update(stoichiometry);
                model.Solution = SolutionInterface.FromModel(
                    model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
                await Publish(() => experiment.UpdateSolution(model));

                Assert.Same(model.Solution, experiment.Solution);
                Assert.Equal("One-Set-Of-Sites", FitSummaryHeaderText(workspace));
                Assert.Equal(stoichiometry.ToString("0", CultureInfo.InvariantCulture),
                    FitSummaryValue(workspace, "N"));
            }

            await Publish(() => experiment.RemoveModel(notifyDataChange: false));
            Assert.Null(experiment.Solution);
            AssertNoFitSummary(workspace);
            Assert.False(experiment.Processor.BaselineCompleted);
            Assert.True(experiment.CanBeAnalyzed);
        }
        finally
        {
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }

        async Task Publish(Action change)
        {
            if (fromWorkerThread) await Task.Run(change);
            else change();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void QueuedSolutionChangeKeepsSelectedExperimentSummary()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var first = CreateReadyExperiment("old-summary.DH");
        var second = CreateReadyExperiment("selected-summary.DH");
        var workspace = new AnalysisWorkspaceControl { Experiment = first };
        var window = new Window { Content = workspace };
        window.Show();
        try
        {
            var model = AttachFittedSolution(first);
            first.UpdateSolution(model);
            workspace.Experiment = second;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(second, workspace.Experiment);
            AssertNoFitSummary(workspace);

            first.RemoveModel(notifyDataChange: false);
            Dispatcher.UIThread.RunJobs();
            AssertNoFitSummary(workspace);
        }
        finally
        {
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static void AssertNoFitSummary(AnalysisWorkspaceControl workspace)
    {
        var message = Assert.IsType<TextBlock>(Assert.Single(workspace.FitSummaryRowsForTesting.Children));
        Assert.Equal("No fit result for the selected experiment.", message.Text);
    }

    static string FitSummaryValue(AnalysisWorkspaceControl workspace, string label)
    {
        var row = workspace.FitSummaryRowsForTesting.Children.Skip(1)
            .Select(control => (Grid)((Border)control).Child!)
            .Single(grid => InlineText((TextBlock)grid.Children[0]) == label);
        return InlineText((TextBlock)row.Children[1]);
    }

    static string InlineText(TextBlock text)
        => string.Concat(text.Inlines!.OfType<Run>().Select(run => run.Text));

    static string FitSummaryHeaderText(AnalysisWorkspaceControl workspace)
    {
        var header = Assert.IsType<Grid>(
            Assert.IsType<Border>(workspace.FitSummaryRowsForTesting.Children[0]).Child);
        return ((TextBlock)header.Children[0]).Text ?? "";
    }

    static List<string> FitSummaryLabels(AnalysisWorkspaceControl workspace)
        => workspace.FitSummaryRowsForTesting.Children
            .Skip(1)
            .Select(row => (TextBlock)((Grid)((Border)row).Child!).Children[0])
            .Select(label => string.Concat(label.Inlines!.OfType<Run>().Select(run => run.Text)))
            .ToList();

    static string PlainText(string markdown)
        => string.Concat(MarkdownProcessor
            .GetSegments(MarkdownProcessor.ProcessWrittenText(markdown))
            .Select(segment => segment.Text));

    [AvaloniaFact]
    public void WeightedFittingAvailabilityPreservesSelectionAndTracksPointInclusion()
    {
        var previous = FittingOptionsController.UseErrorWeightedFitting;
        try
        {
            FittingOptionsController.UseErrorWeightedFitting = true;
            Dispatcher.UIThread.Invoke(() =>
            {
                DataManager.Clear(DataClearMode.ResetSession);
                var experiment = CreateReadyExperiment("weighted-ui.itc");
                foreach (var injection in experiment.Injections)
                    injection.SetPeakArea(new FloatWithError(injection.PeakArea.Value, 1e-8));
                experiment.Injections[0].SetPeakArea(
                    new FloatWithError(experiment.Injections[0].PeakArea.Value));
                DataManager.AddData(experiment);

                var workspace = new AnalysisWorkspaceControl { Experiment = experiment };
                var window = new Window { Content = workspace };
                window.Show();
                try
                {
                    Assert.True(workspace.WeightedFitCheckForTesting.IsChecked == true);
                    Assert.False(workspace.WeightedFitCheckForTesting.IsEnabled);
                    Assert.Contains(
                        "finite peak-area SD larger than zero",
                        ToolTip.GetTip(workspace.WeightedFitCheckForTesting)?.ToString());

                    experiment.Injections[0].ToggleDataPointActive();
                    Dispatcher.UIThread.RunJobs();

                    Assert.True(workspace.WeightedFitCheckForTesting.IsChecked == true);
                    Assert.True(workspace.WeightedFitCheckForTesting.IsEnabled);
                }
                finally
                {
                    window.Close();
                    DataManager.Clear(DataClearMode.ResetSession);
                }
            });
        }
        finally
        {
            FittingOptionsController.UseErrorWeightedFitting = previous;
        }
    }

    [AvaloniaFact]
    public void WeightedFittingAvailabilityUsesAllExperimentsOnlyInGlobalMode()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var first = CreateReadyExperiment("weighted-single-valid.itc", 20);
            var second = CreateReadyExperiment("weighted-global-invalid.itc", 30);
            foreach (var injection in first.Injections.Concat(second.Injections))
                injection.SetPeakArea(new FloatWithError(injection.PeakArea.Value, 1e-8));
            second.Injections[0].SetPeakArea(
                new FloatWithError(second.Injections[0].PeakArea.Value));
            DataManager.AddData(new[] { first, second });

            var workspace = new AnalysisWorkspaceControl { Experiment = first };
            var window = new Window { Content = workspace };
            window.Show();
            try
            {
                Assert.True(workspace.WeightedFitCheckForTesting.IsEnabled);

                workspace.ModeSelectorForTesting.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                Assert.True(workspace.ContextForTesting?.IsMultiExperiment);
                Assert.False(workspace.WeightedFitCheckForTesting.IsEnabled);
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaFact]
    public void SingleExperimentWorkspaceSurvivesTransientGlobalSelectionChange()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var experiment = CreateReadyExperiment("selection-race.itc");
            DataManager.AddData(experiment);

            var workspace = new AnalysisWorkspaceControl { Experiment = experiment };
            var window = new Window { Content = workspace };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var parameterRowCount = workspace.ParameterPanelForTesting.Children.Count;
                Assert.True(workspace.CanRunFit);
                Assert.True(parameterRowCount > 0);

                // Match the transient post-fit state: DataManager has no current
                // experiment while this control still displays the fitted one.
                DataManager.SelectIndex(-1);
                workspace.RefreshIncludedDataState();
                Dispatcher.UIThread.RunJobs();

                Assert.Null(DataManager.Current);
                Assert.True(workspace.CanRunFit);
                Assert.True(workspace.ContextForTesting != null);
                Assert.Same(experiment, workspace.ContextForTesting?.SingleModel.Data);
                Assert.Equal(
                    parameterRowCount,
                    workspace.ParameterPanelForTesting.Children.Count);
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaTheory]
    [InlineData("B,C", "C")]
    [InlineData("B,A", "A")]
    public async Task CancelledFitUsesLatestSelectedExperimentOnNextRun(string selectionPath, string expectedName)
    {
        await WaitForFitToFinish();
        var previousCreateResult = AppSettings.CreateSingleAnalysisResult;
        var previousAutoOpen = AppSettings.AutoOpenNewAnalysisResult;
        var previousFittingOptions = CaptureFittingOptions();
        DataManager.Clear(DataClearMode.ResetSession);

        var first = CreateFitExperiment("selection-race-A.itc", 2000);
        var second = CreateFitExperiment("selection-race-B.itc", -3000);
        var third = CreateFitExperiment("selection-race-C.itc", 1000);
        AttachFittedSolution(first);
        DataManager.AddData(new[] { first, second, third });

        var selected = new Dictionary<string, ExperimentData>(StringComparer.Ordinal)
        {
            ["A"] = first,
            ["B"] = second,
            ["C"] = third
        };
        var workspace = new AnalysisWorkspaceControl { Experiment = first };
        var window = new Window { Content = workspace };
        var analysisStartCount = 0;
        ExperimentData? nextRunData = null;
        Model? firstRunModel = null;
        EventHandler<TerminationFlag> onAnalysisStarted = (sender, _) =>
        {
            analysisStartCount++;
            if (analysisStartCount == 1)
            {
                if (sender is Solver solver) firstRunModel = solver.Model;
                foreach (var key in selectionPath.Split(','))
                    workspace.Experiment = selected[key];
            }
            else if (sender is Solver secondSolver)
            {
                nextRunData = secondSolver.Model.Data;
            }

            workspace.StopFit();
        };

        try
        {
            AppSettings.CreateSingleAnalysisResult = false;
            AppSettings.AutoOpenNewAnalysisResult = false;
            SolverInterface.AnalysisStarted += onAnalysisStarted;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(workspace.CanRunFit);

            workspace.RunFit();
            await WaitForFitToFinish();

            Assert.Same(selected[expectedName], workspace.Experiment);
            Assert.Empty(DataManager.Results);
            var retainedSolution = firstRunModel!.Solution;
            var retainedOffset = firstRunModel.Parameters.Table[ParameterType.Offset].Value;
            var retainedPrediction = firstRunModel.Evaluate(0);

            Assert.True(workspace.CanRunFit);
            var runButton = workspace.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Run Fit"));
            runButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForFitToFinish();

            Assert.Equal(2, analysisStartCount);
            Assert.Same(selected[expectedName], nextRunData);
            // Starting another fit on A intentionally attaches a fresh prepared
            // model. The retained first-fit snapshot itself must remain unchanged.
            if (!ReferenceEquals(selected[expectedName], first))
            {
                Assert.Same(firstRunModel, first.Model);
                Assert.Same(retainedSolution, first.Solution);
            }
            Assert.Same(retainedSolution, firstRunModel.Solution);
            Assert.Equal(retainedOffset, firstRunModel.Parameters.Table[ParameterType.Offset].Value);
            Assert.Equal(retainedPrediction, firstRunModel.Evaluate(0));
        }
        finally
        {
            SolverInterface.AnalysisStarted -= onAnalysisStarted;
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            AppSettings.CreateSingleAnalysisResult = previousCreateResult;
            AppSettings.AutoOpenNewAnalysisResult = previousAutoOpen;
            RestoreFittingOptions(previousFittingOptions);
        }

    }

    [AvaloniaTheory]
    [InlineData("unready")]
    [InlineData("result")]
    [InlineData("none")]
    public async Task MainWindowSelectionDuringFitClearsStaleReadyContext(string selectionTarget)
    {
        await WaitForFitToFinish();
        var application = Application.Current!;
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        var previousCreateResult = AppSettings.CreateSingleAnalysisResult;
        var previousAutoOpen = AppSettings.AutoOpenNewAnalysisResult;
        var previousFittingOptions = CaptureFittingOptions();
        DataManager.Clear(DataClearMode.ResetSession);
        var first = CreateFitExperiment("main-selection-A.itc", 2000);
        var second = CreateFitExperiment("main-selection-B.itc", -3000);
        var unready = CreateFitExperiment("main-selection-unready.itc", 2000);
        unready.Injections.RemoveRange(2, 3);
        var resultModel = AttachFittedSolution(first);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(
            new Solver { Model = resultModel }));
        DataManager.AddData(new ITCDataContainer[] { first, second, unready, result });
        MainWindow? window = null;
        ExperimentData? startedData = null;
        var analysisStartCount = 0;
        ListBox? items = null;
        AnalysisWorkspaceControl? workspace = null;
        EventHandler<TerminationFlag> onAnalysisStarted = (sender, _) =>
        {
            analysisStartCount++;
            if (sender is Solver solver) startedData = solver.Model.Data;
            items!.SelectedIndex = selectionTarget switch
            {
                "unready" => 2,
                "result" => 3,
                _ => -1
            };
            workspace!.StopFit();
        };

        application.Styles.Add(theme);
        try
        {
            AppSettings.CreateSingleAnalysisResult = false;
            AppSettings.AutoOpenNewAnalysisResult = false;
            window = new MainWindow();
            window.Show();
            items = window.FindControl<ListBox>("ItemsList")!;
            var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
            workspace = window.FindControl<AnalysisWorkspaceControl>("AnalysisWorkspace")!;

            items.SelectedIndex = 0;
            tabs.SelectedIndex = 2;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(workspace.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, window));
            Assert.Same(first, workspace.Experiment);
            var runButton = workspace.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Run Fit"));
            Assert.True(runButton.IsEnabled);

            SolverInterface.AnalysisStarted += onAnalysisStarted;
            runButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForFitToFinish();
            Assert.Same(first, startedData);
            Assert.Null(workspace.ContextForTesting);
            Assert.False(workspace.CanRunFit);
            if (selectionTarget == "unready") Assert.Same(unready, workspace.Experiment);
            else Assert.Null(workspace.Experiment);
            Assert.Single(DataManager.Results);

            workspace.RunFit();
            Assert.Equal(1, analysisStartCount);
        }
        finally
        {
            SolverInterface.AnalysisStarted -= onAnalysisStarted;
            if (window != null)
            {
                DocumentDirtyTracker.MarkClean();
                window.Close();
                Assert.False(window.IsVisible);
            }
            DataManager.Clear(DataClearMode.ResetSession);
            AppSettings.CreateSingleAnalysisResult = previousCreateResult;
            AppSettings.AutoOpenNewAnalysisResult = previousAutoOpen;
            RestoreFittingOptions(previousFittingOptions);
            application.Styles.Remove(theme);
        }

    }

    [AvaloniaFact]
    public async Task SuccessfulMainWindowFitKeepsRetainedAWhenNextFitTargetsB()
    {
        await WaitForFitToFinish();
        var application = Application.Current!;
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        var previousCreateResult = AppSettings.CreateSingleAnalysisResult;
        var previousAutoOpen = AppSettings.AutoOpenNewAnalysisResult;
        var previousFittingOptions = CaptureFittingOptions();
        DataManager.Clear(DataClearMode.ResetSession);
        var first = CreateFitExperiment("successful-selection-A.itc", 2000);
        var second = CreateFitExperiment("successful-selection-B.itc", -3000);
        DataManager.AddData(new[] { first, second });
        MainWindow? window = null;
        AnalysisWorkspaceControl? workspace = null;
        ListBox? items = null;
        var analysisStartCount = 0;
        Model? firstRunModel = null;
        ExperimentData? secondRunData = null;
        double? secondRunOffset = null;
        bool secondRunOffsetLocked = false;
        EventHandler<TerminationFlag> onAnalysisStarted = (sender, _) =>
        {
            analysisStartCount++;
            if (sender is not Solver solver) return;
            if (analysisStartCount == 1)
            {
                firstRunModel = solver.Model;
                CoreWorkspaceFor(workspace!).SetParameterOverride(
                    ParameterType.Offset, 250, isLocked: true, rebuild: false);
                items!.SelectedIndex = 1;
            }
            else
            {
                secondRunData = solver.Model.Data;
                secondRunOffset = solver.Model.Parameters.Table[ParameterType.Offset].Value;
                secondRunOffsetLocked = solver.Model.Parameters.Table[ParameterType.Offset].IsLocked;
                workspace!.StopFit();
            }
        };

        application.Styles.Add(theme);
        try
        {
            AppSettings.CreateSingleAnalysisResult = false;
            AppSettings.AutoOpenNewAnalysisResult = false;
            window = new MainWindow();
            window.Show();
            items = window.FindControl<ListBox>("ItemsList")!;
            var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
            workspace = window.FindControl<AnalysisWorkspaceControl>("AnalysisWorkspace")!;
            items.SelectedIndex = 0;
            tabs.SelectedIndex = 2;
            window.UpdateLayout();
            workspace.ErrorMethodComboForTesting.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            SolverInterface.AnalysisStarted += onAnalysisStarted;
            var runButton = workspace.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Run Fit"));
            Assert.True(runButton.IsEnabled);
            runButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForFitToFinish();

            Assert.Equal(1, analysisStartCount);
            Assert.Same(second, workspace.Experiment);
            Assert.Equal(250, workspace.ContextForTesting!.SingleModel.Parameters.Table[ParameterType.Offset].Value);
            Assert.True(workspace.ContextForTesting.SingleModel.Parameters.Table[ParameterType.Offset].IsLocked);
            Assert.Same(firstRunModel, first.Model);
            Assert.NotNull(first.Solution);
            var retainedSolution = first.Solution;
            var retainedOffset = first.Model!.Parameters.Table[ParameterType.Offset].Value;
            var retainedPrediction = first.Model.Evaluate(0);
            Assert.Empty(DataManager.Results);

            var completionStatus = workspace.FitStatusForTesting;
            Assert.StartsWith("Analysis complete in ", completionStatus);
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("Analysis complete in ", workspace.FitStatusForTesting);
            Assert.Equal(completionStatus, workspace.FitStatusForTesting);
            Assert.Same(second, workspace.ContextForTesting!.SingleModel.Data);
            Assert.True(workspace.CanRunFit);

            // The next fit starts immediately from the selected B context.
            runButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForFitToFinish();

            Assert.Equal(2, analysisStartCount);
            Assert.Same(second, secondRunData);
            Assert.Equal(250, secondRunOffset);
            Assert.True(secondRunOffsetLocked);
            Assert.Same(firstRunModel, first.Model);
            Assert.Same(retainedSolution, first.Solution);
            Assert.Equal(retainedOffset, first.Model!.Parameters.Table[ParameterType.Offset].Value);
            Assert.Equal(retainedPrediction, first.Model.Evaluate(0));

            // An ordinary idle selection change clears the preserved completion text.
            items.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("5/5 points with fitted solution", workspace.FitStatusForTesting);
        }
        finally
        {
            SolverInterface.AnalysisStarted -= onAnalysisStarted;
            if (window != null)
            {
                DocumentDirtyTracker.MarkClean();
                window.Close();
                Assert.False(window.IsVisible);
            }
            DataManager.Clear(DataClearMode.ResetSession);
            AppSettings.CreateSingleAnalysisResult = previousCreateResult;
            AppSettings.AutoOpenNewAnalysisResult = previousAutoOpen;
            RestoreFittingOptions(previousFittingOptions);
            application.Styles.Remove(theme);
        }
    }

    [AvaloniaFact]
    public async Task UnreadyOrMissingExperimentCannotReusePreviouslyReadyFitContext()
    {
        await WaitForFitToFinish();
        DataManager.Clear(DataClearMode.ResetSession);
        var ready = CreateFitExperiment("ready-before-unready.itc", 2000);
        var unready = new ExperimentData("unready-selection.itc");
        var workspace = new AnalysisWorkspaceControl { Experiment = ready };
        var window = new Window { Content = workspace };
        var analysisStartCount = 0;
        EventHandler<TerminationFlag> onAnalysisStarted = (_, _) => analysisStartCount++;
        SolverInterface.AnalysisStarted += onAnalysisStarted;
        window.Show();
        try
        {
            Assert.True(workspace.CanRunFit);
            workspace.Experiment = unready;
            Assert.Same(unready, workspace.Experiment);
            Assert.False(workspace.CanRunFit);
            workspace.RunFit();
            Assert.Equal(0, analysisStartCount);

            workspace.Experiment = null;
            Assert.Null(workspace.Experiment);
            Assert.False(workspace.CanRunFit);
            workspace.RunFit();
            Assert.Equal(0, analysisStartCount);
        }
        finally
        {
            SolverInterface.AnalysisStarted -= onAnalysisStarted;
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    [AvaloniaFact]
    public async Task ProgrammaticRunFitRebuildsWhenReadyContextBelongsToAnotherExperiment()
    {
        await WaitForFitToFinish();
        DataManager.Clear(DataClearMode.ResetSession);
        var first = CreateFitExperiment("stale-context-A.itc", 2000);
        var second = CreateFitExperiment("stale-context-B.itc", -3000);
        DataManager.AddData(new[] { first, second });
        var previousFittingOptions = CaptureFittingOptions();
        var workspace = new AnalysisWorkspaceControl { Experiment = first };
        var window = new Window { Content = workspace };
        ExperimentData? startedData = null;
        EventHandler<TerminationFlag> onAnalysisStarted = (sender, _) =>
        {
            if (sender is Solver solver) startedData = solver.Model.Data;
            workspace.StopFit();
        };
        SolverInterface.AnalysisStarted += onAnalysisStarted;
        window.Show();
        try
        {
            var coreWorkspace = CoreWorkspaceFor(workspace);
            Assert.True(coreWorkspace.TryRebuild(new[] { second }));
            Assert.Same(second, coreWorkspace.Context.SingleModel.Data);

            // RunFit must validate the active experiment identity itself, even
            // when called directly while the button reflects stale readiness.
            workspace.RunFit();
            Assert.True(workspace.CanStopFit, workspace.FitStatusForTesting);
            await WaitForFitToFinish();
            Assert.Same(first, startedData);
            Assert.Same(first, workspace.ContextForTesting!.SingleModel.Data);
        }
        finally
        {
            SolverInterface.AnalysisStarted -= onAnalysisStarted;
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            RestoreFittingOptions(previousFittingOptions);
        }
    }

    [AvaloniaFact]
    public async Task GlobalFitNavigationPreservesIncludedOrderWhileFitIsActive()
    {
        await WaitForFitToFinish();
        var application = Application.Current!;
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        var previousCreateResult = AppSettings.CreateGlobalAnalysisResult;
        var previousAutoOpen = AppSettings.AutoOpenNewAnalysisResult;
        var previousFittingOptions = CaptureFittingOptions();
        DataManager.Clear(DataClearMode.ResetSession);
        var first = CreateFitExperiment("global-selection-A.itc", 2000);
        var second = CreateFitExperiment("global-selection-B.itc", -3000);
        var excluded = CreateFitExperiment("global-selection-excluded.itc", 1000);
        DataManager.AddData(new[] { first, second, excluded });
        excluded.Include = false;
        MainWindow? window = null;
        application.Styles.Add(theme);
        try
        {
            window = new MainWindow();
            window.Show();
            var items = window.FindControl<ListBox>("ItemsList")!;
            var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
            var workspace = window.FindControl<AnalysisWorkspaceControl>("AnalysisWorkspace")!;
            items.SelectedIndex = 0;
            tabs.SelectedIndex = 2;
            window.UpdateLayout();
            workspace.ModeSelectorForTesting.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            var expected = new[] { first, second };
            Assert.True(workspace.ContextForTesting!.IsMultiExperiment);
            Assert.Equal(expected, workspace.ContextForTesting.GlobalModel.Models.Select(model => model.Data));
            Assert.True(workspace.CanRunFit);

            ExperimentData[]? startedInputs = null;
            var fitStartCount = 0;
            EventHandler<TerminationFlag> onAnalysisStarted = (sender, _) =>
            {
                fitStartCount++;
                if (sender is GlobalSolver solver)
                    startedInputs = solver.Model.Models.Select(model => model.Data).ToArray();
                items.SelectedIndex = -1;
                workspace.StopFit();
            };
            AppSettings.CreateGlobalAnalysisResult = false;
            AppSettings.AutoOpenNewAnalysisResult = false;
            SolverInterface.AnalysisStarted += onAnalysisStarted;
            try
            {
                var runButton = workspace.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "Run Fit"));
                Assert.True(runButton.IsEnabled);
                runButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForFitToFinish();
            }
            finally
            {
                SolverInterface.AnalysisStarted -= onAnalysisStarted;
            }

            Assert.Equal(expected, startedInputs);
            Assert.Null(workspace.Experiment);
            Assert.False(workspace.CanRunFit);
            Assert.Null(workspace.ContextForTesting);
            workspace.RunFit();
            Assert.False(ProcessingActivity.IsBusy);
            Assert.Null(workspace.ContextForTesting);

            items.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(excluded, workspace.Experiment);
            Assert.Equal(expected, workspace.ContextForTesting!.GlobalModel.Models.Select(model => model.Data));
            Assert.Equal(expected, DataManager.IncludedData);
            Assert.True(workspace.CanRunFit);

            var globalRunButton = workspace.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Run Fit"));
            SolverInterface.AnalysisStarted += onAnalysisStarted;
            try
            {
                globalRunButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForFitToFinish();
            }
            finally
            {
                SolverInterface.AnalysisStarted -= onAnalysisStarted;
            }
            Assert.Equal(2, fitStartCount);
            Assert.Equal(expected, startedInputs);
            Assert.Null(workspace.Experiment);
            Assert.False(workspace.CanRunFit);
            Assert.Null(workspace.ContextForTesting);

            items.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(second, workspace.Experiment);
            Assert.Equal(expected, workspace.ContextForTesting!.GlobalModel.Models.Select(model => model.Data));
            Assert.Equal(expected, DataManager.IncludedData);
            Assert.True(workspace.CanRunFit);
        }
        finally
        {
            if (window != null)
            {
                DocumentDirtyTracker.MarkClean();
                window.Close();
                Assert.False(window.IsVisible);
            }
            DataManager.Clear(DataClearMode.ResetSession);
            AppSettings.CreateGlobalAnalysisResult = previousCreateResult;
            AppSettings.AutoOpenNewAnalysisResult = previousAutoOpen;
            RestoreFittingOptions(previousFittingOptions);
            application.Styles.Remove(theme);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MainWindowFitHonorsResultCreationAndAutoOpenSettings(bool createResult, bool autoOpen)
    {
        await WaitForFitToFinish();
        var application = Application.Current!;
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        var previousCreateResult = AppSettings.CreateSingleAnalysisResult;
        var previousAutoOpen = AppSettings.AutoOpenNewAnalysisResult;
        var previousFittingOptions = CaptureFittingOptions();
        DataManager.Clear(DataClearMode.ResetSession);
        var experiment = CreateFitExperiment("result-options.itc", 2000);
        DataManager.AddData(experiment);
        MainWindow? window = null;
        AnalysisResult? autoOpenRequested = null;
        EventHandler<AnalysisResult> onResultSelected = (_, result) => autoOpenRequested = result;
        DataManager.AnalysisResultSelected += onResultSelected;
        application.Styles.Add(theme);
        try
        {
            AppSettings.CreateSingleAnalysisResult = createResult;
            AppSettings.AutoOpenNewAnalysisResult = autoOpen;
            window = new MainWindow();
            window.Show();
            var items = window.FindControl<ListBox>("ItemsList")!;
            var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
            var workspace = window.FindControl<AnalysisWorkspaceControl>("AnalysisWorkspace")!;
            items.SelectedIndex = -1;
            items.SelectedIndex = 0;
            tabs.SelectedIndex = 2;
            window.UpdateLayout();
            workspace.ErrorMethodComboForTesting.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            var runButton = workspace.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Run Fit"));
            Assert.True(runButton.IsEnabled);

            runButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForFitToFinish();

            Assert.Equal(createResult ? 1 : 0, DataManager.Results.Count);
            if (createResult && autoOpen)
            {
                // Core requests auto-open. Avalonia's later selection restoration
                // is tracked separately as CR-038.
                Assert.Same(Assert.Single(DataManager.Results), autoOpenRequested);
            }
            else
            {
                Assert.Null(autoOpenRequested);
                Assert.Null(DataManager.SelectedResult);
                Assert.Same(experiment, workspace.Experiment);
            }

            if (!createResult)
            {
                Assert.StartsWith("Analysis complete in ", workspace.FitStatusForTesting);
                experiment.RemoveModel();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("5/5 integrated points", workspace.FitStatusForTesting);
            }
        }
        finally
        {
            DataManager.AnalysisResultSelected -= onResultSelected;
            if (window != null)
            {
                DocumentDirtyTracker.MarkClean();
                window.Close();
                Assert.False(window.IsVisible);
            }
            DataManager.Clear(DataClearMode.ResetSession);
            AppSettings.CreateSingleAnalysisResult = previousCreateResult;
            AppSettings.AutoOpenNewAnalysisResult = previousAutoOpen;
            RestoreFittingOptions(previousFittingOptions);
            application.Styles.Remove(theme);
        }
    }

    static ExperimentData CreateFitExperiment(string fileName, double heatPerMole)
    {
        var experiment = CreateReadyExperiment(fileName);
        foreach (var injection in experiment.Injections)
            injection.SetPeakArea(new FloatWithError(heatPerMole * injection.InjectionMass, 1e-8));
        return experiment;
    }

    static CoreAnalysisWorkspace CoreWorkspaceFor(AnalysisWorkspaceControl control)
        => (CoreAnalysisWorkspace)typeof(AnalysisWorkspaceControl)
            .GetField("workspace", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(control)!;

    static FittingOptionsSnapshot CaptureFittingOptions()
        => new(
            FittingOptionsController.Algorithm,
            FittingOptionsController.ErrorEstimationMethod,
            FittingOptionsController.BootstrapIterations,
            FittingOptionsController.IncludeConcentrationVariance,
            FittingOptionsController.UnlockBootstrapParameters,
            FittingOptionsController.UseErrorWeightedFitting);

    static void RestoreFittingOptions(FittingOptionsSnapshot snapshot)
    {
        FittingOptionsController.Algorithm = snapshot.Algorithm;
        FittingOptionsController.ErrorEstimationMethod = snapshot.ErrorEstimationMethod;
        FittingOptionsController.BootstrapIterations = snapshot.BootstrapIterations;
        FittingOptionsController.IncludeConcentrationVariance = snapshot.IncludeConcentrationVariance;
        FittingOptionsController.UnlockBootstrapParameters = snapshot.UnlockBootstrapParameters;
        FittingOptionsController.UseErrorWeightedFitting = snapshot.UseErrorWeightedFitting;
    }

    readonly record struct FittingOptionsSnapshot(
        SolverAlgorithm Algorithm,
        ErrorEstimationMethod ErrorEstimationMethod,
        int BootstrapIterations,
        bool IncludeConcentrationVariance,
        bool UnlockBootstrapParameters,
        bool UseErrorWeightedFitting);

    static async Task WaitForFitToFinish()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (ProcessingActivity.IsBusy && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.False(ProcessingActivity.IsBusy);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnlockParametersControlInitializesFromFittingOptions(bool enabled)
    {
        var previous = FittingOptionsController.UnlockBootstrapParameters;
        try
        {
            FittingOptionsController.UnlockBootstrapParameters = enabled;

            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();

                Assert.Equal(enabled, workspace.UnlockParametersCheck.IsChecked == true);
                Assert.Equal("Unlock parameters", workspace.UnlockParametersCheck.Content);
                Assert.Equal(
                    "Unlock locked parameters during the error estimation pass.",
                    ToolTip.GetTip(workspace.UnlockParametersCheck));
            });
        }
        finally
        {
            FittingOptionsController.UnlockBootstrapParameters = previous;
        }
    }

    [AvaloniaFact]
    public void ChangingUnlockParametersUpdatesModelCloneDefaults()
    {
        var previous = FittingOptionsController.UnlockBootstrapParameters;
        try
        {
            FittingOptionsController.UnlockBootstrapParameters = false;

            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();
                workspace.UnlockParametersCheck.IsChecked = true;

                Assert.True(FittingOptionsController.UnlockBootstrapParameters);
                Assert.True(ModelCloneOptions.DefaultOptions.UnlockBootstrapParameters);
            });
        }
        finally
        {
            FittingOptionsController.UnlockBootstrapParameters = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcentrationUncertaintyControlInitializesFromFittingOptions(bool enabled)
    {
        var previous = FittingOptionsController.IncludeConcentrationVariance;
        try
        {
            FittingOptionsController.IncludeConcentrationVariance = enabled;

            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();

                Assert.Equal(enabled, workspace.ConcentrationUncertaintyCheck.IsChecked == true);
                Assert.Equal("Concentration uncertainty", workspace.ConcentrationUncertaintyCheck.Content);
                Assert.Equal(
                    "Include cell and syringe concentration uncertainty during residual-bootstrap error estimation.",
                    ToolTip.GetTip(workspace.ConcentrationUncertaintyCheck));
            });
        }
        finally
        {
            FittingOptionsController.IncludeConcentrationVariance = previous;
        }
    }

    [AvaloniaFact]
    public void ChangingConcentrationUncertaintyUpdatesModelCloneDefaults()
    {
        var previous = FittingOptionsController.IncludeConcentrationVariance;
        try
        {
            FittingOptionsController.IncludeConcentrationVariance = false;

            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();
                workspace.ConcentrationUncertaintyCheck.IsChecked = true;

                Assert.True(FittingOptionsController.IncludeConcentrationVariance);
                Assert.True(ModelCloneOptions.DefaultOptions.IncludeConcentrationErrorsInBootstrap);
            });
        }
        finally
        {
            FittingOptionsController.IncludeConcentrationVariance = previous;
        }
    }

    [AvaloniaFact]
    public void LeaveOneOutDisablesBootstrapOnlyControlsWithoutChangingTheirValues()
    {
        var previousMethod = FittingOptionsController.ErrorEstimationMethod;
        var previousIterations = FittingOptionsController.BootstrapIterations;
        var previousConcentrationUncertainty = FittingOptionsController.IncludeConcentrationVariance;
        var previousUnlock = FittingOptionsController.UnlockBootstrapParameters;
        try
        {
            FittingOptionsController.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            FittingOptionsController.BootstrapIterations = 500;
            FittingOptionsController.IncludeConcentrationVariance = true;
            FittingOptionsController.UnlockBootstrapParameters = true;

            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();
                var originalIterations = workspace.BootstrapIterationsBoxForTesting.Text;
                var originalConcentrationUncertainty = workspace.ConcentrationUncertaintyCheck.IsChecked;
                var originalUnlock = workspace.UnlockParametersCheck.IsChecked;

                workspace.ErrorMethodComboForTesting.SelectedIndex = 2;

                Assert.False(workspace.BootstrapIterationsBoxForTesting.IsEnabled);
                Assert.False(workspace.ConcentrationUncertaintyCheck.IsEnabled);
                Assert.False(workspace.UnlockParametersCheck.IsEnabled);
                Assert.Equal(originalIterations, workspace.BootstrapIterationsBoxForTesting.Text);
                Assert.Equal(originalConcentrationUncertainty, workspace.ConcentrationUncertaintyCheck.IsChecked);
                Assert.Equal(originalUnlock, workspace.UnlockParametersCheck.IsChecked);

                workspace.ErrorMethodComboForTesting.SelectedIndex = 1;

                Assert.True(workspace.BootstrapIterationsBoxForTesting.IsEnabled);
                Assert.True(workspace.ConcentrationUncertaintyCheck.IsEnabled);
                Assert.True(workspace.UnlockParametersCheck.IsEnabled);
                Assert.Equal("500", workspace.BootstrapIterationsBoxForTesting.Text);
                Assert.True(workspace.ConcentrationUncertaintyCheck.IsChecked);
                Assert.True(workspace.UnlockParametersCheck.IsChecked);
            });

            Assert.Equal(500, FittingOptionsController.BootstrapIterations);
            Assert.True(FittingOptionsController.IncludeConcentrationVariance);
            Assert.True(FittingOptionsController.UnlockBootstrapParameters);
        }
        finally
        {
            FittingOptionsController.ErrorEstimationMethod = previousMethod;
            FittingOptionsController.BootstrapIterations = previousIterations;
            FittingOptionsController.IncludeConcentrationVariance = previousConcentrationUncertainty;
            FittingOptionsController.UnlockBootstrapParameters = previousUnlock;
        }
    }

    [AvaloniaFact]
    public void RestoreAnalysisDefaultsUsesPreferencesWithoutSavingThem()
    {
        var previousAlgorithm = AppSettings.DefaultSolverAlgorithm;
        var previousErrorMethod = AppSettings.DefaultErrorEstimationMethod;
        var previousBootstrapIterations = AppSettings.DefaultBootstrapIterations;
        var previousIncludeConcentrationErrors = AppSettings.IncludeConcentrationErrorsInBootstrap;
        var previousWeightedFitting = AppSettings.UseInjectionErrorWeightedFitting;
        var previousParameterLimitSetting = AppSettings.ParameterLimitSetting;
        var previousSingleResult = AppSettings.CreateSingleAnalysisResult;
        var previousGlobalResult = AppSettings.CreateGlobalAnalysisResult;
        var previousAutoOpen = AppSettings.AutoOpenNewAnalysisResult;
        var previousTolerance = AppSettings.OptimizerTolerance;
        var previousMaximumIterations = AppSettings.MaximumOptimizerIterations;
        var previousLiveUnlock = FittingOptionsController.UnlockBootstrapParameters;
        var settingsUpdated = false;
        EventHandler settingsUpdatedHandler = (_, _) => settingsUpdated = true;

        try
        {
            AppSettings.DefaultSolverAlgorithm = SolverAlgorithm.LevenbergMarquardt;
            AppSettings.DefaultErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut;
            AppSettings.DefaultBootstrapIterations = 500;
            AppSettings.IncludeConcentrationErrorsInBootstrap = true;
            AppSettings.UseInjectionErrorWeightedFitting = true;
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Extended;
            AppSettings.CreateSingleAnalysisResult = true;
            AppSettings.CreateGlobalAnalysisResult = false;
            AppSettings.AutoOpenNewAnalysisResult = false;
            AppSettings.OptimizerTolerance = 0.8;
            AppSettings.MaximumOptimizerIterations = 10_000;

            FittingOptionsController.UnlockBootstrapParameters = true;

            AppSettings.SettingsDidUpdate += settingsUpdatedHandler;
            Dispatcher.UIThread.Invoke(() =>
            {
                var workspace = new AnalysisWorkspaceControl();
                workspace.RestoreAnalysisDefaults();
            });

            Assert.Equal(SolverAlgorithm.LevenbergMarquardt, FittingOptionsController.Algorithm);
            Assert.Equal(ErrorEstimationMethod.LeaveOneOut, FittingOptionsController.ErrorEstimationMethod);
            Assert.Equal(500, FittingOptionsController.BootstrapIterations);
            Assert.True(FittingOptionsController.IncludeConcentrationVariance);
            Assert.True(FittingOptionsController.UseErrorWeightedFitting);
            Assert.False(FittingOptionsController.UnlockBootstrapParameters);
            Assert.False(settingsUpdated);

            Assert.Equal(ParameterLimitSetting.Extended, AppSettings.ParameterLimitSetting);
            Assert.True(AppSettings.CreateSingleAnalysisResult);
            Assert.False(AppSettings.CreateGlobalAnalysisResult);
            Assert.False(AppSettings.AutoOpenNewAnalysisResult);
            Assert.Equal(0.8, AppSettings.OptimizerTolerance, 12);
            Assert.Equal(10_000, AppSettings.MaximumOptimizerIterations);
        }
        finally
        {
            AppSettings.SettingsDidUpdate -= settingsUpdatedHandler;

            AppSettings.DefaultSolverAlgorithm = previousAlgorithm;
            AppSettings.DefaultErrorEstimationMethod = previousErrorMethod;
            AppSettings.DefaultBootstrapIterations = previousBootstrapIterations;
            AppSettings.IncludeConcentrationErrorsInBootstrap = previousIncludeConcentrationErrors;
            AppSettings.UseInjectionErrorWeightedFitting = previousWeightedFitting;
            AppSettings.ParameterLimitSetting = previousParameterLimitSetting;
            AppSettings.CreateSingleAnalysisResult = previousSingleResult;
            AppSettings.CreateGlobalAnalysisResult = previousGlobalResult;
            AppSettings.AutoOpenNewAnalysisResult = previousAutoOpen;
            AppSettings.OptimizerTolerance = previousTolerance;
            AppSettings.MaximumOptimizerIterations = previousMaximumIterations;
            FittingOptionsController.UnlockBootstrapParameters = previousLiveUnlock;
        }
    }

    [AvaloniaFact]
    public void SequentialSiteCountEditorOffersTwoThroughFourAsDropdownChoices()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var option = ExperimentAttribute.FromKey(AttributeKey.SequentialSiteCount);
            ExperimentAttribute? applied = null;
            var status = "";
            var row = ModelOptionRowBuilder.Build(
                AttributeKey.SequentialSiteCount,
                option,
                new Dictionary<AttributeKey, ExperimentAttribute>
                {
                    [AttributeKey.SequentialSiteCount] = option
                },
                (_, value) => applied = value,
                value => status = value);

            var border = Assert.IsType<Border>(row);
            var panel = Assert.IsType<StackPanel>(border.Child);
            var combo = Assert.IsType<ComboBox>(panel.Children[1]);
            var choices = combo.Items.OfType<ComboBoxItem>().ToList();
            Assert.Equal(new[] { 2, 3, 4 }, choices.Select(item => Assert.IsType<int>(item.Tag)));
            Assert.Equal(
                new[] { "2 binding sites", "3 binding sites", "4 binding sites" },
                choices.Select(item => Assert.IsType<string>(item.Content)));
            Assert.All(choices, item => Assert.True(item.Content?.ToString()?.Length < 20));

            combo.SelectedItem = choices[1];
            Assert.Equal(3, Assert.IsType<ExperimentAttribute>(applied).IntValue);
            Assert.Contains("3 binding sites", status);

            combo.SelectedItem = choices[2];
            Assert.Equal(4, Assert.IsType<ExperimentAttribute>(applied).IntValue);
        });
    }

    [AvaloniaFact]
    public void CompetitiveConcentrationRowUsesLigandLabelAndExplainsTotalCompetitor()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var option = ExperimentAttribute.FromKey(AttributeKey.PreboundLigandConc);
            option.OptionName = "[Ligand]";
            var status = "";
            var row = ModelOptionRowBuilder.Build(
                AttributeKey.PreboundLigandConc,
                option,
                new Dictionary<AttributeKey, ExperimentAttribute>
                {
                    [AttributeKey.PreboundLigandConc] = option
                },
                (_, _) => { },
                value => status = value);

            var border = Assert.IsType<Border>(row);
            var panel = Assert.IsType<StackPanel>(border.Child);
            var title = Assert.IsType<TextBlock>(panel.Children[0]);
            var titleText = string.Concat(
                title.Inlines?.OfType<Run>().Select(run => run.Text ?? string.Empty)
                ?? Enumerable.Empty<string>());
            Assert.Equal("[Ligand]", titleText);

            var tooltip = Assert.IsType<TextBlock>(panel.Children[2]);
            Assert.Contains("Total concentration of the competitor ligand", tooltip.Text);

            var editor = Assert.IsType<StackPanel>(panel.Children[1]);
            var fromAttributes = Assert.IsType<CheckBox>(editor.Children[0]);
            fromAttributes.IsChecked = true;
            Assert.Equal(
                "Total competitor concentration will be read from experiment attributes",
                status);
        });
    }

    [AvaloniaFact]
    public void ParameterRowShowsOutOfRangeAutomaticStartingValue()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var parameter = new Parameter(ParameterType.Offset, -50001);
            var row = AnalysisParameterRowBuilder.Build(
                parameter,
                (_, _, _) => { },
                _ => { },
                _ => { },
                () => false);

            var border = Assert.IsType<Border>(row);
            var panel = Assert.IsType<StackPanel>(border.Child);
            var warning = Assert.IsType<TextBlock>(panel.Children[^1]);
            Assert.Equal(InitialParameterLimitViolationDetector.StartingValueAdjustmentAdvisory, warning.Text);
        });
    }

    [AvaloniaFact]
    public void LockedParameterRowAcceptsValueOutsideFittingLimits()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var parameter = new Parameter(ParameterType.Offset, -50001);
            (double value, bool locked)? applied = null;
            var row = AnalysisParameterRowBuilder.Build(
                parameter,
                (_, value, locked) => applied = (value, locked),
                _ => { },
                _ => { },
                () => false);

            var lockCheck = Assert.Single(row.GetVisualDescendants().OfType<CheckBox>());
            lockCheck.IsChecked = true;

            Assert.NotNull(applied);
            Assert.Equal(-50001, applied.Value.value);
            Assert.True(applied.Value.locked);
        });
    }

    [AvaloniaFact]
    public void AcceptedOutOfRangeGuessCanBeUnlockedButNewFreeTypingIsRejected()
    {
        var previousLimits = AppSettings.ParameterLimitSetting;
        AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
        try
        {
        Dispatcher.UIThread.Invoke(() =>
        {
            var parameter = new Parameter(ParameterType.Offset, 60001.234, islocked: true);
            (double value, bool locked)? applied = null;
            var acceptedRow = AnalysisParameterRowBuilder.Build(
                parameter, (_, value, locked) => applied = (value, locked), _ => { }, _ => { }, () => false);
            var acceptedLock = Assert.Single(acceptedRow.GetVisualDescendants().OfType<CheckBox>());
            acceptedLock.IsChecked = false;
            Assert.Equal((60001.234d, false), applied);
            var acceptedInput = Assert.Single(acceptedRow.GetVisualDescendants().OfType<TextBox>());
            acceptedInput.Text = "40";
            acceptedInput.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent, acceptedInput));
            acceptedInput.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });
            Assert.Equal((40000d, false), applied);
            acceptedLock.IsChecked = true;
            Assert.Equal((40000d, true), applied);

            (double value, bool locked)? typedApplied = null;
            string status = null;
            var freeRow = AnalysisParameterRowBuilder.Build(
                new Parameter(ParameterType.Offset, 0),
                (_, value, locked) => typedApplied = (value, locked), _ => { }, value => status = value, () => false);
            var input = Assert.Single(freeRow.GetVisualDescendants().OfType<TextBox>());
            input.Text = "60";
            input.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent, input));
            input.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.Null(typedApplied);
            Assert.Contains("outside", status);
        });
        }
        finally { AppSettings.ParameterLimitSetting = previousLimits; }
    }

    [AvaloniaFact]
    public void ExpandingLimitsClearsAutomaticStartingValueWarning()
    {
        var previous = AppSettings.ParameterLimitSetting;
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
                var parameter = new Parameter(ParameterType.Offset, -50001);
                var standard = AnalysisParameterRowBuilder.Build(
                    parameter, (_, _, _) => { }, _ => { }, _ => { }, () => false);
                Assert.Contains(standard.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == InitialParameterLimitViolationDetector.StartingValueAdjustmentAdvisory);

                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Extended;
                parameter.RefreshLimits();
                var expanded = AnalysisParameterRowBuilder.Build(
                    parameter, (_, _, _) => { }, _ => { }, _ => { }, () => false);
                Assert.DoesNotContain(expanded.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == InitialParameterLimitViolationDetector.StartingValueAdjustmentAdvisory);
            });
        }
        finally
        {
            AppSettings.ParameterLimitSetting = previous;
        }
    }

    [AvaloniaFact]
    public void AnotherCalculationDisablesRunFitAndRejectsProgrammaticStarts()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var experiment = CreateReadyExperiment("busy-fit.itc");
        DataManager.AddData(experiment);
        var workspace = new AnalysisWorkspaceControl { Experiment = experiment };
        var window = new Window { Content = workspace };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var original = workspace.ContextForTesting;
            using (var lease = ProcessingActivity.TryBegin()!)
            {
                Dispatcher.UIThread.RunJobs();
                Assert.False(workspace.CanRunFit);
                Assert.False(workspace.CanStopFit);
                workspace.RunFit();
                Assert.Same(original, workspace.ContextForTesting);
                var button = workspace.GetVisualDescendants().OfType<Button>()
                    .Single(control => Equals(control.Content, "Run Fit"));
                Assert.Equal(FittingStatusPresentation.BusyTooltip, ToolTip.GetTip(button));
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(workspace.CanRunFit);
        }
        finally { window.Close(); DataManager.Clear(DataClearMode.ResetSession); }
    }

    [AvaloniaFact]
    public async Task ConsecutiveRunFitCallsKeepTheFirstAnalysisResultIndependent()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var experiment = CreateReadyExperiment("repeat-desktop.itc");
        foreach (var injection in experiment.Injections)
            injection.SetPeakArea(new FloatWithError(2000 * injection.InjectionMass, 1e-8));
        DataManager.AddData(experiment);
        var workspace = new AnalysisWorkspaceControl { Experiment = experiment };
        var window = new Window { Content = workspace };
        window.Show();
        try
        {
            workspace.ErrorMethodComboForTesting.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            workspace.ContextForTesting!.SingleModel.Parameters.Table[ParameterType.Offset].Update(2000);
            workspace.RunFit();
            var first = workspace.ContextForTesting!.SingleModel;
            await WaitForFit();
            Assert.NotNull(first.Solution);
            var retained = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = first }));
            var prediction = first.Evaluate(0);
            var reportedOffset = first.Solution.Parameters[ParameterType.Offset].Value;
            workspace.RunFit();
            var second = workspace.ContextForTesting!.SingleModel;
            Assert.NotSame(first, second);
            Assert.Same(second, experiment.Model);
            await WaitForFit();
            second.Parameters.Table[ParameterType.Offset].Update(3000);
            second.LossFunction(second.Parameters.GetFittedParameterArray(), false);
            Assert.Equal(prediction, retained.Model.Models[0].Evaluate(0));
            Assert.Equal(reportedOffset, first.Parameters.Table[ParameterType.Offset].Value);
        }
        finally
        {
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }

        async Task WaitForFit()
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while ((ProcessingActivity.IsBusy || workspace.CanStopFit) && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.False(ProcessingActivity.IsBusy);
            Assert.False(workspace.CanStopFit);
            Assert.True(workspace.CanRunFit);
        }
    }

    [AvaloniaFact]
    public async Task RunFitPreflightAllowsReusedOutOfRangeStartAfterCoreNormalization()
    {
        var previous = AppSettings.ParameterLimitSetting;
        var previousFittingOptions = CaptureFittingOptions();
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            DataManager.Clear(DataClearMode.ResetSession);
            var experiment = CreateReadyExperiment();
            var attachedModel = new OneSetOfSites(experiment);
            attachedModel.InitializeParameters(experiment);
            attachedModel.Parameters.Table[ParameterType.Offset].Update(50001);
            attachedModel.Solution = SolutionInterface.FromModel(
                attachedModel,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            experiment.Model = attachedModel;
            DataManager.AddData(experiment);

            var workspace = new AnalysisWorkspaceControl { Experiment = experiment };
            var window = new Window { Content = workspace };
            window.Show();
            try
            {
                Assert.True(workspace.CanRunFit);
                Assert.False(workspace.CanStopFit);
                workspace.RunFit();
                Assert.True(workspace.CanStopFit);
                Assert.True(workspace.GraphForTesting.IsReadOnly);
            }
            finally
            {
                workspace.StopFit();
                await WaitForFitToFinish();
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        }
        finally
        {
            AppSettings.ParameterLimitSetting = previous;
            RestoreFittingOptions(previousFittingOptions);
        }
    }

    [AvaloniaFact]
    public void SequentialSiteCountImmediatelyRebuildsParameterRows()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var experiment = CreateReadyExperiment();
            DataManager.AddData(experiment);

            var workspace = new AnalysisWorkspaceControl { Experiment = experiment };
            var window = new Window { Content = workspace };
            window.Show();

            try
            {
                var sequential = Assert.Single(
                    workspace.ModelComboForTesting.Items
                        .OfType<ComboBoxItem>(),
                    item => item.Tag is AnalysisModel model
                        && model == AnalysisModel.SequentialBindingSites);
                workspace.ModelComboForTesting.SelectedItem = sequential;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(5, workspace.ParameterPanelForTesting.Children.Count);
                Assert.Equal(2, Assert.IsType<SequentialBindingSites>(
                    workspace.ContextForTesting?.SingleModel).SiteCount);

                var optionBorder = Assert.IsType<Border>(
                    Assert.Single(workspace.OptionPanelForTesting.Children));
                var optionStack = Assert.IsType<StackPanel>(optionBorder.Child);
                var countCombo = Assert.IsType<ComboBox>(optionStack.Children[1]);
                countCombo.SelectedItem = Assert.Single(
                    countCombo.Items.OfType<ComboBoxItem>(),
                    item => item.Tag is int count && count == 4);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(9, workspace.ParameterPanelForTesting.Children.Count);
                var model = Assert.IsType<SequentialBindingSites>(
                    workspace.ContextForTesting?.SingleModel);
                Assert.Equal(4, model.SiteCount);
                Assert.Contains(ParameterType.Affinity4, model.Parameters.Table.Keys);
                Assert.Contains(ParameterType.Enthalpy4, model.Parameters.Table.Keys);
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaFact]
    public void SequentialGlobalModeRendersOneConstraintControlPerFamily()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var first = CreateReadyExperiment("constraint-first.itc", 20);
            var second = CreateReadyExperiment("constraint-second.itc", 30);
            DataManager.AddData(new[] { first, second });

            var workspace = new AnalysisWorkspaceControl { Experiment = first };
            var window = new Window { Content = workspace };
            window.Show();

            try
            {
                workspace.ModeSelectorForTesting.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                var sequential = Assert.Single(
                    workspace.ModelComboForTesting.Items
                        .OfType<ComboBoxItem>(),
                    item => item.Tag is AnalysisModel model
                        && model == AnalysisModel.SequentialBindingSites);
                workspace.ModelComboForTesting.SelectedItem = sequential;
                Dispatcher.UIThread.RunJobs();

                Assert.True(workspace.ContextForTesting?.IsMultiExperiment);
                var labels = workspace.ParameterPanelForTesting
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(text => text.Text)
                    .ToList();
                Assert.Single(labels, text => text == "Affinity");
                Assert.Single(labels, text => text == "Enthalpy");
                Assert.DoesNotContain("Affinity 2", labels);
                Assert.DoesNotContain("Enthalpy 2", labels);
                var constraintCombos = workspace.ParameterPanelForTesting.GetVisualDescendants()
                    .OfType<ComboBox>().Where(combo => combo.Items.OfType<ComboBoxItem>()
                        .Any(item => item.Tag is VariableConstraint)).ToList();
                var affinity = Assert.Single(constraintCombos, combo => combo.Items.OfType<ComboBoxItem>()
                    .Any(item => Equals(item.Content, "Shared Kd")));
                Assert.Equal(new[] { "Independent", "Shared ΔG", "Shared Kd", "Thermodynamically linked" }.OrderBy(text => text),
                    affinity.Items.OfType<ComboBoxItem>().Select(item => item.Content?.ToString()).OrderBy(text => text));
                Assert.All(affinity.Items.OfType<ComboBoxItem>(), item =>
                    Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(item)?.ToString())));
                var enthalpy = Assert.Single(constraintCombos, combo => combo.Items.OfType<ComboBoxItem>()
                    .Any(item => Equals(item.Content, "Temperature dependent")));
                Assert.Equal(new[] { "Independent", "Shared", "Temperature dependent" }.OrderBy(text => text),
                    enthalpy.Items.OfType<ComboBoxItem>().Select(item => item.Content?.ToString()).OrderBy(text => text));
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaFact]
    public void GlobalModeNavigationPreservesAttachedSolutions()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var first = CreateReadyExperiment("global-navigation-first.itc", 20);
            var second = CreateReadyExperiment("global-navigation-second.itc", 30);
            var firstAttachedModel = AttachFittedSolution(first);
            var secondAttachedModel = AttachFittedSolution(second);
            var firstAttachedSolution = first.Solution;
            var secondAttachedSolution = second.Solution;
            DataManager.AddData(new[] { first, second });

            var workspace = new AnalysisWorkspaceControl { Experiment = first };
            var window = new Window { Content = workspace };
            window.Show();

            try
            {
                workspace.ModeSelectorForTesting.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                workspace.Experiment = second;
                Dispatcher.UIThread.RunJobs();
                workspace.Experiment = first;
                Dispatcher.UIThread.RunJobs();

                Assert.Same(firstAttachedModel, first.Model);
                Assert.Same(firstAttachedSolution, first.Solution);
                Assert.Same(secondAttachedModel, second.Model);
                Assert.Same(secondAttachedSolution, second.Solution);
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaFact]
    public void HeatCapacityEditorLabelsEnergyPerMolePerKelvin()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var row = AnalysisParameterRowBuilder.Build(
                new Parameter(ParameterType.HeatCapacity4, -1000),
                (_, _, _) => { },
                _ => { },
                _ => { },
                () => false);
            var window = new Window { Content = row };
            window.Show();
            try
            {
                Assert.Contains(
                    row.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.EndsWith("/(mol·K)") == true);
            }
            finally
            {
                window.Close();
            }
        });
    }

    static Model AttachFittedSolution(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        experiment.Model = model;
        return model;
    }

    static ExperimentData CreateNullComparisonExperiment(
        string fileName, double delta, double rmsdMicrojoules, bool pooledGlobal = false)
    {
        var experiment = CreateReadyExperiment(fileName);
        var model = AttachFittedSolution(experiment);
        var comparison = new NullModelComparison
        {
            NullModelId = "offset",
            BindingFitSucceeded = true,
            NullFitSucceeded = true,
            DeltaAicc = delta,
            BindingInformationCriteria = RestoreCriteria(100, parameterCount: 1, residualRmsdMicrojoules: 1),
            NullInformationCriteria = RestoreCriteria(100 + delta, parameterCount: 2, residualRmsdMicrojoules: rmsdMicrojoules),
            Members = { new NullModelComparisonMember { ExperimentId = fileName } }
        };
        model.Solution!.NullComparison = comparison;
        if (pooledGlobal)
        {
            _ = GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model });
            model.Solution.NullComparison = null;
        }
        return experiment;
    }

    static FitInformationCriteria RestoreCriteria(double aicc, int parameterCount, double residualRmsdMicrojoules)
        => FitInformationCriteria.Restore(
            observationCount: 20,
            fittedParameterCount: parameterCount,
            likelihoodParameterCount: parameterCount + 1,
            likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance,
            minusTwoLogLikelihood: 90,
            aic: aicc - 4,
            aicc: aicc,
            isAicAvailable: true,
            isAiccAvailable: true,
            aicUnavailableReason: string.Empty,
            aiccUnavailableReason: string.Empty,
            rawResidualSumOfSquares: 0,
            residualRmsdMicrojoules: residualRmsdMicrojoules,
            standardizedResidualSumOfSquares: 0,
            logSigmaSquaredSum: 0);

    static ExperimentData CreateReadyExperiment(
        string fileName = "sequential-ui.itc",
        double temperature = 25)
    {
        var experiment = new ExperimentData(fileName)
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = temperature,
            TargetTemperature = temperature,
        };

        for (var index = 0; index < 5; index++)
        {
            var injection = new InjectionData(
                experiment,
                index,
                2e-6,
                experiment.SyringeConcentration * 2e-6,
                include: true)
            {
                ActualCellConcentration = experiment.CellConcentration * 0.99,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) * 5e-6 / (experiment.CellConcentration * 0.99),
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7));
            experiment.Injections.Add(injection);
        }

        return experiment;
    }
}
