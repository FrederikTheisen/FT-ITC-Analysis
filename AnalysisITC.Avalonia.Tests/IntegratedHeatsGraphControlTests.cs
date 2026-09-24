using System;
using System.Globalization;
using System.Linq;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using AnalysisITC.Avalonia.Analysis;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

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
    public void OffscreenExcludedHeatCannotInterceptHoverOrClick()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var excluded = experiment.Injections[0];
            excluded.ToggleDataPointActive();
            excluded.SetPeakArea(new FloatWithError(-4e-5, 1e-8));
            var graph = ArrangeGraph(experiment);
            var screen = GraphPoint(graph, excluded, residual: false);
            var viewport = graph.ViewportForTesting;

            Assert.True(excluded.Enthalpy * Energy.ScaleFactor(graph.EnergyUnitForTesting) < viewport.YMin);
            Assert.False(graph.UpdateHoverAtForTesting(screen));
            Assert.False(graph.ToggleInjectionAtForTesting(screen));
            Assert.False(excluded.Include);
        });
    }

    [Fact]
    public void OverlappingHitRegionsSelectTheNearestInjection()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var first = experiment.Injections[0];
            var second = experiment.Injections[1];
            second.Ratio = first.Ratio + 0.0001;
            second.SetPeakArea(first.PeakArea);
            var graph = ArrangeGraph(experiment);
            var secondScreen = GraphPoint(graph, second, residual: false);

            Assert.True(graph.UpdateHoverAtForTesting(secondScreen));
            Assert.Same(second, graph.HoveredInjectionForTesting);
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
    public void HeatHoverShowsInjectionErrorAndMatchesOffsetDisplay()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var originalFamily = AppSettings.EnergyUnitFamily;
            var originalPrecision = AppSettings.NumberPrecision;
            var originalStyle = AppSettings.UncertaintyDisplayStyle;
            try
            {
                AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
                AppSettings.NumberPrecision = NumberPrecision.AllDecimals;
                AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation;

                var experiment = CreateExperiment();
                var model = new OneSetOfSites(experiment);
                model.InitializeParameters(experiment);
                model.Parameters.Table[ParameterType.Offset].Update(500);
                model.Solution = SolutionInterface.FromModel(
                    model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
                experiment.Model = model;

                var graph = ArrangeGraph(experiment);
                var injection = experiment.Injections[0];
                var raw = graph.HoverLinesForTesting(injection, residual: false)
                    .Single(line => line.StartsWith("Heat: ", StringComparison.Ordinal));

                graph.DrawWithOffset = false;
                graph.FitToData();
                var corrected = graph.HoverLinesForTesting(injection, residual: false)
                    .Single(line => line.StartsWith("Heat: ", StringComparison.Ordinal));

                var rawParts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var correctedParts = corrected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal("±", rawParts[2]);
                Assert.Equal("±", correctedParts[2]);
                Assert.Equal("kJ/mol", correctedParts[4]);

                var rawHeat = double.Parse(rawParts[1], CultureInfo.CurrentCulture);
                var correctedHeat = double.Parse(correctedParts[1], CultureInfo.CurrentCulture);
                var displayedError = double.Parse(correctedParts[3], CultureInfo.CurrentCulture);
                Assert.InRange(Math.Abs(rawHeat - injection.PeakArea.Value / injection.InjectionMass / 1000), 0, 0.0001);
                Assert.InRange(Math.Abs(correctedHeat - (injection.PeakArea.Value / injection.InjectionMass - 500) / 1000), 0, 0.0001);
                Assert.InRange(Math.Abs(displayedError - injection.PeakArea.SD / injection.InjectionMass / 1000), 0, 0.00001);
            }
            finally
            {
                AppSettings.EnergyUnitFamily = originalFamily;
                AppSettings.NumberPrecision = originalPrecision;
                AppSettings.UncertaintyDisplayStyle = originalStyle;
            }
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

    [Fact]
    public void PointerHoverAndRenderingUseCachedModelValues()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var (model, bootstrapModel) = AttachCountingSolution(experiment, includeBootstrap: true);
            var graph = ArrangeGraph(experiment);
            var evaluationsAfterFit = model.EvaluationCount + bootstrapModel!.EvaluationCount;

            var fitPoint = GraphPoint(graph, experiment.Injections[0], residual: false);
            var residualPoint = GraphPoint(graph, experiment.Injections[0], residual: true);

            Assert.False(graph.UpdateHoverAtForTesting(new Point(2, 2)));
            Assert.True(graph.UpdateHoverAtForTesting(fitPoint));
            Assert.True(graph.UpdateHoverAtForTesting(residualPoint));
            Assert.True(graph.UpdateHoverAtForTesting(GraphPoint(graph, experiment.Injections[1], residual: false)));
            Assert.NotEmpty(graph.HoverLinesForTesting(experiment.Injections[0], residual: false));
            Assert.NotEmpty(graph.HoverLinesForTesting(experiment.Injections[0], residual: true));

            using var bitmap = new RenderTargetBitmap(new PixelSize(800, 600));
            bitmap.Render(graph);
            bitmap.Render(graph);

            graph.Measure(new Size(900, 650));
            graph.Arrange(new Rect(0, 0, 900, 650));
            using var resizedBitmap = new RenderTargetBitmap(new PixelSize(900, 650));
            resizedBitmap.Render(graph);

            Assert.Equal(evaluationsAfterFit, model.EvaluationCount + bootstrapModel.EvaluationCount);
        });
    }

    [Fact]
    public void RefreshComputedDataUpdatesCacheWithoutChangingViewport()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var (model, _) = AttachCountingSolution(experiment);
            var graph = ArrangeGraph(experiment);
            var injection = experiment.Injections[0];
            var initialViewport = graph.ViewportForTesting;
            var initialFitValue = graph.CachedFitValueForTesting(injection);
            var evaluationsAfterFit = model.EvaluationCount;

            Assert.True(graph.UpdateHoverAtForTesting(GraphPoint(graph, injection, residual: false)));

            var enthalpy = model.Parameters.Table[ParameterType.Enthalpy1];
            enthalpy.SetValue(enthalpy.Value * 1.5, lockpar: true);
            graph.RefreshComputedData();

            Assert.True(model.EvaluationCount > evaluationsAfterFit);
            Assert.NotEqual(initialFitValue, graph.CachedFitValueForTesting(injection));
            Assert.Equal(initialViewport, graph.ViewportForTesting);
            Assert.Null(graph.HoveredInjectionForTesting);

            enthalpy.SetValue(enthalpy.Value * 20, lockpar: true);
            graph.FitToData();

            Assert.NotEqual(initialViewport, graph.ViewportForTesting);
        });
    }

    [Fact]
    public void RefreshRefitsViewportWhenEnergyUnitChanges()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var originalFamily = AppSettings.EnergyUnitFamily;
            try
            {
                AppSettings.EnergyUnitFamily = EnergyUnitFamily.Joules;
                var experiment = CreateExperiment();
                foreach (var injection in experiment.Injections)
                    injection.SetPeakArea(new FloatWithError(-2e-8, 1e-10));
                var (model, _) = AttachCountingSolution(experiment);
                var graph = ArrangeGraph(experiment);
                Assert.Equal(EnergyUnit.Joule, graph.EnergyUnitForTesting);
                var oldViewport = graph.ViewportForTesting;

                model.Parameters.Table[ParameterType.Offset].Update(5000);
                graph.RefreshComputedData();

                Assert.Equal(EnergyUnit.KiloJoule, graph.EnergyUnitForTesting);
                Assert.NotEqual(oldViewport, graph.ViewportForTesting);
                var fitValue = graph.CachedFitValueForTesting(experiment.Injections[0]);
                Assert.True(fitValue.HasValue);
                Assert.InRange(fitValue.Value, graph.ViewportForTesting.YMin, graph.ViewportForTesting.YMax);
            }
            finally
            {
                AppSettings.EnergyUnitFamily = originalFamily;
            }
        });
    }

    [Fact]
    public void UnifiedYIncludesHeatFromExperimentsWithDifferentXAxisTypes()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DataManager.Clear(DataClearMode.ResetSession);
            try
            {
                var ratioExperiment = CreateExperiment();
                var concentrationExperiment = CreateExperiment();
                concentrationExperiment.CellConcentration = new FloatWithError(0);
                foreach (var injection in concentrationExperiment.Injections)
                    injection.SetPeakArea(new FloatWithError(-8e-5, 1e-8));
                DataManager.AddData(new[] { ratioExperiment, concentrationExperiment });

                Assert.Equal(AnalysisXAxisType.MolarRatio, ratioExperiment.AxisType);
                Assert.Equal(AnalysisXAxisType.TitrantConcentration, concentrationExperiment.AxisType);

                var localGraph = ArrangeGraph(ratioExperiment);
                var unifiedGraph = new IntegratedHeatsGraphControl
                {
                    UnifiedYAxis = true,
                    Experiment = ratioExperiment,
                };
                unifiedGraph.Measure(new Size(800, 600));
                unifiedGraph.Arrange(new Rect(0, 0, 800, 600));

                var otherHeat = concentrationExperiment.Injections[0].Enthalpy
                    * Energy.ScaleFactor(unifiedGraph.EnergyUnitForTesting);
                Assert.True(otherHeat < localGraph.ViewportForTesting.YMin);
                Assert.InRange(otherHeat, unifiedGraph.ViewportForTesting.YMin, unifiedGraph.ViewportForTesting.YMax);
                Assert.Equal(localGraph.ViewportForTesting.XMin, unifiedGraph.ViewportForTesting.XMin);
                Assert.Equal(localGraph.ViewportForTesting.XMax, unifiedGraph.ViewportForTesting.XMax);
            }
            finally
            {
                DataManager.Clear(DataClearMode.ResetSession);
            }
        });
    }

    [Fact]
    public void ExcludedPointsRemainCachedButAreNotInteractiveWhenHidden()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var excluded = experiment.Injections[0];
            excluded.ToggleDataPointActive();
            var graph = new IntegratedHeatsGraphControl
            {
                ShowExcludedPoints = false,
                Experiment = experiment,
            };
            graph.Measure(new Size(800, 600));
            graph.Arrange(new Rect(0, 0, 800, 600));

            Assert.Null(graph.InjectionPointForTesting(excluded, residual: false));
            Assert.Empty(graph.HoverLinesForTesting(excluded, residual: false));
        });
    }

    [Fact]
    public void FailedComputedDataRefreshRetainsPreviousSnapshot()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var experiment = CreateExperiment();
            var (model, _) = AttachCountingSolution(experiment);
            var graph = ArrangeGraph(experiment);
            var injection = experiment.Injections[0];
            var cachedFitValue = graph.CachedFitValueForTesting(injection);
            var viewport = graph.ViewportForTesting;

            Assert.True(graph.UpdateHoverAtForTesting(GraphPoint(graph, injection, residual: false)));
            model.ThrowOnEvaluate = true;

            graph.RefreshComputedData();

            Assert.Equal(cachedFitValue, graph.CachedFitValueForTesting(injection));
            Assert.Equal(viewport, graph.ViewportForTesting);
            Assert.Same(injection, graph.HoveredInjectionForTesting);
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

    static (CountingOneSetOfSites Model, CountingOneSetOfSites? BootstrapModel) AttachCountingSolution(
        ExperimentData experiment,
        bool includeBootstrap = false)
    {
        var model = new CountingOneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(
            model,
            SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        experiment.Model = model;

        CountingOneSetOfSites? bootstrapModel = null;
        if (includeBootstrap)
        {
            bootstrapModel = new CountingOneSetOfSites(experiment);
            bootstrapModel.InitializeParameters(experiment);
            bootstrapModel.Solution = SolutionInterface.FromModel(
                bootstrapModel,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            model.Solution.BootstrapSolutions.Add(bootstrapModel.Solution);
        }

        return (model, bootstrapModel);
    }

    sealed class CountingOneSetOfSites : OneSetOfSites
    {
        public CountingOneSetOfSites(ExperimentData data) : base(data)
        {
        }

        public int EvaluationCount { get; private set; }
        public bool ThrowOnEvaluate { get; set; }

        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            EvaluationCount++;
            if (ThrowOnEvaluate)
                throw new InvalidOperationException("Synthetic graph refresh failure.");
            return base.Evaluate(injectionindex, withoffset);
        }
    }
}
