using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Dialogs;
using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Details;

internal sealed class AttributeOperationsWindow : Window
{
    enum AttributeSelection
    {
        All,
        Individual
    }

    enum TargetSelection
    {
        All,
        Active,
        Specific,
        NameToken
    }

    readonly ExperimentData source;
    readonly ComboBox attributeCombo = new ComboBox { MinWidth = 300 };
    readonly ComboBox targetCombo = new ComboBox { MinWidth = 300 };
    readonly ComboBox experimentCombo = new ComboBox { MinWidth = 300 };
    readonly TextBox tokenBox = new TextBox { MinWidth = 300 };
    readonly TextBlock targetInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock statusText = new TextBlock { TextWrapping = TextWrapping.Wrap };
    readonly Button copyButton = new Button { Content = "Copy Attributes", MinWidth = 118 };
    readonly Button clearButton = new Button { Content = "Clear Source Attributes", MinWidth = 145 };
    readonly List<ExperimentData> targets;

    public AttributeOperationsWindow(ExperimentData source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        targets = DataManager.Data.Where(data => data != source).OrderBy(data => data.Name).ToList();

        Title = "Attribute Operations";
        Width = 570;
        Height = 430;
        MinWidth = 520;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        AppTheme.Bind(this, BackgroundProperty, AppTheme.WorkspaceBackground);

        PopulateChoices();
        BuildLayout();
        targetCombo.SelectionChanged += (_, _) => UpdateTargetControls();
        tokenBox.TextChanged += (_, _) => UpdateTargetControls();
        copyButton.Click += (_, _) => Copy();
        clearButton.Click += async (_, _) => await ClearSourceAsync();
        UpdateTargetControls();
    }

    public static Task<bool> ShowAsync(Window owner, ExperimentData source)
    {
        return new AttributeOperationsWindow(source).ShowDialog<bool>(owner);
    }

    void PopulateChoices()
    {
        attributeCombo.Items.Add(new ComboBoxItem
        {
            Content = "All attributes",
            Tag = AttributeSelection.All
        });

        foreach (var attribute in source.Attributes)
        {
            attributeCombo.Items.Add(new ComboBoxItem
            {
                Content = AttributeTitle(attribute),
                Tag = attribute
            });
        }

        attributeCombo.SelectedIndex = 0;

        AddTargetChoice("All other experiments", TargetSelection.All);
        AddTargetChoice("Active experiments", TargetSelection.Active);
        AddTargetChoice("Specific experiment", TargetSelection.Specific);
        AddTargetChoice("Experiment names containing…", TargetSelection.NameToken);
        targetCombo.SelectedIndex = 0;

        foreach (var experiment in targets)
        {
            experimentCombo.Items.Add(new ComboBoxItem
            {
                Content = ExperimentTitle(experiment),
                Tag = experiment
            });
        }

        if (experimentCombo.Items.Count > 0)
            experimentCombo.SelectedIndex = 0;
    }

    void AddTargetChoice(string title, TargetSelection selection)
    {
        targetCombo.Items.Add(new ComboBoxItem
        {
            Content = title,
            Tag = selection
        });
    }

    void BuildLayout()
    {
        var title = new TextBlock
        {
            Text = "Copy experiment attributes",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        AppTheme.Bind(title, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

        var sourceLabel = Note($"Source: {ExperimentTitle(source)}");
        var explanation = Note("Choose all attributes or one attribute, then select the experiments that should receive a copy.");

        var form = new StackPanel { Spacing = 9 };
        form.Children.Add(title);
        form.Children.Add(sourceLabel);
        form.Children.Add(explanation);
        form.Children.Add(Labeled("Attributes", attributeCombo));
        form.Children.Add(Labeled("Targets", targetCombo));
        form.Children.Add(experimentCombo);
        form.Children.Add(tokenBox);
        form.Children.Add(targetInfo);
        form.Children.Add(statusText);

        var cancel = new Button { Content = "Cancel", MinWidth = 82 };
        cancel.Click += (_, _) => Close(false);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { clearButton, cancel, copyButton }
        };

        var border = new Border
        {
            Padding = new Thickness(16),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children = { footer, form }
            }
        };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = border;
    }

