using System;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public class BufferSubtractionPresentationTests
{
    [Theory]
    [InlineData(2.001, false)]
    [InlineData(2.1, false)]
    [InlineData(1.9, false)]
    [InlineData(2.1001, true)]
    [InlineData(4, true)]
    public void ComparesEvenTheExcludedFirstInjection(double targetVolume, bool shouldWarn)
    {
        var warning = BufferSubtractionPresentation.InjectionVolumeWarning(
            Experiment(2), new[] { Experiment(targetVolume) });
        Assert.Equal(shouldWarn ? "Injection volumes differ from the reference." : "", warning);
    }

    [Fact]
    public void ComparesOverlappingUsablePairsAtTheirRecordedPositions()
    {
        var reference = Experiment(null, 2, 0, double.NaN, -1, 4, 2);
        var matching = Experiment(3, null, 2, 2, 2, double.PositiveInfinity, 2, 8);
        var different = Experiment(3, null, 2, 2, 2, 2, 4);

        Assert.Empty(BufferSubtractionPresentation.InjectionVolumeWarning(reference, new[] { matching }));
        Assert.Empty(BufferSubtractionPresentation.InjectionVolumeWarning(matching, new[] { reference }));
        Assert.Equal("Injection volumes differ from the reference for 2 of 3 selected targets.",
            BufferSubtractionPresentation.InjectionVolumeWarning(reference, new[] { matching, different, Experiment(1, 4) }));
        Assert.Empty(BufferSubtractionPresentation.InjectionVolumeWarning(null, new[] { matching }));
        Assert.Empty(BufferSubtractionPresentation.InjectionVolumeWarning(reference, Array.Empty<ExperimentData>()));
    }

    static ExperimentData Experiment(params double?[] microlitres)
    {
        var data = new ExperimentData("experiment");
        foreach (var volume in microlitres)
            data.Injections.Add(volume.HasValue
                ? InjectionData.FromPEAQFile(data, data.Injections.Count, false, 0, volume.Value * 1e-6, 10, 1, 25)
                : null);
        return data;
    }
}
