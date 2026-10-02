using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using AnalysisITC.Avalonia.Styling;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Avalonia.Dialogs;

internal sealed class ExperimentIdentifiersWindow : Window
{
    const double SelectionColumnWidth = 32;
    const double IdentifierColumnWidth = 190;
    const double ColumnGap = 10;
    // Fluent scroll bars overlay the content; keep row content clear of them.
    const double ScrollBarAllowance = 14;

    readonly List<Draft> drafts;
    readonly bool showsTable;
    readonly CheckBox selectAll = new() { MinWidth = 0, Padding = new Thickness(0) };
    readonly TextBox sharedCellId = IdentifierBox("Cell sample/batch ID");
    readonly TextBox sharedSyringeId = IdentifierBox("Syringe sample/batch ID");
    readonly Button fillBlanks = DialogButton("Fill Blanks", 100);
    readonly Button replaceValues = DialogButton("Replace Values", 120);
    bool updatingSelection;

    internal CheckBox SelectAllCheck => selectAll;
    internal TextBox SharedCellIdBox => sharedCellId;
    internal Button FillBlanksButton => fillBlanks;
    internal Button ReplaceValuesButton => replaceValues;

    public ExperimentIdentifiersWindow(IEnumerable<ExperimentData> experiments, bool reviewsImport = false)
    {
        drafts = experiments.Select(experiment => new Draft(experiment)).ToList();
        showsTable = drafts.Count > 1;
        Title = reviewsImport ? "Experiment Identifiers" : "Edit Experiment Identifiers";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        Width = showsTable ? 900 : 520;
        MinWidth = showsTable ? 760 : 440;
        MaxHeight = 680;
        CanResize = showsTable;

        var heading = new TextBlock
        {
            Text = !reviewsImport ? "Experiment Identifiers"
                : showsTable ? "Identify Imported Experiments" : "Identify Imported Experiment",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        };
        AppTheme.Bind(heading, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        var description = SecondaryText(reviewsImport
            ? "Add laboratory identifiers for the imported data. All fields are optional and values may repeat. Skip keeps the imported data without identifiers."
            : "All fields are optional and values may repeat between experiments.");
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 14), Children = { heading, description } };