    void UpdateTargetControls()
    {
        var target = SelectedTargetSelection();
        experimentCombo.IsVisible = target == TargetSelection.Specific;
        tokenBox.IsVisible = target == TargetSelection.NameToken;
        tokenBox.PlaceholderText = "Enter a name token";

        var matchingTargets = MatchingTargets(target);
        targetInfo.Text = target switch
        {
            TargetSelection.All => $"{targets.Count} other experiment{(targets.Count == 1 ? "" : "s")} available.",
            TargetSelection.Active => $"{matchingTargets.Count} active experiment{(matchingTargets.Count == 1 ? "" : "s")} available.",
            TargetSelection.Specific => experimentCombo.Items.Count == 0 ? "No other experiments available." : "One experiment will receive the copy.",
            TargetSelection.NameToken => $"{matchingTargets.Count} matching experiment{(matchingTargets.Count == 1 ? "" : "s")}.",
            _ => ""
        };

        copyButton.IsEnabled = matchingTargets.Count > 0 &&
            (target != TargetSelection.NameToken || !string.IsNullOrWhiteSpace(tokenBox.Text));
    }

    TargetSelection SelectedTargetSelection()
    {
        return targetCombo.SelectedItem is ComboBoxItem { Tag: TargetSelection selection }
            ? selection
            : TargetSelection.All;
    }

    ExperimentAttribute? SelectedAttribute()
    {
        return attributeCombo.SelectedItem is ComboBoxItem { Tag: ExperimentAttribute attribute }
            ? attribute
            : null;
    }

    List<ExperimentData> MatchingTargets(TargetSelection selection)
    {
        return selection switch
        {
            TargetSelection.All => targets,
            TargetSelection.Active => targets.Where(data => data.Include).ToList(),
            TargetSelection.Specific => experimentCombo.SelectedItem is ComboBoxItem { Tag: ExperimentData experiment }
                ? new List<ExperimentData> { experiment }
                : new List<ExperimentData>(),
            TargetSelection.NameToken => string.IsNullOrWhiteSpace(tokenBox.Text)
                ? new List<ExperimentData>()
                : targets.Where(data => (data.Name ?? "").IndexOf(tokenBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList(),
            _ => new List<ExperimentData>()
        };
    }

    void Copy()
    {
        var attribute = SelectedAttribute();
        var selection = SelectedTargetSelection();

        switch (selection)
        {
            case TargetSelection.All:
                if (attribute == null) DataManager.CopySelectedAttributesToAll();
                else DataManager.CopySelectedAttributeToAll(attribute);
                break;
            case TargetSelection.Active:
                if (attribute == null) DataManager.CopySelectedAttributesToActive();
                else DataManager.CopySelectedAttributeToActive(attribute);
                break;
            case TargetSelection.Specific:
                if (experimentCombo.SelectedItem is not ComboBoxItem { Tag: ExperimentData experiment }) return;
                if (attribute == null) DataManager.CopySelectedAttributesToExperiment(experiment);
                else DataManager.CopySelectedAttributeToExperiment(attribute, experiment);
                break;
            case TargetSelection.NameToken:
                if (attribute == null) DataManager.CopySelectedAttributesToNameToken(tokenBox.Text ?? "");
                else DataManager.CopySelectedAttributeToNameToken(attribute, tokenBox.Text ?? "");
                break;
        }

        Close(true);
    }

    async Task ClearSourceAsync()
    {
        if (AppSettings.ConfirmRemoveDelete && !await ConfirmationDialogWindow.ConfirmAsync(
                this,
                "Clear Attributes",
                $"Are you sure you want to remove all attributes from {ExperimentTitle(source)}?",
                "Keep",
                "Clear"))
            return;

        source.ClearAttributes();
        DataManager.InvokeUpdateDataViewCells();
        DataManager.InvokeUpdateTable();
        StatusBarManager.SetStatus($"Cleared attributes from {ExperimentTitle(source)}", 3000);
        Close(true);
    }

    static string AttributeTitle(ExperimentAttribute attribute)
    {
        var value = attribute.GetDisplayValue(DataManager.Current);
        return string.IsNullOrWhiteSpace(value)
            ? attribute.GetDisplayName()
            : $"{attribute.GetDisplayName()}: {value}";
    }

    static string ExperimentTitle(ExperimentData experiment)
    {
        return string.IsNullOrWhiteSpace(experiment.Name) ? experiment.FileName : experiment.Name;
    }

    static TextBlock Note(string text)
    {
        var note = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 2)
        };
        AppTheme.Bind(note, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
        return note;
    }

    static Control Labeled(string label, Control control)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("145,*"),
            ColumnSpacing = 8
        };
        grid.Children.Add(Note(label));
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }
}
