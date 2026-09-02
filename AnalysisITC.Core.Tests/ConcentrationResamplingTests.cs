using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ConcentrationResamplingTests
{
    [Fact]
    public void LognormalFactorsArePositiveMeanPreservingAndHaveRequestedSd()
    {
        const double fractionalSd = 0.1;
        // This test intentionally uses one stream for the population. The small
        // fixed-seed tolerance makes the distribution contract reproducible.
        var random = new Random(123);
        var factors = Enumerable.Range(0, 100_000)
            .Select(_ => Distribution.LognormalFactor(fractionalSd, random))
            .ToArray();

        Assert.All(factors, factor =>
        {
            Assert.True(double.IsFinite(factor));
            Assert.True(factor > 0);
        });

        var mean = factors.Average();
        var populationSd = Math.Sqrt(factors.Select(factor => (factor - mean) * (factor - mean)).Average());

        Assert.InRange(mean, 0.998, 1.002);
        Assert.InRange(populationSd, 0.098, 0.102);
    }

    [Fact]
    public void LognormalFactorsAreDeterministicAndZeroIsIdentity()
    {
        var first = Distribution.LognormalFactor(0.25, new Random(173));
        var second = Distribution.LognormalFactor(0.25, new Random(173));

        Assert.Equal(first, second);

        var random = new Random(173);
        Assert.Equal(1.0, Distribution.LognormalFactor(0, random));
        Assert.Equal(new Random(173).NextDouble(), random.NextDouble());
    }

    [Fact]
    public void LognormalFactorsRejectInvalidFractionalSd()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Distribution.LognormalFactor(-0.01, new Random(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Distribution.LognormalFactor(double.NaN, new Random(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Distribution.LognormalFactor(double.PositiveInfinity, new Random(1)));
    }

    [Fact]
    public void ConcentrationCloneUsesExplicitErrorsAndPropagatesFactorsCoherently()
    {
        var source = CreateExperiment(
            new FloatWithError(30e-6, 3e-6),
            new FloatWithError(100e-6, 10e-6));
        source.AddSegment(new TandemExperimentSegment(0, 30e-6, 0));
        source.AddSegment(new TandemExperimentSegment(2, 21e-6, 4e-6));

        var options = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
            IncludeConcentrationErrorsInBootstrap = true,
            EnableAutoConcentrationVariance = true,
            AutoConcentrationVariance = 0.5,
        };
        const int seed = 37;
        var expectedRandom = new Random(seed);
        expectedRandom.Next(2);
        expectedRandom.Next(2);
        var expectedCellFactor = Distribution.LognormalFactor(0.1, expectedRandom);
        var expectedSyringeFactor = Distribution.LognormalFactor(0.1, expectedRandom);

        var clone = source.GetSynthClone(options, new Random(seed));

        Assert.Equal(source.CellConcentration.Value * expectedCellFactor, clone.CellConcentration.Value, 12);
        Assert.Equal(source.CellConcentration.SD * expectedCellFactor, clone.CellConcentration.SD, 12);
        Assert.Equal(source.SyringeConcentration.Value * expectedSyringeFactor, clone.SyringeConcentration.Value, 12);
        Assert.Equal(source.SyringeConcentration.SD * expectedSyringeFactor, clone.SyringeConcentration.SD, 12);

        foreach (var sourceInjection in source.Injections)
        {
            var cloneInjection = clone.Injections.Single(injection => injection.ID == sourceInjection.ID);
            var expectedCell = sourceInjection.ActualCellConcentration * expectedCellFactor;
            var expectedTitrant = sourceInjection.ActualTitrantConcentration * expectedSyringeFactor;

            Assert.Equal(expectedCell, cloneInjection.ActualCellConcentration, 12);
            Assert.Equal(expectedTitrant, cloneInjection.ActualTitrantConcentration, 12);
            Assert.Equal(expectedTitrant / expectedCell, cloneInjection.Ratio, 12);
            Assert.Equal(clone.SyringeConcentration.Value * cloneInjection.Volume, cloneInjection.InjectionMass, 12);
        }

        Assert.Equal(source.Segments.Count, clone.Segments.Count);
        for (var index = 0; index < source.Segments.Count; index++)
        {
            var sourceSegment = source.Segments[index];
            var cloneSegment = clone.Segments[index];
            Assert.Equal(sourceSegment.SegmentInitialActiveCellConc * expectedCellFactor,
                cloneSegment.SegmentInitialActiveCellConc, 12);
            Assert.Equal(sourceSegment.SegmentInitialActiveTitrantConc * expectedSyringeFactor,
                cloneSegment.SegmentInitialActiveTitrantConc, 12);
        }

        Assert.Equal(30e-6, source.CellConcentration.Value, 12);
        Assert.Equal(100e-6, source.SyringeConcentration.Value, 12);
        Assert.Equal(30e-6, source.Injections[0].ActualCellConcentration, 12);
        Assert.Equal(30e-6, source.Segments[0].SegmentInitialActiveCellConc, 12);
    }

    [Fact]
    public void ConcentrationCloneUsesAutomaticErrorsOnlyWhenExplicitErrorsAreMissing()
    {
        var source = CreateExperiment(new FloatWithError(30e-6), new FloatWithError(100e-6));
        var options = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
            IncludeConcentrationErrorsInBootstrap = true,
            EnableAutoConcentrationVariance = true,
            AutoConcentrationVariance = 0.2,
        };
        var expectedRandom = new Random(41);
        expectedRandom.Next(2);
        expectedRandom.Next(2);
        var expectedCellFactor = Distribution.LognormalFactor(0.2, expectedRandom);
        var expectedSyringeFactor = Distribution.LognormalFactor(0.2, expectedRandom);

        var clone = source.GetSynthClone(options, new Random(41));

        Assert.Equal(source.CellConcentration.Value * expectedCellFactor, clone.CellConcentration.Value, 12);
        Assert.Equal(source.SyringeConcentration.Value * expectedSyringeFactor, clone.SyringeConcentration.Value, 12);
    }

    [Fact]
    public void ConcentrationCloneDisabledLeavesConcentrationsAndFallbackRatiosUnchanged()
    {
        var source = CreateExperiment(new FloatWithError(30e-6), new FloatWithError(100e-6));
        source.Injections[0].ActualCellConcentration = 0;
        source.Injections[0].Ratio = 73;
        var options = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            IncludeConcentrationErrorsInBootstrap = false,
            EnableAutoConcentrationVariance = true,
            AutoConcentrationVariance = 0.2,
        };

        var clone = source.GetSynthClone(options, new Random(41));

        Assert.Equal(source.CellConcentration.Value, clone.CellConcentration.Value, 12);
        Assert.Equal(source.SyringeConcentration.Value, clone.SyringeConcentration.Value, 12);
        Assert.Equal(0, clone.Injections[0].ActualCellConcentration);
        Assert.Equal(73, clone.Injections[0].Ratio);
    }

    [Fact]
    public void LeaveOneOutIgnoresStaleConcentrationVarianceOptions()
    {
        var source = CreateExperiment(
            new FloatWithError(30e-6, 3e-6),
            new FloatWithError(100e-6, 10e-6));
        source.AddSegment(new TandemExperimentSegment(0, 30e-6, 7e-6));
        var options = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut,
            IncludeConcentrationErrorsInBootstrap = true,
            EnableAutoConcentrationVariance = true,
            AutoConcentrationVariance = 0.5,
            DiscardedDataPoint = 1,
        };

        var clone = source.GetSynthClone(options, new Random(41));

        Assert.Equal(source.CellConcentration.Value, clone.CellConcentration.Value, 12);
        Assert.Equal(source.CellConcentration.SD, clone.CellConcentration.SD, 12);
        Assert.Equal(source.SyringeConcentration.Value, clone.SyringeConcentration.Value, 12);
        Assert.Equal(source.SyringeConcentration.SD, clone.SyringeConcentration.SD, 12);
        Assert.Equal(source.Segments[0].SegmentInitialActiveCellConc,
            clone.Segments[0].SegmentInitialActiveCellConc, 12);
        Assert.Equal(source.Segments[0].SegmentInitialActiveTitrantConc,
            clone.Segments[0].SegmentInitialActiveTitrantConc, 12);

        foreach (var sourceInjection in source.Injections)
        {
            var cloneInjection = clone.Injections.Single(injection => injection.ID == sourceInjection.ID);
            Assert.Equal(sourceInjection.ActualCellConcentration,
                cloneInjection.ActualCellConcentration, 12);
            Assert.Equal(sourceInjection.ActualTitrantConcentration,
                cloneInjection.ActualTitrantConcentration, 12);
            Assert.Equal(sourceInjection.Ratio, cloneInjection.Ratio, 12);
        }
    }

    [Fact]
    public void ConcentrationClonePreservesFallbackRatiosForInvalidCellStates()
    {
        var source = CreateExperiment(new FloatWithError(30e-6), new FloatWithError(100e-6));
        source.Injections[0].ActualCellConcentration = 0;
        source.Injections[0].Ratio = 73;
        source.Injections[1].ActualCellConcentration = double.NaN;
        source.Injections[1].Ratio = 91;
        var options = new ModelCloneOptions
        {
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            IncludeConcentrationErrorsInBootstrap = true,
            EnableAutoConcentrationVariance = true,
            AutoConcentrationVariance = 0.2,
        };

        var clone = source.GetSynthClone(options, new Random(41));

        Assert.Equal(73, clone.Injections[0].Ratio);
        Assert.Equal(91, clone.Injections[1].Ratio);
    }

    static ExperimentData CreateExperiment(FloatWithError cell, FloatWithError syringe)
    {
        var experiment = new ExperimentData("concentration-resampling.itc")
        {
            CellConcentration = cell,
            SyringeConcentration = syringe,
            CellVolume = 1e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };

        AddInjection(experiment, 0, 30e-6, 5e-6, 0.1);
        AddInjection(experiment, 1, 24e-6, 10e-6, 0.4);
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
        experiment.Model = model;
        return experiment;
    }

    static void AddInjection(ExperimentData experiment, int id, double cell, double titrant, double ratio)
    {
        experiment.Injections.Add(new InjectionData(experiment, id, 2e-6, 0, include: true)
        {
            ActualCellConcentration = cell,
            ActualTitrantConcentration = titrant,
            Ratio = ratio,
        });
    }
}
