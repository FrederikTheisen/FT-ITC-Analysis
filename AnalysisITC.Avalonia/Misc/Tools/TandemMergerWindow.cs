using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Avalonia.Workspace;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using static AnalysisITC.Avalonia.Workspace.WorkspaceControlBuilder;

namespace AnalysisITC.Avalonia.Tools
{
    public sealed class TandemMergerWindow : Window
    {
        readonly List<TandemMergeItem> items;
        readonly ListBox experimentList = new ListBox { SelectionMode = SelectionMode.Multiple };
        readonly ComboBox modeCombo = Combo(new[] { "Simple tandem", "Fixed back-mixing", "Auto back-mixing" }, 190);
        readonly TextBox deadVolumeBox = TextBox("80");
        readonly Slider mixingSlider = Slider(0, 1, 0.05, 190);
        readonly TextBlock mixingLabel = Text("20%");
        readonly Slider secondMixingSlider = Slider(0, 1, 0.05, 190);
        readonly TextBlock secondMixingLabel = Text("20%");
        readonly Slider thirdMixingSlider = Slider(0, 1, 0.05, 190);
        readonly TextBlock thirdMixingLabel = Text("20%");
        readonly CheckBox individualMixingCheck = Check(
            "Individual reload fractions",
            false,
            "Set a separate back-mixing fraction for each reload when three or four experiments are selected.");
        readonly CheckBox removeOverflowCheck = Check("Remove titrated overflow", true);
        readonly TextBlock statusText = Text();
        readonly Button createButton = Button("Create", 82);
        readonly Button cancelButton = Button("Cancel", 82);
        readonly Button moveUpButton = Button("Up", 56);
        readonly Button moveDownButton = Button("Down", 70);
        readonly ProgressBar progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 7 };

        readonly Slider[] mixingSliders;
        readonly TextBlock[] mixingLabels;
        Border firstMixingRow = null!;
        Border secondMixingRow = null!;
        Border thirdMixingRow = null!;
        TextBlock firstMixingCaption = null!;

        bool isBusy;
        public bool Created { get; private set; }

        internal ComboBox ModeComboForTesting => modeCombo;
        internal ListBox ExperimentListForTesting => experimentList;
        internal CheckBox IndividualMixingCheckForTesting => individualMixingCheck;
        internal IReadOnlyList<Slider> MixingSlidersForTesting => mixingSliders;
        internal IReadOnlyList<Control> MixingRowsForTesting => new[] { firstMixingRow, secondMixingRow, thirdMixingRow };
        internal IReadOnlyList<double>? IndividualTransitionMixingFractionsForTesting() =>
            IndividualTransitionMixingFractions(SelectedItems().Count, SelectedMode());
        internal void SelectExperimentCountForTesting(int count)
        {
            var selectedItems = experimentList.SelectedItems;
            if (selectedItems == null) return;

            var selectedCount = Math.Max(0, Math.Min(count, items.Count));
            for (var index = 0; index < items.Count; index++)
            {
                var shouldBeSelected = index < selectedCount;
                var isSelected = selectedItems.Contains(items[index]);
                if (shouldBeSelected && !isSelected)
                    selectedItems.Add(items[index]);
                else if (!shouldBeSelected && isSelected)
                    selectedItems.Remove(items[index]);
            }
        }

        public TandemMergerWindow()
        {
            mixingSliders = new[] { mixingSlider, secondMixingSlider, thirdMixingSlider };
            mixingLabels = new[] { mixingLabel, secondMixingLabel, thirdMixingLabel };

            items = DataManager.Data
                .Where(data => data.HasThermogram && !data.IsTandemExperiment)
                .Select(data => new TandemMergeItem(data))
                .ToList();

            Title = "Experiment Merger";
            Width = 780;
            Height = 590;
            MinWidth = 660;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            AppTheme.Bind(this, BackgroundProperty, AppTheme.WorkspaceBackground);

            BuildLayout();
            PopulateList(selectAll: true);
            WireEvents();
            SetAllMixingValues(0.20);
            RefreshControls();
        }

