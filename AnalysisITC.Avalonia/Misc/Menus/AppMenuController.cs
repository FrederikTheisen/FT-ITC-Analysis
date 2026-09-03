using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;

namespace AnalysisITC.Avalonia.Menus;

internal sealed class AppMenuController
{
    readonly MainWindow window;
    readonly Dictionary<string, AppMenuCommand> commands = new();
    readonly List<MenuNode> applicationMenuNodes = new();
    readonly List<MenuNode> windowMenuNodes = new();
    readonly List<(NativeMenuItem Item, AppMenuCommand Command, MenuNode Node)> nativeCommandItems = new();
    readonly List<(NativeMenuItem Item, MenuNode Node)> nativeMenuItems = new();
    readonly List<(NativeMenuItemSeparator Item, MenuNode Node)> nativeSeparatorItems = new();
    bool nativeMenuInstalled;

    public AppMenuController(MainWindow window)
    {
        this.window = window;
        BuildCommands();
        BuildMenu();
    }

    public void Install()
    {
        foreach (var command in commands.Values.Where(command => command.Gesture != null))
        {
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = command.Gesture!,
                Command = command
            });
        }

        if (OperatingSystem.IsMacOS())
        {
            nativeCommandItems.Clear();
            nativeMenuItems.Clear();
            nativeSeparatorItems.Clear();
            NativeMenu.SetMenu(window, CreateNativeMenu(windowMenuNodes, trackNativeCommands: true));
            nativeMenuInstalled = true;
        }

        Refresh();
    }

    public void Refresh()
    {
        foreach (var command in commands.Values)
            command.RaiseCanExecuteChanged();

        RefreshWindowMenu();
        RefreshNativeMenuState();
    }

    void RefreshWindowMenu()
    {
        if (OperatingSystem.IsMacOS())
        {
            window.MenuHost.IsVisible = false;
            return;
        }

        window.MenuHost.IsVisible = true;
        window.MenuHost.Items.Clear();
        foreach (var item in WindowMenuNodes(includeApplicationMenu: true).Select(CreateMenuItem))
            window.MenuHost.Items.Add(item);
    }

    void RefreshNativeMenuState()
    {
        if (!nativeMenuInstalled) return;

        foreach (var (item, command, node) in nativeCommandItems)
        {
            item.IsVisible = node.IsVisible;
            item.IsEnabled = command.CanExecute(null);
            item.ToggleType = command.HasCheckState ? MenuItemToggleType.CheckBox : MenuItemToggleType.None;
            item.IsChecked = command.IsChecked;
        }

        foreach (var (item, node) in nativeSeparatorItems)
            item.IsVisible = node.IsVisible;

        for (var i = nativeMenuItems.Count - 1; i >= 0; i--)
        {
            var (item, node) = nativeMenuItems[i];
            item.IsVisible = node.IsVisible;
            item.IsEnabled = node.IsEnabled && node.Children.Any(child => !child.IsSeparator && child.IsVisible);
        }
    }

    void BuildCommands()
    {
        var commandModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        Add("open", "Open...", window.OpenFilesFromMenuAsync, gesture: new KeyGesture(Key.O, commandModifier));
        Add("save", "Save", window.SaveDocumentAsync, window.HasDocumentContent, gesture: new KeyGesture(Key.S, commandModifier));
        Add("saveas", "Save As...", window.SaveDocumentAsAsync, window.HasDocumentContent, gesture: new KeyGesture(Key.S, commandModifier | KeyModifiers.Shift));
        Add("saveselected", "Save Selected...", window.SaveSelectedAsync, window.HasSelectedItem);
        Add("clearall", "Remove All Data/Results", window.ClearDataWithConfirmationAsync, window.HasDocumentContent);
        Add("exportdata", "Export Data...", () => window.ExportDataAsync(selectedOnly: false), window.HasDataLoaded, gesture: new KeyGesture(Key.E, commandModifier));
        Add("exportpeaks", "Export Integrated Peaks...", window.ExportPeaksAsync, window.HasAnyProcessedData);
        Add("print", "Print...", window.PrintActiveGraphAsync, window.CanPrintActiveGraph, gesture: new KeyGesture(Key.P, commandModifier));

        Add("undo", "Undo Delete", window.UndoDeleteAsync, window.CanUndoDelete, gesture: new KeyGesture(Key.Z, commandModifier));
        Add("duplicate", "Duplicate Data", window.DuplicateSelectedDataAsync, window.HasSelectedExperiment);
        Add("copyattributes", "Copy Attributes to All", window.CopyAttributesToAllAsync, window.SelectedExperimentHasAttributes);
        Add("attributeoperations", "Attribute Operations...", window.OpenAttributeOperationsAsync, window.SelectedExperimentHasAttributes);
        Add("clearattributes", "Clear Attributes", window.ClearSelectedAttributesAsync, window.SelectedExperimentHasAttributes);
        Add("clearprocessing", "Clear Processing/Results", window.ClearProcessingResultsAsync, window.HasAnyResults);
        Add("enableall", "Enable All", () => window.SetAllExperimentInclusionAsync(true), window.CanEnableAnyExperiment);
        Add("disableall", "Disable All", () => window.SetAllExperimentInclusionAsync(false), window.CanDisableAnyExperiment);
        Add("invertactive", "Invert Active", window.InvertExperimentInclusionAsync, window.HasDataLoaded, gesture: new KeyGesture(Key.I, commandModifier));

        Add("sortname", "By Name", () => window.SortDataAsync(AnalysisITC.Core.Application.DataManager.SortMode.Name), window.HasDocumentContent);
        Add("sortdate", "By Date", () => window.SortDataAsync(AnalysisITC.Core.Application.DataManager.SortMode.Date), window.HasDocumentContent);
        Add("sorttemperature", "By Temperature", () => window.SortDataAsync(AnalysisITC.Core.Application.DataManager.SortMode.Temperature), window.HasDataLoaded);
        Add("sorttype", "By Type", () => window.SortDataAsync(AnalysisITC.Core.Application.DataManager.SortMode.Type), window.HasDocumentContent);
        Add("sortionic", "By Ionic Strength", () => window.SortDataAsync(AnalysisITC.Core.Application.DataManager.SortMode.IonicStrength), window.HasExperimentsWithAttributes);
        Add("sortprotonation", "By Protonation Enthalpy", () => window.SortDataAsync(AnalysisITC.Core.Application.DataManager.SortMode.ProtonationEnthalpy), window.HasExperimentsWithAttributes);

        Add("experimentdetails", "Details...", window.OpenSelectedDetailsFromMenuAsync, window.HasSelectedExperiment);
        Add("exportselecteddata", "Export Selected Data...", () => window.ExportDataAsync(selectedOnly: true), window.HasSelectedExperiment);
        Add("toggleinclude", "Active", window.ToggleSelectedExperimentInclusionAsync, window.SelectedExperimentCanToggleInclusion, window.SelectedExperimentIsIncluded);
        Add("clearsolution", "Clear Solution", window.ClearSelectedExperimentSolutionAsync, window.SelectedExperimentHasSolution);
        Add("removedata", "Remove Data", window.RemoveSelectedItemAsync, window.HasSelectedExperiment);

        Add("experimentdesigner", "Experiment Designer...", window.OpenExperimentDesignerAsync);
        Add("experimentmerger", "Experiment Merger...", window.OpenTandemMergerAsync, window.CanOpenTandemMergerTool);
        Add("buffersubtraction", "Buffer Subtraction...", window.OpenBufferSubtractionToolAsync, window.CanOpenBufferSubtractionTool);
        Add("analysisresultexporter", "Analysis Result Exporter...", window.OpenAnalysisResultExporterAsync, window.HasAnyResults);
        Add("analysisreport", "Analysis Report...", window.OpenAnalysisReportAsync, window.HasAnyResults);
        Add("supportingfigurecanvas", "Supporting Figure...", window.OpenSupportingFigureCanvasAsync, window.HasDocumentContent);

        Add("resultdetails", "Details...", window.OpenSelectedDetailsFromMenuAsync, window.HasSelectedResult);
        Add("updateresult", "Update Result", window.UpdateSelectedResultAsync, window.SelectedResultCanUpdate);
        Add("copyresulttable", "Copy Result Table", window.CopyResultTableAsync, window.SelectedResultHasSolution);
        Add("loadresultsolutions", "Load Solutions to Experiments", window.LoadSelectedResultSolutionsAsync, window.SelectedResultHasMemberSolutions);
        Add("selectresultexperiments", "Set Active Experiments", window.SelectResultExperimentsAsync, window.SelectedResultHasMemberSolutions);
        Add("exportresultfigures", "Export Associated Final Figures...", window.ExportFinalFigureAsync, window.SelectedResultHasMemberSolutions);
        Add("exportanalysisreport", "Export Analysis Report...", window.OpenAnalysisReportAsync, window.HasSelectedResult);
        Add("removeresult", "Remove Result", window.RemoveSelectedItemAsync, window.HasSelectedResult);
        Add("about", "About FT-ITC Analysis", window.ShowAboutAsync);
        Add("preferences", "Preferences...", window.OpenPreferencesAsync, gesture: new KeyGesture(Key.OemComma, commandModifier));
        Add("quit", "Quit FT-ITC Analysis", window.QuitAsync, gesture: new KeyGesture(Key.Q, commandModifier));
        Add("helpguide", "Help and Guide", window.OpenHelpGuideAsync, gesture: new KeyGesture(Key.F1, KeyModifiers.None));
        Add("technicalhelp", "Technical Details", window.OpenTechnicalHelpAsync);
        Add("citation", "Citation", window.OpenCitationAsync);
        Add("support", "Contact Support...", window.OpenSupportAsync);
        Add("copysupportreport", "Copy Support Report", window.CopySupportReportAsync);
        Add("openwebsite", "FT-ITC Analysis Website", window.OpenWebsiteAsync);
        Add("openviewer", "FT-ITC Project Viewer", window.OpenViewerAsync);
        Add("opensourcerepository", "Open Source Repository", window.OpenSourceRepositoryAsync);

    }

    void BuildMenu()
    {
        applicationMenuNodes.Add(Command("about"));
        applicationMenuNodes.Add(Command("preferences"));
        applicationMenuNodes.Add(Separator());
        applicationMenuNodes.Add(Command("quit"));

        windowMenuNodes.Add(Menu("File",
            Command("open"),
            Separator(),
            Command("save"),
            Command("saveas"),
            Command("saveselected"),
            Separator(),
            Command("clearall"),
            Separator(),
            Command("exportdata"),
            Command("exportpeaks"),
            Command("print")));

        windowMenuNodes.Add(Menu("Edit",
            Command("undo"),
            Separator(),
            Command("duplicate"),
            Command("copyattributes"),
            Command("attributeoperations"),
            Command("clearattributes"),
            Command("clearprocessing"),
            Separator(),
            Command("enableall"),
            Command("disableall"),
            Command("invertactive"),
            Separator(),
            Menu("Sort",
                Command("sortname"),
                Command("sortdate"),
                Command("sorttemperature"),
                Command("sorttype"),
                Separator(),
                Command("sortionic"),
                Command("sortprotonation"))));

        windowMenuNodes.Add(Menu("Selection", window.HasSelectedItem, SelectionNodes(includeSelectionOnlyTools: true)));

        windowMenuNodes.Add(Menu("Tools",
            Command("experimentdesigner"),
            Separator(),
            Command("experimentmerger"),
            Command("buffersubtraction"),
            Separator(),
            Command("analysisresultexporter"),
            Command("analysisreport"),
            Command("supportingfigurecanvas")));

        windowMenuNodes.Add(Menu("Help",
            Command("helpguide"),
            Command("technicalhelp"),
            Separator(),
            Command("openwebsite"),
            Command("openviewer"),
            Command("opensourcerepository"),
            Separator(),
            Command("citation"),
            Separator(),
            Command("support"),
            Command("copysupportreport")));
    }

    void Add(
        string id,
        string title,
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Func<bool>? isChecked = null,
        KeyGesture? gesture = null)
    {
        commands[id] = new AppMenuCommand(id, title, execute, canExecute, isChecked, gesture);
    }

    MenuNode Command(string id, Func<bool>? isVisible = null) => new(commands[id], isVisible);
    static MenuNode Separator() => MenuNode.Separator;
    static MenuNode Separator(Func<bool> isVisible) => new(isVisible);
    static MenuNode Menu(string title, params MenuNode[] children) => new(title, children.ToList());
    static MenuNode Menu(string title, Func<bool> isEnabled, params MenuNode[] children) => new(title, children.ToList(), isEnabled);

    MenuNode[] SelectionNodes(bool includeSelectionOnlyTools)
    {
        var nodes = new List<MenuNode>();
        var experimentVisible = window.HasSelectedExperiment;
        var resultVisible = window.HasSelectedResult;

        nodes.AddRange(new[]
        {
            Command("experimentdetails", experimentVisible),
            Command("toggleinclude", experimentVisible),
            Separator(experimentVisible),
            Command("attributeoperations", experimentVisible),
            Command("clearattributes", experimentVisible),
            Separator(experimentVisible),
            Command("saveselected", experimentVisible),
            Command("exportselecteddata", experimentVisible),
            Command("duplicate", experimentVisible),
            Separator(experimentVisible),
            Command("clearsolution", experimentVisible),
        });

        if (includeSelectionOnlyTools)
        {
            nodes.Add(Separator(experimentVisible));
            nodes.Add(Command("experimentmerger", experimentVisible));
            nodes.Add(Command("buffersubtraction", experimentVisible));
        }

        nodes.Add(Separator(experimentVisible));
        nodes.Add(Command("removedata", experimentVisible));

        nodes.AddRange(new[]
        {
            Command("resultdetails", resultVisible),
            Command("updateresult", resultVisible),
            Separator(resultVisible),
            Command("saveselected", resultVisible),
            Command("copyresulttable", resultVisible),
            Separator(resultVisible),
            Command("loadresultsolutions", resultVisible),
            Command("selectresultexperiments", resultVisible),
            Command("exportresultfigures", resultVisible),
            Command("exportanalysisreport", resultVisible),
            Separator(resultVisible),
            Command("removeresult", resultVisible),
        });

        return nodes.ToArray();
    }

    internal MenuFlyout CreateSelectionContextFlyout() => CreateSelectionFlyout(includeSelectionOnlyTools: false);

    internal MenuFlyout CreateSelectionFlyout(bool includeSelectionOnlyTools)
    {
        var flyout = new MenuFlyout();
        foreach (var node in SelectionNodes(includeSelectionOnlyTools).Where(node => node.IsVisible))
        {
            if (node.IsSeparator)
                flyout.Items.Add(new Separator());
            else
                flyout.Items.Add(CreateMenuItem(node));
        }

        return flyout;
    }

    IEnumerable<MenuNode> WindowMenuNodes(bool includeApplicationMenu)
    {
        if (includeApplicationMenu)
            yield return Menu("FT-ITC Analysis", applicationMenuNodes.ToArray());

        foreach (var node in windowMenuNodes)
            yield return node;
    }

    MenuItem CreateMenuItem(MenuNode node)
    {
        if (node.Command != null)
        {
            return new MenuItem
            {
                Header = node.Command.Title,
                Command = node.Command,
                InputGesture = node.Command.Gesture,
                ToggleType = node.Command.HasCheckState ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
                IsChecked = node.Command.IsChecked,
                IsVisible = node.IsVisible
            };
        }

        var item = new MenuItem
        {
            Header = node.Title,
            IsEnabled = node.IsEnabled && node.Children.Any(child => !child.IsSeparator && child.IsVisible),
            IsVisible = node.IsVisible
        };

        foreach (var child in node.Children)
        {
            if (child.IsSeparator)
                item.Items.Add(new Separator { IsVisible = child.IsVisible });
            else
                item.Items.Add(CreateMenuItem(child));
        }

        return item;
    }

    NativeMenu CreateNativeMenu(IEnumerable<MenuNode> nodes, bool trackNativeCommands)
    {
        var menu = new NativeMenu();

        foreach (var node in nodes)
            menu.Items.Add(CreateNativeItem(node, trackNativeCommands));

        return menu;
    }

    NativeMenuItemBase CreateNativeItem(MenuNode node, bool trackNativeCommands)
    {
        if (node.IsSeparator)
        {
            var separator = new NativeMenuItemSeparator { IsVisible = node.IsVisible };
            if (trackNativeCommands)
                nativeSeparatorItems.Add((separator, node));
            return separator;
        }

        if (node.Command != null)
        {
            var commandItem = new NativeMenuItem
            {
                Header = node.Command.Title,
                Command = node.Command,
                Gesture = node.Command.Gesture,
                ToggleType = node.Command.HasCheckState ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
                IsChecked = node.Command.IsChecked,
                IsEnabled = node.Command.CanExecute(null),
                IsVisible = node.IsVisible
            };

            if (trackNativeCommands)
                nativeCommandItems.Add((commandItem, node.Command, node));

            return commandItem;
        }

        var item = new NativeMenuItem
        {
            Header = node.Title,
            IsEnabled = node.IsEnabled && node.Children.Any(child => !child.IsSeparator && child.IsVisible),
            IsVisible = node.IsVisible
        };

        if (node.Children.Count > 0)
            item.Menu = CreateNativeMenu(node.Children, trackNativeCommands);

        if (trackNativeCommands)
            nativeMenuItems.Add((item, node));

        return item;
    }

    sealed class MenuNode
    {
        public static readonly MenuNode Separator = new();

        MenuNode()
        {
            IsSeparator = true;
            Title = "";
            Children = new List<MenuNode>();
        }

        public MenuNode(Func<bool> isVisible) : this()
        {
            this.isVisible = isVisible;
        }

        public MenuNode(AppMenuCommand command, Func<bool>? isVisible = null)
        {
            Command = command;
            Title = command.Title;
            Children = new List<MenuNode>();
            this.isVisible = isVisible;
        }

        public MenuNode(string title)
        {
            Title = title;
            Children = new List<MenuNode>();
        }

        public MenuNode(string title, List<MenuNode> children, Func<bool>? isEnabled = null)
        {
            Title = title;
            Children = children;
            this.isEnabled = isEnabled;
        }

        public string Title { get; }
        public AppMenuCommand? Command { get; }
        public List<MenuNode> Children { get; }
        public bool IsSeparator { get; }
        readonly Func<bool>? isVisible;
        public bool IsVisible => isVisible?.Invoke() ?? true;
        readonly Func<bool>? isEnabled;
        public bool IsEnabled => isEnabled?.Invoke() ?? true;
    }
}
