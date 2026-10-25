using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class TandemMixingFractionValidationTests
{
    public static IEnumerable<object[]> InvalidFractions() => new[]
    { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -.1, 1.1, -double.Epsilon, Math.BitIncrement(1.0) }
        .Select(value => new object[] { value });

    [Theory]
    [MemberData(nameof(InvalidFractions))]
    public void ProcessingRejectsInvalidFractionsBeforeChangingAnyState(double invalid)
    {
        foreach (var method in Enum.GetValues<DilutionMethod>())
        foreach (var entry in new[] { "shared", "transitions", "import" })
        {
            var data = Source(1);
            data.ReplaceSegments(new[] { new TandemExperimentSegment(0, 17, 19) });
            var originalSegments = data.Segments;
            var original = State(data);
            var originalMethod = data.AppliedDilutionMethod;
            var originalHeatMethod = data.HeatMethod;
            var settings = Settings(entry == "transitions" ? .2 : invalid);
            var fractions = new[] { .2, invalid };

            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                if (entry == "shared")
                    TandemConcatenation.ProcessInjectionsWithBackMixing(data, Segments(), settings, method);
                else if (entry == "import")
                    TandemConcatenation.ProcessInjectionsForTandemImport(data, Segments(), settings, method);
                else
                    TandemConcatenation.ProcessInjectionsWithBackMixing(data, Segments(), settings, fractions, method);
            });

            Assert.Equal("transitionMixingFractions", error.ParamName);
            Assert.Equal(invalid, error.ActualValue);
            Assert.Same(originalSegments, data.Segments);
            Assert.Equal(original, State(data));
            Assert.Equal(originalMethod, data.AppliedDilutionMethod);
            Assert.Equal(originalHeatMethod, data.HeatMethod);
            Assert.Equal(entry == "transitions" ? .2 : invalid, settings.MixingFraction);
            Assert.Equal(new[] { .2, invalid }, fractions);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidFractions))]
    public void MergeOverloadsRejectInvalidFractionsWithoutChangingSources(double invalid)
    {
        var sources = new List<ExperimentData> { Source(1), Source(2), Source(3) };
        var original = sources.Select(State).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TandemConcatenation.ConcatTandemWithBackMixing(sources, Settings(invalid)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TandemConcatenation.ConcatTandemWithBackMixing(sources, Settings(.2), new[] { .2, invalid }));
        for (var i = 0; i < sources.Count; i++) Assert.Equal(original[i], State(sources[i]));
        Assert.All(sources, source => Assert.False(source.IsTandemExperiment));
    }

    [Theory]
    [MemberData(nameof(InvalidFractions))]
    public void InvalidLaterGridValueIsRejectedBeforeAnyCandidateRuns(double invalid)
    {
        var sources = new List<ExperimentData> { Source(1), Source(2) };
        var original = sources.Select(State).ToArray();
        var grid = new[] { 0, .2, invalid };
        var settings = Settings(.2);
        var progress = 0;
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TandemMixingScanner.Run(sources, settings, (_, _) => progress++, grid));
        Assert.Equal("mixingFractions", error.ParamName);
        Assert.Equal(invalid, error.ActualValue);
        Assert.Equal(0, progress);
        Assert.Equal(new[] { 0, .2, invalid }, grid);
        Assert.Equal(.2, settings.MixingFraction);
        for (var i = 0; i < sources.Count; i++) Assert.Equal(original[i], State(sources[i]));
    }

    [Fact]
    public void ExplicitFractionsIgnoreTheUnusedSharedFraction()
    {
        foreach (var method in Enum.GetValues<DilutionMethod>())
        {
            var expected = Source(1);
            var actual = Source(1);
            var fractions = new[] { 0.0, 1.0 };
            TandemConcatenation.ProcessInjectionsWithBackMixing(expected, Segments(), Settings(.2), fractions, method);
            var settings = Settings(double.NaN);
            TandemConcatenation.ProcessInjectionsWithBackMixing(actual, Segments(), settings, fractions, method);
            Assert.Equal(State(expected), State(actual));
            Assert.All(State(actual), value => Assert.True(double.IsFinite(value)));
            Assert.True(double.IsNaN(settings.MixingFraction));
        }
        var sources = new List<ExperimentData> { Source(1), Source(2), Source(3) };
        var merged = TandemConcatenation.ConcatTandemWithBackMixing(sources, Settings(double.NaN), new[] { 0.0, 1.0 });
        Assert.All(State(merged), value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void ScanCandidatesIgnoreTheUnusedSharedFraction()
    {
        var sources = new List<ExperimentData> { Source(1), Source(2) };
        var original = sources.Select(State).ToArray();
        var settings = Settings(double.NaN);
        var scan = TandemMixingScanner.Run(sources, settings, mixingFractions: new[] { 0.0, 1.0 });
        Assert.Equal(new[] { 0.0, 1.0 }, scan.MixingFractions);
        Assert.Equal(new[] { 0.0, 1.0 }, scan.Points.Select(point => point.FirstTransitionMixingFraction));
        Assert.True(double.IsNaN(settings.MixingFraction));
        for (var i = 0; i < sources.Count; i++) Assert.Equal(original[i], State(sources[i]));
    }

    [Fact]
    public void SimpleConcatenationIgnoresInactiveFractions()
    {
        foreach (var method in Enum.GetValues<DilutionMethod>())
        {
            var settings = Settings(double.NaN);
            settings.UseBackMixingMethod = false;
            var shared = Source(1);
            TandemConcatenation.ProcessInjectionsForTandemImport(shared, Segments(), settings, method);
            var explicitFractions = Source(1);
            TandemConcatenation.ProcessInjectionsWithBackMixing(explicitFractions, Segments(), settings,
                new[] { double.PositiveInfinity, -.1 }, method);
            Assert.Equal(State(shared), State(explicitFractions));
            Assert.All(State(shared), value => Assert.True(double.IsFinite(value)));
        }
        var sources = new List<ExperimentData> { Source(1), Source(2), Source(3) };
        var inactive = Settings(double.NaN);
        inactive.UseBackMixingMethod = false;
        Assert.All(State(TandemConcatenation.ConcatTandemWithBackMixing(sources, inactive)),
            value => Assert.True(double.IsFinite(value)));
        Assert.All(State(TandemConcatenation.ConcatTandemWithBackMixing(sources, inactive,
            new[] { double.PositiveInfinity, -.1 })), value => Assert.True(double.IsFinite(value)));
    }

    static TandemConcatenation.BackMixingSettings Settings(double fraction) => new()
    { UseBackMixingMethod = true, DeadVolume = 80e-6, MixingFraction = fraction, DidRemoveOverflow = true };

    static TandemConcatenation.TandemInjectionSegment[] Segments() => new[]
    {
        new TandemConcatenation.TandemInjectionSegment(0, 2),
        new TandemConcatenation.TandemInjectionSegment(2, 2),
        new TandemConcatenation.TandemInjectionSegment(4, 2)
    };

    static double[] State(ExperimentData data) => data.Injections.SelectMany(injection => new[]
    { injection.ActualCellConcentration, injection.ActualTitrantConcentration, injection.Ratio,
      injection.RawPeakArea.Value, injection.RawPeakArea.SD }).Concat(data.Segments?.SelectMany(segment => new[]
    { segment.SegmentInitialActiveCellConc, segment.SegmentInitialActiveTitrantConc }) ?? Enumerable.Empty<double>()).ToArray();

    static ExperimentData Source(int index)
    {
        var data = new ExperimentData($"mixing-fraction-{index}.itc")
        {
            CellVolume = 200e-6, CellConcentration = new(10e-6), SyringeConcentration = new(100e-6),
            MeasuredTemperature = 25, DataPoints = new List<DataPoint> { new(0, 0, 25), new(1, 0, 25) }
        };
        for (var i = 0; i < 6; i++)
        {
            var injection = new InjectionData(data, 5e-6)
            { ActualCellConcentration = 17, ActualTitrantConcentration = 19, Ratio = 23 };
            injection.SetPeakArea(new FloatWithError(-1e-5 / (1 + index + i), 1e-9));
            data.Injections.Add(injection);
        }
        return data;
    }
}
