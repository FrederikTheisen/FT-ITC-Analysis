using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Avalonia.Analysis
{
    public sealed class AnalysisWorkspaceControl : UserControl
    {
        readonly IntegratedHeatsGraphControl graph = new IntegratedHeatsGraphControl();
        readonly TextBlock statusText = Text();
        readonly CheckBox fitCheck = Check("Fit", true);
        readonly CheckBox residualsCheck = Check("Residuals", true);
        readonly CheckBox errorBarsCheck = Check("Error bars", true);
        readonly CheckBox confidenceCheck = Check("Confidence", true);
        readonly CheckBox labelsCheck = Check("Labels", true);
        readonly CheckBox parametersCheck = Check("Parameters", true);
        readonly CheckBox excludedCheck = Check("Excluded", true);
        readonly CheckBox scaleIncludedCheck = Check("Scale included", false);
        readonly CheckBox offsetCheck = Check("Offset", true);
        readonly Button fitViewButton = Button("Fit View", 80);

        ExperimentData? experiment;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? GraphChanged;

        public AnalysisWorkspaceControl()
        {
            BuildLayout();
            WireEvents();
            ApplyOptions();
            UpdateStatus();
        }

        public ExperimentData? Experiment
        {
            get => experiment;
            set
            {
                if (ReferenceEquals(experiment, value)) return;

                UnsubscribeExperiment();
                experiment = value;
                graph.Experiment = value;
                SubscribeExperiment();
                UpdateStatus();
            }
        }

        public void FitToData()
        {
            graph.FitToData();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            UnsubscribeExperiment();
            base.OnDetachedFromVisualTree(e);
        }

        void BuildLayout()
        {
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };

            controls.Children.Add(fitViewButton);
            controls.Children.Add(fitCheck);
            controls.Children.Add(residualsCheck);
            controls.Children.Add(errorBarsCheck);
            controls.Children.Add(confidenceCheck);
            controls.Children.Add(labelsCheck);
            controls.Children.Add(parametersCheck);
            controls.Children.Add(excludedCheck);
            controls.Children.Add(scaleIncludedCheck);
            controls.Children.Add(offsetCheck);
            controls.Children.Add(statusText);

            var root = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Background = Solid("#F5F7FA")
            };

            var controlsBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Solid("#D4DAE1"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = controls
            };
            Grid.SetRow(controlsBorder, 0);

            var graphBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Solid("#D4DAE1"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 10, 0, 0),
                Child = graph
            };
            Grid.SetRow(graphBorder, 1);

            root.Children.Add(controlsBorder);
            root.Children.Add(graphBorder);
            Content = root;
        }

        void WireEvents()
        {
            fitViewButton.Click += (_, _) => graph.FitToData();
            fitCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: false);
            residualsCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: true);
            errorBarsCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: true);
            confidenceCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: false);
            labelsCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: false);
            parametersCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: false);
            excludedCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: true);
            scaleIncludedCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: true);
            offsetCheck.IsCheckedChanged += (_, _) => ApplyOptions(refit: true);

            graph.StatusChanged += (_, status) => StatusChanged?.Invoke(this, status);
            graph.GraphChanged += (_, _) =>
            {
                UpdateStatus();
                GraphChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        void SubscribeExperiment()
        {
            if (experiment == null) return;

            experiment.ProcessingUpdated += ExperimentChanged;
            experiment.SolutionChanged += ExperimentChanged;
            experiment.InjectionIncludeChanged += ExperimentChanged;
        }

        void UnsubscribeExperiment()
        {
            if (experiment == null) return;

            experiment.ProcessingUpdated -= ExperimentChanged;
            experiment.SolutionChanged -= ExperimentChanged;
            experiment.InjectionIncludeChanged -= ExperimentChanged;
        }

        void ExperimentChanged(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                graph.FitToData();
                UpdateStatus();
                GraphChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        void ApplyOptions(bool refit = false)
        {
            graph.ShowFit = fitCheck.IsChecked == true;
            graph.ShowResiduals = residualsCheck.IsChecked == true;
            graph.ShowErrorBars = errorBarsCheck.IsChecked == true;
            graph.ShowConfidenceBand = confidenceCheck.IsChecked == true;
            graph.ShowPointLabels = labelsCheck.IsChecked == true;
            graph.ShowFitParameters = parametersCheck.IsChecked == true;
            graph.ShowExcludedPoints = excludedCheck.IsChecked == true;
            graph.ScaleToIncludedPoints = scaleIncludedCheck.IsChecked == true;
            graph.DrawWithOffset = offsetCheck.IsChecked == true;

            if (refit) graph.FitToData();
            else graph.InvalidateVisual();
        }

        void UpdateStatus()
        {
            if (experiment == null)
            {
                statusText.Text = "No experiment selected";
                return;
            }

            if (!experiment.Processor.IntegrationCompleted)
            {
                statusText.Text = "Process data before analysis";
                return;
            }

            var included = experiment.Injections.FindAll(injection => injection.Include).Count;
            statusText.Text = experiment.Solution == null
                ? $"{included}/{experiment.InjectionCount} integrated points"
                : $"{included}/{experiment.InjectionCount} points with fitted solution";
        }

        static Button Button(string text, double width)
        {
            return new Button
            {
                Content = text,
                MinWidth = width,
                MinHeight = 28,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        static CheckBox Check(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        static TextBlock Text()
        {
            return new TextBlock
            {
                Foreground = Solid("#4D5A66"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));
    }
}
