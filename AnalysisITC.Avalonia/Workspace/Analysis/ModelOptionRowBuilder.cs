using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Analysis
{
    static class ModelOptionRowBuilder
    {
        const double NumericWidth = 132;

        public static Control Build(
            AttributeKey key,
            ExperimentAttribute option,
            IDictionary<AttributeKey, ExperimentAttribute> allOptions,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            var enabled = IsOptionEnabled(key, allOptions);
            var editor = BuildEditor(key, option, allOptions, enabled, apply, setStatus);
            var panel = new StackPanel { Spacing = 3 };

            panel.Children.Add(new TextBlock
            {
                Text = CleanTitle(option.GetDisplayName()),
                FontWeight = FontWeight.SemiBold,
                Foreground = enabled ? WorkspaceControlBuilder.SectionHeaderBrush : WorkspaceControlBuilder.LabelBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = WorkspaceControlBuilder.ControlMargin
            });

            panel.Children.Add(editor);

            var tooltip = key.GetProperties().ToolTip;
            if (!string.IsNullOrWhiteSpace(tooltip) && tooltip != key.GetProperties().Type.ToString())
                panel.Children.Add(WorkspaceControlBuilder.Text(tooltip));

            return new Border
            {
                BorderBrush = WorkspaceControlBuilder.SectionBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Child = panel
            };
        }

        static Control BuildEditor(
            AttributeKey key,
            ExperimentAttribute option,
            IDictionary<AttributeKey, ExperimentAttribute> allOptions,
            bool enabled,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            return key switch
            {
                AttributeKey.NumberOfSites1 or AttributeKey.NumberOfSites2 =>
                    StoichiometryEditor(key, option, enabled, apply, setStatus),
                AttributeKey.PreboundLigandConc =>
                    ConcentrationEditor(key, option, apply, setStatus, allowFromAttributes: true),
                AttributeKey.PreboundLigandAffinity =>
                    AffinityEditor(key, option, apply, setStatus),
                AttributeKey.PreboundLigandEnthalpy =>
                    EnergyEditor(key, option, apply, setStatus),
                AttributeKey.Percentage =>
                    NumericParameterEditor(key, option, option.ParameterValue.Value * 100.0, "%", value => value / 100.0, apply, setStatus),
                _ => BuildDefaultEditor(key, option, allOptions, enabled, apply, setStatus)
            };
        }

        static Control BuildDefaultEditor(
            AttributeKey key,
            ExperimentAttribute option,
            IDictionary<AttributeKey, ExperimentAttribute> allOptions,
            bool enabled,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            return key.GetProperties().Type switch
            {
                ExperimentAttribute.AttributeType.Bool => BoolEditor(key, option, enabled, apply, setStatus),
                ExperimentAttribute.AttributeType.Int => IntEditor(key, option, enabled, apply, setStatus),
                ExperimentAttribute.AttributeType.Double => DoubleEditor(key, option, enabled, apply, setStatus),
                ExperimentAttribute.AttributeType.ParameterAffinity => AffinityEditor(key, option, apply, setStatus),
                ExperimentAttribute.AttributeType.ParameterConcentration => ConcentrationEditor(key, option, apply, setStatus, allowFromAttributes: false),
                ExperimentAttribute.AttributeType.Parameter => NumericParameterEditor(key, option, option.ParameterValue.Value, "", value => value, apply, setStatus),
                ExperimentAttribute.AttributeType.String => StringEditor(key, option, enabled, apply, setStatus),
                _ => WorkspaceControlBuilder.Text($"Read-only: {option.GetDisplayValue()}")
            };
        }

        static Control StoichiometryEditor(
            AttributeKey key,
            ExperimentAttribute option,
            bool enabled,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            var combo = WorkspaceControlBuilder.Combo(WorkspaceControlBuilder.InspectorFieldWidth);
            var stoichiometries = StoichiometryOptions.Presets.ToList();

            foreach (var item in stoichiometries)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Tag = item,
                    Content = item.Title
                });
            }

            var currentFactor = option.DoubleValue > 0 ? option.DoubleValue : Math.Max(1, option.IntValue);
            var selected = StoichiometryOptions.GetClosest(currentFactor);
            var selectedIndex = stoichiometries.FindIndex(item => item.Preset == selected.Preset);
            combo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            combo.IsEnabled = enabled;

            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem item || item.Tag is not StoichiometryOption stoichiometry) return;

                var copy = option.Copy();
                copy.DoubleValue = stoichiometry.Factor;
                apply(key, copy);
                setStatus($"{CleanTitle(option.GetDisplayName())}: {stoichiometry.Title}");
            };

            return combo;
        }

        static Control BoolEditor(
            AttributeKey key,
            ExperimentAttribute option,
            bool enabled,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            var check = WorkspaceControlBuilder.Check("Enabled", option.BoolValue);
            check.IsEnabled = enabled;
            check.IsCheckedChanged += (_, _) =>
            {
                var copy = option.Copy();
                copy.BoolValue = check.IsChecked == true;
                apply(key, copy);
                setStatus($"{CleanTitle(option.GetDisplayName())} {(copy.BoolValue ? "enabled" : "disabled")}");
            };

            return check;
        }

        static Control IntEditor(
            AttributeKey key,
            ExperimentAttribute option,
            bool enabled,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            return NumericTextEditor(
                key,
                option,
                option.IntValue.ToString(CultureInfo.CurrentCulture),
                "",
                enabled,
                value =>
                {
                    var copy = option.Copy();
                    copy.IntValue = (int)Math.Round(value);
                    return copy;
                },
                value => Math.Abs(value - Math.Round(value)) < 1e-9,
                apply,
                setStatus);
        }

        static Control DoubleEditor(
            AttributeKey key,
            ExperimentAttribute option,
            bool enabled,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            return NumericTextEditor(
                key,
                option,
                Format(option.DoubleValue),
                "",
                enabled,
                value =>
                {
                    var copy = option.Copy();
                    copy.DoubleValue = value;
                    return copy;
                },
                _ => true,
                apply,
                setStatus);
        }

        static Control NumericParameterEditor(
            AttributeKey key,
            ExperimentAttribute option,
            double displayValue,
            string unitLabel,
            Func<double, double> toStoredValue,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            return NumericTextEditor(
                key,
                option,
                Format(displayValue),
                unitLabel,
                true,
                value =>
                {
                    var copy = option.Copy();
                    copy.ParameterValue = WithExistingError(option.ParameterValue, toStoredValue(value));
                    return copy;
                },
                value => !double.IsNaN(toStoredValue(value)) && !double.IsInfinity(toStoredValue(value)),
                apply,
                setStatus);
        }

        static Control ConcentrationEditor(
            AttributeKey key,
            ExperimentAttribute option,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus,
            bool allowFromAttributes)
        {
            var unit = AppSettings.DefaultConcentrationUnit;
            var manualInputEnabled = !allowFromAttributes || !option.BoolValue;
            var textEditor = NumericTextEditor(
                key,
                option,
                Format(option.ParameterValue.Value * unit.GetMod()),
                unit.GetName(),
                true,
                value =>
                {
                    var copy = option.Copy();
                    copy.ParameterValue = WithExistingError(option.ParameterValue, value / unit.GetMod());
                    return copy;
                },
                value => value >= 0,
                apply,
                setStatus,
                canApply: () => manualInputEnabled);
            textEditor.IsEnabled = manualInputEnabled;

            if (!allowFromAttributes)
                return textEditor;

            var fromAttributes = WorkspaceControlBuilder.Check("From attributes", option.BoolValue);
            fromAttributes.IsCheckedChanged += (_, _) =>
            {
                var copy = option.Copy();
                copy.BoolValue = fromAttributes.IsChecked == true;
                manualInputEnabled = !copy.BoolValue;
                textEditor.IsEnabled = manualInputEnabled;
                apply(key, copy);
                setStatus(copy.BoolValue
                    ? "Ligand concentration will be read from experiment attributes"
                    : "Ligand concentration uses the entered value");
            };

            var panel = WorkspaceControlBuilder.VerticalGroup();
            panel.Children.Add(fromAttributes);
            panel.Children.Add(textEditor);
            return panel;
        }

        static Control AffinityEditor(
            AttributeKey key,
            ExperimentAttribute option,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            var unit = AppSettings.DefaultConcentrationUnit;
            var kd = Math.Pow(10.0, -option.ParameterValue.Value) * unit.GetMod();

            return NumericTextEditor(
                key,
                option,
                Format(kd),
                $"Kd, {unit.GetName()}",
                true,
                value =>
                {
                    var copy = option.Copy();
                    copy.ParameterValue = WithExistingError(option.ParameterValue, -Math.Log10(value / unit.GetMod()));
                    return copy;
                },
                value => value > 0,
                apply,
                setStatus);
        }

        static Control EnergyEditor(
            AttributeKey key,
            ExperimentAttribute option,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            var unit = AppSettings.EnergyUnit;

            return NumericTextEditor(
                key,
                option,
                Format(Energy.ConvertFromJoule(option.ParameterValue.Value, unit)),
                unit.GetUnit() + "/mol",
                true,
                value =>
                {
                    var copy = option.Copy();
                    copy.ParameterValue = WithExistingError(option.ParameterValue, Energy.ConvertToJoule(value, unit));
                    return copy;
                },
                _ => true,
                apply,
                setStatus);
        }

        static Control StringEditor(
            AttributeKey key,
            ExperimentAttribute option,
            bool enabled,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus)
        {
            var textBox = WorkspaceControlBuilder.TextBox(option.StringValue ?? "");
            textBox.IsEnabled = enabled;
            textBox.LostFocus += (_, _) =>
            {
                var copy = option.Copy();
                copy.StringValue = textBox.Text ?? "";
                apply(key, copy);
                setStatus($"{CleanTitle(option.GetDisplayName())} updated");
            };

            return textBox;
        }

        static Control NumericTextEditor(
            AttributeKey key,
            ExperimentAttribute option,
            string text,
            string unitLabel,
            bool enabled,
            Func<double, ExperimentAttribute> copyWithValue,
            Func<double, bool> validate,
            Action<AttributeKey, ExperimentAttribute> apply,
            Action<string> setStatus,
            Func<bool>? canApply = null)
        {
            var textBox = WorkspaceControlBuilder.TextBox(text);
            textBox.Width = NumericWidth;
            textBox.IsEnabled = enabled;

            void ApplyValue()
            {
                if (!textBox.IsEnabled) return;
                if (canApply?.Invoke() == false) return;

                if (!TryParseDouble(textBox.Text, out var value) || !validate(value))
                {
                    setStatus($"Invalid value for {CleanTitle(option.GetDisplayName())}");
                    return;
                }

                var copy = copyWithValue(value);
                apply(key, copy);
                setStatus($"{CleanTitle(option.GetDisplayName())} updated");
            }

            textBox.LostFocus += (_, _) => ApplyValue();
            textBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) ApplyValue();
            };

            if (string.IsNullOrWhiteSpace(unitLabel))
                return textBox;

            return WorkspaceControlBuilder.Row(
                textBox,
                new TextBlock
                {
                    Text = unitLabel,
                    Foreground = WorkspaceControlBuilder.LabelBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                });
        }

        static bool IsOptionEnabled(AttributeKey key, IDictionary<AttributeKey, ExperimentAttribute> allOptions)
        {
            var useSyringeCorrection = allOptions.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var syringeOption)
                && syringeOption.BoolValue;
            var lockDuplicateParameter = allOptions.TryGetValue(AttributeKey.LockDuplicateParameter, out var duplicateOption)
                && duplicateOption.BoolValue;

            return key switch
            {
                AttributeKey.NumberOfSites1 => useSyringeCorrection,
                AttributeKey.NumberOfSites2 => useSyringeCorrection && !lockDuplicateParameter,
                _ => true
            };
        }

        static FloatWithError WithExistingError(FloatWithError current, double value)
        {
            return new FloatWithError(value, current.SD);
        }

        static bool TryParseDouble(string? text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static string CleanTitle(string title)
        {
            return (title ?? "")
                .Replace("*", "")
                .Replace("^", "")
                .Replace("{", "")
                .Replace("}", "");
        }

        static string Format(double value) => value.ToString("G6", CultureInfo.CurrentCulture);
    }
}
