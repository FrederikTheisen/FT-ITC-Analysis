using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

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

        public static readonly IBrush WorkspaceBackgroundBrush = Solid("#F5F7FA");
        public static readonly IBrush PanelBackgroundBrush = Brushes.White;
        public static readonly IBrush PanelBorderBrush = Solid("#D4DAE1");
        public static readonly IBrush SectionBorderBrush = Solid("#E3E7EC");
        public static readonly IBrush SectionHeaderBrush = Solid("#202832");
        public static readonly IBrush LabelBrush = Solid("#607080");
        public static readonly IBrush TextBrush = Solid("#4D5A66");
        public static Thickness ControlMargin => new Thickness(0, 1);
        public static Thickness ScrollContentPadding => new Thickness(8);
        public static Thickness SectionPadding => new Thickness(0, 0, 0, 6);
        public static Thickness ComboPadding => new Thickness(8, 0);
        public static Thickness TextBoxPadding => new Thickness(6, 1);
        public static Thickness ButtonPadding => new Thickness(8, 1);
        public static Thickness InspectorFooterPadding => new Thickness(10, 8);
        public static Thickness InspectorHostMargin => new Thickness(InspectorGap, 0, InspectorGap, 0);

        public static Grid Workspace(Control mainContent, Control inspectorContent, Control? inspectorFooter = null)
        {
            var root = WorkspaceGrid();
            Grid.SetColumn(mainContent, 0);
            root.Children.Add(mainContent);

            var inspectorHost = InspectorHost(inspectorContent, inspectorFooter);
            Grid.SetColumn(inspectorHost, 1);
            root.Children.Add(inspectorHost);

            return root;
        }

        static Grid WorkspaceGrid()
        {
            return new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"*,{InspectorWidth}"),
                Background = WorkspaceBackgroundBrush
            };
        }

        public static Border ContentBorder(Control content)
        {
            return new Border
            {
                Background = PanelBackgroundBrush,
                BorderBrush = PanelBorderBrush,
                BorderThickness = new Thickness(1),
                Child = content
            };
        }

        public static TabControl Inspector()
        {
            return new TabControl()
            {
                Padding = new Thickness(0, 0, 0, 10),
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
                Margin = InspectorHostMargin
            };

            Grid.SetRow(inspectorContent, 0);
            host.Children.Add(inspectorContent);

            if (footer != null)
            {
                Grid.SetRow(footer, 1);
                host.Children.Add(footer);
            }

            return host;
        }

        public static Border InspectorFooter(Control content)
        {
            return new Border
            {
                Background = PanelBackgroundBrush,
                BorderBrush = PanelBorderBrush,
                BorderThickness = new Thickness(1, 1, 1, 1),
                Padding = InspectorFooterPadding,
                Child = content
            };
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
                    FontSize = 12,
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
            return new Border
            {
                Background = PanelBackgroundBrush,
                BorderBrush = PanelBorderBrush,
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Border
                    {
                        Padding = ScrollContentPadding,
                        Child = content
                    }
                }
            };
        }

        public static Border Section(string title, params Control[] controls)
        {
            var header = new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                Foreground = SectionHeaderBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 0, 0, 4)
            };

            return Section(header, controls);
        }

        public static Border Section(TextBlock header, params Control[] controls)
        {
            var panel = new StackPanel { Spacing = SectionControlSpacing };
            header.Width = double.NaN;
            panel.Children.Add(header);
            foreach (var control in controls)
            {
                ApplyControlMargin(control);
                panel.Children.Add(control);
            }

            return new Border
            {
                BorderBrush = SectionBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = SectionPadding,
                Child = panel
            };
        }

        public static Border Labeled(string label, Control control)
        {
            StretchFieldControl(control);

            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"{RowLabelWidth},*"),
                ColumnSpacing = RowSpacing
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = LabelBrush
            });
            Grid.SetColumn(control, 1);
            panel.Children.Add(control);

            return new Border
            {
                Margin = ControlMargin,
                Child = panel
            };
        }

        public static StackPanel Row(params Control[] controls)
        {
            var row = Horizontal(RowSpacing);
            row.Margin = ControlMargin;
            foreach (var control in controls)
                row.Children.Add(control);
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

        public static TextBox TextBox(string text)
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

        public static CheckBox Check(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Height = 20,
                Margin = ControlMargin,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBlock Text(string text = "")
        {
            return new TextBlock
            {
                Text = text,
                Foreground = TextBrush,
                Margin = ControlMargin,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static TextBlock TrimmingText()
        {
            return new TextBlock
            {
                Foreground = TextBrush,
                Margin = ControlMargin,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        public static TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                Foreground = SectionHeaderBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
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

        public static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));
    }
}
