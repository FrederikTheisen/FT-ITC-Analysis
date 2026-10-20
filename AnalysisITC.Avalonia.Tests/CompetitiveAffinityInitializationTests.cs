using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class CompetitiveAffinityInitializationTests
{
    [AvaloniaFact]
    public void SingleCompetitiveEditorStartsFromAttachedSavedKd()
    {
        InUi(() =>
        {
            var data = Data("single-affinity.itc");
            DataManager.AddData(data);
            var savedSolution = data.Solution!;
            var workspace = new AnalysisWorkspaceControl { Experiment = data };
            var window = new Window { Content = workspace };
            try
            {
                window.Show();
                SelectCompetitive(workspace);

                Assert.Equal("100", AffinityEditor(workspace).Text);
                Assert.Equal(7, data.Solution!.Parameters[ParameterType.Affinity1].Value);
                Assert.Equal(savedSolution, data.Solution);
            }
            finally { window.Close(); }
        });
    }

    [AvaloniaFact]
    public void GlobalCompetitiveEditorStartsFromAttachedSavedKdAfterSharingIsSelected()
    {
        InUi(() =>
        {
            var first = Data("global-affinity-1.itc");
            var second = Data("global-affinity-2.itc");
            DataManager.AddData(new[] { first, second });
            var firstSolution = first.Solution!;
            var secondSolution = second.Solution!;
            var workspace = new AnalysisWorkspaceControl { Experiment = first };
            var window = new Window { Content = workspace };
            try
            {
                window.Show();
                workspace.ModeSelectorForTesting.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                SelectCompetitive(workspace);
                var affinityConstraint = ConstraintCombo(workspace);
                affinityConstraint.SelectedItem = affinityConstraint.Items.OfType<ComboBoxItem>()
                    .Single(item => Equals(item.Tag, VariableConstraint.SameForAll));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("100", AffinityEditor(workspace).Text);
                Assert.Equal(7, first.Solution!.Parameters[ParameterType.Affinity1].Value);
                Assert.Equal(7, second.Solution!.Parameters[ParameterType.Affinity1].Value);
                Assert.Equal(firstSolution, first.Solution);
                Assert.Equal(secondSolution, second.Solution);
            }
            finally { window.Close(); }
        });
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitKdOverrideSurvivesWorkspaceRebuild(bool global, bool locked)
    {
        InUi(() =>
        {
            var first = Data("override-affinity-1.itc");
            var second = Data("override-affinity-2.itc");
            DataManager.AddData(global ? new[] { first, second } : new[] { first });
            var workspace = new AnalysisWorkspaceControl { Experiment = first };
            var window = new Window { Content = workspace };
            try
            {
                window.Show();
                if (global)
                {
                    workspace.ModeSelectorForTesting.SelectedIndex = 1;
                    Dispatcher.UIThread.RunJobs();
                }
                SelectCompetitive(workspace);
                if (global)
                {
                    var affinityConstraint = ConstraintCombo(workspace);
                    affinityConstraint.SelectedItem = affinityConstraint.Items.OfType<ComboBoxItem>()
                        .Single(item => Equals(item.Tag, VariableConstraint.SameForAll));
                    Dispatcher.UIThread.RunJobs();
                }

                if (locked)
                {
                    AffinityRow(workspace).GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
                    Dispatcher.UIThread.RunJobs();
                }
                var row = AffinityRow(workspace);
                var editor = row.GetLogicalDescendants().OfType<TextBox>().Single();
                editor.Text = "10";
                editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("10", AffinityEditor(workspace).Text);
                Assert.Equal(8, workspace.ContextForTesting!.ExposedParameters
                    .Single(parameter => parameter.Key == ParameterType.Affinity1).Value, 12);
                Assert.Equal(locked, AffinityRow(workspace).GetLogicalDescendants().OfType<CheckBox>()
                    .Single().IsChecked);
            }
            finally { window.Close(); }
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreDefaultsShowsOneNanomolarWithoutChangingAttachedSolution(bool global)
    {
        InUi(() =>
        {
            var first = Data("restore-affinity-1.itc");
            var second = Data("restore-affinity-2.itc");
            DataManager.AddData(global ? new[] { first, second } : new[] { first });
            var firstSolution = first.Solution!;
            var secondSolution = second.Solution!;
            var workspace = new AnalysisWorkspaceControl { Experiment = first };
            var window = new Window { Content = workspace };
            try
            {
                window.Show();
                if (global)
                {
                    workspace.ModeSelectorForTesting.SelectedIndex = 1;
                    Dispatcher.UIThread.RunJobs();
                }
                SelectCompetitive(workspace);
                if (global)
                {
                    var affinityConstraint = ConstraintCombo(workspace);
                    affinityConstraint.SelectedItem = affinityConstraint.Items.OfType<ComboBoxItem>()
                        .Single(item => Equals(item.Tag, VariableConstraint.SameForAll));
                    Dispatcher.UIThread.RunJobs();
                }

                var restore = window.GetLogicalDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, "Restore defaults"));
                restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                if (global)
                {
                    var affinityConstraint = ConstraintCombo(workspace);
                    affinityConstraint.SelectedItem = affinityConstraint.Items.OfType<ComboBoxItem>()
                        .Single(item => Equals(item.Tag, VariableConstraint.SameForAll));
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.Equal("1", AffinityEditor(workspace).Text);
                Assert.Equal(7, first.Solution!.Parameters[ParameterType.Affinity1].Value);
                Assert.Equal(firstSolution, first.Solution);
                if (global)
                {
                    Assert.Equal(7, second.Solution!.Parameters[ParameterType.Affinity1].Value);
                    Assert.Equal(secondSolution, second.Solution);
                }
            }
            finally { window.Close(); }
        });
    }

    static void InUi(Action test)
    {
        var culture = CultureInfo.CurrentCulture;
        var concentrationUnit = AppSettings.DefaultConcentrationUnit;
        var inputKd = AppSettings.InputAffinityAsDissociationConstant;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            AppSettings.DefaultConcentrationUnit = ConcentrationUnit.nM;
            AppSettings.InputAffinityAsDissociationConstant = true;
            Dispatcher.UIThread.Invoke(() =>
            {
                DataManager.Clear(DataClearMode.ResetSession);
                try { test(); }
                finally { DataManager.Clear(DataClearMode.ResetSession); }
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            AppSettings.DefaultConcentrationUnit = concentrationUnit;
            AppSettings.InputAffinityAsDissociationConstant = inputKd;
        }
    }

    static void SelectCompetitive(AnalysisWorkspaceControl workspace)
    {
        workspace.ModelComboForTesting.SelectedItem = workspace.ModelComboForTesting.Items
            .OfType<ComboBoxItem>().Single(item => Equals(item.Tag, AnalysisModel.CompetitiveBinding));
        Dispatcher.UIThread.RunJobs();
    }

    static ComboBox ConstraintCombo(AnalysisWorkspaceControl workspace) =>
        workspace.ParameterPanelForTesting.GetLogicalDescendants().OfType<ComboBox>()
            .Single(combo => combo.Items.OfType<ComboBoxItem>()
                .Any(item => Equals(item.Content, "Shared Kd")));

    static Control AffinityRow(AnalysisWorkspaceControl workspace) =>
        workspace.ParameterPanelForTesting.Children.Single(row => row.GetLogicalDescendants()
            .OfType<TextBox>().Any(box => Equals(box.Tag, ParameterType.Affinity1)));

    static TextBox AffinityEditor(AnalysisWorkspaceControl workspace) =>
        AffinityRow(workspace).GetLogicalDescendants().OfType<TextBox>().Single();

    static ExperimentData Data(string name)
    {
        var data = new ExperimentData(name)
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

        var model = new CompetitiveBinding(data);
        model.InitializeParameters(data);
        model.Parameters.Table[ParameterType.Affinity1].Update(7, false);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
        return data;
    }
}
