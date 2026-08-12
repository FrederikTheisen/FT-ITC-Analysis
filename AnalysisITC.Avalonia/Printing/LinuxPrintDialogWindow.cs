using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using AnalysisITC.Avalonia.Styling;

namespace AnalysisITC.Avalonia.Printing;

internal sealed class LinuxPrintDialogWindow : Window
{
    readonly IReadOnlyList<CupsPrinter> printers;
    readonly PrintSize sourceSize;
    readonly ComboBox printerBox = new ComboBox { MinWidth = 280 };
    readonly ComboBox mediaBox = new ComboBox { MinWidth = 220 };
    readonly ComboBox orientationBox = new ComboBox { MinWidth = 160 };
    readonly ComboBox colorBox = new ComboBox { MinWidth = 160 };
    readonly NumericUpDown copiesBox = new NumericUpDown { Minimum = 1, Maximum = 999, Value = 1, MinWidth = 100 };
    readonly Button printButton = new Button { Content = "Print", MinWidth = 84 };

    LinuxPrintDialogWindow(IReadOnlyList<CupsPrinter> printers, PrintSize sourceSize)
    {
        this.printers = printers;
        this.sourceSize = sourceSize;

        Title = "Print Graph";
        Width = 500;
        Height = 360;
        MinWidth = 460;
        MinHeight = 330;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var printer in printers) printerBox.Items.Add(printer);
        if (printers.Count > 0)
            printerBox.SelectedIndex = Math.Max(0, printers.ToList().FindIndex(printer => printer.IsDefault));
        else
            printerBox.PlaceholderText = "No CUPS printers available";
        printerBox.IsEnabled = printers.Count > 0;
        printButton.IsEnabled = printers.Count > 0;
        orientationBox.Items.Add("Automatic");
        orientationBox.Items.Add("Portrait");
        orientationBox.Items.Add("Landscape");
        orientationBox.SelectedIndex = 0;
        printerBox.SelectionChanged += (_, _) => RefreshCapabilities();

        var cancel = new Button { Content = "Cancel", MinWidth = 84 };
        cancel.Click += (_, _) => Close(null);
        var savePdf = new Button { Content = "Save as PDF…", MinWidth = 112 };
        savePdf.Click += (_, _) => Close(new LinuxPrintDialogResult(null, true));
        printButton.Click += (_, _) => Submit();

        var form = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 16,
            RowSpacing = 12
        };
        AddRow(form, 0, "Printer", printerBox);
        AddRow(form, 1, "Paper", mediaBox);
        AddRow(form, 2, "Orientation", orientationBox);
        AddRow(form, 3, "Copies", copiesBox);
        AddRow(form, 4, "Color", colorBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, savePdf, printButton }
        };

        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(form);

        var border = new Border { Padding = new Thickness(20), Child = panel };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        Content = border;
        RefreshCapabilities();
    }

    public static Task<LinuxPrintDialogResult?> ShowAsync(
        Window owner,
        IReadOnlyList<CupsPrinter> printers,
        PrintSize sourceSize)
        => new LinuxPrintDialogWindow(printers, sourceSize).ShowDialog<LinuxPrintDialogResult?>(owner);

    void RefreshCapabilities()
    {
        if (printerBox.SelectedItem is not CupsPrinter printer) return;

        mediaBox.Items.Clear();
        mediaBox.Items.Add("Printer default");
        foreach (var media in printer.Media) mediaBox.Items.Add(media);
        var defaultIndex = printer.Media.ToList().FindIndex(media => media.Name == printer.DefaultMedia);
        mediaBox.SelectedIndex = defaultIndex < 0 ? 0 : defaultIndex + 1;

        colorBox.Items.Clear();
        colorBox.Items.Add("Printer default");
        foreach (var color in printer.ColorModes) colorBox.Items.Add(color);
        var defaultColorIndex = printer.ColorModes.ToList().FindIndex(color => color.Value == printer.DefaultColorMode);
        colorBox.SelectedIndex = defaultColorIndex < 0 ? 0 : defaultColorIndex + 1;
        colorBox.IsEnabled = printer.ColorModes.Count > 0;
    }

    void Submit()
    {
        if (printerBox.SelectedItem is not CupsPrinter printer) return;

        var orientation = orientationBox.SelectedIndex switch
        {
            1 => LinuxPrintOrientation.Portrait,
            2 => LinuxPrintOrientation.Landscape,
            _ => sourceSize.Width >= sourceSize.Height
                ? LinuxPrintOrientation.Landscape
                : LinuxPrintOrientation.Portrait
        };
        Close(new LinuxPrintDialogResult(
            new LinuxPrintOptions(
                printer,
                mediaBox.SelectedItem as CupsMedia,
                orientation,
                Math.Max(1, (int)(copiesBox.Value ?? 1)),
                colorBox.SelectedItem as CupsColorMode),
            false));
    }

    static void AddRow(Grid grid, int row, string label, Control field)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 100
        };
        AppTheme.Bind(text, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(text);
        grid.Children.Add(field);
    }
}

internal sealed record LinuxPrintDialogResult(LinuxPrintOptions? PrintOptions, bool SaveAsPdf);
