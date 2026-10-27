using System;
using System.Linq;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using AnalysisITC.Avalonia.Controls;
using AnalysisITC.Avalonia.Processing;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class ProcessingBaselineZoomTests
{
    [AvaloniaTheory]
    [InlineData(false, true, EnergyUnitFamily.Joules, 3, 17)]
    [InlineData(true, true, EnergyUnitFamily.Joules, -6, 6)]
    [InlineData(false, false, EnergyUnitFamily.Joules, -98.25, 118.25)]
    [InlineData(true, false, EnergyUnitFamily.Joules, -107.25, 107.25)]
    [InlineData(false, true, EnergyUnitFamily.Calories, 3, 17)]
    [InlineData(true, true, EnergyUnitFamily.Calories, -6, 6)]
    public void BaselineZoomUsesNativeBaselineFittingPoints(
        bool corrected, bool discardIntegrated, EnergyUnitFamily family, double minimum, double maximum)
    {
        var originalFamily = AppSettings.EnergyUnitFamily;
        try
        {
            AppSettings.EnergyUnitFamily = family;
            var experiment = CreateExperiment(discardIntegrated);
            var graph = new ProcessingGraphControl { Experiment = experiment, ShowBaselineCorrected = corrected };
            var before = View(graph);

            graph.ZoomBaseline();

            // Eligible powers are 8, 10, 10, 12, 10 µW: mean 10, nearest extreme 2.
            // Native padding is 3 × 2 µW, around baseline 9–11 µW or corrected zero.
            // Including peaks gives mean 43.75 and nearest extreme 35.75 µW.
            var divisor = family == EnergyUnitFamily.Calories ? 4.184 : 1;
            var after = View(graph);
            Assert.Equal(minimum / divisor, after.YMin, tolerance: 0.0001);
            Assert.Equal(maximum / divisor, after.YMax, tolerance: 0.0001);
            Assert.Equal(before.XMin, after.XMin);
            Assert.Equal(before.XMax, after.XMax);
            Assert.Equal(ProcessingGraphControl.VerticalZoomMode.Baseline, graph.CurrentVerticalZoomMode);
        }
        finally { AppSettings.EnergyUnitFamily = originalFamily; }
    }

    [AvaloniaFact]
    public void PresetControlsAdjustOnlyBaselineModeAndStayLocalToTheView()
    {
        var originalFamily = AppSettings.EnergyUnitFamily;
        try
        {
            AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
            var workspace = new ProcessingWorkspaceControl { Experiment = CreateExperiment() };
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            T Field<T>(string name) => (T)typeof(ProcessingWorkspaceControl).GetField(name, flags)!.GetValue(workspace)!;
            var selector = Field<SegmentedSelector>("baselineZoomSelector");
            var graph = Field<ProcessingGraphControl>("graph");
            var window = new Window { Width = 1000, Height = 650, Content = workspace };
            window.Show();
            try
            {
                Assert.Equal(1, selector.SelectedIndex);
                var allDataView = View(graph);
                selector.ChoiceButtons[3].IsChecked = true;
                Assert.Equal(allDataView, View(graph));
                Assert.Equal(ProcessingGraphControl.VerticalZoomMode.AllData, graph.CurrentVerticalZoomMode);

                Field<ToggleButton>("baselineZoomButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertRange(graph, 7.5, 12.5);
                selector.ChoiceButtons[0].IsChecked = true;
                AssertRange(graph, -3, 23);
                selector.ChoiceButtons[2].IsChecked = true;
                AssertRange(graph, 6, 14);
                Field<CheckBox>("correctedCheck").IsChecked = true;
                AssertRange(graph, -3, 3);
                Field<CheckBox>("correctedCheck").IsChecked = false;
                AssertRange(graph, 6, 14);

                // A different experiment retains this view's setting; a new view starts at 1×.
                workspace.Experiment = CreateExperiment();
                Assert.Equal(2, selector.SelectedIndex);
                Field<ToggleButton>("baselineZoomButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertRange(graph, 6, 14);
                var otherWorkspace = new ProcessingWorkspaceControl();
                var otherSelector = (SegmentedSelector)typeof(ProcessingWorkspaceControl)
                    .GetField("baselineZoomSelector", flags)!.GetValue(otherWorkspace)!;
                Assert.Equal(1, otherSelector.SelectedIndex);
            }
            finally { window.Close(); }
        }
        finally { AppSettings.EnergyUnitFamily = originalFamily; }
    }

    static void AssertRange(ProcessingGraphControl graph, double minimum, double maximum)
    {
        var view = View(graph);
        Assert.Equal(minimum, view.YMin, tolerance: 0.0001);
        Assert.Equal(maximum, view.YMax, tolerance: 0.0001);
        Assert.Equal(ProcessingGraphControl.VerticalZoomMode.Baseline, graph.CurrentVerticalZoomMode);
    }

    [AvaloniaFact]
    public void BaselinePresetsFitInTheDisplayInspector()
    {
        var theme = new global::Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var workspace = new ProcessingWorkspaceControl { Experiment = CreateExperiment() };
        var window = new Window { Width = 900, Height = 600, Content = workspace };
        try
        {
            window.Show();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var tabs = (TabControl)typeof(ProcessingWorkspaceControl).GetField("controlsPanel", flags)!.GetValue(workspace)!;
            tabs.SelectedIndex = 1;
            window.UpdateLayout();
            var selector = (SegmentedSelector)typeof(ProcessingWorkspaceControl).GetField("baselineZoomSelector", flags)!.GetValue(workspace)!;
            Assert.True(selector.IsEffectivelyVisible);
            Assert.True(selector.Bounds.Width >= 160);
            foreach (var button in selector.ChoiceButtons)
            {
                Assert.True(button.Bounds.Width >= 35);
                Assert.True(button.Bounds.Height >= 24);
            }
            var directory = Environment.GetEnvironmentVariable("FTITC_BASELINE_QA_DIRECTORY");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, "avalonia-baseline-zoom.png"));
            }
        }
        finally
        {
            window.Close();
            Application.Current.Styles.Remove(theme);
        }
    }

    internal static ExperimentData CreateExperiment(bool discardIntegrated = true)
    {
        var experiment = new ExperimentData("baseline-zoom.itc");
        var powers = new[] { 8, 10, 100, 100, 100, 10, 12, 10 };
        for (var i = 0; i < powers.Length; i++)
            experiment.DataPoints.Add(new DataPoint(i, powers[i] * 1e-6f));
        var injection = InjectionData.FromPEAQFile(experiment, 0, true, 2, 1e-6, 5, 1, 25);
        experiment.Injections.Add(injection);
        injection.SetIntegrationLengthByTime(2);
        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        experiment.Processor.DiscardIntegratedPoints = discardIntegrated;
        experiment.Processor.Interpolator.Baseline = new[] { 9, 9, 9, 10, 10, 11, 11, 11 }
            .Select(value => new Energy(value * 1e-6)).ToList();
        experiment.Processor.SubtractBaseline();
        return experiment;
    }

    internal static (double XMin, double XMax, double YMin, double YMax) View(ProcessingGraphControl graph)
    {
        var view = typeof(ProcessingGraphControl).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph)!;
        double Read(string name) => (double)view.GetType().GetProperty(name)!.GetValue(view)!;
        return (Read("XMin"), Read("XMax"), Read("YMin"), Read("YMax"));
    }
}
