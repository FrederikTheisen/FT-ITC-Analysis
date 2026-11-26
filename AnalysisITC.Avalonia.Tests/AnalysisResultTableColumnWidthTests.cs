using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AnalysisITC.Avalonia.Results;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class AnalysisResultTableColumnWidthTests
{
    [AvaloniaFact]
    public void AutomaticExperimentWidthIsCappedInAWideViewport()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AppSettings.RememberResultTableColumnWidthsForSession = false;
            var workspace = new AnalysisResultWorkspaceControl
            {
                Result = CreateResult(new string('W', 200))
            };

            var window = new Window { Width = 1800, Height = 700, Content = workspace };
            window.Styles.Add(new FluentTheme());
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var grid = workspace.ResultTableGridForTesting!;
                var scroll = grid.GetVisualAncestors().OfType<ScrollViewer>().First();
                var otherColumnsWidth = grid.ColumnDefinitions.Skip(1).Sum(column => column.Width.Value);

                Assert.True(scroll.Bounds.Width > otherColumnsWidth + 300);
                Assert.Equal(300, grid.ColumnDefinitions[0].Width.Value);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [AvaloniaFact]
    public void NaturalWidthsContainRenderedHeadersAndCellValues()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AppSettings.RememberResultTableColumnWidthsForSession = false;
            var workspace = new AnalysisResultWorkspaceControl { Result = CreateResult("normal.itc") };
            var window = new Window { Width = 1100, Height = 700, Content = workspace };
            window.Styles.Add(new FluentTheme());
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var grid = workspace.ResultTableGridForTesting!;

                foreach (var border in grid.Children.OfType<Border>())
                {
                    var columnWidth = grid.ColumnDefinitions[Grid.GetColumn(border)].ActualWidth;
                    double requiredWidth;

                    if (border.Child is Grid header)
                    {
                        var label = header.Children.OfType<TextBlock>().Single();
                        var grip = header.Children.OfType<Border>().Single();
                        label.Measure(Size.Infinity);
                        requiredWidth = label.DesiredSize.Width + grip.Width;
                    }
                    else
                    {
                        var label = Assert.IsType<TextBlock>(border.Child);
                        label.Measure(Size.Infinity);
                        requiredWidth = label.DesiredSize.Width;
                    }

                    Assert.True(columnWidth >= Math.Ceiling(requiredWidth + border.BorderThickness.Right));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [AvaloniaTheory]
    [InlineData(-0.001, false, false)]
    [InlineData(-0.001, false, true)]
    [InlineData(-1e-20, true, false)]
    public void AutomaticDataColumnFitsStyledUncertaintyUpToTheCap(double lower, bool exceedsCap, bool assignAfterLoad)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var previousStyle = AppSettings.UncertaintyDisplayStyle;
            var previousPrecision = AppSettings.NumberPrecision;
            var previousRememberWidths = AppSettings.RememberResultTableColumnWidthsForSession;
            AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
            AppSettings.NumberPrecision = NumberPrecision.Standard;
            AppSettings.RememberResultTableColumnWidthsForSession = false;

            var window = new Window { Width = 850, Height = 700 };
            window.Styles.Add(new FluentTheme());
            window.Styles.Add(new Style(selector => selector.OfType<TextBlock>())
            {
                Setters = { new Setter(TextBlock.LetterSpacingProperty, 2.0) }
            });
            try
            {
                var workspace = new AnalysisResultWorkspaceControl();
                var result = CreateResult("normal.itc", stoichiometry: new FloatWithError(0, 0.01, lower, 0.02));
                if (!assignAfterLoad) workspace.Result = result;
                window.Content = workspace;
                window.Show();
                Dispatcher.UIThread.RunJobs();
                if (assignAfterLoad)
                {
                    Assert.True(workspace.IsLoaded);
                    workspace.Result = result;
                    Dispatcher.UIThread.RunJobs();
                }

                var grid = workspace.ResultTableGridForTesting!;
                var cell = Assert.Single(grid.Children.OfType<Border>(), border =>
                    border.Child is TextBlock label && label.Text?.Contains("±") == true);
                var column = Grid.GetColumn(cell);
                var header = Assert.Single(grid.Children.OfType<Border>(), border =>
                    Grid.GetRow(border) == 0 && Grid.GetColumn(border) == column);
                var label = Assert.IsType<TextBlock>(cell.Child);
                var headerLabel = Assert.IsType<Grid>(header.Child).Children.OfType<TextBlock>().Single();
                Assert.Equal(2.0, label.LetterSpacing);
                var displayedWithEllipsis = label.TextLayout.TextLines.Any(line => line.HasCollapsed);
                label.Measure(Size.Infinity);
                headerLabel.Measure(Size.Infinity);

                Assert.True(label.DesiredSize.Width > headerLabel.DesiredSize.Width);
                var requiredWidth = label.DesiredSize.Width + cell.BorderThickness.Right;
                Assert.Equal(exceedsCap, requiredWidth > 300);
                Assert.Equal(exceedsCap, displayedWithEllipsis);
                Assert.InRange(grid.ColumnDefinitions[column].ActualWidth, Math.Min(requiredWidth, 300), 300);
                var scroll = grid.GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            }
            finally
            {
                window.Close();
                AppSettings.UncertaintyDisplayStyle = previousStyle;
                AppSettings.NumberPrecision = previousPrecision;
                AppSettings.RememberResultTableColumnWidthsForSession = previousRememberWidths;
            }
        });
    }

    [AvaloniaFact]
    public void ResultTableOmitsSavedReducedChiSquare()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var result = CreateResult("with-diagnostic.itc", new SolverConvergenceSnapshot
            {
                ReducedChiSquareQ = 5,
                ReducedChiSquareObservationCount = 3,
                ReducedChiSquareParameterCount = 1,
            });
            var workspace = new AnalysisResultWorkspaceControl { Result = result };
            var grid = workspace.ResultTableGridForTesting!;
            var headers = grid.Children.OfType<Border>().Where(cell => Grid.GetRow(cell) == 0)
                .ToDictionary(cell => ((Grid)cell.Child!).Children.OfType<TextBlock>().Single().Text!, Grid.GetColumn);
            Assert.Contains("RMSD (µJ)", headers.Keys);
            Assert.DoesNotContain(ReducedChiSquarePresentation.Label, headers.Keys);
        });
    }

    [AvaloniaFact]
    public void ManualWidthUsesMinimumAndSurvivesRefresh()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AppSettings.RememberResultTableColumnWidthsForSession = false;
            var workspace = new AnalysisResultWorkspaceControl { Result = CreateResult("first.itc") };

            workspace.SetResultTableColumnWidthForTesting("Experiment", 20);
            Assert.Equal(90, workspace.ResultTableColumnWidthsForTesting["Experiment"]);

            workspace.SetResultTableColumnWidthForTesting("Experiment", 480);
            workspace.Refresh();

            Assert.Equal(480, workspace.ResultTableColumnWidthsForTesting["Experiment"]);
            Assert.Equal(480, workspace.ResultTableGridForTesting!.ColumnDefinitions[0].Width.Value);
        });
    }

    [AvaloniaFact]
    public void ResultChangeClearsWidthsUnlessSessionMemoryIsEnabled()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            AnalysisResultWorkspaceControl.ResetSessionResultTableColumnWidthsForTesting();
            AppSettings.RememberResultTableColumnWidthsForSession = false;
            var workspace = new AnalysisResultWorkspaceControl { Result = CreateResult("first.itc") };
            workspace.SetResultTableColumnWidthForTesting("Experiment", 210);
            workspace.Result = CreateResult("second.itc");
            Assert.Empty(workspace.ResultTableColumnWidthsForTesting);

            AppSettings.RememberResultTableColumnWidthsForSession = true;
            workspace.SetResultTableColumnWidthForTesting("Experiment", 230);
            workspace.Result = CreateResult("third.itc");
            Assert.Equal(230, workspace.ResultTableColumnWidthsForTesting["Experiment"]);

            AppSettings.RememberResultTableColumnWidthsForSession = false;
            AnalysisResultWorkspaceControl.ResetSessionResultTableColumnWidthsForTesting();
        });
    }

    static AnalysisResult CreateResult(
        string experimentName,
        SolverConvergenceSnapshot? convergence = null,
        FloatWithError? stoichiometry = null)
    {
        var experiment = new ExperimentData(experimentName)
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(100e-6),
            MeasuredTemperature = 25,
        };
        experiment.Injections.Add(new InjectionData(experiment, volume: 1e-6)
        {
            IsIntegrated = true,
            Ratio = 1,
            ActualCellConcentration = 10e-6,
            ActualTitrantConcentration = 10e-6,
        });

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(convergence ?? new SolverConvergenceSnapshot()));
        if (stoichiometry.HasValue)
            model.Solution.Parameters[ParameterType.Nvalue1] = stoichiometry.Value;
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
    }
}
