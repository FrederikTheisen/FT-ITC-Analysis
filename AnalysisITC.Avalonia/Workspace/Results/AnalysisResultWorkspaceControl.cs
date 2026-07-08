using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Avalonia.Workspace;

namespace AnalysisITC.Avalonia.Results
{
    public sealed class AnalysisResultWorkspaceControl : UserControl
    {
        static readonly EnergyUnit[] EvaluationEnergyUnits =
        {
            EnergyUnit.Joule,
            EnergyUnit.KiloJoule,
            EnergyUnit.Cal,
            EnergyUnit.KCal
        };

        static readonly string[] EvaluationEnergyUnitNames = { "J", "kJ", "cal", "kcal" };

        readonly ResultParameterGraphControl graph = new ResultParameterGraphControl();
        readonly StackPanel tableHost = new StackPanel { Spacing = 0 };
        readonly StackPanel summaryPanel = new StackPanel { Spacing = 10 };
        readonly StackPanel experimentsPanel = new StackPanel { Spacing = 8 };
        readonly StackPanel modelPanel = new StackPanel { Spacing = 10 };
        readonly ComboBox temperatureUnitCombo = Combo(new[] { "Celsius", "Kelvin" }, 0, 120);
        readonly TextBox evaluationTemperatureBox = WorkspaceControlBuilder.TextBox("");
        readonly ComboBox evaluationTemperatureUnitCombo = WorkspaceControlBuilder.Combo(new[] { "Celsius", "Kelvin" }, 0, 170);
        readonly ComboBox evaluationEnergyUnitCombo = WorkspaceControlBuilder.Combo(EvaluationEnergyUnitNames, 1, 170);
        readonly StackPanel evaluationRowsPanel = WorkspaceControlBuilder.VerticalGroup();
        readonly Border displaySection;
        readonly Border parameterEvaluationSection;

        AnalysisResult? result;
        bool isUpdatingSelection;
        bool isUpdatingEvaluationControls;
        bool isUpdatingResult;
        bool evaluationUseKelvin;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? DetailsRequested;
        public event EventHandler? ResultUpdated;

        public AnalysisResultWorkspaceControl()
        {
            displaySection = Section("Display", new Control[]
            {
                Labeled("Temperature", temperatureUnitCombo)
            });
            parameterEvaluationSection = BuildParameterEvaluationSection();

            SetEvaluationEnergyUnit(AppSettings.EnergyUnit);
            BuildLayout();
            WireEvents();
            Refresh();
        }

        public AnalysisResult? Result
        {
            get => result;
            set
            {
                if (ReferenceEquals(result, value)) return;

                result = value;
                graph.Result = value;
                DataManager.ClearResultSolutionSelection();
                ResetEvaluationTemperature();
                Refresh();
            }
        }

        public void FitToData()
        {
            graph.FitToData();
        }

        public bool UseKelvinTemperature => UseKelvin;
        public EnergyUnit DisplayEnergyUnit => SelectedEvaluationEnergyUnit();

        public void SetTemperatureDisplay(bool kelvin)
        {
            temperatureUnitCombo.SelectedIndex = kelvin ? 1 : 0;
            RefreshTable();
            graph.InvalidateVisual();
        }

        public void SetEnergyDisplay(EnergyUnit unit)
        {
            SetEvaluationEnergyUnit(unit);
            if (AppSettings.EnergyUnit != unit)
            {
                AppSettings.EnergyUnit = unit;
                AppSettings.Save();
            }

            RefreshTable();
            graph.InvalidateVisual();
            RefreshParameterEvaluation();
        }

        public void SetUncertaintyDisplay(UncertaintyDisplayStyle style)
        {
            AppSettings.UncertaintyDisplayStyle = style;
            AppSettings.Save();
            RefreshTable();
            RefreshParameterEvaluation();
        }

        public async Task UpdateResultAsync()
        {
            if (result == null || isUpdatingResult) return;

            try
            {
                isUpdatingResult = true;
                RefreshSummary();
                StatusChanged?.Invoke(this, "Updating analysis result...");

                var convergence = await AnalysisResultUpdater.UpdateAsync(result);

                Refresh();
                ResultUpdated?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, $"{convergence.Algorithm.GetProperties().ShortName} | RMSD = {convergence.Loss:G4}");
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
                StatusChanged?.Invoke(this, $"Result update failed: {ex.Message}");
                RefreshSummary();
            }
            finally
            {
                isUpdatingResult = false;
                RefreshSummary();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            DataManager.ResultSolutionSelectionDidChange += OnResultSolutionSelectionChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            DataManager.ResultSolutionSelectionDidChange -= OnResultSolutionSelectionChanged;
            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            var root = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,330"),
                Background = Solid("#F5F7FA")
            };

