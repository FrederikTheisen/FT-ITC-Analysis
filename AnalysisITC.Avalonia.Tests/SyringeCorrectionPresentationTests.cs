using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Results;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SyringeCorrectionPresentationTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnalysisFixedCountsAndSharingSurviveModeChanges(bool global, bool initiallyShared)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        var data = Data();
        DataManager.AddData(data);
        if (global) DataManager.AddData(Data());
        var workspace = new AnalysisWorkspaceControl { Experiment = data };
        var window = new Window { Content = workspace };
        try
        {
            window.Show();
            workspace.ModelComboForTesting.SelectedItem = workspace.ModelComboForTesting.Items
                .OfType<ComboBoxItem>().Single(item => Equals(item.Tag, AnalysisModel.TwoSetsOfSites));
            if (global) workspace.ModeSelectorForTesting.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var panel = workspace.OptionPanelForTesting;
            SetOptionCheck(panel, AttributeKey.UseSyringeActiveFraction, true);
            SetFixedCount(panel, AttributeKey.NumberOfSites1, 2);
            SetFixedCount(panel, AttributeKey.NumberOfSites2, 3);
            SetOptionCheck(panel, AttributeKey.UseSyringeActiveFraction, false);
            SetOptionCheck(panel, AttributeKey.LockDuplicateParameter, initiallyShared);
            var shared = initiallyShared;

            foreach (var syringe in new[] { false, true, false, true, false })
            {
                SetOptionCheck(panel, AttributeKey.UseSyringeActiveFraction, syringe);
                AssertOptionStates(panel, syringe, shared);
                AssertFixedCounts(panel, workspace.ContextForTesting!.ExposedModelOptions, shared);
                if (!syringe)
                {
                    shared = !shared;
                    SetOptionCheck(panel, AttributeKey.LockDuplicateParameter, shared);
                    AssertOptionStates(panel, syringe, shared);
                    AssertFixedCounts(panel, workspace.ContextForTesting!.ExposedModelOptions, shared);
                }
            }
        }
        finally
        {
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DesignerFixedCountsAndSharingSurviveModeChanges(bool initiallyShared)
    {
        var window = new ExperimentDesignerWindow();
        try
        {
            window.ModelComboForTesting.SelectedIndex = AnalysisModelAttribute.GetAll().IndexOf(AnalysisModel.TwoSetsOfSites);
            var panel = window.OptionPanelForTesting;
            SetOptionCheck(panel, AttributeKey.UseSyringeActiveFraction, true);
            SetFixedCount(panel, AttributeKey.NumberOfSites1, 2);
            SetFixedCount(panel, AttributeKey.NumberOfSites2, 3);
            SetOptionCheck(panel, AttributeKey.UseSyringeActiveFraction, false);
            SetOptionCheck(panel, AttributeKey.LockDuplicateParameter, initiallyShared);
            var shared = initiallyShared;

            foreach (var syringe in new[] { false, true, false, true, false })
            {
                SetOptionCheck(panel, AttributeKey.UseSyringeActiveFraction, syringe);
                AssertOptionStates(panel, syringe, shared);
                AssertFixedCounts(panel, window.GenerationFactoryForTesting!.GetExposedModelOptions(), shared);
                if (!syringe)
                {
                    shared = !shared;
                    SetOptionCheck(panel, AttributeKey.LockDuplicateParameter, shared);
                }
                window.InjectionCountStepperForTesting.Value += 1;
                AssertOptionStates(panel, syringe, shared);
                AssertFixedCounts(panel, window.GenerationFactoryForTesting!.GetExposedModelOptions(), shared);
                AssertFixedCounts(panel, window.CreateFitFactory().GetExposedModelOptions(), shared);
            }
        }
        finally { window.Close(); }
    }

    static Control OptionRow(StackPanel panel, AttributeKey key)
    {
        var title = ModelOptionCatalog.CreateOptions(AnalysisModel.TwoSetsOfSites)[key].GetDisplayName().Replace("^", "");
        return panel.Children.Single(row => Texts(row).Any(text =>
            (text.Text ?? string.Concat(text.Inlines?.OfType<Run>().Select(run => run.Text) ?? Array.Empty<string>())) == title));
    }

    static CheckBox OptionCheck(StackPanel panel, AttributeKey key) =>
        OptionRow(panel, key).GetLogicalDescendants().OfType<CheckBox>().Single();

    static ComboBox FixedCount(StackPanel panel, AttributeKey key) =>
        OptionRow(panel, key).GetLogicalDescendants().OfType<ComboBox>().Single();

    static void SetOptionCheck(StackPanel panel, AttributeKey key, bool value)
    {
        var check = OptionCheck(panel, key);
        Assert.True(check.IsEnabled);
        check.IsChecked = value;
        Dispatcher.UIThread.RunJobs();
    }

    static void SetFixedCount(StackPanel panel, AttributeKey key, double value)
    {
        var combo = FixedCount(panel, key);
        Assert.True(combo.IsEnabled);
        combo.SelectedIndex = StoichiometryOptions.Presets.ToList().FindIndex(option => option.Factor == value);
        Dispatcher.UIThread.RunJobs();
    }

    static void AssertOptionStates(StackPanel panel, bool syringe, bool shared)
    {
        Assert.Equal(syringe, FixedCount(panel, AttributeKey.NumberOfSites1).IsEnabled);
        Assert.Equal(syringe, FixedCount(panel, AttributeKey.NumberOfSites2).IsEnabled);
        Assert.Equal(!syringe, OptionCheck(panel, AttributeKey.LockDuplicateParameter).IsEnabled);
        Assert.Equal(shared, OptionCheck(panel, AttributeKey.LockDuplicateParameter).IsChecked);
        Assert.Equal(AttributeKey.LockDuplicateParameter.GetProperties().ToolTip,
            ToolTip.GetTip(OptionCheck(panel, AttributeKey.LockDuplicateParameter)));
    }

    static void AssertFixedCounts(StackPanel panel, System.Collections.Generic.IDictionary<AttributeKey, ExperimentAttribute> options, bool shared)
    {
        Assert.Equal(shared, options[AttributeKey.LockDuplicateParameter].BoolValue);
        foreach (var pair in new[] { (AttributeKey.NumberOfSites1, 2.0), (AttributeKey.NumberOfSites2, 3.0) })
        {
            Assert.Equal(pair.Item2, options[pair.Item1].DoubleValue);
            Assert.Equal(pair.Item2, StoichiometryOptions.Presets[FixedCount(panel, pair.Item1).SelectedIndex].Factor);
        }
    }

    [AvaloniaTheory]
    [InlineData(AnalysisModel.OneSetOfSites, false)]
    [InlineData(AnalysisModel.TwoSetsOfSites, false)]
    [InlineData(AnalysisModel.CompetitiveBinding, false)]
    [InlineData(AnalysisModel.OneSetOfSites, true)]
    [InlineData(AnalysisModel.TwoSetsOfSites, true)]
    [InlineData(AnalysisModel.CompetitiveBinding, true)]
    public void AnalysisToggleRefreshesLabelAndTooltipWithoutChangingOverride(AnalysisModel modelType, bool global)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var data = Data();
            DataManager.AddData(data);
            if (global) DataManager.AddData(Data());
            var workspace = new AnalysisWorkspaceControl { Experiment = data };
            var window = new Window { Content = workspace };
            try
            {
                window.Show();
                workspace.ModelComboForTesting.SelectedItem = workspace.ModelComboForTesting.Items
                    .OfType<ComboBoxItem>().Single(item => Equals(item.Tag, modelType));
                Dispatcher.UIThread.RunJobs();
                if (global)
                {
                    workspace.ModeSelectorForTesting.SelectedIndex = 1;
                    Dispatcher.UIThread.RunJobs();
                    var constraint = ConstraintRow(workspace, "N-value").Children.OfType<ComboBox>().Single();
                    constraint.SelectedItem = constraint.Items.OfType<ComboBoxItem>()
                        .Single(item => Equals(item.Tag, VariableConstraint.SameForAll));
                    Dispatcher.UIThread.RunJobs();
                }
                var row = AnalysisRow(workspace);
                AssertParameterLabel(row, false);
                var box = row.GetLogicalDescendants().OfType<TextBox>().Single();
                box.Text = .8.ToString(CultureInfo.CurrentCulture);
                box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                Dispatcher.UIThread.RunJobs();
                AnalysisRow(workspace).GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                foreach (var enabled in new[] { true, false, true })
                {
                    CorrectionCheck(workspace.OptionPanelForTesting).IsChecked = enabled;
                    Dispatcher.UIThread.RunJobs();
                    row = AnalysisRow(workspace);
                    AssertParameterLabel(row, enabled);
                    if (global)
                    {
                        var heading = ConstraintRow(workspace, enabled ? "Correction factor" : "N-value")
                            .Children.OfType<TextBlock>().Single();
                        Assert.Equal(enabled
                            ? "Scales delivered titrant concentration during model evaluation."
                            : "Apparent binding sites per macromolecule in the cell.", ToolTip.GetTip(heading));
                    }
                    var parameter = workspace.ContextForTesting!.ExposedParameters
                        .Single(value => value.Key == ParameterType.Nvalue1);
                    Assert.Equal(.8, parameter.Value);
                    Assert.True(parameter.IsLocked);
                }
            }
            finally
            {
                window.Close();
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaTheory]
    [InlineData(AnalysisModel.OneSetOfSites)]
    [InlineData(AnalysisModel.TwoSetsOfSites)]
    [InlineData(AnalysisModel.CompetitiveBinding)]
    public void DesignerToggleRefreshesSameParameterListAndKeepsEnteredValue(AnalysisModel modelType)
    {
        var window = new ExperimentDesignerWindow();
        try
        {
            window.ModelComboForTesting.SelectedIndex = AnalysisModelAttribute.GetAll().IndexOf(modelType);
            var row = window.ParameterPanelForTesting.Children[0];
            AssertParameterLabel(row, false);
            var box = row.GetLogicalDescendants().OfType<TextBox>().Single();
            box.Text = .8.ToString(CultureInfo.CurrentCulture);
            box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            foreach (var enabled in new[] { true, false, true })
            {
                CorrectionCheck(window.OptionPanelForTesting).IsChecked = enabled;
                row = window.ParameterPanelForTesting.Children[0];
                AssertParameterLabel(row, enabled);
                Assert.Equal(.8, window.GenerationFactoryForTesting!.GetExposedParameters()
                    .Single(value => value.Key == ParameterType.Nvalue1).Value);
                Assert.Equal(.8, window.CreateFitFactory().GetExposedParameters()
                    .Single(value => value.Key == ParameterType.Nvalue1).Value);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(AnalysisModel.OneSetOfSites, false)]
    [InlineData(AnalysisModel.TwoSetsOfSites, false)]
    [InlineData(AnalysisModel.CompetitiveBinding, false)]
    [InlineData(AnalysisModel.OneSetOfSites, true)]
    [InlineData(AnalysisModel.TwoSetsOfSites, true)]
    [InlineData(AnalysisModel.CompetitiveBinding, true)]
    public void LockedResultRowsUseEachResultsSavedOptions(AnalysisModel modelType, bool global)
    {
        var workspace = new AnalysisResultWorkspaceControl();
        foreach (var enabled in new[] { true, false, true })
        {
            workspace.Result = Result(modelType, enabled, global);
            var section = workspace.ModelPanelForTesting.Children.Single(child =>
                Texts(child).FirstOrDefault()?.Text == "Locked parameters");
            var title = Texts(section).Single(text => text.Text == (enabled ? "Correction factor" : "N-value"));
            var expected = AnalysisParameterLabelPresentation.Create(ParameterType.Nvalue1,
                workspace.Result.Solution.Model.ModelOptions);
            Assert.Equal(expected.ToolTip, ToolTip.GetTip(title));
            Assert.Contains(Texts(section), text => text.Text == .8.ToString("G6", CultureInfo.CurrentCulture));
            Assert.DoesNotContain(Texts(section), text => text.Text == (enabled ? "N-value" : "Correction factor"));
            if (global)
            {
                var constraints = workspace.ModelPanelForTesting.Children.Single(child =>
                    Texts(child).FirstOrDefault()?.Text == "Constraints");
                var constraintTitle = Texts(constraints).Single(text => text.Text == expected.Title);
                Assert.Equal(expected.ToolTip, ToolTip.GetTip(constraintTitle));
            }
        }
    }

    static void AssertParameterLabel(Control row, bool correction)
    {
        var expectedTitle = correction ? "Correction factor" : "N-value";
        var title = Texts(row).Single(text => text.Text == expectedTitle);
        Assert.Contains(Texts(row), text => correction ? text.Text == "α" : text.Text is "N" or "N1");
        var tooltip = correction
            ? "Scales delivered titrant concentration during model evaluation."
            : "Apparent binding sites per macromolecule in the cell.";
        Assert.Equal(tooltip, ToolTip.GetTip(title));
        Assert.Equal(tooltip, ToolTip.GetTip(row.GetLogicalDescendants().OfType<TextBox>().Single()));
        foreach (var slider in row.GetLogicalDescendants().OfType<Slider>())
            Assert.Equal(tooltip, ToolTip.GetTip(slider));
    }

    static Control AnalysisRow(AnalysisWorkspaceControl workspace) => workspace.ParameterPanelForTesting.Children
        .Single(row => row.GetLogicalDescendants().OfType<TextBox>()
            .Any(box => Equals(box.Tag, ParameterType.Nvalue1)));

    static Grid ConstraintRow(AnalysisWorkspaceControl workspace, string title) => workspace.ParameterPanelForTesting
        .GetLogicalDescendants().OfType<Grid>().Single(grid =>
            grid.Children.OfType<TextBlock>().Any(text => text.Text == title)
            && grid.Children.OfType<ComboBox>().Any());

    static CheckBox CorrectionCheck(StackPanel panel) => panel.Children
        .Single(row => Texts(row).Any(text => string.Equals(
            text.Text ?? string.Concat(text.Inlines?.OfType<Run>().Select(run => run.Text) ?? Array.Empty<string>()),
            "Use Syringe Correction", StringComparison.OrdinalIgnoreCase)))
        .GetLogicalDescendants().OfType<CheckBox>().Single();

    static System.Collections.Generic.IEnumerable<TextBlock> Texts(Control control) =>
        control.GetLogicalDescendants().OfType<TextBlock>();

    static AnalysisResult Result(AnalysisModel type, bool correction, bool global)
    {
        var first = Model(type, correction);
        if (!global)
            return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = first }));
        var models = new[] { first, Model(type, correction) }.ToList();
        var model = new GlobalModel(models)
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions,
        };
        model.Parameters.SetConstraintForParameter(ParameterType.Nvalue1, VariableConstraint.SameForAll);
        model.Parameters.AddorUpdateGlobalParameter(ParameterType.Nvalue1, .8, true);
        foreach (var member in models)
        {
            member.Parameters.Table[ParameterType.Nvalue1].SetGlobal(.8);
            model.Parameters.AddIndivdualParameter(member.Parameters);
        }
        var solution = new GlobalSolution(new GlobalSolver { Model = model },
            models.Select(member => member.Solution).ToList(), Convergence());
        model.Solution = solution;
        return new AnalysisResult(solution);
    }

    static Model Model(AnalysisModel type, bool correction)
    {
        var data = Data();
        Model model = type switch
        {
            AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
            AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
            _ => new OneSetOfSites(data),
        };
        model.InitializeParameters(data);
        model.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = correction;
        model.ApplyModelOptions();
        model.Parameters.Table[ParameterType.Nvalue1].Update(.8, lockpar: true);
        model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
        model.Solution = SolutionInterface.FromModel(model, Convergence());
        return model;
    }

    static SolverConvergence Convergence() => SolverConvergence.FromSnapshot(
        new SolverConvergenceSnapshot { Termination = SolverTermination.Converged });

    static ExperimentData Data()
    {
        var data = new ExperimentData("syringe-label.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        for (var index = 0; index < 5; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, data.SyringeConcentration * 2e-6, true)
            {
                ActualCellConcentration = data.CellConcentration * .99,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) * 5e-6 / (data.CellConcentration * .99),
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7));
            data.Injections.Add(injection);
        }
        return data;
    }
}