        void BuildLayout()
        {
            createButton.Click += async (_, _) => await CreateMergedExperimentAsync();
            cancelButton.Click += (_, _) => Close(false);
            var statusPanel = new StackPanel { Spacing = 4 };
            statusPanel.Children.Add(statusText);
            statusPanel.Children.Add(progressBar);

            experimentList.ItemTemplate = new FuncDataTemplate<TandemMergeItem>((item, _) => ExperimentCell(item));

            var listPanel = new DockPanel { LastChildFill = true };
            var moveButtons = Row(moveUpButton, moveDownButton);
            moveButtons.Margin = WorkspaceControlBuilder.ScrollContentPadding;
            DockPanel.SetDock(moveButtons, Dock.Bottom);
            listPanel.Children.Add(moveButtons);
            listPanel.Children.Add(experimentList);

            var inspector = InspectorPanel();
            inspector.Children.Add(Section("Merge", Labeled("Mode", modeCombo)));
            firstMixingRow = MixingRow(out firstMixingCaption, "Mixing", mixingSlider, mixingLabel);
            secondMixingRow = MixingRow(out _, "Reload 2", secondMixingSlider, secondMixingLabel);
            thirdMixingRow = MixingRow(out _, "Reload 3", thirdMixingSlider, thirdMixingLabel);
            inspector.Children.Add(Section("Back-mixing",
                Labeled("Dead vol. uL", deadVolumeBox),
                individualMixingCheck,
                firstMixingRow,
                secondMixingRow,
                thirdMixingRow,
                removeOverflowCheck));
            inspector.Children.Add(Section("Selection", Text("Select experiments in the order they were measured. Use Up/Down to reorder selected rows.")));

            Content = WorkspaceControlBuilder.Workspace(
                ContentBorder(listPanel),
                Scroll(inspector),
                InspectorFooter(Section("Create",
                    Row(cancelButton, createButton),
                    statusPanel)),
                useOuterMargin: true);
        }

        void WireEvents()
        {
            experimentList.SelectionChanged += (_, _) => RefreshControls();
            modeCombo.SelectionChanged += (_, _) => RefreshControls();
            individualMixingCheck.IsCheckedChanged += (_, _) =>
            {
                if (individualMixingCheck.IsChecked != true)
                    SetAllMixingValues(mixingSlider.Value);

                RefreshControls();
            };
            for (var index = 0; index < mixingSliders.Length; index++)
            {
                var sliderIndex = index;
                mixingSliders[index].PropertyChanged += (_, e) =>
                {
                    if (e.Property != global::Avalonia.Controls.Slider.ValueProperty) return;

                    if (sliderIndex == 0 && individualMixingCheck.IsChecked != true)
                    {
                        secondMixingSlider.Value = mixingSlider.Value;
                        thirdMixingSlider.Value = mixingSlider.Value;
                    }

                    UpdateMixingLabel(sliderIndex);
                    RefreshControls();
                };
            }
            deadVolumeBox.LostFocus += (_, _) => RefreshControls();
            removeOverflowCheck.IsCheckedChanged += (_, _) => RefreshControls();
            moveUpButton.Click += (_, _) => MoveSelected(-1);
            moveDownButton.Click += (_, _) => MoveSelected(1);
        }

        void PopulateList(bool selectAll, HashSet<string>? selectedIds = null)
        {
            experimentList.ItemsSource = null;
            experimentList.ItemsSource = items;
            var selectedItems = experimentList.SelectedItems;
            if (selectedItems == null) return;
            selectedItems.Clear();

            foreach (var item in items)
            {
                if (selectAll || selectedIds?.Contains(item.Id) == true)
                    selectedItems.Add(item);
            }
        }

        void MoveSelected(int offset)
        {
            var selectedIds = SelectedItems().Select(item => item.Id).ToHashSet();
            if (selectedIds.Count == 0) return;

            if (offset < 0)
            {
                for (var i = 1; i < items.Count; i++)
                {
                    if (!selectedIds.Contains(items[i].Id) || selectedIds.Contains(items[i - 1].Id)) continue;
                    (items[i - 1], items[i]) = (items[i], items[i - 1]);
                }
            }
            else
            {
                for (var i = items.Count - 2; i >= 0; i--)
                {
                    if (!selectedIds.Contains(items[i].Id) || selectedIds.Contains(items[i + 1].Id)) continue;
                    (items[i + 1], items[i]) = (items[i], items[i + 1]);
                }
            }

            PopulateList(selectAll: false, selectedIds);
            RefreshControls();
        }

