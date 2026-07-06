using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Details
{
    public sealed class ExperimentDetailsWindow : Window
    {
        readonly ExperimentData data;
        readonly List<ExperimentAttribute> attributes;
        readonly StackPanel attributePanel = new StackPanel { Spacing = 2 };
        readonly TextBlock statusText = Text("");

        readonly TextBox nameBox;
        readonly TextBox cellBox;
        readonly TextBox cellErrorBox;
        readonly TextBox syringeBox;
        readonly TextBox syringeErrorBox;
        readonly TextBox temperatureBox;
        readonly TextBox cellVolumeBox;
        readonly TextBox commentsBox;

        public bool Applied { get; private set; }

        public ExperimentDetailsWindow(ExperimentData data)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            attributes = data.Attributes.Select(attribute => attribute.Copy()).ToList();

            Title = "Experiment Details";
            Width = 720;
            Height = 640;
            MinWidth = 620;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            nameBox = Box(data.Name, 260);
            cellBox = Box((data.CellConcentration.Value * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 110);
            cellErrorBox = Box((data.CellConcentration.SD * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 110);
            syringeBox = Box((data.SyringeConcentration.Value * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 110);
            syringeErrorBox = Box((data.SyringeConcentration.SD * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 110);
            temperatureBox = Box(data.MeasuredTemperature.ToString("G6", CultureInfo.CurrentCulture), 110);
            cellVolumeBox = Box((data.CellVolume * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 110);
            commentsBox = new TextBox
            {
                Text = data.Comments ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80
            };

            if (data.IsTandemExperiment)
            {
                cellBox.IsEnabled = false;
                cellErrorBox.IsEnabled = false;
                syringeBox.IsEnabled = false;
                syringeErrorBox.IsEnabled = false;
                cellVolumeBox.IsEnabled = false;
            }

            BuildLayout();
            RebuildAttributes();
        }

        void BuildLayout()
        {
            var root = new DockPanel
            {
                LastChildFill = true,
                Background = Solid("#F5F7FA")
            };

            var footer = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing = 8,
                Margin = new Thickness(12)
            };
            footer.Children.Add(statusText);

            var cancel = Button("Cancel", 82);
            cancel.Click += (_, _) => Close(false);
            Grid.SetColumn(cancel, 1);
            footer.Children.Add(cancel);

            var apply = Button("Apply", 82);
            apply.Click += (_, _) => Apply();
            Grid.SetColumn(apply, 2);
            footer.Children.Add(apply);

            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var content = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
            content.Children.Add(Section("Experiment", new Control[]
            {
                Labeled("Name", nameBox),
                Labeled("Temperature C", temperatureBox),
                Labeled("Cell volume uL", cellVolumeBox)
            }));
            content.Children.Add(Section("Concentrations", new Control[]
            {
                TwoValueRow("Cell uM", cellBox, "SD uM", cellErrorBox),
                TwoValueRow("Syringe uM", syringeBox, "SD uM", syringeErrorBox)
            }));
            content.Children.Add(Section("Comments", new Control[] { commentsBox }));

            var addAttribute = Button("Add Attribute", 116);
            addAttribute.Click += (_, _) =>
            {
                attributes.Add(new ExperimentAttribute());
                RebuildAttributes();
            };

            var attributeSection = new StackPanel { Spacing = 6 };
            attributeSection.Children.Add(addAttribute);
            attributeSection.Children.Add(attributePanel);
            content.Children.Add(Section("Attributes", new Control[] { attributeSection }));

            root.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            });

            Content = root;
        }

        void RebuildAttributes()
        {
            attributePanel.Children.Clear();

            if (attributes.Count == 0)
            {
                attributePanel.Children.Add(Text("No attributes."));
                return;
            }

            foreach (var attribute in attributes.ToList())
            {
                var row = new ExperimentAttributeEditorControl(attribute, CanUseKey);
                row.RemoveRequested += (_, _) =>
                {
                    attributes.Remove(attribute);
                    RebuildAttributes();
                };
                row.KeyChanged += (_, _) => RebuildAttributes();
                row.SpecialBufferSelected += (_, buffer) =>
                {
                    BufferAttribute.SetupSpecialBuffer(attributes, buffer);
                    RebuildAttributes();
                };
                attributePanel.Children.Add(row);
            }
        }

        bool CanUseKey(ExperimentAttribute current, AttributeKey key)
        {
            if (key == AttributeKey.Null) return true;
            var properties = key.GetProperties();
            if (properties?.AllowMultiple == true) return true;

            return !attributes.Any(attribute => !ReferenceEquals(attribute, current) && attribute.Key == key);
        }

        void Apply()
        {
            if (!TryRead(nameBox, "name", allowEmpty: false, out var name)) return;
            if (!TryReadDouble(temperatureBox, "temperature", out var temperature)) return;

            var cell = data.CellConcentration.Value;
            var cellSd = data.CellConcentration.SD;
            var syringe = data.SyringeConcentration.Value;
            var syringeSd = data.SyringeConcentration.SD;
            var cellVolume = data.CellVolume;

            if (!data.IsTandemExperiment)
            {
                if (!TryReadDouble(cellBox, "cell concentration", out var cellUm)) return;
                if (!TryReadDouble(cellErrorBox, "cell concentration SD", out var cellSdUm)) return;
                if (!TryReadDouble(syringeBox, "syringe concentration", out var syringeUm)) return;
                if (!TryReadDouble(syringeErrorBox, "syringe concentration SD", out var syringeSdUm)) return;
                if (!TryReadDouble(cellVolumeBox, "cell volume", out var cellVolumeUl)) return;

                cell = cellUm / 1_000_000;
                cellSd = cellSdUm / 1_000_000;
                syringe = syringeUm / 1_000_000;
                syringeSd = syringeSdUm / 1_000_000;
                cellVolume = cellVolumeUl / 1_000_000;
            }

            var editors = attributePanel.Children.OfType<ExperimentAttributeEditorControl>().ToList();
            foreach (var editor in editors)
            {
                if (!editor.TryApply(data, out var error))
                {
                    SetStatus(error);
                    return;
                }
            }

            try
            {
                data.Name = name;
                data.MeasuredTemperature = temperature;
                data.CellConcentration = new FloatWithError(cell, cellSd);
                data.SyringeConcentration = new FloatWithError(syringe, syringeSd);
                data.CellVolume = cellVolume;
                data.Comments = commentsBox.Text ?? "";

                data.ClearBufferSubtraction(notify: false);
                data.CopyAttributesFrom(attributes.Where(attribute => attribute.Key != AttributeKey.Null), clear: true, overwriteExisting: true, notify: false);

                RawDataReader.ProcessInjections(data);
                new DataProcessor(data).IntegratePeaks();

                DataManager.InvokeDataDidChange();
                DataManager.InvokeUpdateDataViewCells();
                DataManager.InvokeUpdateTable();
                StatusBarManager.SetStatus($"{data.Name} details updated", 2500);

                Applied = true;
                Close(true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        bool TryRead(TextBox box, string label, bool allowEmpty, out string value)
        {
            value = box.Text?.Trim() ?? "";
            if (allowEmpty || !string.IsNullOrWhiteSpace(value)) return true;

            SetStatus($"Invalid {label}.");
            return false;
        }

        bool TryReadDouble(TextBox box, string label, out double value)
        {
            value = 0;
            var text = box.Text;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;

            SetStatus($"Invalid {label}.");
            return false;
        }

        void SetStatus(string status)
        {
            statusText.Text = status;
        }

        static Border Section(string title, Control[] controls)
        {
            var panel = new StackPanel { Spacing = 5 };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                Foreground = Solid("#202832")
            });
            foreach (var control in controls)
                panel.Children.Add(control);

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Solid("#D4DAE1"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = panel
            };
        }

        static Control Labeled(string label, Control control)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("130,*")
            };
            grid.Children.Add(Text(label));
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        static Control TwoValueRow(string leftLabel, Control left, string rightLabel, Control right)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("130,*,70,*"),
                ColumnSpacing = 8
            };
            grid.Children.Add(Text(leftLabel));
            Grid.SetColumn(left, 1);
            grid.Children.Add(left);
            var rightText = Text(rightLabel);
            Grid.SetColumn(rightText, 2);
            grid.Children.Add(rightText);
            Grid.SetColumn(right, 3);
            grid.Children.Add(right);
            return grid;
        }

        static TextBox Box(string text, double width)
        {
            return new TextBox
            {
                Text = text,
                Width = width,
                Height = 24,
                Padding = new Thickness(6, 1),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static Button Button(string text, double width)
        {
            return new Button
            {
                Content = text,
                MinWidth = width,
                Height = 26,
                Padding = new Thickness(8, 1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static TextBlock Text(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Solid("#4D5A66"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
        }

        static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));
    }
}
