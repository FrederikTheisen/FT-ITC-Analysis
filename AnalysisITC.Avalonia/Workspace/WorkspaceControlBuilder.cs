using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Controls;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Workspace
{
    static class WorkspaceControlBuilder
    {
        public const double InspectorWidth = 330;
        public const double InspectorGap = 10;
        public const double RowLabelWidth = 94;
        public const double InspectorFieldWidth = 170;
        public const double InspectorSectionSpacing = 6;
        public const double SectionControlSpacing = 0;
        public const double RowSpacing = 6;
        public const double InspectorTabFontSize = 13;

        public static Thickness ControlMargin => new Thickness(0, 1);
        public static Thickness ScrollContentPadding => new Thickness(8);
        public static Thickness SectionPadding => new Thickness(0, 0, 0, 6);
        public static Thickness ComboPadding => new Thickness(8, 0);
        public static Thickness TextBoxPadding => new Thickness(6, 1);
        public static Thickness ButtonPadding => new Thickness(8, 1);
        public static Thickness InspectorFooterPadding => new Thickness(10, 8);
        public static Thickness WorkspaceOuterMargin => new Thickness(InspectorGap);
        public static Thickness InspectorGapMargin => new Thickness(InspectorGap, 0, 0, 0);
        public static Thickness FooterGapMargin => new Thickness(0, InspectorGap, 0, 0);

        public static Grid Workspace(Control mainContent, Control inspectorContent, Control? inspectorFooter = null, bool useOuterMargin = false)
        {
            var root = WorkspaceGrid();
            root.Margin = useOuterMargin ? WorkspaceOuterMargin : new Thickness(0);
            mainContent.Margin = new Thickness(0);
            Grid.SetColumn(mainContent, 0);
            root.Children.Add(mainContent);

            var inspectorHost = InspectorHost(inspectorContent, inspectorFooter);
            Grid.SetColumn(inspectorHost, 1);
            root.Children.Add(inspectorHost);

            return root;
        }

        public static Grid ContentWithFooter(Control content, Control footer)
        {
            var host = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto")
            };

            Grid.SetRow(content, 0);
            host.Children.Add(content);

            footer.Margin = FooterGapMargin;
            Grid.SetRow(footer, 1);
            host.Children.Add(footer);

            return host;
        }

        static Grid WorkspaceGrid()
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"*,{InspectorWidth}")
            };
            AppTheme.Bind(grid, Panel.BackgroundProperty, AppTheme.WorkspaceBackground);
            return grid;
        }

        public static Border ContentBorder(Control content)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Child = content
            };
            AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.PanelBorder);
            return border;
        }

        public static TabControl Inspector()
        {
            return new TabControl()
            {
                Padding = new Thickness(0, 0, 0, 0),
            };
        }

        public static TabControl Inspector(params InspectorTabDefinition[] tabs)
        {
            var inspector = Inspector();
            foreach (var tab in tabs)
                inspector.Items.Add(Tab(tab.Header, Scroll(tab.Content)));

            return inspector;
        }

        public static InspectorTabDefinition InspectorTab(string header, Control content)
        {
            return new InspectorTabDefinition(header, content);
        }

        public static Grid InspectorHost(Control inspectorContent, Control? footer = null)
        {
            var host = new Grid
            {
                RowDefinitions = footer == null ? new RowDefinitions("*") : new RowDefinitions("*,Auto"),
                Margin = InspectorGapMargin
            };

            Grid.SetRow(inspectorContent, 0);
            host.Children.Add(inspectorContent);

            if (footer != null)
            {
                footer.Margin = FooterGapMargin;
                Grid.SetRow(footer, 1);
                host.Children.Add(footer);
            }

            return host;
        }

        public static Border InspectorFooter(Control content)
        {
            var border = new Border
            {
                BorderThickness = new Thickness(1, 1, 1, 1),
                Padding = InspectorFooterPadding,
                Child = content
            };
            AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.PanelBorder);
            return border;
        }

        public static StackPanel InspectorPanel()
        {
            return new StackPanel
            {
                Spacing = InspectorSectionSpacing
            };
        }

        public static TabItem Tab(string header, Control content)
        {
            return new TabItem
            {
                Header = new TextBlock
                {
                    Text = header,
                    FontSize = InspectorTabFontSize,
                    TextWrapping = TextWrapping.NoWrap,
                },
                Content = content
            };
        }

        public readonly struct InspectorTabDefinition
        {
            public InspectorTabDefinition(string header, Control content)
            {
                Header = header;
                Content = content;
            }

            public string Header { get; }
            public Control Content { get; }
        }

        public static Control Scroll(Control content)
        {
            var contentBorder = new Border
            {
                Padding = ScrollContentPadding,
                Child = content
            };

            var border = new Border
            {
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = contentBorder
                }
            };
            AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
            AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.PanelBorder);
            return border;
        }

        public static Border Section(string title, params Control[] controls)
        {
            var header = new TextBlock
            {
                Text = title,
            };

            return Section(header, controls);
        }

        public static Border Section(TextBlock header, params Control[] controls)
        {
            header.FontWeight = FontWeight.SemiBold;
            AppTheme.Bind(header, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            header.VerticalAlignment = VerticalAlignment.Center;
            header.Padding = new Thickness(0, 0, 0, 4);
            var panel = new StackPanel { Spacing = SectionControlSpacing };
            header.Width = double.NaN;
            panel.Children.Add(header);
            foreach (var control in controls)
            {
                ApplyControlMargin(control);
                panel.Children.Add(control);
            }

            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = SectionPadding,
                Child = panel
            };
            AppTheme.Bind(border, Border.BorderBrushProperty, AppTheme.SectionBorder);
            return border;
        }

        public static Border Labeled(string label, Control control)
        {
            StretchFieldControl(control);

            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"{RowLabelWidth},*"),
                ColumnSpacing = RowSpacing
            };
            var labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(labelBlock, TextBlock.ForegroundProperty, AppTheme.MutedText);
            panel.Children.Add(labelBlock);
            Grid.SetColumn(control, 1);
            panel.Children.Add(control);

            return new Border
            {
                Margin = ControlMargin,
                Child = panel
            };
        }

        public static Grid FieldWithSuffix(Control field, Control suffix, double suffixWidth = 48)
        {
            StretchFieldControl(field);
            field.Margin = new Thickness(0);
            suffix.Margin = new Thickness(0);
            suffix.HorizontalAlignment = HorizontalAlignment.Right;
            suffix.VerticalAlignment = VerticalAlignment.Center;

            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"*,{suffixWidth}"),
                ColumnSpacing = RowSpacing
            };
            panel.Children.Add(field);
            Grid.SetColumn(suffix, 1);
            panel.Children.Add(suffix);
            return panel;
        }

        public static Grid NumericFieldWithSuffix(NumericUpDown field, string suffix, double suffixLeft = 26)
        {
            StretchFieldControl(field);
            field.Margin = new Thickness(0);

            var suffixText = new TextBlock
            {
                Text = suffix,
                Margin = new Thickness(suffixLeft, 0, 0, 0),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(suffixText, TextBlock.ForegroundProperty, AppTheme.MutedText);

            var panel = new Grid();
            panel.Children.Add(field);
            panel.Children.Add(suffixText);
            return panel;
        }

        public static StackPanel Row(params Control[] controls)
        {
            var row = Horizontal(RowSpacing);
            row.Margin = ControlMargin;
            foreach (var control in controls)
                row.Children.Add(control);
            return row;
        }

        public static Grid EqualWidthRow(params Control[] controls)
        {
            var columns = new ColumnDefinitions();
            for (var i = 0; i < controls.Length; i++)
                columns.Add(new ColumnDefinition(GridLength.Star));

            var row = new Grid
            {
                ColumnDefinitions = columns,
                ColumnSpacing = RowSpacing,
                Margin = ControlMargin
            };

            for (var i = 0; i < controls.Length; i++)
            {
                var control = controls[i];
                StretchFieldControl(control);
                control.Margin = new Thickness(0);
                Grid.SetColumn(control, i);
                row.Children.Add(control);
            }

            return row;
        }

        public static StackPanel Horizontal(double spacing)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = spacing
            };
        }

        public static StackPanel VerticalGroup()
        {
            return new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = SectionControlSpacing
            };
        }

        public static ComboBox Combo(double width)
        {
            return new ComboBox
            {
                Width = width,
                Height = 24,
                Padding = ComboPadding,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static ComboBox Combo(string[] items) => Combo(items, InspectorFieldWidth);

        public static ComboBox Combo(string[] items, double width)
        {
            var combo = Combo(width);
            combo.ItemsSource = items;
            combo.SelectedIndex = 0;
            return combo;
        }

        public static ComboBox Combo(string[] items, int selectedIndex, double width)
        {
            var combo = Combo(items, width);
            combo.SelectedIndex = selectedIndex;
            return combo;
        }

        public static SegmentedSelector Segmented(string[] items, int selectedIndex = 0, double width = InspectorFieldWidth)
        {
            return new SegmentedSelector(items, selectedIndex)
            {
                Width = width,
                Height = 32,
                MinHeight = 32,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Slider Slider(double min, double max, double tickFrequency, double width = InspectorFieldWidth)
        {
            return new Slider
            {
                Minimum = min,
                Maximum = max,
                TickFrequency = tickFrequency,
                Width = width,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static NumericUpDown Stepper(
            decimal value,
            decimal minimum,
            decimal maximum,
            decimal increment = 1,
            double width = InspectorFieldWidth,
            string formatString = "0")
        {
            return new NumericUpDown
            {
                Value = value,
                Minimum = minimum,
                Maximum = maximum,
                Increment = increment,
                FormatString = formatString,
                Width = width,
                Height = 24,
                Padding = TextBoxPadding,
                ClipValueToMinMax = true,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        public static TextBox TextBox(string text = "")
        {
            return new TextBox
            {
                Text = text,
                Height = 24,
                Padding = TextBoxPadding,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        public static Button Button(string text, double width)
        {
            return new Button
            {
                Content = text,
                MinWidth = width,
                Height = 24,
                Padding = ButtonPadding,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        public static ToggleButton Toggle(string text, double width)
        {
            return new ToggleButton
            {
                Content = text,
                MinWidth = width,
                Height = 24,
                Padding = ButtonPadding,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        public static CheckBox Check(string text, bool isChecked = false, string tooltip = "")
        {
            var control = new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Height = 20,
                Margin = ControlMargin,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(control, tooltip);

            return control;
        }

        public static TextBlock Text(string text = "")
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Margin = ControlMargin,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
            return textBlock;
        }

        public static TextBlock MarkdownText(string text)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap
            };

            foreach (var segment in MarkdownProcessor.GetSegments(MarkdownProcessor.ProcessWrittenText(text ?? "")))
            {
                var run = new Run(segment.Text);
                switch (segment.Property)
                {
                    case MarkdownProperty.Bold:
                    case MarkdownProperty.Header1:
                    case MarkdownProperty.Header2:
                        run.FontWeight = FontWeight.SemiBold;
                        break;
                    case MarkdownProperty.Cursive:
                        run.FontStyle = FontStyle.Italic;
                        break;
                    case MarkdownProperty.Subscript:
                        run.BaselineAlignment = BaselineAlignment.Subscript;
                        run.FontSize = 10;
                        break;
                    case MarkdownProperty.Superscript:
                        run.BaselineAlignment = BaselineAlignment.Superscript;
                        run.FontSize = 10;
                        break;
                    case MarkdownProperty.Small:
                        run.FontSize = 11;
                        break;
                }

                textBlock.Inlines ??= new InlineCollection();
                textBlock.Inlines.Add(run);
            }

            return textBlock;
        }

        public static TextBlock TrimmingText()
        {
            var textBlock = new TextBlock
            {
                Margin = ControlMargin,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
            return textBlock;
        }

        public static TextBlock Header(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(textBlock, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            return textBlock;
        }

        public static void ApplyControlMargin(Control control)
        {
            if (control is Panel)
                return;

            if (IsZero(control.Margin))
                control.Margin = ControlMargin;
        }

        static void StretchFieldControl(Control control)
        {
            control.Width = double.NaN;
            control.MinWidth = 0;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        static bool IsZero(Thickness margin)
        {
            return margin.Left == 0
                && margin.Top == 0
                && margin.Right == 0
                && margin.Bottom == 0;
        }
    }
}
