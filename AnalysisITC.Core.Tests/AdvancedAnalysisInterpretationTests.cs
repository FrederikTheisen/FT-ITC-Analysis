using System;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

using Buffer = AnalysisITC.Core.Data.Buffer;

public sealed class AdvancedAnalysisInterpretationTests
{
    [Fact]
    public async Task BuilderUsesCompletedSettingsAndExportsAllAdvancedMappings()
    {
        var result = await LoadAdvancedResult();
        result.Solution.TemperatureDependence[ParameterType.Enthalpy1] = new LinearFitWithError(
            new FloatWithError(-100, 4.5, -105, -99),
            new FloatWithError(-25000, 4, -25008, -24992), 25);
        var completed = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
        result.SpolarRecordAnalysis.RestoreResult(
            FTSRMethod.SRFoldedMode.Intermediate, FTSRMethod.SRTempMode.MeanTemperature,
            new FTSRMethod.SROutput(new FloatWithError(-0.11, 0.01, -0.13, -0.09),
                new FloatWithError(-0.22, 0.02, -0.26, -0.18),
                new FloatWithError(42, 2, 38, 46), new FloatWithError(25, 0.5, 24, 26)), 20, completed);
        result.SpolarRecordAnalysis.FoldedMode = FTSRMethod.SRFoldedMode.ID;
        result.SpolarRecordAnalysis.TempMode = FTSRMethod.SRTempMode.ReferenceTemperature;
        result.ElectrostaticsAnalysis.RestoreResult(
            new IonicStrengthDependenceFit(new FloatWithError(2e-6, 0.1e-6, 1.8e-6, 2.2e-6),
                new FloatWithError(1.2, 0.1, 1.0, 1.4), new FloatWithError(0.3, 0.05, 0.2, 0.4), true),
            new LinearFitWithError(new FloatWithError(1.5, 0.1, 1.3, 1.7),
                new FloatWithError(-12, 0.2, -12.4, -11.6), 0),
            15, 16, completed, ErrorEstimationMethod.BootstrapResiduals);
        result.ProtonationAnalysis.RestoreResult(
            new FloatWithError(-25000, 500, -26000, -24000),
            new FloatWithError(0.8, 0.05, 0.7, 0.9), 18, completed,
            ErrorEstimationMethod.BootstrapResiduals);

        var report = new AnalysisReport { Name = "Advanced interpretation" };
        report.SetResultIds(new[] { result.UniqueID });
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);

        var spolar = Assert.Single(package.Result.AdvancedAnalyses, item => item.Type == "spolar-record");
        Assert.Equal("intermediate", spolar.CompletedFoldedMode);
        Assert.Equal("mean-temperature", spolar.CompletedTemperatureMode);
        Assert.Equal("random-input-sampling", spolar.UncertaintyPropagation);
        Assert.Equal(42, Value(spolar, "residue-estimate").Value);
        Assert.Equal(297.15, Value(spolar, "reference-temperature").Confidence95Lower);
        Assert.Null(spolar.UncertaintyMethod);

        var electrostatics = Assert.Single(package.Result.AdvancedAnalyses, item => item.Type == "electrostatics");
        Assert.True(electrostatics.UsesCurvature);
        Assert.True(electrostatics.IonicStrengthAvailable);
        Assert.True(electrostatics.CounterIonReleaseAvailable);
        Assert.Equal(15, electrostatics.IonicStrengthCompletedIterations);
        Assert.Equal(16, electrostatics.CounterIonReleaseCompletedIterations);
        Assert.Equal(0.3, Value(electrostatics, "curvature").Value);

        var protonation = Assert.Single(package.Result.AdvancedAnalyses, item => item.Type == "protonation");
        Assert.Equal("buffer-protonation-enthalpy", protonation.XAxis);
        Assert.Equal("observed-binding-enthalpy", protonation.YAxis);
        Assert.Equal("protonation-change", protonation.SlopeParameter);
        Assert.Equal("binding-enthalpy", protonation.InterceptParameter);
        Assert.Equal(-25000, Value(protonation, "binding-enthalpy").Value);
        Assert.Equal(0.8, Value(protonation, "protonation-change").Value);