        void RefreshControls()
        {
            var selected = SelectedItems();
            var mode = SelectedMode();
            var autoAllowed = mode != MergeMode.AutoBackMixing || selected.Count <= 3;
            var backMixing = mode != MergeMode.Simple;
            var individualAvailable = mode == MergeMode.FixedBackMixing
                && selected.Count is 3 or 4;

            if (!individualAvailable && individualMixingCheck.IsChecked == true)
                individualMixingCheck.IsChecked = false;

            var individualEnabled = individualAvailable
                && individualMixingCheck.IsChecked == true;

            deadVolumeBox.IsEnabled = backMixing && !isBusy;
            mixingSlider.IsEnabled = mode == MergeMode.FixedBackMixing && !isBusy;
            secondMixingSlider.IsEnabled = individualEnabled && !isBusy;
            thirdMixingSlider.IsEnabled = individualEnabled && !isBusy;
            individualMixingCheck.IsVisible = individualAvailable;
            individualMixingCheck.IsEnabled = individualAvailable && !isBusy;
            firstMixingCaption.Text = individualEnabled ? "Reload 1" : "Mixing";
            secondMixingRow.IsVisible = individualEnabled;
            thirdMixingRow.IsVisible = individualEnabled && selected.Count == 4;
            removeOverflowCheck.IsEnabled = backMixing && !isBusy;
            moveUpButton.IsEnabled = !isBusy && selected.Count > 0;
            moveDownButton.IsEnabled = !isBusy && selected.Count > 0;
            experimentList.IsEnabled = !isBusy;
            modeCombo.IsEnabled = !isBusy;
            createButton.IsEnabled = !isBusy && selected.Count >= 2 && autoAllowed && TryReadSettings(out _);
            cancelButton.IsEnabled = !isBusy;

            if (selected.Count < 2)
                SetStatus("Select at least two experiments.");
            else if (!autoAllowed)
                SetStatus("Auto back-mixing is available for up to three experiments.");
            else if (!TryReadSettings(out _))
                SetStatus("Invalid back-mixing settings.");
            else
                SetStatus($"{selected.Count} experiments selected.");
        }

