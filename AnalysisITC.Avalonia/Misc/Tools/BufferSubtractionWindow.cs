using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Tools
{
    public sealed class BufferSubtractionWindow : Window
    {
        readonly ComboBox referenceCombo = Combo(210);
        readonly ComboBox methodCombo = Combo(new[] { "Matched", "Linear", "Exp. decay" }, 210);
        readonly CheckBox focusYAxisCheck = Check("Focus Y axis on buffer data", false);
        readonly ListBox targetList = new ListBox { SelectionMode = SelectionMode.Multiple };
        readonly BufferSubtractionGraphControl graph = new BufferSubtractionGraphControl();
        readonly TextBlock referenceInfoText = Text();
        readonly TextBlock statusText = Text();
        readonly Button applyButton = Button("Apply", 82);

        readonly List<ExperimentData> experiments = DataManager.Data.ToList();

        public bool Applied { get; private set; }

        public BufferSubtractionWindow()
        {
            Title = "Buffer Subtraction";
            Width = 900;
            Height = 610;
            MinWidth = 720;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = WorkspaceBackgroundBrush;

            BuildLayout();
            PopulateReferenceExperiments();
            WireEvents();
            RefreshTargets();
            RefreshPreview();
        }

        void BuildLayout()
        {
            applyButton.Click += (_, _) => Apply();

            var inspector = InspectorPanel();
            inspector.Children.Add(Section("Reference",
                Labeled("Experiment", referenceCombo),
                Labeled("Method", methodCombo),
                focusYAxisCheck,
                referenceInfoText));
            inspector.Children.Add(Section("Targets", targetList));

            targetList.ItemTemplate = new FuncDataTemplate<ExperimentData>((data, _) => ExperimentCell(data));

            Content = WorkspaceControlBuilder.Workspace(
                ContentBorder(graph),
                Scroll(inspector),
                InspectorFooter(Section("Apply",
                    applyButton,
                    statusText)),
                useOuterMargin: true);
        }

        void PopulateReferenceExperiments()
        {
            referenceCombo.Items.Clear();
            foreach (var experiment in experiments)
            {
                referenceCombo.Items.Add(new ComboBoxItem
                {
                    Content = experiment.Name,
                    Tag = experiment
                });
            }

            referenceCombo.SelectedIndex = experiments.Count > 0 ? 0 : -1;
            methodCombo.SelectedIndex = (int)AppSettings.BufferSubtractionDefaultMethod;
        }

        void WireEvents()
        {
            referenceCombo.SelectionChanged += (_, _) =>
            {
                RefreshTargets();
                RefreshPreview();
            };
            methodCombo.SelectionChanged += (_, _) => RefreshPreview();
            focusYAxisCheck.IsCheckedChanged += (_, _) => RefreshPreview();
            targetList.SelectionChanged += (_, _) => RefreshPreview();
            graph.BufferPointIncludeChanged += (_, _) => RefreshPreview();
        }

        void RefreshTargets()
        {
            var reference = SelectedReference();
            var targets = experiments
                .Where(experiment => !ReferenceEquals(experiment, reference))
                .ToList();

            targetList.ItemsSource = targets;
            targetList.SelectedItems?.Clear();
            Validate();
        }

        void RefreshPreview()
        {
            var reference = SelectedReference();
            var targets = SelectedTargets();
            referenceInfoText.Text = ReferenceInfo(reference);

            var settings = reference == null
                ? null
                : new BufferSubtractionSettings(reference.UniqueID, SelectedMethod());
            var model = BufferSubtractionCalculator.BuildModel(reference, settings);

            graph.SetData(reference, targets, model, focusYAxisCheck.IsChecked == true);
            Validate();
        }

        void Validate()
        {
            applyButton.IsEnabled = SelectedReference() != null && SelectedTargets().Count > 0;
        }

        ExperimentData? SelectedReference()
        {
            return (referenceCombo.SelectedItem as ComboBoxItem)?.Tag as ExperimentData;
        }

        List<ExperimentData> SelectedTargets()
        {
            return targetList.SelectedItems?
                .OfType<ExperimentData>()
                .Where(target => !ReferenceEquals(target, SelectedReference()))
                .ToList() ?? new List<ExperimentData>();
        }

        BufferSubtractionMethod SelectedMethod()
        {
            return methodCombo.SelectedIndex switch
            {
                1 => BufferSubtractionMethod.Linear,
                2 => BufferSubtractionMethod.ExponentialDecay,
                _ => BufferSubtractionMethod.MatchedInjection
            };
        }

        void Apply()
        {
            try
            {
                var reference = SelectedReference();
                var targets = SelectedTargets();
                if (reference == null || targets.Count == 0)
                {
                    SetStatus("Select a reference and at least one target.");
                    return;
                }

                foreach (var target in targets)
                    target.SetBufferSubtraction(reference, SelectedMethod(), notify: false);

                reference.Include = false;
                DataManager.InvokeDataDidChange();
                DataManager.InvokeUpdateTable();
                Applied = true;
                Close(true);
            }
            catch (HandledException ex)
            {
                SetStatus(ex.Message);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        Control ExperimentCell(ExperimentData? data)
        {
            if (data == null) return Text();

            var panel = new StackPanel { Spacing = 1, Margin = new Thickness(8, 4) };
            panel.Children.Add(new TextBlock
            {
                Text = data.Name,
                FontWeight = FontWeight.SemiBold,
                Foreground = SectionHeaderBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"{data.UIShortDateWithTime} | {data.MeasuredTemperature:G3} °C | {data.SyringeConcentration.AsFormattedConcentration(true)}",
                FontSize = 12,
                Foreground = LabelBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            return panel;
        }

        static string ReferenceInfo(ExperimentData? reference)
        {
            if (reference == null) return "No reference selected.";

            var state = reference.Processor?.IntegrationCompleted == true
                ? "Processed"
                : "Not yet processed";
            return $"{reference.UIShortDateWithTime}\n{reference.MeasuredTemperature:G3} °C | [Syringe] {reference.SyringeConcentration.AsFormattedConcentration(true)} | [Cell] {reference.CellConcentration.AsFormattedConcentration(true)}\n{state}";
        }

        void SetStatus(string message)
        {
            statusText.Text = message ?? "";
            StatusBarManager.SetStatus(message ?? "", 3000);
        }
    }
}
