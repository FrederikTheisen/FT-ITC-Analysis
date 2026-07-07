using System;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Analysis
{
    static class AnalysisParameterRowBuilder
    {
        const double ValueWidth = 132;

        public static Control Build(
            Parameter parameter,
            Action<ParameterType, double, bool> apply,
            Action<ParameterType> reset,
            Action<string> setStatus,
            Func<bool> isUpdating)
        {
            var display = ParameterDisplay.From(parameter);
            var valueBox = WorkspaceControlBuilder.TextBox(display.TextValue);
            valueBox.Width = ValueWidth;

            var lockCheck = WorkspaceControlBuilder.Check("Locked", parameter.IsLocked);
            lockCheck.MinWidth = 86;

            void ApplyParameter()
            {
                if (isUpdating()) return;

                if (string.IsNullOrWhiteSpace(valueBox.Text))
                {
                    reset(parameter.Key);
                    setStatus($"{display.Title} reset");
                    return;
                }

                if (!double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var editorValue))
                {
                    setStatus($"Invalid value for {display.Title}");
                    return;
                }

                if (!display.TryToParameterValue(editorValue, out var parameterValue))
                {
                    setStatus($"Invalid value for {display.Title}");
                    return;
                }

                apply(parameter.Key, parameterValue, lockCheck.IsChecked == true);
                setStatus($"{display.Title} updated");
            }

            valueBox.LostFocus += (_, _) => ApplyParameter();
            valueBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) ApplyParameter();
            };
            lockCheck.IsCheckedChanged += (_, _) => ApplyParameter();

            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };
            header.Children.Add(BuildTitle(display));
            Grid.SetColumn(lockCheck, 1);
            header.Children.Add(lockCheck);

            var editor = WorkspaceControlBuilder.Row(
                valueBox,
                new TextBlock
                {
                    Text = display.UnitLabel,
                    Foreground = WorkspaceControlBuilder.LabelBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });

            var panel = new StackPanel { Spacing = 1 };
            panel.Children.Add(header);
            panel.Children.Add(editor);

            return new Border
            {
                BorderBrush = WorkspaceControlBuilder.SectionBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Child = panel
            };
        }

        static Control BuildTitle(ParameterDisplay display)
        {
            var panel = new StackPanel
            {
                Spacing = 0,
                Margin = WorkspaceControlBuilder.ControlMargin
            };

            panel.Children.Add(new TextBlock
            {
                Text = display.Title,
                FontWeight = FontWeight.SemiBold,
                Foreground = WorkspaceControlBuilder.SectionHeaderBrush,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(display.SymbolLabel))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = display.SymbolLabel,
                    FontSize = 12,
                    FontStyle = FontStyle.Italic,
                    Foreground = WorkspaceControlBuilder.LabelBrush,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return panel;
        }

        static string StateText(Parameter parameter)
        {
            if (parameter.IsFitted) return "fitted";
            if (parameter.IsGloballyDetermined) return "global";
            return "locked";
        }

        sealed class ParameterDisplay
        {
            ParameterDisplay(string title, string symbolLabel, string unitLabel, string textValue, Func<double, double?> convertToParameter)
            {
                Title = title;
                SymbolLabel = symbolLabel;
                UnitLabel = unitLabel;
                TextValue = textValue;
                this.convertToParameter = convertToParameter;
            }

            readonly Func<double, double?> convertToParameter;

            public string Title { get; }
            public string SymbolLabel { get; }
            public string UnitLabel { get; }
            public string TextValue { get; }

            public bool TryToParameterValue(double editorValue, out double parameterValue)
            {
                var converted = convertToParameter(editorValue);
                parameterValue = converted ?? 0;
                return converted.HasValue && !double.IsNaN(parameterValue) && !double.IsInfinity(parameterValue);
            }

            public static ParameterDisplay From(Parameter parameter)
            {
                var properties = parameter.Key.GetProperties();
                var parent = properties.ParentType;

                if (parent == ParameterType.Affinity1)
                {
                    var unit = AppSettings.DefaultConcentrationUnit;
                    var kdM = Math.Pow(10, -parameter.Value);
                    var kd = kdM * unit.GetMod();

                    return new ParameterDisplay(
                        title: AffinityTitle(parameter.Key),
                        symbolLabel: "Kd",
                        unitLabel: unit.GetName(),
                        textValue: Format(kd),
                        convertToParameter: value => value > 0 ? -Math.Log10(value / unit.GetMod()) : null);
                }

                if (ParameterTypeAttribute.IsEnergyUnitParameter(parameter.Key))
                {
                    var unit = AppSettings.EnergyUnit;

                    return new ParameterDisplay(
                        title: EnergyTitle(parameter.Key),
                        symbolLabel: CleanSymbol(properties.SymbolName),
                        unitLabel: unit.GetUnit() + "/mol",
                        textValue: Format(Energy.ConvertFromJoule(parameter.Value, unit)),
                        convertToParameter: value => Energy.ConvertToJoule(value, unit));
                }

                return new ParameterDisplay(
                    title: ScalarTitle(parameter.Key, properties.Name),
                    symbolLabel: CleanSymbol(properties.SymbolName),
                    unitLabel: "unitless",
                    textValue: Format(parameter.Value),
                    convertToParameter: value => value);
            }

            static string AffinityTitle(ParameterType key)
            {
                return key == ParameterType.Affinity2 ? "Affinity 2" : "Affinity";
            }

            static string EnergyTitle(ParameterType key)
            {
                return key.GetProperties().ParentType switch
                {
                    ParameterType.Enthalpy1 => key == ParameterType.Enthalpy2 ? "Enthalpy 2" : "Enthalpy",
                    ParameterType.Gibbs1 => key == ParameterType.Gibbs2 ? "Gibbs 2" : "Gibbs",
                    ParameterType.EntropyContribution1 => key == ParameterType.EntropyContribution2 ? "Entropy contribution 2" : "Entropy contribution",
                    ParameterType.HeatCapacity1 => key == ParameterType.HeatCapacity2 ? "Heat capacity 2" : "Heat capacity",
                    ParameterType.Offset => "Offset",
                    _ => key.GetProperties().Name
                };
            }

            static string ScalarTitle(ParameterType key, string fallback)
            {
                return key switch
                {
                    ParameterType.Nvalue1 => "N-value",
                    ParameterType.Nvalue2 => "N-value 2",
                    ParameterType.IsomerizationEquilibriumConstant => "Equilibrium constant",
                    ParameterType.IsomerizationRate => "Isomerization rate",
                    ParameterType.CisIsomerPopulationPercentage => "Cis population",
                    _ => fallback
                };
            }

            static string CleanSymbol(string symbol)
            {
                return symbol
                    .Replace("*", "")
                    .Replace("{", "")
                    .Replace("}", "");
            }

            static string Format(double value) => value.ToString("G6", CultureInfo.CurrentCulture);
        }
    }
}
