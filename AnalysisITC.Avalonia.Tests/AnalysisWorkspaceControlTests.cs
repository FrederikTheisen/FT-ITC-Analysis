using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
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

    static ExperimentData CreateReadyExperiment()
    {
        var experiment = new ExperimentData("sequential-ui.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
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
