using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class TandemBufferSubtractionTests : IDisposable
{
    public TandemBufferSubtractionTests() => DataManager.Clear(DataClearMode.ResetSession);
    public void Dispose() => DataManager.Clear(DataClearMode.ResetSession);

    [Theory]
    [InlineData(BufferSubtractionMethod.MatchedInjection)]
    [InlineData(BufferSubtractionMethod.Linear)]
    [InlineData(BufferSubtractionMethod.ExponentialDecay)]
    public void AddingCorrectionToTandemIsRejectedWithoutChangingHeats(BufferSubtractionMethod method)
    {
        var target = Experiment("tandem", 10e-6);
        target.AddSegment(new TandemExperimentSegment(0, target.CellConcentration, 0));
        var blank = Experiment("blank", 2e-6);
        DataManager.AddData(new[] { target, blank });

        var error = Assert.Throws<HandledException>(() => target.SetBufferSubtraction(blank, method, notify: false));

        Assert.Contains("not supported for tandem", error.Message);
        Assert.Null(target.BufferSubtractionSettings);
        Assert.All(target.Injections, injection => Assert.Equal(10e-6, injection.PeakArea.Value));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AttributeEntryPointsRejectCorrectionBeforeChangingTarget(bool stored, bool copy)
    {
        var target = Experiment("tandem", 10e-6);
        target.AddSegment(new TandemExperimentSegment(0, target.CellConcentration, 0));
        var original = new ExperimentAttribute();
        original.UpdateOptionKey(AttributeKey.IonicStrength);
        target.Attributes.Add(original);
        var attribute = Correction(stored);

        Assert.Throws<HandledException>(() =>
        {
            if (copy) target.CopyAttributesFrom(new[] { attribute }, clear: true, notify: false);
            else target.AddOrUpdateAttribute(attribute, notify: false);
        });

        Assert.Same(original, Assert.Single(target.Attributes));
        Assert.All(target.Injections, injection => Assert.Equal(10e-6, injection.PeakArea.Value));
    }

    public static IEnumerable<object[]> CorrectedSources()
    {
        foreach (var mode in new[] { "simple", "fixed", "disabled", "individual", "grid", "adaptive", "model-free", "scan-data" })
            foreach (var index in new[] { 0, 1 })
                foreach (var stored in new[] { false, true })
                    yield return new object[] { mode, index, stored };
    }

    [Theory]
    [MemberData(nameof(CorrectedSources))]
    public void EveryMergeAndScanRejectsAnyCorrectedSource(string mode, int correctedIndex, bool stored)
    {
        var sources = new List<ExperimentData> { Experiment("first", 10e-6), Experiment("second", 10e-6) };
        sources[correctedIndex].AddOrUpdateAttribute(Correction(stored), notify: false);
        var settings = new TandemConcatenation.BackMixingSettings { UseBackMixingMethod = mode != "disabled" };
        var progressCalls = 0;
        Action<int, int> progress = (_, _) => progressCalls++;

        var error = Assert.Throws<ArgumentException>(() =>
        {
            switch (mode)
            {
                case "simple": TandemConcatenation.ConcatTandem(sources); break;
                case "fixed":
                case "disabled": TandemConcatenation.ConcatTandemWithBackMixing(sources, settings); break;
                case "individual": TandemConcatenation.ConcatTandemWithBackMixing(sources, settings, new[] { 0.2 }); break;
                case "grid": TandemMixingScanner.Run(sources, settings, progress, new[] { 0.2 }); break;
                case "adaptive": TandemMixingScanner.FindBestAdaptive(sources, settings, progress); break;
                case "model-free": TandemMixingScanner.FindBestAdaptive(sources, settings, progress, TandemMixingCriterion.ModelFree); break;
                case "scan-data": TandemMixingScanner.BuildScanExperiment(sources); break;
            }
        });

        Assert.Contains("Remove buffer subtraction", error.Message);
        Assert.Equal(0, progressCalls);
        Assert.NotNull(sources[correctedIndex].BufferSubtractionSettings);
        Assert.All(sources[correctedIndex].Injections, injection => Assert.Equal(8e-6, injection.PeakArea.Value, 15));
    }

    [Fact]
    public void RemovingSourceCorrectionAllowsAnUncorrectedScanExperiment()
    {
        var first = Experiment("first", 10e-6);
        var second = Experiment("second", 10e-6);
        second.AddOrUpdateAttribute(Correction(stored: true), notify: false);
        Assert.True(second.ClearBufferSubtraction(notify: false));

        var (scan, _) = TandemMixingScanner.BuildScanExperiment(new[] { first, second });

        Assert.Null(scan.BufferSubtractionSettings);
        Assert.All(scan.Injections, injection => Assert.Equal(10e-6, injection.PeakArea.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingStoredReferenceValuesOnTandemIsRejected(bool copy)
    {
        var target = Experiment("tandem", 10e-6);
        var original = Correction(stored: true);
        target.AddOrUpdateAttribute(original, notify: false);
        target.AddSegment(new TandemExperimentSegment(0, target.CellConcentration, 0));
        var changed = new BufferSubtractionSettings(original.StringValue, BufferSubtractionMethod.MatchedInjection,
            new BufferSubtractionReferenceSnapshot("blank", new[] { new BufferSubtractionReferencePoint(1, new FloatWithError(3e-6)) }))
            .ToAttribute();

        Assert.Throws<HandledException>(() =>
        {
            if (copy) target.CopyAttributesFrom(new[] { changed }, clear: true, notify: false);
            else target.AddOrUpdateAttribute(changed, notify: false);
        });

        Assert.Same(original, target.Attributes.Single());
        Assert.All(target.Injections, injection => Assert.Equal(8e-6, injection.PeakArea.Value, 15));
    }

    [Fact]
    public void ASourceWithMissingReferenceAndNoStoredValuesStillBlocksMerging()
    {
        var sources = new List<ExperimentData> { Experiment("first", 10e-6), Experiment("second", 10e-6) };
        sources[1].Attributes.Add(new BufferSubtractionSettings("missing", BufferSubtractionMethod.Linear).ToAttribute());
        Assert.Throws<ArgumentException>(() => TandemConcatenation.ConcatTandem(sources));
        Assert.Throws<ArgumentException>(() => TandemMixingScanner.BuildScanExperiment(sources));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("active")]
    [InlineData("specific")]
    [InlineData("name")]
    [InlineData("single-all")]
    [InlineData("single-active")]
    [InlineData("single-specific")]
    [InlineData("single-name")]
    public void CopyMenuActionsRejectTheWholeBatchWithAHandledWarning(string action)
    {
        var ordinary = Experiment("target ordinary", 10e-6);
        var original = new ExperimentAttribute();
        original.UpdateOptionKey(AttributeKey.IonicStrength);
        ordinary.Attributes.Add(original);
        var tandem = Experiment("target tandem", 10e-6);
        tandem.AddSegment(new TandemExperimentSegment(0, tandem.CellConcentration, 0));
        var source = Experiment("source", 10e-6);
        source.AddOrUpdateAttribute(Correction(stored: true), notify: false);
        DataManager.AddData(new[] { ordinary, tandem, source });
        Assert.Same(source, DataManager.Current);
        DocumentDirtyTracker.MarkClean();
        var showMessage = AppEventHandler.ShowAppMessage;
        var warnings = new List<Exception>();
        AppEventHandler.ShowAppMessage = (_, error) => warnings.Add(error);
        try
        {
            var attribute = source.Attributes.Single();
            switch (action)
            {
                case "all": DataManager.CopySelectedAttributesToAll(clear: true); break;
                case "active": DataManager.CopySelectedAttributesToActive(clear: true); break;
                case "specific": DataManager.CopySelectedAttributesToExperiment(tandem, clear: true); break;
                case "name": DataManager.CopySelectedAttributesToNameToken("target", clear: true); break;
                case "single-all": DataManager.CopySelectedAttributeToAll(attribute); break;
                case "single-active": DataManager.CopySelectedAttributeToActive(attribute); break;
                case "single-specific": DataManager.CopySelectedAttributeToExperiment(attribute, tandem); break;
                case "single-name": DataManager.CopySelectedAttributeToNameToken(attribute, "target"); break;
            }

            Assert.Contains("not supported for tandem", Assert.Single(warnings).Message);
            Assert.Same(original, Assert.Single(ordinary.Attributes));
            Assert.Empty(tandem.Attributes);
            Assert.False(DocumentDirtyTracker.IsDirty);
            Assert.All(ordinary.Injections.Concat(tandem.Injections), injection => Assert.Equal(10e-6, injection.PeakArea.Value));
        }
        finally { AppEventHandler.ShowAppMessage = showMessage; }
    }

    [Fact]
    public void CopyingBufferSubtractionToAnOrdinaryTargetRemainsAvailable()
    {
        var target = Experiment("target", 10e-6);
        var source = Experiment("source", 10e-6);
        source.AddOrUpdateAttribute(Correction(stored: true), notify: false);
        DataManager.AddData(new[] { target, source });

        DataManager.CopySelectedAttributeToExperiment(source.Attributes.Single(), target);

        Assert.NotNull(target.BufferSubtractionSettings.Snapshot);
        Assert.All(target.Injections, injection => Assert.Equal(8e-6, injection.PeakArea.Value, 15));
    }

    [Fact]
    public void CopyingOtherAttributesToATandemRemainsAvailable()
    {
        var target = Experiment("tandem", 10e-6);
        target.AddSegment(new TandemExperimentSegment(0, target.CellConcentration, 0));
        var source = Experiment("source", 10e-6);
        var attribute = new ExperimentAttribute();
        attribute.UpdateOptionKey(AttributeKey.IonicStrength);
        source.Attributes.Add(attribute);
        DataManager.AddData(new[] { target, source });

        DataManager.CopySelectedAttributesToAll();

        Assert.Equal(AttributeKey.IonicStrength, Assert.Single(target.Attributes).Key);
        Assert.All(target.Injections, injection => Assert.Equal(10e-6, injection.PeakArea.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviouslyStoredTandemCorrectionCanBeRetainedAndRemoved(bool stored)
    {
        var target = Experiment("tandem", 10e-6);
        target.AddOrUpdateAttribute(Correction(stored), notify: false);
        target.AddSegment(new TandemExperimentSegment(0, target.CellConcentration, 0));
        var method = target.BufferSubtractionSettings.Method;
        var reference = target.ReferenceExperiment;
        if (reference != null)
            Assert.False(target.SetBufferSubtraction(reference, method, notify: false));

        var attributes = target.Attributes.Select(attribute => attribute.Copy()).ToList();
        var metadata = new ExperimentAttribute();
        metadata.UpdateOptionKey(AttributeKey.IonicStrength);
        attributes.Add(metadata);
        Assert.True(target.UpdateDetailAttributes(attributes));
        Assert.All(target.Injections, injection => Assert.Equal(8e-6, injection.PeakArea.Value, 15));
        Assert.Throws<HandledException>(() => target.AddOrUpdateAttribute(
            new BufferSubtractionSettings(target.BufferSubtractionSettings.ReferenceExperimentId,
                BufferSubtractionMethod.Linear, target.BufferSubtractionSettings.Snapshot).ToAttribute(), notify: false));

        Assert.True(target.ClearBufferSubtraction(notify: false));
        Assert.Null(target.BufferSubtractionSettings);
        Assert.All(target.Injections, injection => Assert.Equal(10e-6, injection.PeakArea.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeProjectRoundTripRetainsExistingTandemCorrection(bool stored)
    {
        var target = Experiment("tandem", 10e-6);
        target.AddOrUpdateAttribute(Correction(stored), notify: false);
        target.AddSegment(new TandemExperimentSegment(0, target.CellConcentration, 0));
        var contents = stored ? new[] { target } : new[] { target, target.ReferenceExperiment };
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, contents);
        DataManager.Clear(DataClearMode.ResetSession);
        stream.Position = 0;

        var restored = (await FTXTCReader.ReadStream(stream)).OfType<ExperimentData>().ToArray();
        DataManager.AddData(restored);
        DataManager.ApplyOptions();
        var tandem = restored.Single(data => data.Name == "tandem");

        Assert.True(tandem.IsTandemExperiment);
        Assert.NotNull(tandem.BufferSubtractionSettings);
        Assert.All(tandem.Injections, injection => Assert.Equal(8e-6, injection.PeakArea.Value, 15));
        Assert.True(tandem.ClearBufferSubtraction(notify: false));
        Assert.All(tandem.Injections, injection => Assert.Equal(10e-6, injection.PeakArea.Value));
    }

    static ExperimentAttribute Correction(bool stored)
    {
        var blank = Experiment("blank", 2e-6);
        if (!stored) DataManager.AddData(blank);
        return new BufferSubtractionSettings(blank.UniqueID, BufferSubtractionMethod.MatchedInjection,
            stored ? BufferSubtractionReferenceSnapshot.Capture(blank) : null).ToAttribute();
    }

    internal static ExperimentData Experiment(string name, double heat)
    {
        var data = new ExperimentData(name)
        {
            CellVolume = 200e-6,
            CellConcentration = new FloatWithError(20e-6),
            SyringeConcentration = new FloatWithError(200e-6),
        };
        for (var index = 0; index < 8; index++)
        {
            var injection = InjectionData.FromPEAQFile(data, index, true, index * 10, 2e-6, 10, 1, 25);
            injection.SetPeakArea(new FloatWithError(heat));
            data.Injections.Add(injection);
            data.DataPoints.Add(new DataPoint(index * 10, 0, 25));
        }
        return data;
    }
}