            var main = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                RowSpacing = 10
            };

            var graphBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Solid("#D4DAE1"),
                BorderThickness = new Thickness(1),
                Child = graph
            };
            Grid.SetRow(graphBorder, 0);

            var tableBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Solid("#D4DAE1"),
                BorderThickness = new Thickness(1),
                MinHeight = 190,
                MaxHeight = 270,
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = tableHost
                }
            };
            Grid.SetRow(tableBorder, 1);

            main.Children.Add(graphBorder);
            main.Children.Add(tableBorder);

            var inspector = new TabControl
            {
                Margin = new Thickness(10, 0, 0, 0)
            };
            inspector.Items.Add(Tab("Summary", Scroll(summaryPanel)));
            inspector.Items.Add(Tab("Experiments", Scroll(experimentsPanel)));
            inspector.Items.Add(Tab("Model", Scroll(modelPanel)));

            Grid.SetColumn(main, 0);
            Grid.SetColumn(inspector, 1);
            root.Children.Add(main);
            root.Children.Add(inspector);

            Content = root;
        }

        void WireEvents()
        {
            temperatureUnitCombo.SelectionChanged += (_, _) =>
            {
                RefreshTable();
                graph.InvalidateVisual();
            };
            evaluationTemperatureBox.LostFocus += (_, _) => RefreshParameterEvaluation();
            evaluationTemperatureBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;

                RefreshParameterEvaluation();
                e.Handled = true;
            };
            evaluationTemperatureUnitCombo.SelectionChanged += (_, _) => ChangeEvaluationTemperatureUnit();
            evaluationEnergyUnitCombo.SelectionChanged += (_, _) => ChangeEvaluationEnergyUnit();
        }

        void OnResultSolutionSelectionChanged(object? sender, SolutionInterface? e)
        {
            if (isUpdatingSelection) return;

            RefreshTable();
            graph.InvalidateVisual();
        }

        public void Refresh()
        {
            RefreshSummary();
            RefreshExperiments();
            RefreshModel();
            RefreshTable();
            graph.FitToData();
        }

        void RefreshSummary()
        {
            summaryPanel.Children.Clear();

            if (result == null)
            {
                summaryPanel.Children.Add(Text("No analysis result selected."));
                return;
            }

            var solution = result.Solution;
            var convergence = solution.Convergence;
            var report = result.ValidityReport;
            var detailsButton = new Button
            {
                Content = "Details...",
                MinHeight = 26,
                Padding = new Thickness(8, 1),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            detailsButton.Click += (_, _) => DetailsRequested?.Invoke(this, EventArgs.Empty);

            var updateButton = new Button
            {
                Content = isUpdatingResult ? "Updating..." : "Update Result",
                MinHeight = 26,
                Padding = new Thickness(8, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = !isUpdatingResult && result.Solution?.Model != null
            };
            updateButton.Click += async (_, _) => await UpdateResultAsync();

            summaryPanel.Children.Add(WorkspaceControlBuilder.Row(detailsButton, updateButton));

            summaryPanel.Children.Add(Section("Result", new Control[]
            {
                Pair("Name", result.Name),
                Pair("Model", solution.SolutionName),
                Pair("Experiments", solution.Solutions.Count.ToString(CultureInfo.CurrentCulture)),
                Pair("RMSD", solution.Loss.ToString("G4", CultureInfo.CurrentCulture))
            }));

            summaryPanel.Children.Add(Section("Solver", new Control[]
            {
                Pair("Algorithm", convergence?.Algorithm.GetProperties().Name ?? ""),
                Pair("Iterations", convergence?.Iterations.ToString(CultureInfo.CurrentCulture) ?? ""),
                Pair("Fitting", solution.UseWeightedFitting ? "Weighted injection errors" : "Unweighted"),
                Pair("Errors", solution.ErrorEstimationMethod.Description()),
                Pair("Bootstrap", solution.BootstrapIterations.ToString(CultureInfo.CurrentCulture))
            }));

            summaryPanel.Children.Add(displaySection);
            summaryPanel.Children.Add(parameterEvaluationSection);
            summaryPanel.Children.Add(BuildValiditySection(report));
            RefreshParameterEvaluation();
        }

        Border BuildParameterEvaluationSection()
        {
            return WorkspaceControlBuilder.Section(
                "Parameter Evaluation",
                WorkspaceControlBuilder.Labeled("Temperature", evaluationTemperatureBox),
                WorkspaceControlBuilder.Labeled("Temp unit", evaluationTemperatureUnitCombo),
                WorkspaceControlBuilder.Labeled("Energy", evaluationEnergyUnitCombo),
                evaluationRowsPanel);
        }

        void ResetEvaluationTemperature()
        {
            isUpdatingEvaluationControls = true;
            try
            {
                evaluationUseKelvin = evaluationTemperatureUnitCombo.SelectedIndex == 1;
                SetEvaluationEnergyUnit(AppSettings.EnergyUnit);

                if (result == null)
                    evaluationTemperatureBox.Text = "";
                else
                    SetEvaluationTemperatureText(AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result));
            }
            finally
            {
                isUpdatingEvaluationControls = false;
            }
        }

        void ChangeEvaluationTemperatureUnit()
        {
            if (isUpdatingEvaluationControls) return;

            var temperatureCelsius = TryReadEvaluationTemperatureCelsius(out var parsedTemperature)
                ? parsedTemperature
                : result == null
                    ? 25.0
                    : AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);

            evaluationUseKelvin = evaluationTemperatureUnitCombo.SelectedIndex == 1;
            SetEvaluationTemperatureText(temperatureCelsius);
            RefreshParameterEvaluation();
        }

        void ChangeEvaluationEnergyUnit()
        {
            if (isUpdatingEvaluationControls) return;

            var energyUnit = SelectedEvaluationEnergyUnit();
            if (AppSettings.EnergyUnit != energyUnit)
            {
                AppSettings.EnergyUnit = energyUnit;
                AppSettings.Save();
            }

            RefreshTable();
            graph.InvalidateVisual();
            RefreshParameterEvaluation();
            StatusChanged?.Invoke(this, "Result energy unit updated.");
        }

        void RefreshParameterEvaluation()
        {
            evaluationRowsPanel.Children.Clear();

            if (result == null)
            {
                evaluationRowsPanel.Children.Add(WorkspaceControlBuilder.Text("No analysis result selected."));
                return;
            }

            if (!TryReadEvaluationTemperatureCelsius(out var temperatureCelsius))
            {
                evaluationRowsPanel.Children.Add(WorkspaceControlBuilder.Text("Invalid evaluation temperature."));
                return;
            }

            if (temperatureCelsius < -273.15)
            {
                temperatureCelsius = -273.15;
                SetEvaluationTemperatureText(temperatureCelsius);
            }

            var evaluation = AnalysisResultParameterEvaluator.Evaluate(
                result,
                temperatureCelsius,
                SelectedEvaluationEnergyUnit(),
                AppSettings.UncertaintyDisplayStyle);

            if (!evaluation.IsAvailable)
            {
                evaluationRowsPanel.Children.Add(WorkspaceControlBuilder.Text(evaluation.Message));
                return;
            }

            foreach (var row in evaluation.Rows)
            {
                var pair = Pair(row.Label, row.Value);
                if (!string.IsNullOrWhiteSpace(row.Tooltip))
                    ToolTip.SetTip(pair, row.Tooltip);
                evaluationRowsPanel.Children.Add(pair);
            }
        }

        void RefreshExperiments()
        {
            experimentsPanel.Children.Clear();

            if (result?.Solution?.Solutions == null || result.Solution.Solutions.Count == 0)
            {
                experimentsPanel.Children.Add(Text("No experiments are included."));
                return;
            }

            foreach (var solution in result.Solution.Solutions)
            {
                var data = solution.Data;
                experimentsPanel.Children.Add(Section(data?.Name ?? "Experiment", new Control[]
                {
                    Pair("Date", data?.UIShortDateWithTime ?? ""),
                    Pair("Temperature", data == null ? "" : $"{data.MeasuredTemperature:G3} °C"),
                    Pair("Status", solution.IsValid ? "Solution valid" : "Solution invalid")
                }));
            }
        }

        void RefreshModel()
        {
            modelPanel.Children.Clear();

            if (result == null)
            {
                modelPanel.Children.Add(Text("No model selected."));
                return;
            }

            var options = result.Solution.Model.ModelOptions;
            if (options != null && options.Count > 0)
            {
                modelPanel.Children.Add(Section("Model options", options
                    .Select(option => Pair(OptionName(option.Key, option.Value), OptionValue(option.Key, option.Value)))
                    .Cast<Control>()
                    .ToArray()));
            }
            else
            {
                modelPanel.Children.Add(Section("Model options", new Control[] { Text("None") }));
            }

            var constraints = result.Solution.Model.Parameters.Constraints;
            var activeConstraints = constraints.Where(constraint => constraint.Value != VariableConstraint.None).ToList();
            if (activeConstraints.Count == 0)
            {
                modelPanel.Children.Add(Section("Constraints", new Control[] { Text("None") }));
            }
            else
            {
                modelPanel.Children.Add(Section("Constraints", activeConstraints
                    .Select(constraint => Pair(constraint.Key.GetEnumDescription(), constraint.Value.GetEnumDescription()))
                    .Cast<Control>()
                    .ToArray()));
            }
        }

        void RefreshTable()
        {
            tableHost.Children.Clear();

            if (result == null)
            {
                tableHost.Children.Add(Message("No analysis result selected."));
                return;
            }

            var table = AnalysisResultOverviewTable.Build(result, AppSettings.EnergyUnit, UseKelvin);
            if (table.Columns.Count == 0 || table.Rows.Count == 0)
            {
                tableHost.Children.Add(Message("No fitted solutions are available."));
                return;
            }

            var grid = new Grid
            {
                Background = Brushes.White
            };

            foreach (var column in table.Columns)
                grid.ColumnDefinitions.Add(new ColumnDefinition(column.PreferredWidth, GridUnitType.Pixel));

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < table.Rows.Count; i++)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var column = table.Columns[columnIndex];
                AddTableCell(grid, column.Title, columnIndex, 0, column.Alignment, isHeader: true, isSelected: false, null);
            }

            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                var row = table.Rows[rowIndex];
                var selected = ReferenceEquals(row.Solution, DataManager.SelectedResultSolution);

                for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
                {
                    var column = table.Columns[columnIndex];
                    AddTableCell(grid, row[column.Id], columnIndex, rowIndex + 1, column.Alignment, isHeader: false, selected, row.Solution);
                }
            }

            tableHost.Children.Add(grid);
        }

        Border BuildValiditySection(AnalysisResultValidityReport report)
        {
            var color = report.Status switch
            {
                AnalysisResultValidity.Valid => "#22863A",
                AnalysisResultValidity.PartialInvalid => "#B7791F",
                AnalysisResultValidity.Invalid => "#C53030",
                _ => "#B7791F"
            };

            var lines = new List<Control>
            {
                new TextBlock
                {
                    Text = ValidityTitle(report.Status),
                    Foreground = Solid(color),
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            if (report.Reasons.Count == 0)
            {
                lines.Add(Text(report.Status == AnalysisResultValidity.Valid
                    ? "Cached data matches current data."
                    : "Validity could not be determined."));
            }
            else
            {
                foreach (var reason in report.Reasons)
                    lines.Add(Text(reason));
            }

            return Section("Validity", lines.ToArray());
        }

        void AddTableCell(Grid grid, string text, int column, int row, AnalysisResultColumnAlignment alignment, bool isHeader, bool isSelected, SolutionInterface? solution)
        {
            var textBlock = new TextBlock
            {
                Text = text ?? "",
                Margin = new Thickness(8, 5),
                FontSize = isHeader ? 11 : 12,
                FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = isHeader ? Solid("#202832") : Solid("#202832"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignmentFor(alignment)
            };

            var border = new Border
            {
                BorderBrush = Solid("#E3E7EC"),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = isHeader
                    ? Solid("#F5F7FA")
                    : isSelected
                        ? Solid("#EAF1FF")
                        : row % 2 == 0 ? Solid("#FFFFFF") : Solid("#FAFBFC"),
                Child = textBlock,
                MinHeight = isHeader ? 30 : 28
            };

            if (!isHeader && solution != null)
            {
                border.Cursor = new Cursor(StandardCursorType.Hand);
                border.PointerPressed += (_, e) =>
                {
                    isUpdatingSelection = true;
                    DataManager.SelectResultSolution(solution);
                    isUpdatingSelection = false;
                    RefreshTable();
                    graph.InvalidateVisual();
                    StatusChanged?.Invoke(this, solution.Data?.Name ?? "Solution selected");
                    e.Handled = true;
                };
            }

            Grid.SetColumn(border, column);
            Grid.SetRow(border, row);
            grid.Children.Add(border);
        }

        EnergyUnit SelectedEvaluationEnergyUnit()
        {
            var index = evaluationEnergyUnitCombo.SelectedIndex;
            return index >= 0 && index < EvaluationEnergyUnits.Length
                ? EvaluationEnergyUnits[index]
                : EnergyUnit.KiloJoule;
        }

        void SetEvaluationEnergyUnit(EnergyUnit unit)
        {
            var index = Array.IndexOf(EvaluationEnergyUnits, unit);
            if (index < 0)
                index = Array.IndexOf(EvaluationEnergyUnits, EnergyUnit.KiloJoule);

            evaluationEnergyUnitCombo.SelectedIndex = Math.Max(0, index);
        }

        bool TryReadEvaluationTemperatureCelsius(out double temperatureCelsius)
        {
            temperatureCelsius = 0;
            var text = evaluationTemperatureBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var displayed) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out displayed))
            {
                return false;
            }

            temperatureCelsius = evaluationUseKelvin ? displayed - 273.15 : displayed;
            return true;
        }

        void SetEvaluationTemperatureText(double temperatureCelsius)
        {
            var displayed = evaluationUseKelvin ? temperatureCelsius + 273.15 : temperatureCelsius;
            evaluationTemperatureBox.Text = displayed.ToString("G6", CultureInfo.CurrentCulture);
        }

        bool UseKelvin => temperatureUnitCombo.SelectedIndex == 1;

        static string ValidityTitle(AnalysisResultValidity status)
        {
            return status switch
            {
                AnalysisResultValidity.Valid => "Analysis is valid",
                AnalysisResultValidity.PartialInvalid => "Partially invalid",
                AnalysisResultValidity.Invalid => "Invalid",
                _ => "Unknown status"
            };
        }

        static string OptionName(AttributeKey key, ExperimentAttribute option)
        {
            return string.IsNullOrWhiteSpace(option.OptionName)
                ? key.GetEnumDescription()
                : option.OptionName;
        }

        static string OptionValue(AttributeKey key, ExperimentAttribute option)
        {
            return key switch
            {
                AttributeKey.PreboundLigandAffinity => (1.0 / FWEMath.Pow(10.0, option.ParameterValue)).AsConcentration(AppSettings.DefaultConcentrationUnit, withunit: true),
                AttributeKey.PreboundLigandEnthalpy => new Energy(option.ParameterValue).ToFormattedString(AppSettings.EnergyUnit, true, true),
                AttributeKey.PreboundLigandConc when option.BoolValue => "From experiment attribute",
                AttributeKey.NumberOfSites1 => StoichiometryOptions.FormatAsTitle(option.DoubleValue > 0 ? option.DoubleValue : option.IntValue),
                AttributeKey.NumberOfSites2 => StoichiometryOptions.FormatAsTitle(option.DoubleValue > 0 ? option.DoubleValue : option.IntValue),
                _ => option.ToString()
            };
        }

        static HorizontalAlignment HorizontalAlignmentFor(AnalysisResultColumnAlignment alignment)
        {
            return alignment switch
            {
                AnalysisResultColumnAlignment.Left => HorizontalAlignment.Left,
                AnalysisResultColumnAlignment.Center => HorizontalAlignment.Center,
                _ => HorizontalAlignment.Right,
            };
        }

        static TabItem Tab(string header, Control content)
        {
            return new TabItem
            {
                Header = new TextBlock
                {
                    Text = header,
                    FontSize = 11,
                    TextWrapping = TextWrapping.NoWrap
                },
                Content = content
            };
        }

        static Control Scroll(Control content)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Solid("#D4DAE1"),
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Border
                    {
                        Padding = new Thickness(10),
                        Child = content
                    }
                }
            };
        }

        static Border Section(string title, Control[] controls)
        {
            var panel = new StackPanel { Spacing = 7 };
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
                BorderBrush = Solid("#E3E7EC"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 10),
                Child = panel
            };
        }

        static Border Labeled(string label, Control control)
        {
            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("92,*")
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Solid("#607080"),
                FontSize = 11
            });
            Grid.SetColumn(control, 1);
            panel.Children.Add(control);

            return new Border { Child = panel };
        }

        static Border Pair(string label, string value)
        {
            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("92,*"),
                ColumnSpacing = 8
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Solid("#607080"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Top
            });
            var valueText = new TextBlock
            {
                Text = value ?? "",
                Foreground = Solid("#202832"),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valueText, 1);
            panel.Children.Add(valueText);

            return new Border { Child = panel };
        }

        static TextBlock Text(string text = "")
        {
            return new TextBlock
            {
                Text = text ?? "",
                Foreground = Solid("#4D5A66"),
                TextWrapping = TextWrapping.Wrap
            };
        }

        static TextBlock Message(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Solid("#607080"),
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap
            };
        }

        static ComboBox Combo(string[] items, int selectedIndex, double width)
        {
            return new ComboBox
            {
                ItemsSource = items,
                SelectedIndex = selectedIndex,
                Width = width,
                MinHeight = 28,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));
    }
}
