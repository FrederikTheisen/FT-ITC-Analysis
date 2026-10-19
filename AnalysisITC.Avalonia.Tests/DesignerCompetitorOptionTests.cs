using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class DesignerCompetitorOptionTests
{
    [AvaloniaTheory]
    [InlineData(ConcentrationUnit.µM, "25", "1", 25e-6, 6.0, "µM")]
    [InlineData(ConcentrationUnit.nM, "25000", "1000", 25e-6, 6.0, "nM")]
    [InlineData(ConcentrationUnit.mM, "0.025", "0.001", 25e-6, 6.0, "mM")]
    public void ConcentrationAndKdUseLabeledPreferredUnits(
        ConcentrationUnit unit, string concentration, string kd,
        double expectedConcentration, double expectedLogKa, string unitLabel)
    {
        WithWindow(unit, EnergyUnitFamily.Joules, window =>
        {
            var concRow = Row(window, AttributeKey.PreboundLigandConc);
            var kdRow = Row(window, AttributeKey.PreboundLigandAffinity);
            Assert.Contains(unitLabel, Labels(concRow));
            Assert.Contains("Kd, " + unitLabel, Labels(kdRow));
            Assert.NotNull(ToolTip.GetTip(Box(concRow)));
            Assert.NotNull(ToolTip.GetTip(Box(kdRow)));

            Commit(Box(concRow), concentration);
            Commit(Box(kdRow), "2");
            Commit(Box(kdRow), kd, enter: true);

            Assert.Equal(expectedConcentration, Value(window, AttributeKey.PreboundLigandConc), 14);
            Assert.Equal(expectedLogKa, Value(window, AttributeKey.PreboundLigandAffinity), 12);

            window.InjectionCountStepperForTesting.Value = 21;
            Assert.Equal(double.Parse(concentration, CultureInfo.InvariantCulture),
                double.Parse(Box(Row(window, AttributeKey.PreboundLigandConc)).Text!, CultureInfo.CurrentCulture), 10);
            Assert.Equal(double.Parse(kd, CultureInfo.InvariantCulture),
                double.Parse(Box(Row(window, AttributeKey.PreboundLigandAffinity)).Text!, CultureInfo.CurrentCulture), 10);
            var fit = window.CreateFitFactory();
            Assert.Equal(expectedConcentration, fit.GetExposedModelOptions()[AttributeKey.PreboundLigandConc].ParameterValue.Value, 14);
            Assert.Equal(expectedLogKa, fit.GetExposedModelOptions()[AttributeKey.PreboundLigandAffinity].ParameterValue.Value, 12);
        });
    }

    [AvaloniaTheory]
    [InlineData(EnergyUnitFamily.Joules, "-40", -40000, "kJ/mol")]
    [InlineData(EnergyUnitFamily.Calories, "-10", -41840, "kcal/mol")]
    public void EnthalpyUsesLabeledEnergyUnits(EnergyUnitFamily family, string text, double expected, string label)
    {
        WithWindow(ConcentrationUnit.µM, family, window =>
        {
            var row = Row(window, AttributeKey.PreboundLigandEnthalpy);
            Assert.Contains(label, Labels(row));
            Assert.NotNull(ToolTip.GetTip(Box(row)));
            Commit(Box(row), "-20");
            Commit(Box(row), text, enter: true);
            Assert.Equal(expected, Value(window, AttributeKey.PreboundLigandEnthalpy), 8);

            window.InjectionCountStepperForTesting.Value = 21;
            row = Row(window, AttributeKey.PreboundLigandEnthalpy);
            Assert.Contains(label, Labels(row));
            Assert.Equal(double.Parse(text, CultureInfo.InvariantCulture),
                double.Parse(Box(row).Text!, CultureInfo.CurrentCulture), 10);
            Assert.Equal(expected, window.CreateFitFactory().GetExposedModelOptions()[AttributeKey.PreboundLigandEnthalpy].ParameterValue.Value, 8);
        });
    }

    [AvaloniaTheory]
    [InlineData(EnergyUnitFamily.Joules, "-0.04", -40, "J/mol", -40)]
    [InlineData(EnergyUnitFamily.Calories, "-0.01", -41.84, "cal/mol", -10)]
    public void RebuiltSmallEnthalpiesUseTheNewlyDisplayedScale(
        EnergyUnitFamily family, string text, double expected, string rebuiltLabel, double rebuiltValue)
    {
        WithWindow(ConcentrationUnit.µM, family, window =>
        {
            Commit(Box(Row(window, AttributeKey.PreboundLigandEnthalpy)), text);
            Assert.Equal(expected, Value(window, AttributeKey.PreboundLigandEnthalpy), 10);
            window.InjectionCountStepperForTesting.Value = 21;
            var row = Row(window, AttributeKey.PreboundLigandEnthalpy);
            Assert.Contains(rebuiltLabel, Labels(row));
            Assert.Equal(rebuiltValue, double.Parse(Box(row).Text!, CultureInfo.CurrentCulture), 10);
            Commit(Box(row), "-5");
            Assert.Equal(family == EnergyUnitFamily.Joules ? -5 : -20.92,
                Value(window, AttributeKey.PreboundLigandEnthalpy), 10);
        });
    }

    [AvaloniaTheory]
    [InlineData(AttributeKey.PreboundLigandConc, "12.3456789012345")]
    [InlineData(AttributeKey.PreboundLigandAffinity, "0.987654321098765")]
    [InlineData(AttributeKey.PreboundLigandEnthalpy, "-43.210123456789")]
    public void UnchangedRoundedFieldsRetainTheFullSimulationValue(AttributeKey key, string text)
    {
        WithWindow(ConcentrationUnit.µM, EnergyUnitFamily.Joules, window =>
        {
            Commit(Box(Row(window, key)), text);
            var expected = Value(window, key);
            window.InjectionCountStepperForTesting.Value = 21;
            var box = Box(Row(window, key));
            box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(expected, Value(window, key));

            window.ModelComboForTesting.SelectedIndex = AnalysisModelAttribute.GetAll().IndexOf(AnalysisModel.OneSetOfSites);
            window.ModelComboForTesting.SelectedIndex = AnalysisModelAttribute.GetAll().IndexOf(AnalysisModel.CompetitiveBinding);
            Assert.Equal(expected, Value(window, key));
        });
    }

    [AvaloniaFact]
    public void OpenFieldsKeepTheirLabeledUnitsUntilRebuilt()
    {
        WithWindow(ConcentrationUnit.µM, EnergyUnitFamily.Joules, window =>
        {
            AppSettings.DefaultConcentrationUnit = ConcentrationUnit.nM;
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Calories;
            Commit(Box(Row(window, AttributeKey.PreboundLigandConc)), "20");
            Commit(Box(Row(window, AttributeKey.PreboundLigandAffinity)), "2");
            Commit(Box(Row(window, AttributeKey.PreboundLigandEnthalpy)), "-45");
            Assert.Equal(20e-6, Value(window, AttributeKey.PreboundLigandConc), 14);
            Assert.Equal(5.698970004336019, Value(window, AttributeKey.PreboundLigandAffinity), 12);
            Assert.Equal(-45000, Value(window, AttributeKey.PreboundLigandEnthalpy), 8);

            window.InjectionCountStepperForTesting.Value = 21;
            Assert.Contains("nM", Labels(Row(window, AttributeKey.PreboundLigandConc)));
            Assert.Contains("Kd, nM", Labels(Row(window, AttributeKey.PreboundLigandAffinity)));
            Assert.Contains("kcal/mol", Labels(Row(window, AttributeKey.PreboundLigandEnthalpy)));
            Assert.Equal(20000, double.Parse(Box(Row(window, AttributeKey.PreboundLigandConc)).Text!, CultureInfo.CurrentCulture), 8);
            Assert.Equal(2000, double.Parse(Box(Row(window, AttributeKey.PreboundLigandAffinity)).Text!, CultureInfo.CurrentCulture), 8);
        });
    }

    [AvaloniaTheory]
    [InlineData(AttributeKey.PreboundLigandAffinity, "0")]
    [InlineData(AttributeKey.PreboundLigandAffinity, "-1")]
    [InlineData(AttributeKey.PreboundLigandAffinity, "NaN")]
    [InlineData(AttributeKey.PreboundLigandAffinity, "Infinity")]
    [InlineData(AttributeKey.PreboundLigandConc, "-1")]
    [InlineData(AttributeKey.PreboundLigandConc, "NaN")]
    [InlineData(AttributeKey.PreboundLigandConc, "Infinity")]
    [InlineData(AttributeKey.PreboundLigandEnthalpy, "NaN")]
    [InlineData(AttributeKey.PreboundLigandEnthalpy, "Infinity")]
    [InlineData(AttributeKey.PreboundLigandEnthalpy, "1e308")]
    public void InvalidEntriesPreserveTheSimulation(AttributeKey key, string text)
    {
        WithWindow(ConcentrationUnit.µM, EnergyUnitFamily.Joules, window =>
        {
            var before = Value(window, key);
            var heats = window.GenerationFactoryForTesting!.Model.Data.Injections.Select(i => i.PeakArea.Value).ToArray();
            Commit(Box(Row(window, key)), text);
            Assert.Equal(before, Value(window, key));
            Assert.Equal(heats, window.GenerationFactoryForTesting!.Model.Data.Injections.Select(i => i.PeakArea.Value));
        });
    }

    [AvaloniaFact]
    public void DecimalCommaInputUsesTheDisplayedUnits()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            WithWindow(ConcentrationUnit.µM, EnergyUnitFamily.Joules, window =>
            {
                Commit(Box(Row(window, AttributeKey.PreboundLigandConc)), "12,5");
                Commit(Box(Row(window, AttributeKey.PreboundLigandAffinity)), "0,5");
                Commit(Box(Row(window, AttributeKey.PreboundLigandEnthalpy)), "-40,5");
                Assert.Equal(12.5e-6, Value(window, AttributeKey.PreboundLigandConc), 14);
                Assert.Equal(6.301029995663981, Value(window, AttributeKey.PreboundLigandAffinity), 12);
                Assert.Equal(-40500, Value(window, AttributeKey.PreboundLigandEnthalpy), 8);
            });
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [AvaloniaTheory]
    [InlineData(AttributeKey.PreboundLigandConc, "2", "0.2", 2e-6, 0.2e-6)]
    [InlineData(AttributeKey.PreboundLigandAffinity, "2", "0.2", 5.698970004336019, 0.04342944819032518)]
    [InlineData(AttributeKey.PreboundLigandEnthalpy, "-40", "2", -40000, 2000)]
    public void OrdinaryEditorStillConvertsValuesAndUncertainties(
        AttributeKey key, string text, string error, double expectedValue, double expectedSd)
    {
        WithWindow(ConcentrationUnit.µM, EnergyUnitFamily.Joules, window =>
        {
            var options = window.GenerationFactoryForTesting!.GetExposedModelOptions();
            ExperimentAttribute? applied = null;
            var row = ModelOptionRowBuilder.Build(key, options[key], options,
                (_, copy) => applied = copy, _ => { });
            var boxes = row.GetLogicalDescendants().OfType<TextBox>().ToArray();
            Assert.Equal(2, boxes.Length);
            Assert.Single(row.GetLogicalDescendants().OfType<CheckBox>());
            Commit(boxes[0], text);
            Commit(boxes[1], error, enter: true);
            Assert.NotNull(applied);
            Assert.Equal(expectedValue, applied.ParameterValue.Value, 12);
            Assert.Equal(expectedSd, applied.ParameterValue.SD, 12);
        });
    }

    static Control Row(ExperimentDesignerWindow window, AttributeKey key)
    {
        var keys = window.GenerationFactoryForTesting!.GetExposedModelOptions().Keys.ToList();
        return window.OptionPanelForTesting.Children[keys.IndexOf(key)];
    }

    static TextBox Box(Control row) => row.GetLogicalDescendants().OfType<TextBox>().Single();
    static string[] Labels(Control row) => row.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
    static double Value(ExperimentDesignerWindow window, AttributeKey key) =>
        window.GenerationFactoryForTesting!.GetExposedModelOptions()[key].ParameterValue.Value;

    static void Commit(TextBox box, string text, bool enter = false)
    {
        box.Text = text.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
        if (enter) box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        else box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
    }

    static void WithWindow(ConcentrationUnit unit, EnergyUnitFamily family, Action<ExperimentDesignerWindow> test)
    {
        var previousUnit = AppSettings.DefaultConcentrationUnit;
        var previousFamily = AppSettings.EnergyUnitFamily;
        ExperimentDesignerWindow? window = null;
        try
        {
            AppSettings.DefaultConcentrationUnit = unit;
            AppSettings.EnergyUnitFamily = family;
            window = new ExperimentDesignerWindow();
            window.ModelComboForTesting.SelectedIndex = AnalysisModelAttribute.GetAll().IndexOf(AnalysisModel.CompetitiveBinding);
            test(window);
        }
        finally
        {
            window?.Close();
            AppSettings.DefaultConcentrationUnit = previousUnit;
            AppSettings.EnergyUnitFamily = previousFamily;
        }
    }
}