        async Task CreateMergedExperimentAsync()
        {
            var selected = SelectedExperiments();
            if (selected.Count < 2 || !TryReadSettings(out var settings)) return;

            isBusy = true;
            progressBar.Value = 0;
            RefreshControls();

            try
            {
                ExperimentData merged;
                var mode = SelectedMode();

                if (mode == MergeMode.AutoBackMixing)
                {
                    SetStatus("Scanning tandem back-mixing...");
                    var bestPoint = await Task.Run(() => TandemMixingScanner.FindBestAdaptive(
                        selected,
                        settings.Copy(),
                        (completed, total) => Dispatcher.UIThread.Post(() =>
                        {
                            progressBar.Value = total <= 0 ? 0 : completed / (double)total;
                            SetStatus($"Scanning tandem back-mixing... {100 * progressBar.Value:0}%");
                        })));

                    if (bestPoint == null)
                        throw new InvalidOperationException("The tandem back-mixing scan did not produce a valid fit.");

                    SetStatus("Creating merged experiment...");
                    progressBar.Value = 1;
                    merged = TandemConcatenation.ConcatTandemWithBackMixing(selected, settings, bestPoint.TransitionMixingFractions);
                }
                else if (mode == MergeMode.FixedBackMixing)
                {
                    SetStatus("Creating merged experiment...");
                    var transitionMixingFractions = IndividualTransitionMixingFractions(selected.Count, mode);
                    merged = transitionMixingFractions == null
                        ? TandemConcatenation.ConcatTandemWithBackMixing(selected, settings)
                        : TandemConcatenation.ConcatTandemWithBackMixing(selected, settings, transitionMixingFractions);
                }
                else
                {
                    SetStatus("Creating merged experiment...");
                    merged = TandemConcatenation.ConcatTandem(selected);
                }

                await merged.Processor.ProcessData();
                DataManager.AddData(merged);
                Created = true;
                Close(true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            finally
            {
                isBusy = false;
                RefreshControls();
            }
        }

        bool TryReadSettings(out TandemConcatenation.BackMixingSettings settings)
        {
            settings = new TandemConcatenation.BackMixingSettings
            {
                UseBackMixingMethod = SelectedMode() != MergeMode.Simple,
                MixingFraction = mixingSlider.Value,
                DidRemoveOverflow = removeOverflowCheck.IsChecked == true,
                RemoveOverflowVolume = 0
            };

            if (!double.TryParse(deadVolumeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var deadVolumeUl))
                return false;

            settings.DeadVolume = Math.Max(0, deadVolumeUl) * 1e-6;
            return true;
        }

        List<TandemMergeItem> SelectedItems()
        {
            return experimentList.SelectedItems?.OfType<TandemMergeItem>().ToList() ?? new List<TandemMergeItem>();
        }

        List<ExperimentData> SelectedExperiments()
        {
            var selectedIds = SelectedItems().Select(item => item.Id).ToHashSet();
            return items
                .Where(item => selectedIds.Contains(item.Id))
                .Select(item => item.Data)
                .ToList();
        }

        MergeMode SelectedMode()
        {
            return modeCombo.SelectedIndex switch
            {
                1 => MergeMode.FixedBackMixing,
                2 => MergeMode.AutoBackMixing,
                _ => MergeMode.Simple
            };
        }

        IReadOnlyList<double>? IndividualTransitionMixingFractions(int selectedExperimentCount, MergeMode mode)
        {
            if (mode != MergeMode.FixedBackMixing
                || individualMixingCheck.IsChecked != true
                || selectedExperimentCount is not (3 or 4))
                return null;

            return mixingSliders
                .Take(selectedExperimentCount - 1)
                .Select(slider => slider.Value)
                .ToList();
        }

        void SetAllMixingValues(double value)
        {
            foreach (var slider in mixingSliders)
                slider.Value = value;

            for (var index = 0; index < mixingLabels.Length; index++)
                UpdateMixingLabel(index);
        }

        void UpdateMixingLabel(int index)
        {
            mixingLabels[index].Text = $"{100 * mixingSliders[index].Value:0}%";
        }

        Border MixingRow(out TextBlock caption, string text, Slider slider, TextBlock valueLabel)
        {
            caption = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            AppTheme.Bind(caption, TextBlock.ForegroundProperty, AppTheme.MutedText);

            valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
            valueLabel.VerticalAlignment = VerticalAlignment.Center;
            valueLabel.TextWrapping = TextWrapping.NoWrap;
            valueLabel.Margin = new Thickness(0);
            ToolTip.SetTip(slider, text switch
            {
                "Reload 2" => "Back-mixing fraction after experiment 2 and before experiment 3.",
                "Reload 3" => "Back-mixing fraction after experiment 3 and before experiment 4.",
                _ => "Shared back-mixing fraction, or Reload 1 when individual fractions are enabled."
            });

            var field = FieldWithSuffix(slider, valueLabel, 56);
            var panel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"{WorkspaceControlBuilder.RowLabelWidth},*"),
                ColumnSpacing = WorkspaceControlBuilder.RowSpacing
            };
            panel.Children.Add(caption);
            Grid.SetColumn(field, 1);
            panel.Children.Add(field);

            return new Border
            {
                Margin = WorkspaceControlBuilder.ControlMargin,
                Child = panel
            };
        }

        Control ExperimentCell(TandemMergeItem? item)
        {
            if (item == null) return Text();

            var data = item.Data;
            var panel = new StackPanel { Spacing = 1, Margin = new Thickness(8, 4) };
            var name = new TextBlock
            {
                Text = data.Name,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            AppTheme.Bind(name, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
            panel.Children.Add(name);
            var details = new TextBlock
            {
                Text = $"{data.UIShortDateWithTime} | {data.MeasuredTemperature:G3} °C | {data.SyringeConcentration.AsFormattedConcentration(true)}",
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            AppTheme.Bind(details, TextBlock.ForegroundProperty, AppTheme.MutedText);
            panel.Children.Add(details);
            return panel;
        }

        void SetStatus(string message)
        {
            statusText.Text = message ?? "";
        }

        sealed class TandemMergeItem
        {
            public TandemMergeItem(ExperimentData data)
            {
                Data = data;
                Id = data.UniqueID;
            }

            public string Id { get; }
            public ExperimentData Data { get; }
        }

        enum MergeMode
        {
            Simple,
            FixedBackMixing,
            AutoBackMixing
        }
    }
}
