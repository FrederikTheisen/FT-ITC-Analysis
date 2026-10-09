using System;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class BufferSubtractionDuplicationTests : IDisposable
{
    const int InjectionCount = 4;
    const double TargetHeat = 10e-6;
    const double BlankHeat = 2e-6;
    const double UpdatedBlankHeat = 3e-6;

    public BufferSubtractionDuplicationTests() => DataManager.Clear(DataClearMode.ResetSession);
    public void Dispose() => DataManager.Clear(DataClearMode.ResetSession);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateKeepsCorrectedHeatsAndFollowsReferenceUpdates(bool withRawData)
    {
        var (target, blank) = AddTargetAndBlank(withRawData);
        Assert.All(target.Injections, injection => Assert.Equal(TargetHeat - BlankHeat, injection.PeakArea.Value, 15));

        var copy = Duplicate(target);

        Assert.Equal(target.BufferSubtractionSettings.ReferenceExperimentId, copy.BufferSubtractionSettings.ReferenceExperimentId);
        Assert.Equal(target.BufferSubtractionSettings.Method, copy.BufferSubtractionSettings.Method);
        Assert.All(copy.Injections, injection =>
        {
            Assert.Equal(TargetHeat, injection.RawPeakArea.Value, 15);
            Assert.Equal(TargetHeat - BlankHeat, injection.PeakArea.Value, 15);
        });

        SetHeats(blank, UpdatedBlankHeat);
        blank.UpdateProcessing();

        Assert.All(target.Injections, injection => Assert.Equal(TargetHeat - UpdatedBlankHeat, injection.PeakArea.Value, 15));
        Assert.All(copy.Injections, injection => Assert.Equal(TargetHeat - UpdatedBlankHeat, injection.PeakArea.Value, 15));
    }

    [Fact]
    public void ClearingTheDuplicateBufferLeavesTheOriginalSubscribed()
    {
        var (target, blank) = AddTargetAndBlank(withRawData: false);
        var copy = Duplicate(target);

        Assert.True(copy.ClearBufferSubtraction(notify: false));

        Assert.NotNull(target.BufferSubtractionSettings);
        Assert.All(copy.Injections, injection => Assert.Equal(TargetHeat, injection.PeakArea.Value, 15));
        Assert.All(target.Injections, injection => Assert.Equal(TargetHeat - BlankHeat, injection.PeakArea.Value, 15));

        SetHeats(blank, UpdatedBlankHeat);
        blank.UpdateProcessing();

        Assert.All(copy.Injections, injection => Assert.Equal(TargetHeat, injection.PeakArea.Value, 15));
        Assert.All(target.Injections, injection => Assert.Equal(TargetHeat - UpdatedBlankHeat, injection.PeakArea.Value, 15));
    }

    [Theory]
    [InlineData(1, PeakHeatDirection.Endothermal)]
    [InlineData(-1, PeakHeatDirection.Exothermal)]
    public void HeatDirectionUsesRawHeatsRegardlessOfBufferAssignmentOrder(
        double sign, PeakHeatDirection expected)
    {
        var blank = CreateExperiment("blank", sign * 20e-6, withRawData: false);
        var target = CreateExperiment("target", sign * TargetHeat, withRawData: false);
        DataManager.AddData(blank);
        DataManager.AddData(target);
        target.UpdateProcessing();

        target.SetReferenceExperiment(blank, notify: false);
        Assert.All(target.Injections, injection =>
        {
            Assert.Equal(-sign * TargetHeat, injection.PeakArea.Value, 15);
            Assert.Equal(expected, injection.HeatDirection);
        });

        // Reintegration with subtraction already assigned must keep the raw direction.
        SetHeats(target, sign * TargetHeat);
        target.UpdateProcessing();
        Assert.All(target.Injections, injection => Assert.Equal(expected, injection.HeatDirection));
        Assert.Equal(expected, target.AverageHeatDirection);

        var copy = Duplicate(target);
        copy.CalculateExperimentHeatDirection();
        Assert.All(copy.Injections, injection => Assert.Equal(expected, injection.HeatDirection));
        Assert.Equal(expected, copy.AverageHeatDirection);

        // Changing the reference reverses the corrected sign without changing raw heats.
        SetHeats(blank, 0);
        blank.UpdateProcessing();
        foreach (var data in new[] { target, copy })
        {
            Assert.All(data.Injections, injection =>
            {
                Assert.Equal(sign * TargetHeat, injection.PeakArea.Value, 15);
                Assert.Equal(expected, injection.HeatDirection);
            });
            Assert.Equal(expected, data.AverageHeatDirection);
        }

        target.ClearBufferSubtraction(notify: false);
        Assert.All(target.Injections, injection => Assert.Equal(expected, injection.HeatDirection));
        Assert.Equal(expected, target.AverageHeatDirection);
    }

    [Theory]
    [InlineData(4, 1, 10, PeakHeatDirection.Endothermal)]
    [InlineData(-4, 1, 10, PeakHeatDirection.Exothermal)]
    [InlineData(2, 1, 0, PeakHeatDirection.Unknown)]
    [InlineData(-2, 1, 0, PeakHeatDirection.Unknown)]
    [InlineData(3, 1, 0, PeakHeatDirection.Unknown)]
    [InlineData(-3, 1, 0, PeakHeatDirection.Unknown)]
    [InlineData(0, 0, 0, PeakHeatDirection.Unknown)]
    public void HeatDirectionUsesRawSdAndStrictThreeSdThreshold(
        double rawHeat, double rawSd, double blankSd, PeakHeatDirection expected)
    {
        // Values are in µJ here; the expected classifications are specified independently.
        var blank = CreateExperiment("blank", 20e-6, withRawData: false);
        foreach (var injection in blank.Injections)
            injection.SetPeakArea(new FloatWithError(20e-6, blankSd * 1e-6));
        var target = CreateExperiment("target", 0, withRawData: false);
        DataManager.AddData(blank);
        DataManager.AddData(target);
        target.SetReferenceExperiment(blank, notify: false);

        foreach (var injection in target.Injections)
            injection.SetPeakArea(new FloatWithError(rawHeat * 1e-6, rawSd * 1e-6));
        target.UpdateProcessing();

        Assert.All(target.Injections, injection =>
        {
            Assert.Equal(rawHeat * 1e-6, injection.RawPeakArea.Value, 15);
            Assert.Equal((rawHeat - 20) * 1e-6, injection.PeakArea.Value, 15);
            Assert.Equal(Math.Sqrt(rawSd * rawSd + blankSd * blankSd) * 1e-6,
                injection.PeakArea.SD, 15);
            Assert.Equal(expected, injection.HeatDirection);
        });
        Assert.Equal(expected, target.AverageHeatDirection);
    }

    static (ExperimentData Target, ExperimentData Blank) AddTargetAndBlank(bool withRawData)
    {
        var blank = CreateExperiment("blank", BlankHeat, withRawData);
        var target = CreateExperiment("target", TargetHeat, withRawData);
        DataManager.AddData(blank);
        DataManager.AddData(target);
        target.SetBufferSubtraction(blank, BufferSubtractionMethod.MatchedInjection, notify: false);
        return (target, blank);
    }

    static ExperimentData Duplicate(ExperimentData source)
    {
        DataManager.DuplicateSelectedData(source);
        return DataManager.Data.Last();
    }

    static ExperimentData CreateExperiment(string id, double heat, bool withRawData)
    {
        var data = new ExperimentData(id + ".itc")
        {
            CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6),
            CellVolume = 200e-6, TargetTemperature = 25, MeasuredTemperature = 25,
        };
        data.SetID(id);
        if (withRawData)
        {
            for (var time = 0; time <= 30 * (InjectionCount + 1); time += 2) data.DataPoints.Add(new DataPoint(time, 0, 25));
            data.BaseLineCorrectedDataPoints = data.DataPoints.Select(dp => dp.Copy()).ToList();
        }
        for (var index = 0; index < InjectionCount; index++)
            data.Injections.Add(new InjectionData(data, index, 2e-6, 2e-10, include: true) { Time = 30 + index * 30 });
        SetHeats(data, heat);
        return data;
    }

    static void SetHeats(ExperimentData data, double heat)
    {
        foreach (var injection in data.Injections) injection.SetPeakArea(new FloatWithError(heat, 1e-8));
    }
}
