using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace AnalysisITC.Avalonia.Controls;

/// <summary>
/// A compact, equal-width selector for a small fixed set of mutually exclusive choices.
/// </summary>
public partial class SegmentedSelector : UserControl
{
    readonly Grid optionsGrid;
    readonly List<RadioButton> choices = new();
    readonly string groupName = Guid.NewGuid().ToString("N");
    string[] options = Array.Empty<string>();
    bool isUpdatingSelection;
    int selectedIndex = -1;

    public SegmentedSelector()
    {
        InitializeComponent();
        optionsGrid = this.FindControl<Grid>("OptionsGrid")
            ?? throw new InvalidOperationException("Segmented selector layout was not initialized.");
    }

    public SegmentedSelector(IEnumerable<string> options, int selectedIndex = 0)
        : this()
    {
        SetOptions(options, selectedIndex);
    }

    /// <summary>
    /// Replaces the selector's fixed choices and selects the supplied index.
    /// </summary>
    public void SetOptions(IEnumerable<string> options, int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(options);

        var labels = options.ToArray();
        if (labels.Length < 2)
            throw new ArgumentException("A segmented selector requires at least two options.", nameof(options));
        if (labels.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Segmented selector options must have labels.", nameof(options));
        if (selectedIndex < 0 || selectedIndex >= labels.Length)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        choices.Clear();
        optionsGrid.Children.Clear();
        optionsGrid.ColumnDefinitions.Clear();
        this.options = labels;
        this.selectedIndex = -1;

        for (var index = 0; index < this.options.Length; index++)
        {
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var choice = new RadioButton
            {
                Content = this.options[index],
                GroupName = groupName,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsTabStop = index == selectedIndex,
                BorderThickness = index == 0 ? new Thickness(0) : new Thickness(1, 0, 0, 0)
            };
            choice.Classes.Add("segmented-choice");
            AutomationProperties.SetName(choice, this.options[index]);
            choice.IsCheckedChanged += OnChoiceChecked;
            choice.KeyDown += OnChoiceKeyDown;
            Grid.SetColumn(choice, index);
            optionsGrid.Children.Add(choice);
            choices.Add(choice);
        }

        SetSelectedIndex(selectedIndex, raiseSelectionChanged: false);
    }

    public event EventHandler? SelectionChanged;

    public IReadOnlyList<string> Options => options;

    public int SelectedIndex
    {
        get => selectedIndex;
        set => SetSelectedIndex(value, raiseSelectionChanged: true);
    }

    internal IReadOnlyList<RadioButton> ChoiceButtons => choices;

    void OnChoiceChecked(object? sender, RoutedEventArgs e)
    {
        if (isUpdatingSelection || sender is not RadioButton { IsChecked: true } choice) return;

        var index = choices.IndexOf(choice);
        if (index >= 0)
            SetSelectedIndex(index, raiseSelectionChanged: true);
    }

    void OnChoiceKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not RadioButton choice) return;

        var index = choices.IndexOf(choice);
        if (index < 0) return;

        var nextIndex = e.Key switch
        {
            Key.Left or Key.Up => Math.Max(0, index - 1),
            Key.Right or Key.Down => Math.Min(choices.Count - 1, index + 1),
            Key.Home => 0,
            Key.End => choices.Count - 1,
            _ => index
        };

        if (nextIndex == index && e.Key is not (Key.Left or Key.Up or Key.Right or Key.Down or Key.Home or Key.End))
            return;

        SelectedIndex = nextIndex;
        choices[nextIndex].Focus();
        e.Handled = true;
    }

    void SetSelectedIndex(int value, bool raiseSelectionChanged)
    {
        if (value < 0 || value >= choices.Count)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (selectedIndex == value) return;

        isUpdatingSelection = true;
        try
        {
            for (var index = 0; index < choices.Count; index++)
            {
                var isSelected = index == value;
                choices[index].IsChecked = isSelected;
                choices[index].IsTabStop = isSelected;
            }
        }
        finally
        {
            isUpdatingSelection = false;
        }

        selectedIndex = value;
        if (raiseSelectionChanged)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
