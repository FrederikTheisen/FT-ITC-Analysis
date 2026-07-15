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
using AnalysisITC.Avalonia.Styling;

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
            Width = 760;
            Height = 660;
            MinWidth = 620;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            nameBox = WideBox(data.Name);
            cellBox = Box((data.CellConcentration.Value * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 90);
            cellErrorBox = Box((data.CellConcentration.SD * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 76);
            syringeBox = Box((data.SyringeConcentration.Value * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 90);
            syringeErrorBox = Box((data.SyringeConcentration.SD * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 76);
            temperatureBox = Box(data.MeasuredTemperature.ToString("G6", CultureInfo.CurrentCulture), 110);
            cellVolumeBox = Box((data.CellVolume * 1_000_000).ToString("G6", CultureInfo.CurrentCulture), 110);
            commentsBox = new TextBox
            {
                Text = data.Comments ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80,
                Padding = new Thickness(8, 6),
                FontSize = 13
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
                LastChildFill = true
            };
            AppTheme.Bind(root, Panel.BackgroundProperty, AppTheme.WorkspaceBackground);

            var footer = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing = 8,
                Margin = new Thickness(12, 10)
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

            var footerBorder = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = footer
            };
            AppTheme.Bind(footerBorder, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(footerBorder, Border.BorderBrushProperty, AppTheme.PanelBorder);
            DockPanel.SetDock(footerBorder, Dock.Bottom);
            root.Children.Add(footerBorder);

            var header = Header("Experiment Details", data.Name, $"{data.UIShortDateWithTime} | {System.IO.Path.GetFileName(data.FileName)}");
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var details = new StackPanel { Spacing = 10 };
            details.Children.Add(Section("Experiment", new Control[]
            {
                FullWidthLabeled("Name", nameBox)
            }));

            var topGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 10
            };

            var experimentSection = Section("Experiment", new Control[]
            {
                Labeled("Temperature (C)", temperatureBox),
                Labeled("Cell volume (uL)", cellVolumeBox)
            });
            topGrid.Children.Add(experimentSection);

            var concentrationRows = new List<Control>
            {
                TwoValueRow("Cell (uM)", cellBox, "±", cellErrorBox),
                TwoValueRow("Syringe (uM)", syringeBox, "±", syringeErrorBox)
            };
            if (data.IsTandemExperiment)
                concentrationRows.Add(Note("Concentrations and cell volume are controlled by the tandem experiment."));
            var concentrationSection = Section("Concentrations", concentrationRows.ToArray());
            Grid.SetColumn(concentrationSection, 1);
            topGrid.Children.Add(concentrationSection);

            details.Children.Add(topGrid);
            details.Children.Add(Section("Comments", new Control[] { commentsBox }));

            var addAttribute = Button("Add Attribute", 116);
            addAttribute.Click += (_, _) =>
            {
                attributes.Add(new ExperimentAttribute());
                RebuildAttributes();
            };

            var attributeHeader = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(addAttribute, Dock.Right);
            attributeHeader.Children.Add(addAttribute);
            attributeHeader.Children.Add(Note("Attributes describe buffer, salts, ionic strength, species, and buffer subtraction."));

            var attributeSection = new StackPanel { Spacing = 8 };
            attributeSection.Children.Add(attributeHeader);
            attributeSection.Children.Add(attributePanel);

            var tabs = new TabControl
            {
                Margin = new Thickness(12),
                Items =
                {
                    Tab("Details", Scroll(details)),
                    Tab("Attributes", Scroll(Section("Attributes", new Control[] { attributeSection })))
                }
            };

            root.Children.Add(tabs);

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
            var panel = new StackPanel { Spacing = 7 };
            var titleBlock = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold
            };
            AppTheme.Bind(titleBlock, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            panel.Children.Add(titleBlock);
            foreach (var control in controls)
                panel.Children.Add(control);

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10),
                Child = panel
            };
            AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.PanelBorder);
            return border;
        }

        static Border Header(string title, string primary, string secondary)
        {
            var panel = new StackPanel { Spacing = 2 };
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold
            };
            AppTheme.Bind(titleBlock, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            panel.Children.Add(titleBlock);
            var primaryBlock = new TextBlock
            {
                Text = primary,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            AppTheme.Bind(primaryBlock, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            panel.Children.Add(primaryBlock);
            var secondaryBlock = new TextBlock
            {
                Text = secondary,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            AppTheme.Bind(secondaryBlock, TextBlock.ForegroundProperty, AppTheme.MutedText);
            panel.Children.Add(secondaryBlock);

            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 12),
                Child = panel
            };
            AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.PanelBorder);
            return border;
        }

        static TabItem Tab(string header, Control content)
        {
            return new TabItem
            {
                Header = new TextBlock
                {
                    Text = header,
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap
                },
                Content = content
            };
        }

        static Control Scroll(Control content)
        {
            return new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Padding = new Thickness(0, 10, 0, 0),
                    Child = content
                }
            };
        }

        static Control Labeled(string label, Control control)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("122,Auto"),
                ColumnSpacing = 10,
                MinHeight = 32
            };
            grid.Children.Add(FormLabel(label));
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        static Control FullWidthLabeled(string label, Control control)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("122,*"),
                ColumnSpacing = 10,
                MinHeight = 32
            };
            grid.Children.Add(FormLabel(label));
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        static Control TwoValueRow(string leftLabel, Control left, string rightLabel, Control right)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("104,90,22,76"),
                ColumnSpacing = 8,
                MinHeight = 32
            };
            grid.Children.Add(FormLabel(leftLabel));
            Grid.SetColumn(left, 1);
            grid.Children.Add(left);
            var rightText = FormLabel(rightLabel);
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
                Height = 30,
                MinHeight = 30,
                MaxHeight = 30,
                Padding = new Thickness(8, 0),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static TextBox WideBox(string text)
        {
            return new TextBox
            {
                Text = text,
                Height = 30,
                MinHeight = 30,
                MaxHeight = 30,
                Padding = new Thickness(8, 0),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch,
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

        static TextBlock Note(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.MutedText);
            return textBlock;
        }

        static TextBlock Text(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
            return textBlock;
        }

        static TextBlock FormLabel(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };
            AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
            return textBlock;
        }
    }
}
