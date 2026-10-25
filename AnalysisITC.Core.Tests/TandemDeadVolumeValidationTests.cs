using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class TandemDeadVolumeValidationTests
{
    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidVolumeIsRejectedBeforeProcessingChangesExistingState(double volume)
    {
        foreach (var method in Enum.GetValues<DilutionMethod>())
        foreach (var importing in new[] { false, true })
        {
            var data = Source(1);
            data.ReplaceSegments(new[] { new TandemExperimentSegment(0, 17, 19) });
            var originalSegments = data.Segments;
            var originalState = State(data);
            var settings = Settings(volume);

            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                if (importing)
                    TandemConcatenation.ProcessInjectionsForTandemImport(data, Segments(), settings, method);
                else
                    TandemConcatenation.ProcessInjectionsWithBackMixing(data, Segments(), settings, method);
            });

            Assert.Equal("DeadVolume", error.ParamName);
            Assert.Equal(originalState, State(data));
            Assert.Same(originalSegments, data.Segments);
            Assert.Equal(volume, settings.DeadVolume);
        }
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void BothMergeOverloadsRejectInvalidVolumeWithoutChangingSources(double volume)
    {
        var sources = new List<ExperimentData> { Source(1), Source(2) };
        var original = sources.Select(State).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TandemConcatenation.ConcatTandemWithBackMixing(sources, Settings(volume)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TandemConcatenation.ConcatTandemWithBackMixing(sources, Settings(volume), new[] { .2 }));
        for (var i = 0; i < sources.Count; i++) Assert.Equal(original[i], State(sources[i]));
        Assert.All(sources, source => Assert.False(source.IsTandemExperiment));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidVolumeFailsBeforeAnyScanCandidate(double volume)
    {
        var sources = new List<ExperimentData> { Source(1), Source(2) };
        var original = sources.Select(State).ToArray();
        var progressCalls = 0;
        void Progress(int completed, int total) => progressCalls++;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TandemMixingScanner.Run(sources, Settings(volume), Progress, new[] { 0.0 }));
        foreach (var criterion in Enum.GetValues<TandemMixingCriterion>())
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TandemMixingScanner.FindBestAdaptive(sources, Settings(volume), Progress, criterion));

        Assert.Equal(0, progressCalls);
        for (var i = 0; i < sources.Count; i++) Assert.Equal(original[i], State(sources[i]));
    }

    [Theory]
    [InlineData(DilutionMethod.MicroCal, false)]
    [InlineData(DilutionMethod.MicroCal, true)]
    [InlineData(DilutionMethod.Exponential, false)]
    [InlineData(DilutionMethod.Exponential, true)]
    [InlineData(DilutionMethod.DiscreteDisplacement, false)]
    [InlineData(DilutionMethod.DiscreteDisplacement, true)]
    public void ZeroAndPositiveVolumeRetainWholeCompartmentMassBalance(DilutionMethod method, bool remove)
    {
        foreach (var dead in new[] { 0.0, .2 })
        {
            var data = Source(1);
            var settings = Settings(dead);
            settings.DidRemoveOverflow = remove;
            settings.MixingFraction = 1;
            TandemConcatenation.ProcessInjectionsWithBackMixing(data, Segments(), settings, method);

            // Full mixing without removal conserves 1 + dead mol cell material
            // and .2 mol ligand in 1 + dead + .2 L, independently of displacement.
            var expectedCell = (1 + dead) / (1 + dead + .2);
            var expectedLigand = .2 / (1 + dead + .2);
            if (remove)
            {
                var retained = method == DilutionMethod.MicroCal ? 9.0 / 11
                    : method == DilutionMethod.Exponential ? Math.Exp(-.2) : .8;
                var ligand = method == DilutionMethod.MicroCal ? .18
                    : method == DilutionMethod.Exponential ? 1 - Math.Exp(-.2) : .2;
                // At dead=0 all expelled liquid is removed. At dead=.2,
                // removal takes half the dead compartment's material.
                expectedCell = dead == 0 ? retained : (retained + 1.2) / 2.4;
                expectedLigand = dead == 0 ? ligand : (ligand + .2) / 2.4;
            }

            Assert.Equal(expectedCell, data.Segments[1].SegmentInitialActiveCellConc, 14);
            Assert.Equal(expectedLigand, data.Segments[1].SegmentInitialActiveTitrantConc, 14);
            Assert.All(State(data), value => Assert.True(double.IsFinite(value)));
        }
    }

    [Fact]
    public void SimpleProcessingIgnoresInactiveInvalidVolume()
    {
        foreach (var method in Enum.GetValues<DilutionMethod>())
        {
            var data = Source(1);
            var settings = Settings(double.NaN);
            settings.UseBackMixingMethod = false;
            TandemConcatenation.ProcessInjectionsForTandemImport(data, Segments(), settings, method);
            Assert.All(State(data), value => Assert.True(double.IsFinite(value)));
        }
        var sources = new List<ExperimentData> { Source(1), Source(2) };
        var inactive = Settings(double.NaN);
        inactive.UseBackMixingMethod = false;
        Assert.All(State(TandemConcatenation.ConcatTandemWithBackMixing(sources, inactive)),
            value => Assert.True(double.IsFinite(value)));
    }

    static TandemConcatenation.BackMixingSettings Settings(double volume) => new()
    { UseBackMixingMethod = true, DeadVolume = volume, MixingFraction = .2 };

    static TandemConcatenation.TandemInjectionSegment[] Segments() => new[]
    { new TandemConcatenation.TandemInjectionSegment(0, 1), new TandemConcatenation.TandemInjectionSegment(1, 1) };

    static double[] State(ExperimentData data) => data.Injections.SelectMany(injection => new[]
    { injection.ActualCellConcentration, injection.ActualTitrantConcentration, injection.Ratio }).ToArray();

    static ExperimentData Source(int index)
    {
        var data = new ExperimentData($"dead-volume-{index}.itc")
        {
            CellVolume = 1, CellConcentration = new(1), SyringeConcentration = new(1),
            DataPoints = new List<DataPoint> { new(0, 0, 25), new(1, 0, 25) }
        };
        foreach (var volume in new[] { .2, .3 })
        {
            var injection = new InjectionData(data, volume)
            { ActualCellConcentration = 17, ActualTitrantConcentration = 19, Ratio = 23 };
            injection.SetPeakArea(new FloatWithError(-1e-5, 1e-9));
            data.Injections.Add(injection);
        }
        return data;
    }
}
