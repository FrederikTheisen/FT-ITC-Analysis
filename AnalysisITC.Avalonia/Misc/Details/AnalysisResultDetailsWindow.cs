using System;
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
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Avalonia.Styling;

namespace AnalysisITC.Avalonia.Details
{
    public sealed class AnalysisResultDetailsWindow : Window
    {
        readonly AnalysisResult result;
        readonly TextBox nameBox;
        readonly TextBox commentsBox;
        readonly TextBlock statusText = Text("");

        public bool Applied { get; private set; }

        public AnalysisResultDetailsWindow(AnalysisResult result)
        {
            this.result = result ?? throw new ArgumentNullException(nameof(result));

            Title = "Result Details";
            Width = 760;
            Height = 660;
            MinWidth = 600;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            nameBox = Box(result.Name, 300);
            commentsBox = new TextBox
            {
                Text = result.Comments ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 90,
                Padding = new Thickness(8, 6),
                FontSize = 13
            };

            BuildLayout();
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

            var header = Header("Result Details", result.Name, result.GetListDescriptionString().Replace(Environment.NewLine, " | "));
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var details = new StackPanel { Spacing = 10 };
            details.Children.Add(Section("Result", new Control[]
            {
                Labeled("Name", nameBox),
                Labeled("Date", Text(result.UILongDateWithTime))
            }));
            details.Children.Add(Section("Comments", new Control[] { commentsBox }));
            details.Children.Add(BuildSummarySection());

            var tabs = new TabControl
            {
                Margin = new Thickness(12),
                Items =
                {
                    Tab("Details", Scroll(details)),
                    Tab("Experiments", Scroll(BuildExperimentsSection())),
                    Tab("Actions", Scroll(BuildActionsSection()))
                }
            };

            root.Children.Add(tabs);

            Content = root;
        }

        Border BuildSummarySection()
        {
            var solution = result.Solution;
            var convergence = solution.Convergence;

            return Section("Summary", new Control[]
            {
                Pair("Experiments", solution.Solutions.Count.ToString(CultureInfo.CurrentCulture)),
                Pair("Model", solution.SolutionName),
                Pair("RMSD / loss", solution.Loss.ToString("G4", CultureInfo.CurrentCulture)),
                Pair("Algorithm", convergence?.Algorithm.GetProperties().Name ?? ""),
                Pair("Iterations", convergence?.Iterations.ToString(CultureInfo.CurrentCulture) ?? ""),
                Pair("Solve time", convergence?.Time.ToString() ?? ""),
                Pair("Error method", solution.ErrorEstimationMethod.Description()),
                Pair("Bootstrap", $"{solution.BootstrapIterations} iterations"),
                Pair("Bootstrap time", convergence?.ErrorEstimationTime.ToString() ?? ""),
                Pair("Fitting", solution.UseWeightedFitting ? "Weighted injection errors" : "Unweighted"),
                Pair("Concentration errors", solution.Model.ModelCloneOptions?.IncludeConcentrationErrorsInBootstrap == true ? "Bootstrap enabled" : "Not used"),
                Pair("Validity", ValiditySummary(result.ValidityReport))
            });
        }

        Border BuildActionsSection()
        {
            var copy = Button("Copy result table", 150);
            copy.Click += (_, _) =>
            {
                try
                {
                    Exporter.CopyToClipboard(result, result.AppropriateAffinityUnit, AppSettings.EnergyUnit, usekelvin: false);
                    SetStatus("Result table copied.");
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                }
            };

            var load = Button("Load solutions to experiments", 190);
            load.Click += (_, _) =>
            {
                DataManager.LoadResultSolutionsToExperiments(result);
                DataManager.InvokeDataDidChange();
                DataManager.InvokeUpdateTable();
                StatusBarManager.SetStatus("Result solutions loaded into experiments", 3000);
                SetStatus("Solutions loaded.");
                Applied = true;
            };

            var enable = Button("Select result experiments", 180);
            enable.Click += (_, _) =>
            {
                var ids = result.Solution.Solutions
                    .Select(solution => solution.Data?.UniqueID)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet();

                foreach (var data in DataManager.Data)
                    data.Include = ids.Contains(data.UniqueID);

                DataManager.InvokeDataInclusionDidChange();
                DataManager.InvokeUpdateTable();
                StatusBarManager.SetStatus("Experiments used by result selected", 3000);
                SetStatus("Experiment inclusion updated.");
                Applied = true;
            };

            return Section("Actions", new Control[]
            {
                Text("These actions update the current session immediately."),
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    ItemWidth = double.NaN,
                    ItemHeight = double.NaN,
                    Children = { copy, load, enable }
                }
            });
        }

        Control BuildExperimentsSection()
        {
            var panel = new StackPanel { Spacing = 10 };

            foreach (var solution in result.Solution.Solutions)
            {
                var data = solution.Data;
                panel.Children.Add(Section(data?.Name ?? solution.SolutionName, new Control[]
                {
                    Pair("Date", data?.UIShortDateWithTime ?? ""),
                    Pair("Temperature", data == null ? "" : $"{data.MeasuredTemperature:G3} C"),
                    Pair("Status", solution.IsValid ? "Solution valid" : "Solution invalid")
                }));
            }

            if (panel.Children.Count == 0)
                panel.Children.Add(Section("Experiments", new Control[] { Text("No experiments are attached to this result.") }));

            return panel;
        }

        void Apply()
        {
            var name = nameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatus("Invalid name.");
                return;
            }

            result.Name = name;
            result.Comments = commentsBox.Text ?? "";

            DataManager.InvokeDataDidChange();
            DataManager.InvokeUpdateDataViewCells();
            DataManager.InvokeUpdateTable();
            StatusBarManager.SetStatus($"{result.Name} details updated", 2500);

            Applied = true;
            Close(true);
        }

        static string ValiditySummary(AnalysisResultValidityReport report)
        {
            if (report.Reasons.Count == 0)
                return report.Status.ToString();

            return $"{report.Status}: {string.Join("; ", report.Reasons)}";
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
                ColumnDefinitions = new ColumnDefinitions("132,Auto"),
                ColumnSpacing = 10,
                MinHeight = 32
            };
            grid.Children.Add(FormLabel(label));
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        static Border Pair(string label, string value)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("132,*"),
                ColumnSpacing = 10,
                MinHeight = 28
            };
            grid.Children.Add(FormLabel(label));
            var valueText = Text(value);
            AppTheme.Bind(valueText, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(valueText);
            return new Border { Child = grid };
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

        static Button Button(string text, double width)
        {
            return new Button
            {
                Content = text,
                MinWidth = width,
                Height = 26,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(8, 1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static TextBlock Text(string text)
        {
            var textBlock = new TextBlock
            {
                Text = text ?? "",
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
