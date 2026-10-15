using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Avalonia.Controls;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class MacWorkflowHeaderTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlatformLayoutShowsOneWorkflowNavigatorAndPreservesContent(bool mac)
    {
        WithWindow(mac, window =>
        {
            LoadExperiment(window);
            var header = Find<Grid>(window, "MacTitleBar");
            var selector = Find<SegmentedSelector>(window, "MacWorkflowSelector");
            var tabs = Find<TabControl>(window, "WorkspaceTabs");
            var strip = Assert.Single(tabs.GetVisualDescendants().OfType<ItemsPresenter>(),
                presenter => presenter.Name == "PART_ItemsPresenter");
            Assert.Equal(mac, header.IsVisible);
            Assert.Equal(!mac, strip.IsVisible);
            Assert.Equal(!mac, window.MenuContainer.IsVisible);
            Assert.Equal(!mac, window.MenuHost.IsVisible);
            Assert.Equal(mac, window.ExtendClientAreaToDecorationsHint);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.Equal(4, tabs.Items.Count);

            var contents = tabs.Items.Cast<TabItem>().Select(tab => tab.Content).ToArray();
            for (var index = 0; index < contents.Length; index++)
            {
                tabs.SelectedIndex = index;
                window.UpdateLayout();
                Assert.Same(contents[index], tabs.SelectedContent);
                Assert.True(Assert.IsAssignableFrom<Control>(tabs.SelectedContent).IsEffectivelyVisible);
            }

            if (mac)
            {
                Assert.Equal(52, window.ExtendClientAreaTitleBarHeightHint);
                Assert.Equal(0, window.MenuContainer.Bounds.Height);
                Assert.Equal("Experiment workflow", AutomationProperties.GetName(selector));
                Assert.Equal(new[]
                {
                    WorkflowNavigationPresentation.OverviewTooltip,
                    WorkflowNavigationPresentation.ProcessingTooltip,
                    WorkflowNavigationPresentation.AnalysisTooltip,
                    WorkflowNavigationPresentation.FinalFigureTooltip,
                }, selector.ChoiceButtons.Select(choice => ToolTip.GetTip(choice)));

                foreach (var width in new[] { 1000, 1200 })
                {
                    window.Width = width;
                    window.UpdateLayout();
                    Assert.Equal(52, header.Bounds.Height);
                    Assert.Equal(new Size(440, 28), selector.Bounds.Size);
                    var centre = selector.TranslatePoint(new Point(220, 14), window)!.Value;
                    Assert.Equal(window.ClientSize.Width / 2, centre.X, precision: 3);
                    Assert.Equal(26, centre.Y, precision: 3);
                }

                // Widening the sidebar must not move the title-bar selector.
                var body = tabs.GetVisualAncestors().OfType<Grid>()
                    .First(grid => grid.Children.OfType<GridSplitter>().Any());
                body.ColumnDefinitions[0].Width = new GridLength(400);
                window.UpdateLayout();
                Assert.Equal(window.ClientSize.Width / 2,
                    selector.TranslatePoint(new Point(220, 14), window)!.Value.X, precision: 3);
            }
            else
            {
                Assert.True(window.MenuContainer.Bounds.Height > 0);
                Assert.True(strip.Bounds.Height > 0);
            }
        });
    }

    [AvaloniaFact]
    public void PointerKeyboardAndProgrammaticChangesStayInSyncWithoutDuplicateEvents()
    {
        WithWindow(true, window =>
        {
            LoadExperiment(window);
            var tabs = Find<TabControl>(window, "WorkspaceTabs");
            var selector = Find<SegmentedSelector>(window, "MacWorkflowSelector");
            var tabChanges = 0;
            var selectorChanges = 0;
            tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, tabs)) tabChanges++; };
            selector.SelectionChanged += (_, _) => selectorChanges++;

            foreach (var index in new[] { 1, 2, 3, 0 })
            {
                var choice = selector.ChoiceButtons[index];
                var point = choice.TranslatePoint(new Point(choice.Bounds.Width / 2, 14), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.Equal(index, tabs.SelectedIndex);
                Assert.Equal(index, selector.SelectedIndex);
            }
            Assert.Equal(4, tabChanges);
            Assert.Equal(4, selectorChanges);

            tabs.SelectedIndex = 2;
            tabs.SelectedIndex = 2;
            Assert.Equal(2, selector.SelectedIndex);
            Assert.Equal(5, tabChanges);
            Assert.Equal(5, selectorChanges);

            foreach (var (key, index) in new[] { (Key.Left, 1), (Key.Home, 0), (Key.End, 3), (Key.Right, 3) })
            {
                var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
                selector.ChoiceButtons[selector.SelectedIndex].RaiseEvent(args);
                Assert.True(args.Handled);
                Assert.Equal(index, tabs.SelectedIndex);
                Assert.True(selector.ChoiceButtons[index].IsFocused);
                Assert.Single(selector.ChoiceButtons, choice => choice.IsTabStop);
            }
            Assert.Equal(8, tabChanges);
            Assert.Equal(8, selectorChanges);
        });
    }

    [AvaloniaFact]
    public void ResultAndEmptyStatesRetainTheWorkflowAndExistingWorkspaceSettings()
    {
        WithWindow(true, window =>
        {
            var selector = Find<SegmentedSelector>(window, "MacWorkflowSelector");
            var title = Find<TextBlock>(window, "MacResultTitle");
            var tabs = Find<TabControl>(window, "WorkspaceTabs");
            var list = Find<ListBox>(window, "ItemsList");
            Assert.True(selector.IsVisible);
            Assert.False(selector.IsEnabled);
            Assert.False(title.IsVisible);

            var experiment = LoadExperiment(window);
            var result = CreateResult(experiment);
            DataManager.AddData(result);
            Dispatcher.UIThread.RunJobs();
            Select(window, experiment);
            var contents = tabs.Items.Cast<TabItem>().Select(tab => tab.Content).ToArray();
            var analysis = Find<AnalysisWorkspaceControl>(window, "AnalysisWorkspace");
            selector.SelectedIndex = 2;
            analysis.BootstrapIterationsBoxForTesting.Text = "321";
            analysis.UnifiedAxesCheckForTesting.IsChecked = true;
            foreach (var index in new[] { 3, 0, 1, 2 }) selector.SelectedIndex = index;

            Select(window, result);
            Assert.False(selector.IsVisible);
            Assert.True(title.IsVisible);
            Assert.Equal("Analysis result", title.Text);
            Assert.False(tabs.IsVisible);
            Assert.True(Find<Control>(window, "ResultWorkspace").IsVisible);

            Select(window, experiment);
            Assert.True(selector.IsVisible);
            Assert.True(selector.IsEnabled);
            Assert.False(title.IsVisible);
            Assert.Equal(2, selector.SelectedIndex);
            Assert.Equal(2, tabs.SelectedIndex);
            Assert.Equal("321", analysis.BootstrapIterationsBoxForTesting.Text);
            Assert.True(analysis.UnifiedAxesCheckForTesting.IsChecked);
            Assert.Equal(contents, tabs.Items.Cast<TabItem>().Select(tab => tab.Content));

            list.SelectedIndex = -1;
            Assert.True(selector.IsVisible);
            Assert.False(selector.IsEnabled);
            Assert.Equal(2, selector.SelectedIndex);
            DataManager.Clear(DataClearMode.ResetSession);
            Dispatcher.UIThread.RunJobs();
            Assert.False(title.IsVisible);
            Assert.False(selector.IsEnabled);
            Assert.Equal(2, selector.SelectedIndex);
            LoadExperiment(window);
            Assert.True(selector.IsEnabled);
            Assert.Equal(2, selector.SelectedIndex);
        });
    }

    [AvaloniaFact]
    public async Task ProjectLoadAndClearUpdateTheHeaderWithoutResettingTheWorkflow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workflow-header-{Guid.NewGuid():N}.ftxtc");
        var previousLastPath = AppSettings.LastDocumentPath;
        var previousLastPaths = AppSettings.LastDocumentPaths;
        try
        {
            await FTXTCWriter.WriteFileAsync(path, new[] { CreateExperiment() });
            await WithWindowAsync(true, async window =>
            {
                window.SetStartupReady();
                var selector = Find<SegmentedSelector>(window, "MacWorkflowSelector");
                Assert.False(selector.IsEnabled);
                await window.OpenQueuedPathsAsync(new[] { path });
                Dispatcher.UIThread.RunJobs();
                Assert.Single(window.DataListEntries);
                Assert.True(selector.IsEnabled);
                selector.SelectedIndex = 3;

                var clearing = window.ClearDataWithConfirmationAsync();
                var confirmation = Assert.Single(window.OwnedWindows);
                Assert.Single(confirmation.GetVisualDescendants().OfType<Button>(),
                    button => Equals(button.Content, "Remove"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await clearing;
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(window.DataListEntries);
                Assert.False(selector.IsEnabled);
                Assert.True(selector.IsVisible);
                Assert.Equal(3, selector.SelectedIndex);
                await window.OpenQueuedPathsAsync(new[] { path });
                Dispatcher.UIThread.RunJobs();
                Assert.True(selector.IsEnabled);
                Assert.Equal(3, selector.SelectedIndex);
                Assert.Equal(3, Find<TabControl>(window, "WorkspaceTabs").SelectedIndex);
            });
        }
        finally
        {
            AppSettings.LastDocumentPath = previousLastPath;
            AppSettings.LastDocumentPaths = previousLastPaths;
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void SystemTitleAndDocumentStatusStillFollowTheProject()
    {
        WithWindow(true, window =>
        {
            LoadExperiment(window);
            FTITCFormat.CurrentAccessedAppDocumentPath = "/tmp/Workflow header.ftxtc";
            DocumentDirtyTracker.MarkDirty();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Workflow header", window.Title);
            Assert.Contains("[M]", window.Title);
            Assert.Contains("Workflow header", Find<TextBlock>(window, "SecondaryStatusText").Text);
            DocumentDirtyTracker.MarkClean();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("[M]", window.Title);
        });
    }

    static T Find<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;

    static void Select(MainWindow window, ITCDataContainer item)
    {
        var list = Find<ListBox>(window, "ItemsList");
        list.SelectedItem = window.DataListEntries.Single(entry => ReferenceEquals(entry.Item, item));
        window.UpdateLayout();
    }

    static ExperimentData LoadExperiment(MainWindow window)
    {
        var experiment = CreateExperiment();
        DataManager.AddData(experiment);
        Dispatcher.UIThread.RunJobs();
        Select(window, experiment);
        return experiment;
    }

    static ExperimentData CreateExperiment()
    {
        var experiment = new ExperimentData("header-test.itc")
        {
            CellVolume = 200e-6,
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
        };
        experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6) { IsIntegrated = true });
        return experiment;
    }

    static AnalysisResult CreateResult(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        model.Solution = SolutionInterface.FromModel(model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
    }

    static void WithWindow(bool mac, Action<MainWindow> assertion) =>
        WithWindowAsync(mac, window =>
        {
            assertion(window);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    static async Task WithWindowAsync(bool mac, Func<MainWindow, Task> assertion)
    {
        var previousPath = FTITCFormat.CurrentAccessedAppDocumentPath;
        DataManager.Clear(DataClearMode.ResetSession);
        var application = Application.Current!;
        var previousVariant = application.RequestedThemeVariant;
        var colors = new ResourceInclude(new Uri("avares://FT-ITC Analysis/"))
        {
            Source = new Uri("avares://FT-ITC Analysis/Styling/AppTheme.axaml")
        };
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        application.Resources.MergedDictionaries.Add(colors);
        application.Styles.Add(theme);
        application.RequestedThemeVariant = ThemeVariant.Light;
        var window = new MainWindow(mac);
        window.Show();
        try
        {
            await assertion(window);
        }
        finally
        {
            DocumentDirtyTracker.MarkClean();
            window.Close();
            DataManager.Clear(DataClearMode.ResetSession);
            FTITCFormat.CurrentAccessedAppDocumentPath = previousPath;
            Dispatcher.UIThread.RunJobs();
            application.RequestedThemeVariant = previousVariant;
            application.Styles.Remove(theme);
            application.Resources.MergedDictionaries.Remove(colors);
        }
    }
}