        var dependence = Assert.Single(package.Result.TemperatureDependence, item => item.ParameterId == "enthalpy-1");
        Assert.Equal(4, dependence.InterceptStandardDeviation);
        Assert.Equal(-25008, dependence.InterceptConfidence95Lower);
        Assert.Equal(4.5, dependence.SlopeStandardDeviation);
        Assert.Equal(-99, dependence.SlopeConfidence95Upper);
    }

    [Fact]
    public async Task PartialElectrostaticsDoesNotInventMissingComponentOrSamplingCount()
    {
        var result = await LoadAdvancedResult();
        result.ElectrostaticsAnalysis.RestoreResult(
            new IonicStrengthDependenceFit(new FloatWithError(2e-6), new FloatWithError(1.2), new FloatWithError(0), false),
            null, 0, 0, DateTime.UtcNow, ErrorEstimationMethod.None);
        var report = new AnalysisReport { Name = "Advanced interpretation" };
        report.SetResultIds(new[] { result.UniqueID });
        var electrostatics = Assert.Single(AnalysisInterpretationPackageBuilder.Build(report, result).Result.AdvancedAnalyses,
            item => item.Type == "electrostatics");

        Assert.True(electrostatics.IonicStrengthAvailable);
        Assert.False(electrostatics.CounterIonReleaseAvailable);
        Assert.Equal(0, electrostatics.IonicStrengthCompletedIterations);
        Assert.Null(electrostatics.CounterIonReleaseCompletedIterations);
        Assert.Null(electrostatics.CounterIonReleaseAvailable == true ? Value(electrostatics, "counter-ion-release") : null);
        Assert.DoesNotContain(electrostatics.Values, item => item.Id == "counter-ion-release");
    }

    [Fact]
    public async Task CounterIonOnlyElectrostaticsPreservesIonicStrengthAbsence()
    {
        var result = await LoadAdvancedResult();
        result.ElectrostaticsAnalysis.RestoreResult(
            null,
            new LinearFitWithError(new FloatWithError(1.5, 0.1), new FloatWithError(-12, 0.2), 0),
            0, 16, DateTime.UtcNow, ErrorEstimationMethod.None);
        var report = new AnalysisReport { Name = "Advanced interpretation" };
        report.SetResultIds(new[] { result.UniqueID });
        var electrostatics = Assert.Single(AnalysisInterpretationPackageBuilder.Build(report, result).Result.AdvancedAnalyses,
            item => item.Type == "electrostatics");

        Assert.False(electrostatics.IonicStrengthAvailable);
        Assert.True(electrostatics.CounterIonReleaseAvailable);
        Assert.Null(electrostatics.IonicStrengthCompletedIterations);
        Assert.Equal(16, electrostatics.CounterIonReleaseCompletedIterations);
        Assert.DoesNotContain(electrostatics.Values, item => item.Id == "kd-zero-ionic-strength");
        Assert.Contains(electrostatics.Values, item => item.Id == "counter-ion-release");
    }

    [Fact]
    public async Task UnknownCompletedSpolarSettingsStayUnknownInsteadOfUsingEditedControls()
    {
        var result = await LoadAdvancedResult();
        result.SpolarRecordAnalysis.RestoreResult(
            FTSRMethod.SRFoldedMode.Glob, FTSRMethod.SRTempMode.MeanTemperature,
            new FTSRMethod.SROutput(new FloatWithError(-1), new FloatWithError(-2), new FloatWithError(3), new FloatWithError(25)),
            10, DateTime.UtcNow);
        result.SpolarRecordAnalysis.FoldedMode = FTSRMethod.SRFoldedMode.ID;
        result.SpolarRecordAnalysis.TempMode = FTSRMethod.SRTempMode.ReferenceTemperature;
        typeof(FTSRMethod).GetProperty("CompletedFoldedMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(result.SpolarRecordAnalysis, null);
        typeof(FTSRMethod).GetProperty("CompletedTempMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(result.SpolarRecordAnalysis, null);

        var package = AnalysisInterpretationPackageBuilder.Build(CreateReport(result), result);
        var spolar = Assert.Single(package.Result.AdvancedAnalyses, item => item.Type == "spolar-record");
        Assert.Null(spolar.CompletedFoldedMode);
        Assert.Null(spolar.CompletedTemperatureMode);
    }

    [Fact]
    public async Task AdvancedEvidenceSurvivesProjectRoundTripCopyDebugExportAndChangesFreshness()
    {
        var result = await LoadAdvancedResult();
        result.SpolarRecordAnalysis.RestoreResult(
            FTSRMethod.SRFoldedMode.Intermediate, FTSRMethod.SRTempMode.ReferenceTemperature,
            new FTSRMethod.SROutput(new FloatWithError(-1), new FloatWithError(-2), new FloatWithError(3), new FloatWithError(25)),
            10, DateTime.UtcNow);
        result.ElectrostaticsAnalysis.RestoreResult(
            new IonicStrengthDependenceFit(new FloatWithError(2e-6), new FloatWithError(1.2), new FloatWithError(0), false), null,
            4, 0, DateTime.UtcNow, ErrorEstimationMethod.None);
        var report = CreateReport(result);
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        var copied = AnalysisInterpretationThermograms.Copy(package);
        Assert.Equal("intermediate", Assert.Single(copied.Result.AdvancedAnalyses, item => item.Type == "spolar-record").CompletedFoldedMode);
        Assert.Equal("reference-temperature", Assert.Single(copied.Result.AdvancedAnalyses, item => item.Type == "spolar-record").CompletedTemperatureMode);

        var archiveBytes = AnalysisInterpretationDebugExport.CreateArchive(package);
        using (var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read))
        using (var reader = new StreamReader(archive.GetEntry("canonical-package.json")!.Open()))
        using (var json = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd()))
        {
            var spolar = json.RootElement.GetProperty("results")[0].GetProperty("advancedAnalyses").EnumerateArray()
                .Single(item => item.GetProperty("type").GetString() == "spolar-record");
            Assert.Equal("intermediate", spolar.GetProperty("completedFoldedMode").GetString());
        }

        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, result.Solution.Solutions.Select(item => item.Data).Distinct(), new[] { result });
        stream.Position = 0;
        var restored = (await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>().Single();
        restored.SpolarRecordAnalysis.FoldedMode = FTSRMethod.SRFoldedMode.ID;
        var restoredPackage = AnalysisInterpretationPackageBuilder.Build(CreateReport(restored), restored);
        Assert.Equal("intermediate", Assert.Single(restoredPackage.Result.AdvancedAnalyses, item => item.Type == "spolar-record").CompletedFoldedMode);

        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        {
            InputFingerprint = prompt.InputFingerprint,
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme,
            InterpretationMarkdown = "## Overall interpretation\nStored.",
        });
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, result).Status);
        result.SpolarRecordAnalysis.RestoreResult(
            FTSRMethod.SRFoldedMode.Intermediate, FTSRMethod.SRTempMode.ReferenceTemperature,
            new FTSRMethod.SROutput(new FloatWithError(-9), new FloatWithError(-2), new FloatWithError(3), new FloatWithError(25)),
            10, DateTime.UtcNow);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, AnalysisInterpretationService.EvaluateFreshness(report, result).Status);
    }

    static InterpretationAdvancedValue Value(InterpretationAdvancedAnalysisEvidence analysis, string id) =>
        analysis.Values.Single(item => item.Id == id);

    static AnalysisReport CreateReport(AnalysisResult result)
    {
        var report = new AnalysisReport { Name = "Advanced interpretation" };
        report.SetResultIds(new[] { result.UniqueID });
        return report;
    }

    static async Task<AnalysisResult> LoadAdvancedResult()
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-set.ftitc"));
        var sourceResult = (await FTITCReader.ReadStream(source)).OfType<AnalysisResult>().Single();
        var buffers = new[] { Buffer.Hepes, Buffer.Tris, Buffer.SodiumPhosphate };
        for (var index = 0; index < sourceResult.Solution.Solutions.Count; index++)
        {
            var data = sourceResult.Solution.Solutions[index].Data;
            data.MeasuredTemperature = 20 + index * 5;
            data.Attributes.RemoveAll(attribute => attribute.Key is AttributeKey.Salt or AttributeKey.Buffer);
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
            salt.IntValue = (int)Salt.NaCl;
            salt.ParameterValue = new FloatWithError(0.05 + index * 0.05);
            data.Attributes.Add(salt);
            var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            buffer.IntValue = (int)buffers[index % buffers.Length];
            buffer.DoubleValue = 7.4;
            data.Attributes.Add(buffer);
        }

        var members = sourceResult.Solution.Solutions;
        var globalModel = new GlobalModel(members.Select(solution => solution.Model).ToList())
        {
            Parameters = sourceResult.Model.Parameters,
            ModelCloneOptions = sourceResult.Model.ModelCloneOptions,
        };
        var globalSolution = new GlobalSolution(new GlobalSolver
        {
            Model = globalModel,
            ErrorEstimationMethod = sourceResult.Solution.ErrorEstimationMethod,
            UseErrorWeightedFitting = sourceResult.Solution.UseWeightedFitting,
        }, members, sourceResult.Solution.Convergence);
        globalModel.Solution = globalSolution;
        return new AnalysisResult(globalSolution);
    }
}
