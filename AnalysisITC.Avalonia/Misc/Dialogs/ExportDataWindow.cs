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
    readonly ExportAccessoryViewSettings settings;
    readonly TextBox nameBox;
    readonly ComboBox formatBox;
    readonly ComboBox selectionBox;
    readonly CheckBox unifyTimeAxisCheck;
    readonly CheckBox correctedDataCheck;
    readonly CheckBox fittedValuesCheck;
    readonly CheckBox offsetCorrectedCheck;
    readonly TextBlock descriptionText;
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

        nameBox = new TextBox { Text = settings.OutputBaseName, MinWidth = 250 };
        formatBox = new ComboBox
        {
            ItemsSource = new[]
            {
                ExportType.InterchangeCsv,
                ExportType.Data,
                ExportType.Peaks,
                ExportType.CSV,
                ExportType.ITCsim,
                ExportType.PYTC,
                ExportType.MicroCal
            },
            SelectedItem = settings.Export,
            MinWidth = 250
        };
        selectionBox = new ComboBox
        {
            ItemsSource = Enum.GetValues<ExportDataSelection>(),
            SelectedItem = settings.Selection,
            MinWidth = 250
        };
        unifyTimeAxisCheck = Check("Unify time axis", settings.UnifyTimeAxis);
        correctedDataCheck = Check("Export baseline-corrected trace", settings.ExportBaselineCorrectDataPoints);
        fittedValuesCheck = Check("Include fitted model values", settings.Columns.HasFlag(ExportColumns.Fit));
        offsetCorrectedCheck = Check("Export offset-corrected peaks", settings.ExportOffsetCorrected);
        descriptionText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
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
        if (formatBox.SelectedItem is not ExportType export) return;
        settings.Export = export;
        optionsPanel.Children.Clear();
        descriptionText.Text = export.GetProperties().Description;

        switch (export)
        {
            case ExportType.Data:
                optionsPanel.Children.Add(unifyTimeAxisCheck);
                optionsPanel.Children.Add(correctedDataCheck);
                break;
            case ExportType.Peaks:
            case ExportType.CSV:
                optionsPanel.Children.Add(fittedValuesCheck);
                optionsPanel.Children.Add(offsetCorrectedCheck);
                break;
            case ExportType.ITCsim:
                optionsPanel.Children.Add(offsetCorrectedCheck);
                break;
            case ExportType.InterchangeCsv:
                optionsPanel.Children.Add(new TextBlock
                {
                    Text = "Each file contains typed trace and injection rows with raw/corrected power, integrated heats, SD, fitted values, and residuals when available.",
                    TextWrapping = TextWrapping.Wrap
                });
                break;
        }

        RefreshAvailability();
    }

    void RefreshAvailability()
    {
        if (selectionBox.SelectedItem is not ExportDataSelection selection) return;
        settings.Selection = selection;
        settings.SetData();
        correctedDataCheck.IsEnabled = settings.BaselineCorrectionEnabled;
        fittedValuesCheck.IsEnabled = settings.FittedPeakExportEnabled;
        offsetCorrectedCheck.IsEnabled = settings.FittedPeakExportEnabled;
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
        settings.UnifyTimeAxis = unifyTimeAxisCheck.IsChecked == true;
        settings.ExportBaselineCorrectDataPoints = correctedDataCheck.IsChecked == true;
        settings.ExportOffsetCorrected = offsetCorrectedCheck.IsChecked == true;
        settings.Columns = fittedValuesCheck.IsChecked == true
            ? settings.Columns | ExportColumns.Fit
            : settings.Columns & ~ExportColumns.Fit;
        settings.ExportFittedPeaks = settings.Columns.HasFlag(ExportColumns.Fit);
        settings.SetData();
        Close(true);
    }

    static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value };

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
