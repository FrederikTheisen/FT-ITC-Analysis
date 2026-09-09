using System;
using System.Linq;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisInterpretationThermogramTests
{
    [Fact]
    public void EmptyAndNonfiniteRawDataHaveNoTrace()
    {
        Assert.Null(AnalysisInterpretationThermograms.Compress(Array.Empty<(double, double, double?)>()));
        Assert.Null(AnalysisInterpretationThermograms.Compress(new[]
        {
            (double.NaN, 1d, (double?)1), (1d, double.PositiveInfinity, (double?)1)
        }));
    }

    [Fact]
    public void EarliestValidTimeAnchorsHalfOpenBinsAndPartialTail()
    {
        var trace = AnalysisInterpretationThermograms.Compress(new[]
        {
            (34d, 4d, (double?)null), (3d, 1d, (double?)null),
            (18d, 2d, (double?)null), (double.NegativeInfinity, 99d, (double?)null)
        });
        Assert.Equal(3, trace.AnchorTimeSeconds);
        Assert.Equal(15, trace.BinWidthSeconds);
        Assert.Equal(2, trace.PowerOffsetWatts);
        Assert.Equal(4, trace.SourceSampleCount);
        Assert.Equal(3, trace.FiniteSampleCount);
        Assert.Equal(3, trace.PowerMinMax.Count);
        Assert.Equal(new double?[] { -1_000_000, -1_000_000 }, trace.PowerMinMax[0]);
        Assert.Equal(new double?[] { 0, 0 }, trace.PowerMinMax[1]);
        Assert.Equal(new double?[] { 2_000_000, 2_000_000 }, trace.PowerMinMax[2]);
        Assert.Null(trace.BaselineMinMax);
    }

    [Fact]
    public void BaselineExtremaNeedNotOccurAtPowerExtrema()
    {
        // Median raw power is 5 W. Baseline extremes occur at the two middle-power points.
        var trace = AnalysisInterpretationThermograms.Compress(new[]
        {
            (0d, 0d, (double?)10), (1d, 10d, (double?)3),
            (2d, 5d, (double?)50), (3d, 5d, (double?)-2)
        });
        Assert.Equal(5, trace.PowerOffsetWatts);
        Assert.Equal(new double?[] { -5_000_000, 5_000_000 }, Assert.Single(trace.PowerMinMax));
        Assert.Equal(new double?[] { -7_000_000, 45_000_000 }, Assert.Single(trace.BaselineMinMax));
    }

    [Fact]
    public void GapsStayAlignedAndBaselineCanExistWithoutFinitePower()
    {
        var trace = AnalysisInterpretationThermograms.Compress(new[]
        {
            (0d, 1d, (double?)double.NaN), (16d, double.NaN, (double?)9),
            (45d, 3d, (double?)double.PositiveInfinity)
        });
        Assert.Equal(4, trace.PowerMinMax.Count);
        Assert.Equal(4, trace.BaselineMinMax.Count);
        Assert.Equal(new double?[] { null, null }, trace.PowerMinMax[1]);
        Assert.Equal(new double?[] { null, null }, trace.PowerMinMax[2]);
        Assert.Equal(new double?[] { 7_000_000, 7_000_000 }, trace.BaselineMinMax[1]);
        Assert.Equal(new double?[] { null, null }, trace.BaselineMinMax[0]);
        Assert.Equal(new double?[] { null, null }, trace.BaselineMinMax[2]);
        Assert.Equal(new double?[] { null, null }, trace.BaselineMinMax[3]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void ConstantAndSingleSampleIntervalsArePairs(int count)
    {
        var trace = AnalysisInterpretationThermograms.Compress(
            Enumerable.Range(0, count).Select(i => ((double)i, 4d, (double?)null)));
        Assert.Equal(new double?[] { 0, 0 }, Assert.Single(trace.PowerMinMax));
    }

    [Fact]
    public void ExcessiveTimeSpansDoNotAllocateDenseArrays()
    {
        Assert.Null(AnalysisInterpretationThermograms.Compress(new[]
        {
            (0d, 1d, (double?)null), (double.MaxValue, 2d, (double?)null)
        }));
    }

    [Fact]
    public void ActualPackageSerializationAndCopyPreservePairsAndOmitUnavailableBaseline()
    {
        var rawOnly = AnalysisInterpretationThermograms.Compress(new[] { (5d, 2d, (double?)null) });
        var withBaseline = AnalysisInterpretationThermograms.Compress(new[] { (5d, 2d, (double?)3) });
        var package = new AnalysisInterpretationPackage();
        package.Results.Add(new InterpretationResultEvidence
        {
            Experiments = new() { new() { Thermogram = rawOnly } }
        });
        package.SupportingExperiments.Add(new() { Thermogram = withBaseline });
        AnalysisInterpretationThermograms.UpdateBoundary(package);
        var copy = AnalysisInterpretationThermograms.Copy(package);
        Assert.True(copy.DataBoundary.ContainsRawThermogramSamples);
        Assert.True(copy.DataBoundary.ContainsBaselineArrays);
        Assert.Equal(new double?[] { 0, 0 }, copy.Results[0].Experiments[0].Thermogram.PowerMinMax[0]);
        Assert.Equal(new double?[] { 1_000_000, 1_000_000 }, copy.SupportingExperiments[0].Thermogram.BaselineMinMax[0]);
        using var json = JsonDocument.Parse(AnalysisInterpretationPromptBuilder.Build(copy).CanonicalPackageJson);
        var trace = json.RootElement.GetProperty("results")[0].GetProperty("experiments")[0].GetProperty("thermogram");
        Assert.Equal("uniform-minmax-v1", trace.GetProperty("encoding").GetString());
        Assert.Equal(2, trace.GetProperty("powerMinMax")[0].GetArrayLength());
        foreach (var removed in new[] { "samples", "endpoints", "sourceIndex", "timeSeconds", "retainedSampleCount", "baselineMinMax" })
            Assert.False(trace.TryGetProperty(removed, out _), removed);
        Assert.True(AnalysisInterpretationThermograms.OmitTraces(copy, "test omission"));
        Assert.Null(copy.Results[0].Experiments[0].Thermogram);
        Assert.Null(copy.SupportingExperiments[0].Thermogram);
        Assert.False(copy.DataBoundary.ContainsRawThermogramSamples);
        Assert.False(copy.DataBoundary.ContainsBaselineArrays);
        Assert.DoesNotContain("test omission", package.Omissions);
        Assert.NotNull(package.SupportingExperiments[0].Thermogram);
    }
}
