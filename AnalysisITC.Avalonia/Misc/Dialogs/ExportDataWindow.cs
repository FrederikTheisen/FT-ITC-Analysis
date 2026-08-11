using System;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class ExportDataWindow : Window
{
    static readonly ExportSelectionOption[] SelectionOptions =
    {
        new("Selected experiment", ExportDataSelection.SelectedData),
        new("Active experiments", ExportDataSelection.IncludedData),
        new("All experiments", ExportDataSelection.AllData)
    };

    readonly ExportAccessoryViewSettings settings;
    readonly TextBox nameBox;
    readonly ComboBox formatBox;
    readonly ComboBox selectionBox;
    readonly CheckBox correctedDataCheck;
    readonly CheckBox offsetCorrectedCheck;
    readonly TextBlock descriptionText;
    readonly TextBlock unitsText;
    readonly StackPanel optionsPanel;
    readonly TextBlock statusText;

    ExportDataWindow(ExportAccessoryViewSettings settings)
    {
        this.settings = settings;
        Title = "Export Data";
        Width = 520;
        MinWidth = 460;
        Height = 430;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        AppTheme.Bind(this, BackgroundProperty, AppTheme.PanelBackground);

        nameBox = new TextBox { Text = settings.OutputBaseName, MinWidth = 250 };
        formatBox = new ComboBox { MinWidth = 250 };
        AddFormatItem("Thermogram Data", ExportType.Data);
        AddFormatItem("Integrated Peaks", ExportType.Peaks);
        AddFormatItem("Combined Data", ExportType.InterchangeCsv);
        formatBox.Items.Add(new Separator { IsHitTestVisible = false });
        formatBox.Items.Add(new ComboBoxItem
        {
            Content = "Other programs",
            IsEnabled = false,
            FontWeight = FontWeight.SemiBold
        });
        AddFormatItem("MicroCal / SEDPHAT", ExportType.MicroCal);
        AddFormatItem("pytc", ExportType.PYTC);
        AddFormatItem("ITCsim", ExportType.ITCsim);
        SelectFormat(settings.Export);
        selectionBox = new ComboBox
        {
            ItemsSource = SelectionOptions,
            SelectedItem = SelectionOptions.First(option => option.Value == settings.Selection),
            MinWidth = 250
        };
        correctedDataCheck = Check("Export baseline-corrected trace", settings.ExportBaselineCorrectDataPoints);
        offsetCorrectedCheck = Check("Export offset-corrected peaks", settings.ExportOffsetCorrected);
        descriptionText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        unitsText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        statusText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AppTheme.Bind(statusText, TextBlock.ForegroundProperty, AppTheme.StatusError);
        optionsPanel = new StackPanel { Spacing = 6 };

        formatBox.SelectionChanged += (_, _) => RefreshOptions();
        selectionBox.SelectionChanged += (_, _) => RefreshAvailability();

        var content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                Labeled("Output name", nameBox),
                Labeled("Format", formatBox),
                Labeled("Data", selectionBox),
                new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 2), Child = descriptionText },
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = "Output units", FontWeight = FontWeight.SemiBold },
                        unitsText
                    }
                },
                optionsPanel,
                statusText
            }
        };
        AppTheme.Bind(content, Panel.BackgroundProperty, AppTheme.PanelBackground);

        var cancel = new Button { Content = "Cancel", MinWidth = 82 };
        cancel.Click += (_, _) => Close(false);
        var export = new Button { Content = "Choose Folder...", MinWidth = 110 };
        export.Click += (_, _) => Apply();
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16, 0, 16, 14),
            Children = { cancel, export }
        };

        var root = new DockPanel { LastChildFill = true };
        AppTheme.Bind(root, Panel.BackgroundProperty, AppTheme.PanelBackground);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(content);
        Content = root;
        RefreshOptions();
    }

    public static async Task<bool> ConfigureAsync(Window owner, ExportAccessoryViewSettings settings)
    {
        var dialog = new ExportDataWindow(settings);
        return await dialog.ShowDialog<bool>(owner);
    }

    void RefreshOptions()
    {
        if (formatBox.SelectedItem is not ComboBoxItem { Tag: ExportType export }) return;
        settings.Export = export;
        optionsPanel.Children.Clear();
        descriptionText.Text = export.GetProperties().Description;

        switch (export)
        {
            case ExportType.Data:
                optionsPanel.Children.Add(correctedDataCheck);
                break;
            case ExportType.Peaks:
                optionsPanel.Children.Add(offsetCorrectedCheck);
                break;
            case ExportType.ITCsim:
                optionsPanel.Children.Add(offsetCorrectedCheck);
                break;
            case ExportType.InterchangeCsv:
                optionsPanel.Children.Add(new TextBlock
                {
                    Text = "Each file places thermogram and integrated-peak columns side by side. Raw data, corrected data, fitted values, and residuals are included when available.",
                    TextWrapping = TextWrapping.Wrap
                });
                break;
        }

        RefreshAvailability();
    }

    void RefreshAvailability()
    {
        if (selectionBox.SelectedItem is not ExportSelectionOption selection) return;
        settings.Selection = selection.Value;
        settings.SetData();
        correctedDataCheck.IsEnabled = settings.BaselineCorrectionEnabled;
        offsetCorrectedCheck.IsEnabled = settings.FittedPeakExportEnabled;
        RefreshUnitDescription();
    }

    void RefreshUnitDescription()
    {
        if (formatBox.SelectedItem is ComboBoxItem { Tag: ExportType export })
            unitsText.Text = ExportFormatDescription.GetOutputUnits(export, settings.Data);
    }

    void Apply()
    {
        var name = nameBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            statusText.Text = "Enter an output name.";
            return;
        }

        settings.OutputBaseName = name;
        settings.ExportBaselineCorrectDataPoints = correctedDataCheck.IsChecked == true;
        settings.ExportOffsetCorrected = offsetCorrectedCheck.IsChecked == true;
        settings.ExportFittedPeaks = settings.Columns.HasFlag(ExportColumns.Fit);
        settings.SetData();
        if (settings.Data.Count == 0)
        {
            statusText.Text = "Select an experiment or choose Active experiments or All experiments.";
            return;
        }
        Close(true);
    }

    static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value };

    sealed class ExportSelectionOption
    {
        public ExportSelectionOption(string label, ExportDataSelection value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }
        public ExportDataSelection Value { get; }

        public override string ToString() => Label;
    }

    void AddFormatItem(string label, ExportType format)
    {
        formatBox.Items.Add(new ComboBoxItem { Content = label, Tag = format });
    }

    void SelectFormat(ExportType export)
    {
        var selected = formatBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is ExportType format && format == export)
            ?? formatBox.Items.OfType<ComboBoxItem>()
                .First(item => item.Tag is ExportType format && format == ExportType.InterchangeCsv);
        formatBox.SelectedItem = selected;
    }

    static Control Labeled(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), ColumnSpacing = 10 };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        AppTheme.Bind(text, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }
}