        var skip = DialogButton(reviewsImport ? "Skip" : "Cancel", 90);
        skip.IsCancel = true;
        skip.Click += (_, _) => Close();
        var apply = DialogButton("Apply", 90);
        apply.IsDefault = true;
        apply.Click += (_, _) => Apply();
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { skip, apply }
        };

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(showsTable ? BuildTable() : BuildForm());

        var border = new Border { Padding = new Thickness(20), Child = root };
        AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.PanelBackground);
        Content = border;
    }

    public static Task ShowAsync(Window owner, IEnumerable<ExperimentData> experiments) =>
        new ExperimentIdentifiersWindow(experiments, reviewsImport: true).ShowDialog(owner);

    public static Task ShowAsync(Window owner, ExperimentData experiment) =>
        new ExperimentIdentifiersWindow(new[] { experiment }).ShowDialog(owner);

    Control BuildForm()
    {
        var draft = drafts.Single();
        draft.ExperimentId = IdentifierBox("Optional", draft.Experiment.ExternalExperimentId);
        draft.CellId = IdentifierBox("Optional", draft.Experiment.CellSampleId);
        draft.SyringeId = IdentifierBox("Optional", draft.Experiment.SyringeSampleId);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
        };
        AddFormRow(grid, 0, "Experiment", ExperimentName(draft.Experiment));
        AddFormRow(grid, 1, "Experiment ID", draft.ExperimentId);
        AddFormRow(grid, 2, "Cell sample/batch ID", draft.CellId);
        AddFormRow(grid, 3, "Syringe sample/batch ID", draft.SyringeId);
        return grid;
    }

    static void AddFormRow(Grid grid, int row, string label, Control control)
    {
        var text = SecondaryText(label);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(0, 0, 16, 0);
        control.Margin = new Thickness(0, row == 0 ? 0 : 4, 0, 4);
        Grid.SetRow(text, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    Control BuildTable()
    {
        selectAll.IsChecked = true;
        selectAll.Click += (_, _) => SetAllSelected(selectAll.IsChecked == true);
        ToolTip.SetTip(selectAll, "Select all");

        var header = TableRow();
        header.Children.Add(Cell(selectAll, 0));
        header.Children.Add(Cell(HeaderText("Experiment"), 1));
        header.Children.Add(Cell(HeaderText("Experiment ID"), 2));
        header.Children.Add(Cell(HeaderText("Cell sample/batch ID"), 3));
        header.Children.Add(Cell(HeaderText("Syringe sample/batch ID"), 4));
        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        AppTheme.Bind(headerBorder, Border.BackgroundProperty, AppTheme.TableHeaderBackground);
        AppTheme.Bind(headerBorder, Border.BorderBrushProperty, AppTheme.SectionBorder);

        var rows = new StackPanel();
        for (var index = 0; index < drafts.Count; index++)
            rows.Children.Add(BuildDraftRow(drafts[index], index));
        var scroll = new ScrollViewer
        {
            Content = rows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var table = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        table.Children.Add(headerBorder);
        table.Children.Add(scroll);
        var tableBorder = new Border { Child = table, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        AppTheme.Bind(tableBorder, Border.BorderBrushProperty, AppTheme.SectionBorder);

        var shared = BuildSharedRow();
        DockPanel.SetDock(shared, Dock.Bottom);
        var body = new DockPanel();
        body.Children.Add(shared);
        body.Children.Add(tableBorder);
        UpdateSharedActions();
        return body;
    }

    Control BuildDraftRow(Draft draft, int index)
    {
        draft.Selection = new CheckBox { IsChecked = true, MinWidth = 0, Padding = new Thickness(0) };
        draft.Selection.IsCheckedChanged += (_, _) => SelectionChanged();
        draft.ExperimentId = IdentifierBox("Optional", draft.Experiment.ExternalExperimentId);
        draft.CellId = IdentifierBox("Optional", draft.Experiment.CellSampleId);
        draft.SyringeId = IdentifierBox("Optional", draft.Experiment.SyringeSampleId);

        var row = TableRow();
        row.Children.Add(Cell(draft.Selection, 0));
        row.Children.Add(Cell(ExperimentName(draft.Experiment), 1));
        row.Children.Add(Cell(draft.ExperimentId, 2));
        row.Children.Add(Cell(draft.CellId, 3));
        row.Children.Add(Cell(draft.SyringeId, 4));
        var border = new Border { Child = row };
        if (index % 2 == 1) AppTheme.Bind(border, Border.BackgroundProperty, AppTheme.TableAlternateRow);
        return border;
    }

    Control BuildSharedRow()
    {
        var label = SecondaryText("Shared sample IDs for checked rows");
        label.VerticalAlignment = VerticalAlignment.Center;
        sharedCellId.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) UpdateSharedActions(); };
        sharedSyringeId.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) UpdateSharedActions(); };
        fillBlanks.Click += (_, _) => ApplyShared(sharedCellId.Text, sharedSyringeId.Text, replace: false);
        replaceValues.Click += (_, _) => ApplyShared(sharedCellId.Text, sharedSyringeId.Text, replace: true);
        ToolTip.SetTip(fillBlanks, "Enter the shared IDs only where checked rows are blank.");
        ToolTip.SetTip(replaceValues, "Overwrite the cell or syringe IDs of all checked rows.");

        var inputs = TableRow();
        inputs.Margin = new Thickness(1, 12, 1, 0);
        var labelCell = Cell(label, 0);
        Grid.SetColumnSpan(labelCell, 3);
        inputs.Children.Add(labelCell);
        inputs.Children.Add(Cell(sharedCellId, 3));
        inputs.Children.Add(Cell(sharedSyringeId, 4));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, ScrollBarAllowance + 1, 0),
            Children = { fillBlanks, replaceValues }
        };
        return new StackPanel { Children = { inputs, buttons } };
    }

    static Grid TableRow() => new()
    {
        ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(SelectionColumnWidth, GridUnitType.Pixel),
            new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 140 },
            new ColumnDefinition(IdentifierColumnWidth + ColumnGap, GridUnitType.Pixel),
            new ColumnDefinition(IdentifierColumnWidth + ColumnGap, GridUnitType.Pixel),
            new ColumnDefinition(IdentifierColumnWidth + ColumnGap + ScrollBarAllowance, GridUnitType.Pixel),
        },
        MinHeight = 40,
    };

    static Control Cell(Control content, int column)
    {
        content.VerticalAlignment = VerticalAlignment.Center;
        content.Margin = column switch
        {
            0 => new Thickness(10, 6, 0, 6),
            4 => new Thickness(ColumnGap, 6, ScrollBarAllowance, 6),
            _ => new Thickness(column == 1 ? 6 : ColumnGap, 6, 0, 6),
        };
        Grid.SetColumn(content, column);
        return content;
    }

    static TextBlock ExperimentName(ExperimentData experiment)
    {
        var text = new TextBlock
        {
            Text = experiment.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrWhiteSpace(experiment.FileName)) ToolTip.SetTip(text, experiment.FileName);
        AppTheme.Bind(text, TextBlock.ForegroundProperty, AppTheme.PrimaryText);
        return text;
    }

    static TextBlock HeaderText(string text)
    {
        var block = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        AppTheme.Bind(block, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
        return block;
    }

    static TextBlock SecondaryText(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        AppTheme.Bind(block, TextBlock.ForegroundProperty, AppTheme.SecondaryText);
        return block;
    }

    static TextBox IdentifierBox(string placeholder, string? value = null) => new()
    {
        Text = value ?? "",
        PlaceholderText = placeholder,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    static Button DialogButton(string text, double minWidth) => new()
    {
        Content = text,
        MinWidth = minWidth,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    void SetAllSelected(bool selected)
    {
        updatingSelection = true;
        foreach (var draft in drafts)
            if (draft.Selection != null) draft.Selection.IsChecked = selected;
        updatingSelection = false;
        SelectionChanged();
    }

    void SelectionChanged()
    {
        if (updatingSelection) return;
        var selected = drafts.Count(IsSelected);
        selectAll.IsChecked = selected == drafts.Count ? true : selected == 0 ? false : null;
        UpdateSharedActions();
    }

    void UpdateSharedActions()
    {
        var hasValue = !string.IsNullOrWhiteSpace(sharedCellId.Text) || !string.IsNullOrWhiteSpace(sharedSyringeId.Text);
        var enabled = hasValue && drafts.Any(IsSelected);
        fillBlanks.IsEnabled = enabled;
        replaceValues.IsEnabled = enabled;
    }

    // A single experiment has no selection column; shared actions then apply to it.
    static bool IsSelected(Draft draft) => draft.Selection?.IsChecked != false;

    internal void ApplyShared(string? cellId, string? syringeId, bool replace)
    {
        var cellValue = cellId?.Trim() ?? "";
        var syringeValue = syringeId?.Trim() ?? "";
        foreach (var draft in drafts.Where(IsSelected))
        {
            draft.CellId!.Text = ApplySharedValue(draft.CellId.Text, cellValue, replace);
            draft.SyringeId!.Text = ApplySharedValue(draft.SyringeId.Text, syringeValue, replace);
        }
    }

    internal static string ApplySharedValue(string? current, string? shared, bool replace)
    {
        var value = shared?.Trim() ?? "";
        if (value.Length == 0) return current ?? "";
        return replace || string.IsNullOrWhiteSpace(current) ? value : current;
    }

    internal void Apply()
    {
        ApplyDrafts();
        Close(true);
    }

    internal void ApplyDrafts()
    {
        foreach (var draft in drafts)
        {
            draft.Experiment.ExternalExperimentId = draft.ExperimentId?.Text ?? "";
            draft.Experiment.CellSampleId = draft.CellId?.Text ?? "";
            draft.Experiment.SyringeSampleId = draft.SyringeId?.Text ?? "";
        }
    }

    sealed class Draft
    {
        public Draft(ExperimentData experiment) => Experiment = experiment;
        public ExperimentData Experiment { get; }
        public CheckBox? Selection { get; set; }
        public TextBox? ExperimentId { get; set; }
        public TextBox? CellId { get; set; }
        public TextBox? SyringeId { get; set; }
    }
}
