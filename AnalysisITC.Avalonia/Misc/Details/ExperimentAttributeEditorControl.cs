using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Avalonia.Styling;
using BufferKind = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Avalonia.Details
{
    public sealed class ExperimentAttributeEditorControl : UserControl
    {
        const double ControlHeight = 28;
        readonly ExperimentAttribute attribute;
        readonly Func<ExperimentAttribute, AttributeKey, bool> canUseKey;
        readonly ComboBox keyCombo = new ComboBox
        {
            Width = 160,
            Height = ControlHeight,
            MinHeight = ControlHeight,
            MaxHeight = ControlHeight,
            FontSize = 13,
            Padding = new Thickness(7, 0),
            Margin = new Thickness(0, 0, 4, 3)
        };
        readonly WrapPanel editorPanel = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        ComboBox? enumCombo;
        ComboBox? referenceCombo;
        ComboBox? methodCombo;
        TextBox? valueBox;
        TextBox? errorBox;
        TextBox? doubleBox;
        TextBox? stringBox;
        CheckBox? boolBox;

        public event EventHandler? RemoveRequested;
        public event EventHandler? KeyChanged;
        public event EventHandler<BufferKind>? SpecialBufferSelected;

        public ExperimentAttributeEditorControl(ExperimentAttribute attribute, Func<ExperimentAttribute, AttributeKey, bool> canUseKey)
        {
            this.attribute = attribute;
            this.canUseKey = canUseKey;

            Build();
        }

        public ExperimentAttribute Attribute => attribute;

        public bool TryApply(ExperimentData experiment, out string error)
        {
            error = "";

            if (attribute.Key == AttributeKey.Null)
                return true;

            try
            {
                switch (attribute.Key)
                {
                    case AttributeKey.Buffer:
                        return ApplyBuffer(out error);
                    case AttributeKey.Salt:
                        return ApplySalt(out error);
                    case AttributeKey.IonicStrength:
                        return ApplyConcentration(out error);
                    case AttributeKey.BufferSubtraction:
                        return ApplyBufferSubtraction(experiment, out error);
                    case AttributeKey.Species:
                        return ApplySpecies(out error);
                    default:
                        return ApplyGeneric(out error);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        void Build()
        {
            var removeIcon = new Path
            {
                Data = Geometry.Parse("M6,6 L14,14 M14,6 L6,14"),
                StrokeThickness = 1,
                StrokeLineCap = PenLineCap.Round,
                Width = 12,
                Height = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform
            };
            AppTheme.Bind(removeIcon, Shape.StrokeProperty, AppTheme.SecondaryText);

            var removeButton = new Button
            {
                Content = removeIcon,
                Width = ControlHeight,
                Height = ControlHeight,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 3),
            };
            removeButton.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);

            keyCombo.ItemsSource = BuildKeyChoices();
            keyCombo.SelectedItem = ((IEnumerable<Choice<AttributeKey>>)keyCombo.ItemsSource)
                .FirstOrDefault(choice => choice.Value == attribute.Key);
            keyCombo.SelectionChanged += (_, _) =>
            {
                if (keyCombo.SelectedItem is not Choice<AttributeKey> choice) return;
                attribute.UpdateOptionKey(choice.Value);
                BuildEditor();
                KeyChanged?.Invoke(this, EventArgs.Empty);
            };

            var root = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
                ColumnSpacing = 4
            };

            root.Children.Add(removeButton);
            Grid.SetColumn(keyCombo, 1);
            root.Children.Add(keyCombo);
            Grid.SetColumn(editorPanel, 2);
            root.Children.Add(editorPanel);

            var content = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 5),
                Margin = new Thickness(0, 0, 0, 4),
                Child = root
            };
            AppTheme.Bind(content, Border.BackgroundProperty, AppTheme.TableAlternateRow);
            AppTheme.Bind(content, Border.BorderBrushProperty, AppTheme.SectionBorder);
            Content = content;
            BuildEditor();
        }

        List<Choice<AttributeKey>> BuildKeyChoices()
        {
            var keys = new[] { AttributeKey.Null }
                .Concat(ExperimentAttribute.AvailableExperimentAttributes)
                .Where(key => key == attribute.Key || canUseKey(attribute, key))
                .Distinct()
                .ToList();

            return keys.Select(key => new Choice<AttributeKey>(key, key == AttributeKey.Null ? "Select attribute" : key.GetProperties().Name)).ToList();
        }

        void BuildEditor()
        {
            editorPanel.Children.Clear();
            enumCombo = null;
            referenceCombo = null;
            methodCombo = null;
            valueBox = null;
            errorBox = null;
            doubleBox = null;
            stringBox = null;
            boolBox = null;

            switch (attribute.Key)
            {
                case AttributeKey.Buffer:
                    AddBufferEditor();
                    break;
                case AttributeKey.Salt:
                    AddSaltEditor();
                    break;
                case AttributeKey.IonicStrength:
                    AddValueWithUnit("mM", attribute.ParameterValue.Value * 1000, attribute.ParameterValue.SD * 1000, includeError: false);
                    break;
                case AttributeKey.BufferSubtraction:
                    AddReferenceEditor();
                    break;
                case AttributeKey.Species:
                    AddSpeciesEditor();
                    break;
                default:
                    AddGenericEditor();
                    break;
            }
        }

        void AddBufferEditor()
        {
            var choices = BufferAttribute.GetUIBuffers()
                .Select(buffer => new Choice<BufferKind>(buffer, buffer.GetProperties().AttributedName.Replace("{", "").Replace("}", "")))
                .ToList();
            enumCombo = Combo(choices, choices.FirstOrDefault(choice => (int)choice.Value == attribute.IntValue), 116, 220);
            enumCombo.SelectionChanged += (_, _) =>
            {
                if (enumCombo.SelectedItem is not Choice<BufferKind> choice) return;
                if (choice.Value is BufferKind.PBS or BufferKind.TBS)
                    SpecialBufferSelected?.Invoke(this, choice.Value);
            };
            editorPanel.Children.Add(enumCombo);

            editorPanel.Children.Add(Label("pH"));
            doubleBox = Box(attribute.DoubleValue > 0 ? attribute.DoubleValue.ToString("G4", CultureInfo.CurrentCulture) : "7.4", 48);
            editorPanel.Children.Add(doubleBox);
            AddValueWithUnit("mM", attribute.ParameterValue.Value * 1000, attribute.ParameterValue.SD * 1000, includeError: false);
        }

        void AddSaltEditor()
        {
            var choices = SaltAttribute.GetSalts()
                .Select(salt => new Choice<Salt>(salt, salt.GetProperties().AttributedName.Replace("{", "").Replace("}", "")))
                .ToList();
            enumCombo = Combo(choices, choices.FirstOrDefault(choice => (int)choice.Value == attribute.IntValue), 116, 190);
            editorPanel.Children.Add(enumCombo);
            AddValueWithUnit("mM", attribute.ParameterValue.Value * 1000, attribute.ParameterValue.SD * 1000, includeError: false);
        }

        void AddReferenceEditor()
        {
            var refs = DataManager.Data
                .Select(data => new Choice<string>(data.UniqueID, data.Name))
                .ToList();
            referenceCombo = Combo(refs, refs.FirstOrDefault(choice => choice.Value == attribute.StringValue), 150, 260);
            editorPanel.Children.Add(referenceCombo);

            var methods = new[]
            {
                BufferSubtractionMethod.MatchedInjection,
                BufferSubtractionMethod.Linear,
                BufferSubtractionMethod.ExponentialDecay
            }.Select(method => new Choice<BufferSubtractionMethod>(method, method.GetDisplayName())).ToList();
            methodCombo = Combo(methods, methods.FirstOrDefault(choice => (int)choice.Value == attribute.IntValue), 88, 180);
            editorPanel.Children.Add(methodCombo);
        }

        void AddSpeciesEditor()
        {
            var choices = ExperimentAttribute.SpeciesLocationOptions
                .Select(option => new Choice<int>(option.Item1, option.Item2))
                .ToList();
            enumCombo = Combo(choices, choices.FirstOrDefault(choice => choice.Value == attribute.IntValue), 84, 120);
            editorPanel.Children.Add(enumCombo);

            stringBox = Box(attribute.StringValue ?? "", 140);
            stringBox.PlaceholderText = "Species";
            editorPanel.Children.Add(stringBox);
        }

        void AddGenericEditor()
        {
            var type = attribute.Key.GetProperties()?.Type;
            switch (type)
            {
                case ExperimentAttribute.AttributeType.Bool:
                    boolBox = new CheckBox { IsChecked = attribute.BoolValue, Height = 22 };
                    editorPanel.Children.Add(boolBox);
                    break;
                case ExperimentAttribute.AttributeType.Int:
                    doubleBox = Box(attribute.IntValue.ToString(CultureInfo.CurrentCulture), 70);
                    editorPanel.Children.Add(doubleBox);
                    break;
                case ExperimentAttribute.AttributeType.Double:
                    doubleBox = Box(attribute.DoubleValue.ToString("G6", CultureInfo.CurrentCulture), 90);
                    editorPanel.Children.Add(doubleBox);
                    break;
                case ExperimentAttribute.AttributeType.String:
                    stringBox = Box(attribute.StringValue ?? "", 150);
                    editorPanel.Children.Add(stringBox);
                    break;
                default:
                    AddValueWithUnit("", attribute.ParameterValue.Value, attribute.ParameterValue.SD, includeError: true);
                    break;
            }
        }

        void AddValueWithUnit(string unit, double value, double error, bool includeError)
        {
            valueBox = Box(value.ToString("G6", CultureInfo.CurrentCulture), 82);
            editorPanel.Children.Add(valueBox);
            if (includeError)
            {
                errorBox = Box(error.ToString("G6", CultureInfo.CurrentCulture), 72);
                errorBox.PlaceholderText = "SD";
                editorPanel.Children.Add(errorBox);
            }
            if (!string.IsNullOrWhiteSpace(unit))
                editorPanel.Children.Add(Label(unit));
        }

        bool ApplyBuffer(out string error)
        {
            error = "";
            if (enumCombo?.SelectedItem is not Choice<BufferKind> bufferChoice)
            {
                error = "Select a buffer.";
                return false;
            }

            if (!TryReadDouble(doubleBox, "pH", out var pH, out error)) return false;
            if (!TryReadDouble(valueBox, "buffer concentration", out var concentration, out error)) return false;
            if (pH < 0 || pH > 14)
            {
                error = "pH must be between 0 and 14.";
                return false;
            }

            attribute.IntValue = (int)bufferChoice.Value;
            attribute.DoubleValue = pH;
            attribute.ParameterValue = new FloatWithError(concentration / 1000);
            return true;
        }

        bool ApplySalt(out string error)
        {
            error = "";
            if (enumCombo?.SelectedItem is not Choice<Salt> saltChoice)
            {
                error = "Select a salt.";
                return false;
            }

            if (!TryReadDouble(valueBox, "salt concentration", out var concentration, out error)) return false;
            attribute.IntValue = (int)saltChoice.Value;
            attribute.ParameterValue = new FloatWithError(concentration / 1000);
            return true;
        }

        bool ApplyConcentration(out string error)
        {
            if (!TryReadDouble(valueBox, "concentration", out var concentration, out error)) return false;
            attribute.ParameterValue = new FloatWithError(concentration / 1000);
            return true;
        }

        bool ApplyBufferSubtraction(ExperimentData experiment, out string error)
        {
            error = "";
            if (referenceCombo?.SelectedItem is not Choice<string> referenceChoice)
            {
                error = "Select a reference experiment.";
                return false;
            }

            var method = methodCombo?.SelectedItem is Choice<BufferSubtractionMethod> methodChoice
                ? methodChoice.Value
                : BufferSubtractionMethod.MatchedInjection;

            attribute.StringValue = referenceChoice.Value;
            attribute.IntValue = (int)method;
            var reference = DataManager.Data.FirstOrDefault(data => data.UniqueID == referenceChoice.Value);
            if (reference == null)
            {
                error = "Reference experiment is missing.";
                return false;
            }
            if (reference.UniqueID == experiment.UniqueID)
            {
                error = "An experiment cannot use itself as buffer reference.";
                return false;
            }

            return true;
        }

        bool ApplySpecies(out string error)
        {
            error = "";
            attribute.IntValue = enumCombo?.SelectedItem is Choice<int> locationChoice
                ? locationChoice.Value
                : (int)ExperimentSpeciesLocation.Cell;
            attribute.StringValue = stringBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(attribute.StringValue))
            {
                error = "Species name cannot be empty.";
                return false;
            }
            return true;
        }

        bool ApplyGeneric(out string error)
        {
            error = "";
            var type = attribute.Key.GetProperties()?.Type;
            switch (type)
            {
                case ExperimentAttribute.AttributeType.Bool:
                    attribute.BoolValue = boolBox?.IsChecked == true;
                    return true;
                case ExperimentAttribute.AttributeType.Int:
                    if (!TryReadDouble(doubleBox, "integer value", out var intValue, out error)) return false;
                    attribute.IntValue = (int)Math.Round(intValue);
                    return true;
                case ExperimentAttribute.AttributeType.Double:
                    if (!TryReadDouble(doubleBox, "value", out var doubleValue, out error)) return false;
                    attribute.DoubleValue = doubleValue;
                    return true;
                case ExperimentAttribute.AttributeType.String:
                    attribute.StringValue = stringBox?.Text ?? "";
                    return true;
                default:
                    if (!TryReadDouble(valueBox, "value", out var value, out error)) return false;
                    var sd = 0.0;
                    if (errorBox != null && !TryReadDouble(errorBox, "SD", out sd, out error)) return false;
                    attribute.ParameterValue = new FloatWithError(value, sd);
                    return true;
            }
        }

        static bool TryReadDouble(TextBox? box, string label, out double value, out string error)
        {
            value = 0;
            error = "";
            var text = box?.Text;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;

            error = $"Invalid {label}.";
            return false;
        }

        static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 3)
            };
        }

        static TextBox Box(string text, double width)
        {
            return new TextBox
            {
                Text = text,
                Width = width,
                Height = 28,
                MinHeight = 28,
                MaxHeight = 28,
                Margin = new Thickness(0, 0, 4, 3),
                Padding = new Thickness(7, 0),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static ComboBox Combo<T>(IReadOnlyList<Choice<T>> choices, Choice<T>? selected, double minWidth, double maxWidth)
        {
            var width = DynamicComboWidth(choices, minWidth, maxWidth);

            return new ComboBox
            {
                ItemsSource = choices,
                SelectedItem = selected ?? choices.FirstOrDefault(),
                Width = width,
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                Height = 28,
                MinHeight = 28,
                MaxHeight = 28,
                Margin = new Thickness(0, 0, 4, 3),
                Padding = new Thickness(7, 0),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static double DynamicComboWidth<T>(IReadOnlyList<Choice<T>> choices, double minWidth, double maxWidth)
        {
            var widest = choices.Count == 0 ? 0 : choices.Max(choice => choice.Label?.Length ?? 0);
            var estimatedWidth = 38 + widest * 7.2;

            return Math.Max(minWidth, Math.Min(maxWidth, estimatedWidth));
        }

        sealed class Choice<T>
        {
            public Choice(T value, string label)
            {
                Value = value;
                Label = label;
            }

            public T Value { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }
    }
}
