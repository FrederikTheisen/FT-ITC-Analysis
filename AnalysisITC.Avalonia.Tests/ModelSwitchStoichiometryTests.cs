using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Details;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ModelSwitchStoichiometryTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisModel.OneSetOfSites, false)]
    [InlineData(AnalysisModel.OneSetOfSites, true)]
    [InlineData(AnalysisModel.CompetitiveBinding, false)]
    [InlineData(AnalysisModel.CompetitiveBinding, true)]
    public void DissociationDetailsEditThenModelSwitchShowsDimensionlessN(AnalysisModel target, bool global)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        DataManager.Clear(DataClearMode.ResetSession);
        var first = Data();
        var experiments = global ? new[] { first, Data() } : new[] { first };
        DataManager.AddData(experiments);
        var workspace = new AnalysisWorkspaceControl { Experiment = first };
        var window = new Window { Content = workspace };
        try
        {
            window.Show();
            SelectModel(AnalysisModel.Dissociation);
            if (global) workspace.ModeSelectorForTesting.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            foreach (var data in experiments)
            {
                var details = new ExperimentDetailsWindow(data);
                Field<TextBox>(details, "cellBox").Text = "35";
                Field<TextBox>(details, "syringeBox").Text = "700";
                details.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Apply"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(details.Applied);
                Assert.Equal(AnalysisXAxisType.TitrantConcentration, data.AxisType);
                Assert.Equal(data.Injections.Last().ActualTitrantConcentration, data.Injections.Last().Ratio);
            }
            SelectModel(target);
            if (global)
            {
                var sharing = workspace.ParameterPanelForTesting.GetLogicalDescendants().OfType<ComboBox>()
                    .Single(combo => combo.Parent is Grid grid && grid.Children.OfType<TextBlock>().Any(text => text.Text == "N-value"));
                sharing.SelectedItem = sharing.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, VariableConstraint.SameForAll));
                Dispatcher.UIThread.RunJobs();
            }

            // Five 2 µL shots into 200 µL, 700 µM syringe / 35 µM cell:
            // MicroCal's final molar ratio is (700/35) * .05 * 1.025 = 1.025.
            var parameters = workspace.ContextForTesting!.IsMultiExperiment
                ? workspace.ContextForTesting.GlobalModel.Models.Select(model => model.Parameters.Table[ParameterType.Nvalue1])
                : new[] { workspace.ContextForTesting.SingleModel.Parameters.Table[ParameterType.Nvalue1] };
            var boxes = workspace.ParameterPanelForTesting.GetLogicalDescendants().OfType<TextBox>()
                .Where(box => Equals(box.Tag, ParameterType.Nvalue1)).ToArray();
            Assert.NotEmpty(boxes);
            Console.WriteLine($"ITC-135 Avalonia {target}, global={global}: displayed N={string.Join(", ", boxes.Select(box => box.Text))}; expected .5125");
            foreach (var box in boxes) Assert.Equal(0.5125, double.Parse(box.Text!, CultureInfo.CurrentCulture), 4);
            foreach (var parameter in parameters) Assert.Equal(0.5125, parameter.Value, 12);
            Assert.All(experiments, data => Assert.Equal(data.Injections.Last().ActualTitrantConcentration,
                data.Injections.Last().Ratio));
        }
        finally
        {
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            CultureInfo.CurrentCulture = culture;
        }

        void SelectModel(AnalysisModel model)
        {
            workspace.ModelComboForTesting.SelectedItem = workspace.ModelComboForTesting.Items
                .OfType<ComboBoxItem>().Single(item => Equals(item.Tag, model));
            Dispatcher.UIThread.RunJobs();
        }
    }

    static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    static ExperimentData Data()
    {
        var data = new ExperimentData("model-switch.itc")
        {
            CellConcentration = new FloatWithError(0),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 200e-6,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        for (var i = 0; i < 5; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 420e-6 * 2e-6, true);
            injection.SetPeakArea(new FloatWithError(-2e-6 + i * 1e-7));
            data.Injections.Add(injection);
        }
        RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        var model = new Dissociation(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        data.Model = model;
        return data;
    }
}
