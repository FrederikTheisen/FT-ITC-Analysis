using System;

using Avalonia;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class IntegratedHeatsGraphControlTests
{
    public IntegratedHeatsGraphControlTests()
    {
        AvaloniaTestBootstrap.EnsureInitialized();
    }

    [Fact]
    public void HoverInvalidatesOnlyWhenInjectionIdentityChanges()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var graph = ArrangeGraph(experiment);
            var outside = new Point(2, 2);

            Assert.False(graph.UpdateHoverAtForTesting(outside));
            Assert.Equal(0, graph.HoverInvalidationCountForTesting);

            var firstPoint = GraphPoint(graph, experiment.Injections[0], residual: false);
            Assert.True(graph.UpdateHoverAtForTesting(firstPoint));
            Assert.Same(experiment.Injections[0], graph.HoveredInjectionForTesting);
            Assert.False(graph.HoveredResidualForTesting);
            Assert.Equal(1, graph.HoverInvalidationCountForTesting);

            var handCursor = graph.Cursor;
            Assert.False(graph.UpdateHoverAtForTesting(new Point(firstPoint.X + 1, firstPoint.Y + 1)));
            Assert.Same(handCursor, graph.Cursor);
            Assert.Equal(1, graph.HoverInvalidationCountForTesting);

            var secondPoint = GraphPoint(graph, experiment.Injections[1], residual: false);
            Assert.True(graph.UpdateHoverAtForTesting(secondPoint));
            Assert.Same(experiment.Injections[1], graph.HoveredInjectionForTesting);
            Assert.Same(handCursor, graph.Cursor);
            Assert.Equal(2, graph.HoverInvalidationCountForTesting);

            Assert.True(graph.UpdateHoverAtForTesting(outside));
            var crossCursor = graph.Cursor;
            Assert.Null(graph.HoveredInjectionForTesting);
            Assert.Equal(3, graph.HoverInvalidationCountForTesting);

            Assert.False(graph.UpdateHoverAtForTesting(new Point(3, 3)));
            Assert.Same(crossCursor, graph.Cursor);
            Assert.Equal(3, graph.HoverInvalidationCountForTesting);
        });
    }

    [Fact]
    public void FitAndResidualHitsAreDifferentHoverTargets()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            AttachSolution(experiment);
            var graph = ArrangeGraph(experiment);
            var injection = experiment.Injections[0];

            Assert.True(graph.UpdateHoverAtForTesting(GraphPoint(graph, injection, residual: false)));
            Assert.Same(injection, graph.HoveredInjectionForTesting);
            Assert.False(graph.HoveredResidualForTesting);

            Assert.True(graph.UpdateHoverAtForTesting(GraphPoint(graph, injection, residual: true)));
            Assert.Same(injection, graph.HoveredInjectionForTesting);
            Assert.True(graph.HoveredResidualForTesting);
            Assert.Equal(2, graph.HoverInvalidationCountForTesting);
        });
    }

    [Fact]
    public void FitHoverIncludesFittedValueWhenSolutionIsAvailable()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            AttachSolution(experiment);
            var graph = ArrangeGraph(experiment);

            var fitLines = graph.HoverLinesForTesting(experiment.Injections[0], residual: false);
            var residualLines = graph.HoverLinesForTesting(experiment.Injections[0], residual: true);

            Assert.Contains(fitLines, line => line.StartsWith("Fitted: ", StringComparison.Ordinal));
            Assert.DoesNotContain(residualLines, line => line.StartsWith("Fitted: ", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ClearingAndRebuildingInvalidateHoverOnlyWhenNeeded()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var graph = ArrangeGraph(experiment);
            var point = GraphPoint(graph, experiment.Injections[0], residual: false);

            Assert.True(graph.UpdateHoverAtForTesting(point));
            Assert.True(graph.ClearHoverForTesting());
            Assert.Null(graph.HoveredInjectionForTesting);
            Assert.Equal(2, graph.HoverInvalidationCountForTesting);

            Assert.False(graph.ClearHoverForTesting());
            Assert.Equal(2, graph.HoverInvalidationCountForTesting);

            Assert.True(graph.UpdateHoverAtForTesting(point));
            graph.FitToData();

            Assert.Null(graph.HoveredInjectionForTesting);
            Assert.Equal(3, graph.HoverInvalidationCountForTesting);
        });
    }

    [Fact]
    public void ClickingInjectionStillTogglesInclusionAndRefreshesGraph()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var graph = ArrangeGraph(experiment);
            var injection = experiment.Injections[0];
            var point = GraphPoint(graph, injection, residual: false);
            var graphChangedCount = 0;
            graph.GraphChanged += (_, _) => graphChangedCount++;

            Assert.True(graph.UpdateHoverAtForTesting(point));
            Assert.True(injection.Include);

            Assert.True(graph.ToggleInjectionAtForTesting(point));

            Assert.False(injection.Include);
            Assert.Equal(1, graphChangedCount);
            Assert.Null(graph.HoveredInjectionForTesting);
            Assert.Equal(1, graph.HoverInvalidationCountForTesting);
        });
    }

    static IntegratedHeatsGraphControl ArrangeGraph(ExperimentData experiment)
    {
        var graph = new IntegratedHeatsGraphControl { Experiment = experiment };
        graph.Measure(new Size(800, 600));
        graph.Arrange(new Rect(0, 0, 800, 600));
        return graph;
    }

    static Point GraphPoint(IntegratedHeatsGraphControl graph, InjectionData injection, bool residual)
    {
        var point = graph.InjectionPointForTesting(injection, residual);
        Assert.True(point.HasValue);
        return point.Value;
    }

    static ExperimentData CreateExperiment()
    {
        var experiment = new ExperimentData("analysis-hover.itc")
        {
            CellConcentration = new FloatWithError(35e-6),
            SyringeConcentration = new FloatWithError(420e-6),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };

        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(
                experiment,
                index,
                2e-6,
                experiment.SyringeConcentration * 2e-6,
                include: true)
            {
                ActualCellConcentration = experiment.CellConcentration * 0.99,
                ActualTitrantConcentration = (index + 1) * 5e-6,
                Ratio = (index + 1) * 5e-6 / (experiment.CellConcentration * 0.99),
            };
            injection.SetPeakArea(new FloatWithError(-2e-6 + index * 1e-7, 1e-8));
            experiment.Injections.Add(injection);
        }

        return experiment;
    }

    static void AttachSolution(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        experiment.Model = model;
    }
}
