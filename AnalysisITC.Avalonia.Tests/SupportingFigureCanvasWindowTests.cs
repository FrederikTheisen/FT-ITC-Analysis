using System;
using System.Linq;
using System.Reflection;

using AnalysisITC.Avalonia.Tools;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Numerics;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class SupportingFigureCanvasWindowTests
{
    [AvaloniaFact]
    public void OpenSupportingFigureRefreshesExcludedPointsWhenTraceabilityModeChanges()
    {
        var original = AppSettings.TraceabilityModeEnabled;
        SupportingFigureCanvasWindow? window = null;
        try
        {
            AppSettings.TraceabilityModeEnabled = false;
            var experiment = new ExperimentData("excluded.itc")
            {
                CellConcentration = new FloatWithError(1e-3),
                SyringeConcentration = new FloatWithError(1e-3),
                CellVolume = 1.4e-3,
            };
            for (var index = 0; index < 3; index++)
            {
                var injection = new InjectionData(experiment, index, 1e-6, 1e-9, include: index != 0)
                {
                    ActualCellConcentration = 1e-3,
                    ActualTitrantConcentration = (index + 1) * 2e-6,
                };
                injection.SetPeakArea(new FloatWithError(1e-6));
                experiment.Injections.Add(injection);
            }
            window = new SupportingFigureCanvasWindow(new PublicationFigureOptions { ShowBadData = false }, null);
            window.SetCompositionForTesting(new[] { experiment });
            Assert.Equal(2, window.CurrentPlanForTesting!.Figures.Single().FitPanel.Points.Count);

            var settingsChanged = (EventHandler?)typeof(AppSettings)
                .GetField("SettingsDidUpdate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            AppSettings.TraceabilityModeEnabled = true;
            settingsChanged?.Invoke(null, EventArgs.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, window.CurrentPlanForTesting!.Figures.Single().FitPanel.Points.Count);

            AppSettings.TraceabilityModeEnabled = false;
            settingsChanged?.Invoke(null, EventArgs.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, window.CurrentPlanForTesting!.Figures.Single().FitPanel.Points.Count);
        }
        finally
        {
            window?.Close();
            AppSettings.TraceabilityModeEnabled = original;
        }
    }

    [AvaloniaFact]
    public void ClosingAndReopeningRestoresTheOrderedComposition()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var first = new ExperimentData("remember-first.itc");
            var second = new ExperimentData("remember-second.itc");
            DataManager.Clear(DataClearMode.ResetSession);
            DataManager.AddData(new ITCDataContainer[] { first, second });
            try
            {
                var state = new SupportingFigureSelectionState();
                var window = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), first, state);
                window.Show();
                window.SetCompositionForTesting(new[] { second, first });
                window.PlotWidthBoxForTesting.Text = "6";
                window.PlotHeightBoxForTesting.Text = "8";
                window.ColumnsStepperForTesting.Value = 2;
                window.RowsStepperForTesting.Value = 4;
                window.FontSizeStepperForTesting.Value = 9.5m;
                window.SymbolSizeStepperForTesting.Value = 5;
                window.StrokeWidthSelectorForTesting.SelectedIndex = 0;
                window.PanelLettersCheckForTesting.IsChecked = false;
                window.PanelTitlesCheckForTesting.IsChecked = true;
                window.GroupResultsCheckForTesting.IsChecked = false;
                window.InformationBoxesCheckForTesting.IsChecked = false;
                window.PreviewZoomComboForTesting.SelectedIndex = 1;
                window.Close();

                var reopened = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), first, state);
                reopened.Show();
                Assert.Equal(new[] { second, first }, reopened.CurrentPlanForTesting!.Document.Cells
                    .Select(cell => cell.Source.Experiment));
                var options = reopened.CanvasOptionsForTesting;
                Assert.Equal(6, options.PlotWidthCentimeters);
                Assert.Equal(8, options.PlotHeightCentimeters);
                Assert.Equal(2, options.Columns);
                Assert.Equal(4, options.Rows);
                Assert.Equal(9.5, options.FontSize);
                Assert.Equal(5, options.SymbolSize);
                Assert.Equal(0.5, options.StrokeWidth);
                Assert.False(options.ShowPanelLetters);
                Assert.True(options.ShowPanelTitles);
                Assert.False(options.GroupResultFigures);
                Assert.False(options.ShowInformationBoxes);
                Assert.Equal(1, reopened.PreviewZoomComboForTesting.SelectedIndex);
                reopened.SetCompositionForTesting(Array.Empty<ITCDataContainer>());
                reopened.Close();

                var empty = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), first, state);
                Assert.False(empty.CurrentPlanForTesting!.IsValid);
                Assert.Equal(6, empty.CanvasOptionsForTesting.PlotWidthCentimeters);
                Assert.Equal(4, empty.CanvasOptionsForTesting.Rows);
                empty.Close();
            }
            finally
            {
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [AvaloniaFact]
    public void NumericControlsExposeExpectedBoundsAndIncrements()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), null);

            AssertStepper(window.ColumnsStepperForTesting, 1, 6, 1);
            AssertStepper(window.RowsStepperForTesting, 1, 10, 1);
            AssertStepper(window.FontSizeStepperForTesting, 5, 24, 0.5m);
            AssertStepper(window.SymbolSizeStepperForTesting, 3, 14, 0.5m);
            Assert.Equal(3, window.ColumnsStepperForTesting.Value);
            Assert.Equal(3, window.RowsStepperForTesting.Value);
            Assert.Equal(8, window.FontSizeStepperForTesting.Value);
            Assert.Equal(4, window.SymbolSizeStepperForTesting.Value);
            Assert.All(new[]
            {
                window.ColumnsStepperForTesting,
                window.RowsStepperForTesting,
                window.FontSizeStepperForTesting,
                window.SymbolSizeStepperForTesting
            }, stepper => Assert.False(stepper.IsReadOnly));

            window.Close();
        });
    }

    [AvaloniaFact]
    public void StepperValuesAndWeightSelectionDriveCanvasOptions()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), null);
            window.ColumnsStepperForTesting.Value = 6;
            window.RowsStepperForTesting.Value = 10;
            window.FontSizeStepperForTesting.Value = 5.5m;
            window.SymbolSizeStepperForTesting.Value = 3.5m;

            var selector = window.StrokeWidthSelectorForTesting;
            Assert.Equal(new[] { "Light", "Standard" }, selector.Options);
            selector.SelectedIndex = 0;
            var light = window.CanvasOptionsForTesting;
            Assert.Equal(6, light.Columns);
            Assert.Equal(10, light.Rows);
            Assert.Equal(5.5, light.FontSize);
            Assert.Equal(3.5, light.SymbolSize);
            Assert.Equal(0.5, light.StrokeWidth);

            selector.SelectedIndex = 1;
            Assert.Equal(1, window.CanvasOptionsForTesting.StrokeWidth);
            window.Close();
        });
    }

    [AvaloniaFact]
    public void PreviewAndInspectorShowTheSameCalculatedDimensions()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), new ExperimentData("dimensions.itc"));
            var originalPlan = window.CurrentPlanForTesting;
            var inspectorText = window.FigureSizeTextForTesting.Text!;
            var previewText = window.PreviewFigureSizeTextForTesting.Text!;

            Assert.StartsWith("Figure dimensions: ", inspectorText);
            Assert.NotEqual("Figure dimensions: —", inspectorText);
            Assert.Equal(inspectorText["Figure dimensions: ".Length..], previewText);

            window.ColumnsStepperForTesting.Value = 1;
            Assert.NotSame(originalPlan, window.CurrentPlanForTesting);
            Assert.Equal(window.FigureSizeTextForTesting.Text!["Figure dimensions: ".Length..],
                window.PreviewFigureSizeTextForTesting.Text);

            var columnPlan = window.CurrentPlanForTesting;
            window.FontSizeStepperForTesting.Value = 10.5m;
            Assert.NotSame(columnPlan, window.CurrentPlanForTesting);

            window.Close();

            var invalidWindow = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), null);
            Assert.Equal("Figure dimensions: —", invalidWindow.FigureSizeTextForTesting.Text);
            Assert.Equal("Figure dimensions: —", invalidWindow.PreviewFigureSizeTextForTesting.Text);
            invalidWindow.Close();
        });
    }

    [AvaloniaFact]
    public void OverflowPreviewShowsPageCaptionsCountsAndClearsWhenInvalid()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var window = new SupportingFigureCanvasWindow(new PublicationFigureOptions(), null);
            window.ColumnsStepperForTesting.Value = 2;
            window.RowsStepperForTesting.Value = 1;
            window.SetCompositionForTesting(Enumerable.Range(0, 3)
                .Select(index => (ITCDataContainer)new ExperimentData($"preview-page-{index}.itc")));

            Assert.True(window.CurrentPlanForTesting!.IsValid);
            Assert.Equal(2, window.CurrentPlanForTesting.PageCount);
            Assert.Equal("3 panels · 2 pages", window.StatusTextForTesting.Text);
            Assert.Equal(4, window.PreviewPagesForTesting.Children.Count);
            var pageLabels = window.PreviewPagesForTesting.Children.OfType<TextBlock>().ToList();
            Assert.Collection(pageLabels,
                label => Assert.StartsWith("Page 1 · ", label.Text),
                label => Assert.StartsWith("Page 2 · ", label.Text));
            Assert.Equal(window.FigureSizeTextForTesting.Text!["Figure dimensions: ".Length..],
                window.PreviewFigureSizeTextForTesting.Text);

            window.SetCompositionForTesting(Array.Empty<ITCDataContainer>());
            Assert.False(window.CurrentPlanForTesting!.IsValid);
            Assert.Empty(window.PreviewPagesForTesting.Children);
            Assert.Equal("Figure dimensions: —", window.FigureSizeTextForTesting.Text);
            Assert.Equal("Figure dimensions: —", window.PreviewFigureSizeTextForTesting.Text);
            window.Close();
        });
    }

    static void AssertStepper(NumericUpDown stepper, decimal minimum, decimal maximum, decimal increment)
    {
        Assert.Equal(minimum, stepper.Minimum);
        Assert.Equal(maximum, stepper.Maximum);
        Assert.Equal(increment, stepper.Increment);
    }
}
