using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using AnalysisITC.Avalonia.FinalFigure;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class FinalFigureExportOwnershipTests
{
    [AvaloniaTheory]
    [InlineData(0)] // Final Figure has never been visited.
    [InlineData(1)] // Return to Results from Final Figure.
    [InlineData(2)] // Return to Results after switching back to Overview.
    public void AssociatedMenuExportUsesWindowRegardlessOfTabHistory(int history)
    {
        using var test = new Fixture(history);
        test.Figure.LineWidthForTesting.Value = 3.5m;
        test.Figure.SmoothnessForTesting.SelectedIndex = 0;
        var before = test.Figure.GetOptionsSnapshot();

        Assert.Equal(history == 1, TopLevel.GetTopLevel(test.Figure) != null);
        test.ExportFromMenu();

        Assert.Equal(1, test.Picker.FolderCalls);
        Assert.Same(test.Window, Assert.Single(test.Factory.Owners));
        Assert.Same(test.Result, test.Figure.SelectedItem);
        Assert.Same(test.Result, DataManager.SelectedResult);
        Assert.Equal(history == 1 ? 3 : 0, test.Tabs.SelectedIndex);
        Assert.False(test.Tabs.IsVisible);
        var after = test.Figure.GetOptionsSnapshot();
        Assert.Equal(before.FitLineWidth, after.FitLineWidth);
        Assert.Equal(before.FitLineSmoothness, after.FitLineSmoothness);
        Assert.Empty(test.Messages);
        Assert.Equal("2 final figures exported", Assert.Single(test.Statuses));
        var folder = Path.Combine(test.Directory, FigureExportPlanner.SanitizeFileName(test.Result.Name)!);
        Assert.Equal(new[] { "First.pdf", "Second.pdf" }, System.IO.Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToArray());
        foreach (var path in System.IO.Directory.GetFiles(folder))
            Assert.StartsWith("%PDF", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void CancellingAssociatedPickerIsQuietAndWritesNothing()
    {
        using var test = new Fixture(0);
        test.Picker.Cancel = true;
        test.ExportFromMenu();
        Assert.Equal(1, test.Picker.FolderCalls);
        Assert.Empty(System.IO.Directory.GetFileSystemEntries(test.Directory));
        Assert.Empty(test.Statuses);
        Assert.Empty(test.Messages);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableOrThrowingAssociatedPickerReportsOneLoggedFailure(bool throws)
    {
        using var test = new Fixture(0);
        test.Picker.CanPickFolder = throws;
        test.Picker.Failure = throws ? new IOException("figure-owner-picker-failure") : null;
        var logBefore = AppEventHandler.GetLogReport();
        test.ExportFromMenu();
        Assert.Equal(throws ? 1 : 0, test.Picker.FolderCalls);
        Assert.Single(test.Statuses);
        var message = Assert.Single(test.Messages);
        Assert.Equal(HandledException.Severity.Error, message.Level);
        Assert.DoesNotContain("exported", test.Statuses[0], StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(logBefore, AppEventHandler.GetLogReport());
        Assert.Contains(throws ? "figure-owner-picker-failure" : "unavailable", message.Message);
        Assert.Empty(System.IO.Directory.GetFileSystemEntries(test.Directory));
    }

    [AvaloniaFact]
    public void MissingOwnerReportsFailureInsteadOfSilentlyReturning()
    {
        using var test = new Fixture(0);
        // The no-argument entry point must still work when attached and explain
        // why it cannot start when invoked on this detached control.
        test.Figure.ExportPdfAsync().GetAwaiter().GetResult();
        Assert.Single(test.Statuses);
        Assert.Contains("window", Assert.Single(test.Messages).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, test.Picker.FolderCalls);
    }

    [AvaloniaFact]
    public void NoArgumentExportUsesAttachedWindow()
    {
        using var test = new Fixture(1);
        test.Picker.Cancel = true;
        test.Figure.ExportPdfAsync().GetAwaiter().GetResult();
        Assert.Equal(1, test.Picker.FolderCalls);
        Assert.Same(test.Window, Assert.Single(test.Factory.Owners));
        Assert.Empty(test.Messages);
    }

    [AvaloniaFact]
    public void ExplicitOwnerTakesPrecedenceOverAttachedWindow()
    {
        using var test = new Fixture(1);
        var otherOwner = new Window();
        try
        {
            test.Picker.Cancel = true;
            test.Figure.ExportPdfAsync(otherOwner).GetAwaiter().GetResult();
            Assert.Equal(1, test.Picker.FolderCalls);
            Assert.Same(otherOwner, Assert.Single(test.Factory.Owners));
            Assert.Same(test.Window, TopLevel.GetTopLevel(test.Figure));
            Assert.Empty(test.Messages);
        }
        finally { otherOwner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExperimentDispatchUsesExplicitWindowAndChecksSaveCapability(bool canSave)
    {
        using var test = new Fixture(0);
        test.Window.FindControl<ListBox>("ItemsList")!.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(TopLevel.GetTopLevel(test.Figure));
        test.Picker.CanSave = canSave;
        test.Window.ExportFinalFigureAsync().GetAwaiter().GetResult();
        Assert.Same(test.Window, Assert.Single(test.Factory.Owners));
        Assert.Equal(canSave ? 1 : 0, test.Picker.SaveCalls);
        if (canSave)
        {
            Assert.Empty(test.Messages);
            Assert.Empty(test.Statuses);
        }
        else
        {
            Assert.Contains("File selection is unavailable", Assert.Single(test.Messages).Message);
            Assert.Single(test.Statuses);
        }
        Assert.Empty(System.IO.Directory.GetFileSystemEntries(test.Directory));
    }

    sealed class Fixture : IDisposable
    {
        readonly IDisposable services;
        readonly FluentTheme theme = new();
        readonly IExportPromptService originalPrompt = PlatformServices.ExportPromptService;
        readonly PublicationFigureModelSelection originalSelection = FinalFigureModelPresentation.Selection;
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("figure-owner-").FullName;
        public MainWindow Window { get; }
        public FinalFigureWorkspaceControl Figure { get; }
        public TabControl Tabs { get; }
        public AnalysisResult Result { get; }
        public RecordingPicker Picker { get; }
        public PickerFactory Factory { get; }
        public List<string> Statuses { get; } = new();
        public List<HandledException> Messages { get; } = new();

        public Fixture(int history)
        {
            DataManager.Clear(DataClearMode.ResetSession);
            var localWindow = new Window();
            var folder = localWindow.StorageProvider.TryGetFolderFromPathAsync(new Uri(Directory)).GetAwaiter().GetResult();
            Assert.NotNull(folder);
            localWindow.Close();
            Picker = new RecordingPicker(folder);
            Factory = new PickerFactory(Picker);
            services = RegisterPickerFactory(Factory);
            Application.Current!.Styles.Add(theme);
            PlatformServices.RegisterExportPromptService(new ApproveExport());
            FinalFigureModelPresentation.Selection = PublicationFigureModelSelection.BindingModel;
            var experiments = new[] { Experiment("First"), Experiment("Second") };
            var models = experiments.Select(data => data.Model).ToList();
            var global = new GlobalModel(models)
            {
                Parameters = new GlobalModelParameters(),
                ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None }
            };
            foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
            global.Solution = new GlobalSolution(new GlobalSolver { Model = global },
                models.Select(model => model.Solution).ToList(), Converged(), reconstructBootstrap: false);
            Result = new AnalysisResult(global.Solution);
            DataManager.AddData(experiments.Cast<ITCDataContainer>().Append(Result).ToArray());
            Window = new MainWindow(false);
            Window.SetStartupReady();
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            Figure = Window.FindControl<FinalFigureWorkspaceControl>("FinalFigureWorkspace")!;
            Tabs = Window.FindControl<TabControl>("WorkspaceTabs")!;
            var items = Window.FindControl<ListBox>("ItemsList")!;
            items.SelectedIndex = -1;
            if (history > 0)
            {
                items.SelectedIndex = 0;
                Tabs.SelectedIndex = 3;
                Dispatcher.UIThread.RunJobs();
                Assert.Same(Window, TopLevel.GetTopLevel(Figure));
                if (history == 2)
                {
                    Tabs.SelectedIndex = 0;
                    Dispatcher.UIThread.RunJobs();
                }
            }
            items.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Figure.StatusChanged += OnStatus;
            AppEventHandler.ShowAppMessage += OnMessage;
        }

        public void ExportFromMenu()
        {
            var menu = Window.MenuController.CreateSelectionContextFlyout();
            var item = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Export Associated Final Figures..."));
            Assert.True(item.Command!.CanExecute(null));
            item.Command.Execute(null); // The fake picker completes synchronously.
        }

        void OnStatus(object? sender, string status) => Statuses.Add(status);
        void OnMessage(object? sender, HandledException message) => Messages.Add(message);

        public void Dispose()
        {
            Figure.StatusChanged -= OnStatus;
            AppEventHandler.ShowAppMessage -= OnMessage;
            DocumentDirtyTracker.MarkClean();
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            DataManager.Clear(DataClearMode.ResetSession);
            FinalFigureModelPresentation.Selection = originalSelection;
            PlatformServices.RegisterExportPromptService(originalPrompt);
            Application.Current!.Styles.Remove(theme);
            services.Dispose();
            Picker.Folder.Dispose();
            System.IO.Directory.Delete(Directory, true);
        }
    }

    static IDisposable RegisterPickerFactory(IStorageProviderFactory factory)
    {
        // Avalonia 12 omits these public runtime registration APIs from its
        // reference assembly. Use the factory rather than private window fields.
        var locator = typeof(AvaloniaLocator);
        var scope = (IDisposable)locator.GetMethod("EnterScope")!.Invoke(null, null)!;
        var current = locator.GetProperty("CurrentMutable")!.GetValue(null);
        var binding = locator.GetMethod("Bind")!.MakeGenericMethod(typeof(IStorageProviderFactory)).Invoke(current, null)!;
        binding.GetType().GetMethod("ToConstant")!.MakeGenericMethod(typeof(IStorageProviderFactory))
            .Invoke(binding, new object[] { factory });
        return scope;
    }

    static SolverConvergence Converged() => SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Termination = SolverTermination.Converged, Loss = 1e-6 });

    static ExperimentData Experiment(string name)
    {
        var data = new ExperimentData(name + ".itc")
        {
            Name = name, CellConcentration = new FloatWithError(35e-6), SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3, MeasuredTemperature = 25, TargetTemperature = 25
        };
        for (var index = 0; index < 12; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 8.4e-10, true)
            { ActualCellConcentration = 35e-6, ActualTitrantConcentration = (index + 1) * 5e-6, Ratio = (index + 1) / 7d };
            injection.SetPeakArea(new FloatWithError((-20000 + index * 1000) * 8.4e-10, 1e-8));
            data.Injections.Add(injection);
        }
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Solution = SolutionInterface.FromModel(model, Converged());
        data.Model = model;
        return data;
    }

    sealed class PickerFactory(RecordingPicker picker) : IStorageProviderFactory
    {
        public List<TopLevel> Owners { get; } = new();
        public IStorageProvider CreateProvider(TopLevel topLevel)
        {
            Owners.Add(topLevel);
            // Avalonia 12 reserves direct implementations of IStorageProvider.
            // A proxy lets the supported factory supply just the picker behavior.
            var provider = DispatchProxy.Create<IStorageProvider, PickerProxy>();
            ((PickerProxy)(object)provider).Handler = method => method.Name switch
            {
                "get_CanPickFolder" => picker.CanPickFolder,
                "get_CanSave" => picker.CanSave,
                nameof(IStorageProvider.OpenFolderPickerAsync) => picker.OpenFolderPickerAsync(),
                nameof(IStorageProvider.SaveFilePickerAsync) => picker.SaveFilePickerAsync(),
                _ => throw new NotSupportedException(method.Name)
            };
            return provider;
        }
    }

    public class PickerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!);
    }

    sealed class RecordingPicker(IStorageFolder folder)
    {
        public IStorageFolder Folder => folder;
        public int FolderCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public bool Cancel { get; set; }
        public Exception? Failure { get; set; }
        public bool CanSave { get; set; } = true;
        public bool CanPickFolder { get; set; } = true;
        public Task<IReadOnlyList<IStorageFolder>> OpenFolderPickerAsync()
        {
            FolderCalls++;
            return Failure == null
                ? Task.FromResult<IReadOnlyList<IStorageFolder>>(Cancel ? Array.Empty<IStorageFolder>() : new[] { folder })
                : Task.FromException<IReadOnlyList<IStorageFolder>>(Failure);
        }
        public Task<IStorageFile?> SaveFilePickerAsync()
        {
            SaveCalls++;
            return Task.FromResult<IStorageFile?>(null);
        }
    }

    sealed class ApproveExport : IExportPromptService
    {
        public Task<string> ChooseExportFolderAsync(ExportAccessoryViewSettings settings) => throw new NotSupportedException();
        public bool ConfirmOverwrite(IEnumerable<string> outputPaths) => true;
    }
}
