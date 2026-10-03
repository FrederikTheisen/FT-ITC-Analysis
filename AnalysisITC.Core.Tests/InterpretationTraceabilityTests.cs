using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using System.Text.Json;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class InterpretationTraceabilityTests
{
    [Fact]
    public void RawThermogramFingerprintIgnoresProcessingButTracksOrderedSamples()
    {
        var first = Raw();
        var duplicate = Raw();
        first.DataPoints.Add(new DataPoint(0, 1));
        first.DataPoints.Add(new DataPoint(1, 2));
        duplicate.DataPoints.Add(new DataPoint(0, 1));
        duplicate.DataPoints.Add(new DataPoint(1, 2));
        first.BaseLineCorrectedDataPoints = new() { new DataPoint(0, -100), new DataPoint(1, -200) };
        duplicate.BaseLineCorrectedDataPoints = new() { new DataPoint(0, 10), new DataPoint(1, 20) };
        first.Injections.Add(new InjectionData(first, 2e-6));
        duplicate.Injections.Add(new InjectionData(duplicate, 9e-6));
        first.Injections[0].SetPeakArea(new FloatWithError(5, 1));
        duplicate.Injections[0].SetPeakArea(new FloatWithError(50, 10));

        var fingerprint = AnalysisInterpretationPackageBuilder.SourceDataFingerprint(first, out var kind);
        Assert.Equal("rawThermogram", kind);
        Assert.Equal(fingerprint, AnalysisInterpretationPackageBuilder.SourceDataFingerprint(duplicate, out _));
        duplicate.DataPoints[1] = new DataPoint(2, 2);
        Assert.NotEqual(fingerprint, AnalysisInterpretationPackageBuilder.SourceDataFingerprint(duplicate, out _));
    }

    [Fact]
    public void IntegratedOnlyFingerprintTracksRawInputsAndIgnoresInclusion()
    {
        var first = Integrated(10, 2, 1e-6);
        var duplicate = Integrated(10, 2, 1e-6);
        var fingerprint = AnalysisInterpretationPackageBuilder.SourceDataFingerprint(first, out var kind);
        Assert.Equal("integratedHeats", kind);
        Assert.Equal(fingerprint, AnalysisInterpretationPackageBuilder.SourceDataFingerprint(duplicate, out _));

        duplicate.Injections[0].Include = false;
        duplicate.SyringeConcentration = new FloatWithError(0.25, 0.05);
        duplicate.AppliedDilutionMethod = DilutionMethod.Exponential;
        Assert.Equal(fingerprint, AnalysisInterpretationPackageBuilder.SourceDataFingerprint(duplicate, out _));
        duplicate.Injections[0].SetVolume(2e-6);
        Assert.NotEqual(fingerprint, AnalysisInterpretationPackageBuilder.SourceDataFingerprint(duplicate, out _));
        duplicate = Integrated(10, 3, 1e-6);
        Assert.NotEqual(fingerprint, AnalysisInterpretationPackageBuilder.SourceDataFingerprint(duplicate, out _));
        duplicate = Integrated(11, 2, 1e-6);
        Assert.NotEqual(fingerprint, AnalysisInterpretationPackageBuilder.SourceDataFingerprint(duplicate, out _));
        duplicate = Integrated(10, 2, 2e-6);
        Assert.NotEqual(fingerprint, AnalysisInterpretationPackageBuilder.SourceDataFingerprint(duplicate, out _));
    }

    [Fact]
    public void IntegratedOnlyFingerprintTracksInjectionOrder()
    {
        var first = IntegratedPair(1e-6, 2e-6);
        var reordered = IntegratedPair(2e-6, 1e-6);

        Assert.NotEqual(
            AnalysisInterpretationPackageBuilder.SourceDataFingerprint(first, out _),
            AnalysisInterpretationPackageBuilder.SourceDataFingerprint(reordered, out _));
    }

    [Fact]
    public void TraceabilityOmitsEmptyIdentifiersAndUnavailableSource()
    {
        var data = Raw();
        Assert.Null(AnalysisInterpretationPackageBuilder.SourceDataFingerprint(data, out var kind));
        Assert.Null(kind);
    }

    [Fact]
    public void TraceabilityIdentifiersAndFingerprintsSurviveCompactModelInput()
    {
        var package = new AnalysisInterpretationPackage
        {
            Report = new InterpretationReportEvidence { EvidenceId = "report-1", ReportId = "r", Name = "Report" },
            Result = new InterpretationResultEvidence
            {
                EvidenceId = "result-1", ResultId = "x", Name = "Result",
                Experiments =
                {
                    new InterpretationExperimentEvidence
                    {
                        EvidenceId = "experiment-1", ExperimentId = "e", Name = "Experiment",
                        Traceability = new InterpretationTraceabilityEvidence
                        {
                            ExternalExperimentId = "run-17", SourceDataKind = "rawThermogram",
                            SourceDataFingerprint = new string('a', 64),
                        },
                    },
                },
            },
        };

        var modelJson = AnalysisInterpretationPromptBuilder.Build(package).ModelPackageJson;
        using var model = JsonDocument.Parse(modelJson);
        var traceability = model.RootElement.GetProperty("results")[0].GetProperty("experiments")[0].GetProperty("traceability");

        Assert.Equal("run-17", traceability.GetProperty("externalExperimentId").GetString());
        Assert.Equal("rawThermogram", traceability.GetProperty("sourceDataKind").GetString());
        Assert.Equal(new string('a', 64), traceability.GetProperty("sourceDataFingerprint").GetString());
    }

    static ExperimentData Raw() => new("source.itc");

    static ExperimentData Integrated(double heat, double error, double volume)
    {
        var data = Raw();
        var injection = new InjectionData(data, volume);
        injection.SetPeakArea(new FloatWithError(heat, error));
        data.Injections.Add(injection);
        return data;
    }

    static ExperimentData IntegratedPair(double firstVolume, double secondVolume)
    {
        var data = Raw();
        var first = new InjectionData(data, 0, firstVolume, 0, true);
        first.SetPeakArea(new FloatWithError(10, 1));
        var second = new InjectionData(data, 1, secondVolume, 0, true);
        second.SetPeakArea(new FloatWithError(20, 2));
        data.Injections.Add(first);
        data.Injections.Add(second);
        return data;
    }
}
