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
