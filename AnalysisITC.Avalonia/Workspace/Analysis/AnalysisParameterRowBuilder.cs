using System;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Styling;
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

        public static string FormatValueAndLimits(
            ParameterType key,
            double value,
            double lower,
            double upper)
        {
            var parameter = new Parameter(key, value);
            var display = ParameterDisplay.From(parameter);
            var displayedLower = display.FormatParameterValue(lower);
            var displayedUpper = display.FormatParameterValue(upper);
            if (double.TryParse(displayedLower, NumberStyles.Float, CultureInfo.CurrentCulture, out var a)
                && double.TryParse(displayedUpper, NumberStyles.Float, CultureInfo.CurrentCulture, out var b)
                && a > b)
                (displayedLower, displayedUpper) = (displayedUpper, displayedLower);
            return $"{display.FormatParameterValue(value)} [{displayedLower}, {displayedUpper}]";
        }

        internal static (string Name, string Value) ReadOnlyPresentation(Parameter parameter)
        {
            var display = ParameterDisplay.From(parameter);
            var value = display.UnitLabel == "unitless"
                ? display.TextValue
                : $"{display.TextValue} {display.UnitLabel}";
            return (display.Title, value);
        }

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
            valueBox.Tag = parameter.Key;

            var lockCheck = WorkspaceControlBuilder.Check(
                "Locked",
                parameter.IsLocked,
                "Hold this parameter at the entered value while fitting.");
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

                var allowOutsideLimits = lockCheck.IsChecked == true || parameter.IsLocked;
                if (!allowOutsideLimits && !InitialParameterLimitViolationDetector.IsWithinLimits(parameter, parameterValue))
                {
                    setStatus($"{display.Title} is outside {InitialParameterLimitViolationDetector.ActivePolicyName} Limits {display.FormatLimits(parameter)}");
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

            var unitLabel = new TextBlock
            {
                Text = display.UnitLabel,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(unitLabel, TextBlock.ForegroundProperty, AppTheme.MutedText);
            var editor = WorkspaceControlBuilder.Row(valueBox, unitLabel);

            var panel = new StackPanel { Spacing = 1 };
            panel.Children.Add(header);
            panel.Children.Add(editor);

            var initialLimitViolation = InitialParameterLimitViolationDetector
                .Detect(parameter)
                .FirstOrDefault();
            if (initialLimitViolation != null)
            {
                var warning = new TextBlock
                {
                    Text = $"Starting value {display.FormatParameterValue(parameter.Value)} is outside {InitialParameterLimitViolationDetector.ActivePolicyName} Limits {display.FormatLimits(parameter)}. Edit it, restore defaults, or widen Limits.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 2, 4, 0)
                };
                AppTheme.Bind(warning, TextBlock.ForegroundProperty, AppTheme.StatusError);
                panel.Children.Add(warning);
            }

            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Child = panel
            };
            AppTheme.Bind(border, Border.BorderBrushProperty,
                initialLimitViolation == null ? AppTheme.SectionBorder : AppTheme.StatusError);
            return border;
        }

        public static Control BuildDesigner(
            Parameter parameter,
            Action<ParameterType, double> apply,
            Action<string> setStatus,
            Func<bool> isUpdating)
        {
            var display = ParameterDisplay.From(parameter);
            var valueBox = WorkspaceControlBuilder.TextBox(display.TextValue);
            valueBox.Width = ValueWidth;

            var slider = WorkspaceControlBuilder.Slider(0, 1, 0.01);
            slider.Width = double.NaN;
            slider.HorizontalAlignment = HorizontalAlignment.Stretch;
            slider.Margin = WorkspaceControlBuilder.ControlMargin;
            slider.Value = display.SliderPosition(parameter);

            var unitLabel = new TextBlock
            {
                Text = display.UnitLabel,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(unitLabel, TextBlock.ForegroundProperty, AppTheme.MutedText);

            var isUpdatingEditor = false;

            void ApplyTextValue()
            {
                if (isUpdating() || isUpdatingEditor) return;
                if (!double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var editorValue) ||
                    !display.TryToParameterValue(editorValue, out var parameterValue))
                {
                    setStatus($"Invalid value for {display.Title}");
                    return;
                }

                isUpdatingEditor = true;
                slider.Value = display.SliderPosition(parameter, parameterValue);
                isUpdatingEditor = false;
                apply(parameter.Key, parameterValue);
                setStatus($"{display.Title} updated");
            }

            valueBox.LostFocus += (_, _) => ApplyTextValue();
            valueBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) ApplyTextValue();
            };
            slider.ValueChanged += (_, e) =>
            {
                if (isUpdating() || isUpdatingEditor) return;

                var parameterValue = display.ParameterValueAtSliderPosition(parameter, e.NewValue);
                isUpdatingEditor = true;
                valueBox.Text = display.FormatParameterValue(parameterValue);
                isUpdatingEditor = false;
                apply(parameter.Key, parameterValue);
            };

            var panel = new StackPanel { Spacing = 2 };
            panel.Children.Add(BuildTitle(display));
            panel.Children.Add(WorkspaceControlBuilder.Row(valueBox, unitLabel));
            panel.Children.Add(slider);

            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 8),
                Child = panel
            };
            AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.SectionBorder);
            return border;
        }

        static Control BuildTitle(ParameterDisplay display)
        {
            var panel = new StackPanel
            {
                Spacing = 0,
                Margin = WorkspaceControlBuilder.ControlMargin
            };

            var title = new TextBlock
            {
                Text = display.Title,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            AppTheme.Bind(title, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            panel.Children.Add(title);

            if (!string.IsNullOrWhiteSpace(display.SymbolLabel))
            {
                var symbol = new TextBlock
                {
                    Text = display.SymbolLabel,
                    FontSize = 12,
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
                };
                AppTheme.Bind(symbol, TextBlock.ForegroundProperty, AppTheme.MutedText);
                panel.Children.Add(symbol);
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
            ParameterDisplay(
                string title,
                string symbolLabel,
                string unitLabel,
                string textValue,
                Func<double, double?> convertToParameter,
                Func<double, double> convertFromParameter,
                bool reverseSlider = false,
                bool logarithmicSlider = false)
            {
                Title = title;
                SymbolLabel = symbolLabel;
                UnitLabel = unitLabel;
                TextValue = textValue;
                this.convertToParameter = convertToParameter;
                this.convertFromParameter = convertFromParameter;
                this.reverseSlider = reverseSlider;
                this.logarithmicSlider = logarithmicSlider;
            }

            readonly Func<double, double?> convertToParameter;
            readonly Func<double, double> convertFromParameter;
            readonly bool reverseSlider;
            readonly bool logarithmicSlider;

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

            public string FormatParameterValue(double parameterValue) => Format(convertFromParameter(parameterValue));

            public string FormatLimits(Parameter parameter)
            {
                if (parameter?.Limits == null || parameter.Limits.Length < 2)
                    return "(unbounded)";

                var lower = convertFromParameter(parameter.Limits[0]);
                var upper = convertFromParameter(parameter.Limits[1]);
                return $"[{Format(Math.Min(lower, upper))}, {Format(Math.Max(lower, upper))}]";
            }

            public double SliderPosition(Parameter parameter, double? parameterValue = null)
            {
                var limits = SliderLimits(parameter);
                var value = parameterValue.GetValueOrDefault(parameter.Value);
                var position = logarithmicSlider
                    ? (Math.Log10(Math.Max(limits.Minimum, value)) - Math.Log10(limits.Minimum)) /
                        (Math.Log10(limits.Maximum) - Math.Log10(limits.Minimum))
                    : (value - limits.Minimum) / (limits.Maximum - limits.Minimum);
                position = Math.Max(0, Math.Min(1, position));
                return reverseSlider ? 1 - position : position;
            }

            public double ParameterValueAtSliderPosition(Parameter parameter, double position)
            {
                var limits = SliderLimits(parameter);
                position = Math.Max(0, Math.Min(1, position));
                if (reverseSlider) position = 1 - position;
                if (logarithmicSlider)
                {
                    var exponent = Math.Log10(limits.Minimum) +
                        position * (Math.Log10(limits.Maximum) - Math.Log10(limits.Minimum));
                    return Math.Pow(10, exponent);
                }

                return limits.Minimum + position * (limits.Maximum - limits.Minimum);
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
                        convertToParameter: value => value > 0 ? -Math.Log10(value / unit.GetMod()) : null,
                        convertFromParameter: value => Math.Pow(10, -value) * unit.GetMod(),
                        reverseSlider: true);
                }

                if (ParameterTypeAttribute.IsEnergyUnitParameter(parameter.Key))
                {
                    var unit = EnergyUnitResolver.Resolve(AppSettings.EnergyUnitFamily, parameter.Value);
                    var unitLabel = parent == ParameterType.HeatCapacity1
                        ? unit.GetUnit() + "/(mol·K)"
                        : unit.GetUnit() + "/mol";

                    return new ParameterDisplay(
                        title: EnergyTitle(parameter.Key),
                        symbolLabel: CleanSymbol(properties.SymbolName),
                        unitLabel: unitLabel,
                        textValue: Format(Energy.ConvertFromJoule(parameter.Value, unit)),
                        convertToParameter: value => Energy.ConvertToJoule(value, unit),
                        convertFromParameter: value => Energy.ConvertFromJoule(value, unit));
                }

                return new ParameterDisplay(
                    title: ScalarTitle(parameter.Key, properties.Name),
                    symbolLabel: CleanSymbol(properties.SymbolName),
                    unitLabel: "unitless",
                    textValue: Format(parameter.Value),
                    convertToParameter: value => value,
                    convertFromParameter: value => value,
                    logarithmicSlider: parameter.Key is ParameterType.IsomerizationEquilibriumConstant or ParameterType.IsomerizationRate);
            }

            static (double Minimum, double Maximum) SliderLimits(Parameter parameter)
            {
                if (parameter.Limits != null && parameter.Limits.Length >= 2 &&
                    double.IsFinite(parameter.Limits[0]) && double.IsFinite(parameter.Limits[1]) &&
                    parameter.Limits[1] > parameter.Limits[0])
                {
                    return (parameter.Limits[0], parameter.Limits[1]);
                }

                var span = Math.Max(1, Math.Abs(parameter.Value));
                return (parameter.Value - span, parameter.Value + span);
            }

            static string AffinityTitle(ParameterType key)
            {
                return ThermodynamicParameterSlots.TryResolve(key, out var slot, out _)
                    ? "Affinity" + (slot.Index == 1 ? string.Empty : " " + slot.Index)
                    : "Affinity";
            }

            static string EnergyTitle(ParameterType key)
            {
                if (ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family))
                {
                    var suffix = slot.Index == 1 ? string.Empty : " " + slot.Index;
                    return family switch
                    {
                        ThermodynamicParameterFamily.Enthalpy => "Enthalpy" + suffix,
                        ThermodynamicParameterFamily.Gibbs => "Gibbs" + suffix,
                        ThermodynamicParameterFamily.EntropyContribution => "Entropy contribution" + suffix,
                        ThermodynamicParameterFamily.HeatCapacity => "Heat capacity" + suffix,
                        ThermodynamicParameterFamily.Entropy => "Entropy" + suffix,
                        _ => key.GetProperties().Name,
                    };
                }

                return key.GetProperties().ParentType switch
                {
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
