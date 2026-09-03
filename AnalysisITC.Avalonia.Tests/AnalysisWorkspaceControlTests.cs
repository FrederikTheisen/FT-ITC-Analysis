using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisWorkspaceControlTests
{
    public AnalysisWorkspaceControlTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

                workspace.ModeComboForTesting.SelectedIndex = 1;
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

    [Theory]
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

    [Fact]
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

    [Theory]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void RestoreAnalysisDefaultsUsesPreferencesWithoutSavingThem()
    {
        var previousAlgorithm = AppSettings.DefaultSolverAlgorithm;
        var previousErrorMethod = AppSettings.DefaultErrorEstimationMethod;
        var previousBootstrapIterations = AppSettings.DefaultBootstrapIterations;
        var previousIncludeConcentrationErrors = AppSettings.IncludeConcentrationErrorsInBootstrap;
        var previousConcentrationVariance = AppSettings.ConcentrationAutoVariance;
        var previousAutoVarianceEnabled = AppSettings.IsConcentrationAutoVarianceEnabled;
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
            AppSettings.ConcentrationAutoVariance = 0.075;
            AppSettings.IsConcentrationAutoVarianceEnabled = true;
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
            Assert.Equal(0.075, FittingOptionsController.AutoConcentrationVariance, 12);
            Assert.True(FittingOptionsController.EnableAutoConcentrationVariance);
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
            AppSettings.ConcentrationAutoVariance = previousConcentrationVariance;
            AppSettings.IsConcentrationAutoVarianceEnabled = previousAutoVarianceEnabled;
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public void ParameterRowShowsOutOfRangeAutomaticStartingValue()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var parameter = new Parameter(ParameterType.Offset, -30001);
            var row = AnalysisParameterRowBuilder.Build(
                parameter,
                (_, _, _) => { },
                _ => { },
                _ => { },
                () => false);

            var border = Assert.IsType<Border>(row);
            var panel = Assert.IsType<StackPanel>(border.Child);
            var warning = Assert.IsType<TextBlock>(panel.Children[^1]);
            Assert.Contains("outside Standard Limits", warning.Text);
            Assert.Contains("restore defaults", warning.Text);
        });
    }

    [Fact]
    public void LockedParameterRowAcceptsValueOutsideFittingLimits()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var parameter = new Parameter(ParameterType.Offset, -30001);
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
            Assert.Equal(-30001, applied.Value.value);
            Assert.True(applied.Value.locked);
        });
    }

    [Fact]
    public void ExpandingLimitsClearsAutomaticStartingValueWarning()
    {
        var previous = AppSettings.ParameterLimitSetting;
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
                var parameter = new Parameter(ParameterType.Offset, -30001);
                var standard = AnalysisParameterRowBuilder.Build(
                    parameter, (_, _, _) => { }, _ => { }, _ => { }, () => false);
                Assert.Contains(standard.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("outside Standard Limits") == true);

                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Extended;
                parameter.RefreshLimits();
                var expanded = AnalysisParameterRowBuilder.Build(
                    parameter, (_, _, _) => { }, _ => { }, _ => { }, () => false);
                Assert.DoesNotContain(expanded.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("outside Expanded Limits") == true);
            });
        }
        finally
        {
            AppSettings.ParameterLimitSetting = previous;
        }
    }

    [Fact]
    public void RunFitPreflightBlocksReusedOutOfRangeStartWithoutEnteringFittingState()
    {
        var previous = AppSettings.ParameterLimitSetting;
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
                DataManager.Clear(DataClearMode.ResetSession);
                var experiment = CreateReadyExperiment();
                var attachedModel = new OneSetOfSites(experiment);
                attachedModel.InitializeParameters(experiment);
                attachedModel.Parameters.Table[ParameterType.Offset].Update(30001);
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
                    Assert.True(workspace.CanRunFit);
                    Assert.False(workspace.CanStopFit);
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
            AppSettings.ParameterLimitSetting = previous;
        }
    }

    [Fact]
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

    [Fact]
    public void SequentialGlobalModeRendersOneConstraintControlPerFamily()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var first = CreateReadyExperiment();
            var second = CreateReadyExperiment();
            DataManager.AddData(new[] { first, second });

            var workspace = new AnalysisWorkspaceControl { Experiment = first };
            var window = new Window { Content = workspace };
            window.Show();

            try
            {
                workspace.ModeComboForTesting.SelectedIndex = 1;
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
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [Fact]
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
                workspace.ModeComboForTesting.SelectedIndex = 1;
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

    [Fact]
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
