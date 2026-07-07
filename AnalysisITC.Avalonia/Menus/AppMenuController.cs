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
    readonly List<(NativeMenuItem Item, AppMenuCommand Command)> nativeCommandItems = new();
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

        foreach (var (item, command) in nativeCommandItems)
        {
            item.IsEnabled = command.CanExecute(null);
            item.ToggleType = command.HasCheckState ? MenuItemToggleType.CheckBox : MenuItemToggleType.None;
            item.IsChecked = command.IsChecked;
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
        Add("exportfigure", "Export Final Figure...", window.ExportFinalFigureAsync, window.CanExportFinalFigure);
        Add("print", "Print...", window.NotImplementedAsync, () => false, gesture: new KeyGesture(Key.P, commandModifier));

        Add("undo", "Undo Delete", window.UndoDeleteAsync, window.CanUndoDelete, gesture: new KeyGesture(Key.Z, commandModifier));
        Add("duplicate", "Duplicate Data", window.DuplicateSelectedDataAsync, window.HasSelectedExperiment);
        Add("copyattributes", "Copy Attributes to All", window.CopyAttributesToAllAsync, window.SelectedExperimentHasAttributes);
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

        Add("overview", "Overview", () => window.SelectWorkspaceAsync(0), window.HasSelectedExperiment, () => window.ActiveWorkspaceIndex == 0);
        Add("process", "Process Data", () => window.SelectWorkspaceAsync(1), window.HasSelectedExperiment, () => window.ActiveWorkspaceIndex == 1);
        Add("analyze", "Analyze Data", () => window.SelectWorkspaceAsync(2), window.HasSelectedExperiment, () => window.ActiveWorkspaceIndex == 2);
        Add("figure", "Final Figure", () => window.SelectWorkspaceAsync(3), window.HasSelectedExperiment, () => window.ActiveWorkspaceIndex == 3);
        Add("resultview", "Result View", window.ShowResultViewAsync, window.HasSelectedResult, window.HasSelectedResult);

        Add("experimentdetails", "Details...", window.OpenSelectedDetailsFromMenuAsync, window.HasSelectedExperiment);
        Add("exportselecteddata", "Export Selected Data...", () => window.ExportDataAsync(selectedOnly: true), window.HasSelectedExperiment);
        Add("toggleinclude", "Enable/Disable Active", window.ToggleSelectedExperimentInclusionAsync, window.HasSelectedExperiment);
        Add("clearsolution", "Clear Solution", window.ClearSelectedExperimentSolutionAsync, window.SelectedExperimentHasSolution);
        Add("removedata", "Remove Data", window.RemoveSelectedItemAsync, window.HasSelectedExperiment);

        Add("lockprocessor", "Lock/Unlock Processor", window.ToggleSelectedProcessorLockAsync, window.HasSelectedProcessor);
        Add("copyprocesstoactive", "Copy Processing to Active", window.CopyProcessingToActiveAsync, window.HasSelectedProcessor);
        Add("copyprocesstononprocessed", "Copy Processing to Non-Processed", window.CopyProcessingToNonProcessedAsync, window.HasSelectedProcessor);

        Add("createanalysisresult", "Create Analysis Result", window.NotImplementedAsync, () => false);
        Add("autoopenresult", "Auto Open New Result", window.ToggleAutoOpenResultAsync, () => true, window.IsAutoOpenResultEnabled);
        Add("restoreanalysisdefaults", "Restore Analysis Defaults", window.RestoreAnalysisDefaultsAsync);

        Add("resultdetails", "Details...", window.OpenSelectedDetailsFromMenuAsync, window.HasSelectedResult);
        Add("copyresulttable", "Copy Result Table", window.CopyResultTableAsync, window.HasSelectedResult);
        Add("loadresultsolutions", "Load Solutions to Experiments", window.LoadSelectedResultSolutionsAsync, window.HasSelectedResult);
        Add("selectresultexperiments", "Select Result Experiments", window.SelectResultExperimentsAsync, window.HasSelectedResult);
        Add("exportresultfigures", "Export Associated Final Figures...", window.ExportFinalFigureAsync, window.CanExportFinalFigure);
        Add("about", "About FT-ITC Analysis", window.ShowAboutAsync);
        Add("preferences", "Preferences...", window.OpenPreferencesAsync, gesture: new KeyGesture(Key.OemComma, commandModifier));
        Add("quit", "Quit FT-ITC Analysis", window.QuitAsync, gesture: new KeyGesture(Key.Q, commandModifier));

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
            Command("exportfigure"),
            Command("print")));

        windowMenuNodes.Add(Menu("Edit",
            Command("undo"),
            Separator(),
            Command("duplicate"),
            Command("copyattributes"),
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

        windowMenuNodes.Add(Menu("Tools",
            Menu("Analysis",
                Command("autoopenresult"),
                Command("restoreanalysisdefaults"),
                Disabled("Parameter Display"),
                Disabled("Parameter Limits")),
            Separator(),
            Disabled("Experiment Designer"),
            Disabled("Experiment Merger"),
            Disabled("Buffer Subtraction"),
            Disabled("Analysis Result Exporter")));

        windowMenuNodes.Add(Menu("Help",
            Disabled("Help and Guide"),
            Disabled("Citation")));
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

    MenuNode Command(string id) => new(commands[id]);
    static MenuNode Disabled(string title) => new(title);
    static MenuNode Separator() => MenuNode.Separator;
    static MenuNode Menu(string title, params MenuNode[] children) => new(title, children.ToList());

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
                IsChecked = node.Command.IsChecked
            };
        }

        var item = new MenuItem
        {
            Header = node.Title,
            IsEnabled = node.Children.Count > 0
        };

        foreach (var child in node.Children)
        {
            if (child.IsSeparator)
                item.Items.Add(new Separator());
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
            return new NativeMenuItemSeparator();

        if (node.Command != null)
        {
            var commandItem = new NativeMenuItem
            {
                Header = node.Command.Title,
                Command = node.Command,
                Gesture = node.Command.Gesture,
                ToggleType = node.Command.HasCheckState ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
                IsChecked = node.Command.IsChecked,
                IsEnabled = node.Command.CanExecute(null)
            };

            if (trackNativeCommands)
                nativeCommandItems.Add((commandItem, node.Command));

            return commandItem;
        }

        var item = new NativeMenuItem
        {
            Header = node.Title,
            IsEnabled = node.Children.Count > 0
        };

        if (node.Children.Count > 0)
            item.Menu = CreateNativeMenu(node.Children, trackNativeCommands);

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

        public MenuNode(AppMenuCommand command)
        {
            Command = command;
            Title = command.Title;
            Children = new List<MenuNode>();
        }

        public MenuNode(string title)
        {
            Title = title;
            Children = new List<MenuNode>();
        }

        public MenuNode(string title, List<MenuNode> children)
        {
            Title = title;
            Children = children;
        }

        public string Title { get; }
        public AppMenuCommand? Command { get; }
        public List<MenuNode> Children { get; }
        public bool IsSeparator { get; }
    }
}
