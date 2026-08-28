using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class AnalysisResultUpdateDialogWindow : Window
{
    readonly ComboBox iterationCombo = new ComboBox
    {
        MinWidth = 210,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    readonly List<int?> iterationValues = new List<int?>();

    internal ComboBox IterationCombo => iterationCombo;
    internal IReadOnlyList<int?> IterationValues => iterationValues;
    internal int EffectiveStoredIterations { get; }

    internal AnalysisResultUpdateDialogWindow(AnalysisResult result)
    {
        if (!AnalysisResultUpdater.CanOverrideBootstrapIterations(result))
            throw new InvalidOperationException("Bootstrap update options require a residual-bootstrap result.");

        EffectiveStoredIterations = AnalysisResultUpdater.GetEffectiveBootstrapIterations(result);

        Title = "Update Analysis Result";
        Width = 500;
        Height = 330;
        MinWidth = 440;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        AddIterationOption(null, $"Stored setting ({EffectiveStoredIterations.ToString("N0", CultureInfo.CurrentCulture)})");
        foreach (var value in AnalysisResultUpdater.GetLargerBootstrapIterationPresets(result))
            AddIterationOption(value, value.ToString("N0", CultureInfo.CurrentCulture));
        iterationCombo.SelectedIndex = 0;

        var heading = new TextBlock
        {
            Text = "Update Analysis Result",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        };
        AppTheme.Bind(heading, TextBlock.ForegroundProperty, AppTheme.PrimaryText);

        var description = Text(
            "Rerun the complete stored fit using the current experiment data. " +
            "The existing result is replaced only after a usable fit and bootstrap calculation complete.");

        var method = ValueRow("Error method", result.Solution.ErrorEstimationMethod.Description());
        var retained = ValueRow(
            "Retained refits",
            result.Solution.BootstrapIterations.ToString("N0", CultureInfo.CurrentCulture));
        var iterations = ValueRow("Requested iterations", iterationCombo);

        var note = Text(
            "Bootstrap reruns use fresh random streams, so uncertainty values may differ from the saved result.");
        note.FontSize = 11;
        AppTheme.Bind(note, TextBlock.ForegroundProperty, AppTheme.SecondaryText);

        if (iterationValues.Count == 1)
        {
            var maximum = Text("No larger supported bootstrap preset is available.");
            maximum.FontSize = 11;
            AppTheme.Bind(maximum, TextBlock.ForegroundProperty, AppTheme.StatusWarning);
            note = new TextBlock
            {
                Text = note.Text + " " + maximum.Text,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            };
            AppTheme.Bind(note, TextBlock.ForegroundProperty, AppTheme.StatusWarning);
        }

        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);

        var update = new Button { Content = "Update Result", MinWidth = 110 };
        update.Click += (_, _) => Close(SelectedOptions());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, update },
        };

        var panel = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                heading,
                description,
                method,
                retained,
                iterations,
                note,
                buttons,
            },
        };

        var border = new Border { Padding = new Thickness(20), Child = panel };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        Content = border;
    }

    internal AnalysisResultUpdateOptions SelectedOptions()
    {
        var index = iterationCombo.SelectedIndex;
        var value = index >= 0 && index < iterationValues.Count
            ? iterationValues[index]
            : null;
        return new AnalysisResultUpdateOptions(value);
    }

    void AddIterationOption(int? value, string label)
    {
        iterationValues.Add(value);
        iterationCombo.Items.Add(label);
    }

    static TextBlock Text(string value)
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        AppTheme.Bind(text, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        return text;
    }

    static Grid ValueRow(string label, object value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,*"),
            ColumnSpacing = 12,
        };

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AppTheme.Bind(labelText, TextBlock.ForegroundProperty, AppTheme.SecondaryText);

        Control valueControl;
        if (value is Control control)
        {
            valueControl = control;
        }
        else
        {
            valueControl = new TextBlock
            {
                Text = value?.ToString() ?? string.Empty,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AppTheme.Bind(valueControl, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        }

        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(valueControl, 1);
        grid.Children.Add(labelText);
        grid.Children.Add(valueControl);
        return grid;
    }
}
