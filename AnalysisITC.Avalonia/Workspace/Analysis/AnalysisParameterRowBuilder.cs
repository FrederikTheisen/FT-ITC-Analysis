using System;
using System.Collections.Generic;
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
using AnalysisITC.Core.Presentation;
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

        internal static (string Name, string Value, string? ToolTip) ReadOnlyPresentation(
            Parameter parameter,
            IDictionary<AttributeKey, ExperimentAttribute>? options = null)
        {
            var display = ParameterDisplay.From(parameter, options);
            var value = display.UnitLabel == "unitless"
                ? display.TextValue
                : $"{display.TextValue} {display.UnitLabel}";
            return (display.Title, value, display.ToolTip);
        }

        public static Control Build(
            Parameter parameter,
            Action<ParameterType, double, bool> apply,
            Action<ParameterType> reset,
            Action<string> setStatus,
            Func<bool> isUpdating,
            IDictionary<AttributeKey, ExperimentAttribute>? options = null)
        {
            var display = ParameterDisplay.From(parameter, options);
            var valueBox = WorkspaceControlBuilder.TextBox(display.TextValue);
            valueBox.Width = ValueWidth;
            valueBox.Tag = parameter.Key;
            ToolTip.SetTip(valueBox, display.ToolTip);

            var lockCheck = WorkspaceControlBuilder.Check(
                "Locked",
                parameter.IsLocked,
                "Hold this parameter at the entered value while fitting.");
            lockCheck.MinWidth = 86;
            var userEdited = false;
            var acceptedValue = parameter.Value;

            void ApplyParameter()
            {
                if (isUpdating()) return;

                if (string.IsNullOrWhiteSpace(valueBox.Text))
                {
                    userEdited = false;
                    reset(parameter.Key);
                    setStatus($"{display.Title} reset");
                    return;
                }

                var parameterValue = acceptedValue;
                if (userEdited
                    && (!double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var editorValue)
                        || !display.TryToParameterValue(editorValue, out parameterValue)))
                {
                    setStatus($"Invalid value for {display.Title}");
                    return;
                }

                if (userEdited && lockCheck.IsChecked != true
                    && !InitialParameterLimitViolationDetector.IsWithinLimits(parameter, parameterValue))
                {
                    setStatus($"{display.Title} is outside {InitialParameterLimitViolationDetector.ActivePolicyName} Limits {display.FormatLimits(parameter)}");
                    return;
                }

                acceptedValue = parameterValue;
                userEdited = false;
                apply(parameter.Key, parameterValue, lockCheck.IsChecked == true);
                setStatus($"{display.Title} updated");
            }

            valueBox.TextChanged += (_, _) => userEdited = true;
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
            ToolTip.SetTip(unitLabel, display.ToolTip);
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
                    Text = InitialParameterLimitViolationDetector.StartingValueAdjustmentAdvisory,
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
            Func<bool> isUpdating,
            IDictionary<AttributeKey, ExperimentAttribute>? options = null)
        {
            var display = ParameterDisplay.From(parameter, options);
            var valueBox = WorkspaceControlBuilder.TextBox(display.TextValue);
            valueBox.Width = ValueWidth;
            ToolTip.SetTip(valueBox, display.ToolTip);

            var slider = WorkspaceControlBuilder.Slider(0, 1, 0.01);
            var sliderLimits = display.DesignerSliderLimits(parameter, parameter.Value);
            slider.Width = double.NaN;
            slider.HorizontalAlignment = HorizontalAlignment.Stretch;
            slider.Margin = WorkspaceControlBuilder.ControlMargin;
            slider.Value = display.SliderPosition(parameter.Value, sliderLimits);
            ToolTip.SetTip(slider, display.ToolTip);

            var unitLabel = new TextBlock
            {
                Text = display.UnitLabel,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(unitLabel, TextBlock.ForegroundProperty, AppTheme.MutedText);
            ToolTip.SetTip(unitLabel, display.ToolTip);

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
                sliderLimits = display.DesignerSliderLimits(parameter, parameterValue);
                slider.Value = display.SliderPosition(parameterValue, sliderLimits);
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

                var parameterValue = display.ParameterValueAtSliderPosition(e.NewValue, sliderLimits);
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
            ToolTip.SetTip(title, display.ToolTip);
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
                ToolTip.SetTip(symbol, display.ToolTip);
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
                bool logarithmicSlider = false,
                string? toolTip = null)
            {
                Title = title;
                SymbolLabel = symbolLabel;
                UnitLabel = unitLabel;
                TextValue = textValue;
                ToolTip = toolTip;
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
            public string? ToolTip { get; }

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

            public double SliderPosition(double value, (double Minimum, double Maximum) limits)
            {
                var position = logarithmicSlider
                    ? (Math.Log10(Math.Max(limits.Minimum, value)) - Math.Log10(limits.Minimum)) /
                        (Math.Log10(limits.Maximum) - Math.Log10(limits.Minimum))
                    : (value - limits.Minimum) / (limits.Maximum - limits.Minimum);
                position = Math.Max(0, Math.Min(1, position));
                return reverseSlider ? 1 - position : position;
            }

            public double ParameterValueAtSliderPosition(double position, (double Minimum, double Maximum) limits)
            {
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

            public static ParameterDisplay From(
                Parameter parameter,
                IDictionary<AttributeKey, ExperimentAttribute>? options = null)
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

                var label = AnalysisParameterLabelPresentation.Create(parameter.Key, options,
                    includeSiteIndex: options?.ContainsKey(AttributeKey.NumberOfSites2) == true);
                return new ParameterDisplay(
                    title: ScalarTitle(parameter.Key, label.Title),
                    symbolLabel: CleanSymbol(label.Symbol),
                    unitLabel: "unitless",
                    textValue: Format(parameter.Value),
                    convertToParameter: value => value,
                    convertFromParameter: value => value,
                    logarithmicSlider: parent is ParameterType.Nvalue1
                        or ParameterType.IsomerizationEquilibriumConstant
                        or ParameterType.IsomerizationRate,
                    toolTip: label.ToolTip);
            }

            public (double Minimum, double Maximum) DesignerSliderLimits(Parameter parameter, double center)
            {
                // Keep the common designer controls on the same practical scales as macOS.
                // These are editing ranges, independent of the optimizer's parameter limits.
                switch (parameter.Key.GetProperties().ParentType)
                {
                    case ParameterType.Nvalue1: return (0.1, 10);
                    case ParameterType.Affinity1: return (3, 9);
                    case ParameterType.Offset: return (-30000, 30000);
                    case ParameterType.Enthalpy1: return (-100000, 100000);
                }

                double minimum;
                double maximum;
                if (logarithmicSlider)
                {
                    minimum = Math.Max(center / 10, 1e-12);
                    maximum = Math.Max(center * 10, minimum * 10);
                }
                else
                {
                    var halfSpan = Math.Max(Math.Abs(center), parameter.StepSize * 10);
                    minimum = center - halfSpan;
                    maximum = center + halfSpan;
                }

                if (parameter.Limits is { Length: >= 2 } bounds &&
                    double.IsFinite(bounds[0]) && double.IsFinite(bounds[1]) && bounds[1] > bounds[0])
                {
                    minimum = Math.Max(bounds[0], minimum);
                    maximum = Math.Min(bounds[1], maximum);
                }

                return maximum > minimum ? (minimum, maximum) : (center - 1, center + 1);
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
