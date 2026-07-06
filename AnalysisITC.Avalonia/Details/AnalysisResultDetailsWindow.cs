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
            Width = 700;
            Height = 620;
            MinWidth = 600;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            nameBox = Box(result.Name, 280);
            commentsBox = new TextBox
            {
                Text = result.Comments ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 90
            };

            BuildLayout();
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
            content.Children.Add(Section("Result", new Control[]
            {
                Labeled("Name", nameBox),
                Labeled("Date", Text(result.UILongDateWithTime))
            }));
            content.Children.Add(Section("Comments", new Control[] { commentsBox }));
            content.Children.Add(BuildSummarySection());
            content.Children.Add(BuildActionsSection());

            root.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            });

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
            var panel = new StackPanel { Spacing = 6 };
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
                ColumnDefinitions = new ColumnDefinitions("132,*")
            };
            grid.Children.Add(Text(label));
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        static Border Pair(string label, string value)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("132,*"),
                ColumnSpacing = 8
            };
            grid.Children.Add(Text(label));
            var valueText = Text(value);
            valueText.Foreground = Solid("#202832");
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
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(8, 1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static TextBlock Text(string text)
        {
            return new TextBlock
            {
                Text = text ?? "",
                Foreground = Solid("#4D5A66"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
        }

        static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));
    }
}
