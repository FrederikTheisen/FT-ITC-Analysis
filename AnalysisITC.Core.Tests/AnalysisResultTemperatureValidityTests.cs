using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class AnalysisResultTemperatureValidityTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();

    public AnalysisResultTemperatureValidityTests() => AppSettings.MinimumTemperatureSpanForFitting = 3;
    public void Dispose() => original.ApplyToSettings();

    [Theory]
    [InlineData(25, 35)]
    [InlineData(0, -5)]
    [InlineData(-5, 0)]
    public void TemperatureEditInvalidatesSingleResultAndRevertingRestoresValidity(double before, double after)
    {
        var result = Create(before);
        var data = result.Solution.Solutions.Single().Data;
        Assert.Equal(before, result.ValiditySnapshot.Experiments.Single().MeasuredTemperature);
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);

        // Direct assignment deliberately avoids processing invalidation, so this
        // checks the saved-input comparison independently of solution flags.
        data.MeasuredTemperature = after;
        Assert.Equal(AnalysisResultValidity.Invalid, result.ValidityReport.Status);
        Assert.Equal(AnalysisResultHealth.Invalid, result.Health);
        Assert.Contains(result.ValidityReport.Reasons, reason => reason.EndsWith(": temperature changed."));
        Assert.True(result.Solution.IsValid);

        data.MeasuredTemperature = before;
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
    }

    [Fact]
    public void IndependentCollectionWithoutTemperatureDependenceKeepsPartialInvalidity()
    {
        var result = Create(25, 25);
        Assert.False(result.Model.TemperatureDependenceExposed);
        result.Solution.Solutions[0].Data.MeasuredTemperature = 35;
        Assert.Equal(AnalysisResultValidity.PartialInvalid, result.ValidityReport.Status);
        Assert.Equal(AnalysisResultHealth.PartialInvalid, result.Health);
    }

    [Theory]
    [InlineData("temperature", "temperature changed")]
    [InlineData("concentration", "cell concentration changed")]
    [InlineData("inclusion", "injection inclusion changed")]
    [InlineData("integration", "integration windows changed")]
    [InlineData("heat", "processed heat values changed")]
    public void TemperatureDependentIndependentResultIsFullyInvalidForAnyChangedInput(string input, string reason)
    {
        var result = Create(25, 35);
        Assert.True(result.Model.ShouldFitIndividually);
        Assert.True(result.Model.TemperatureDependenceExposed);
        var data = result.Solution.Solutions[1].Data;
        switch (input)
        {
            case "temperature": data.MeasuredTemperature = 25; break;
            case "concentration": data.CellConcentration = new FloatWithError(99e-6); break;
            case "inclusion": data.Injections[0].Include = !data.Injections[0].Include; break;
            case "integration":
                data.Injections[0].SetIntegrationLengthByTime(10);
                data.Injections[0].SetPeakArea(new FloatWithError(0.123));
                break;
            case "heat": data.Injections[0].SetPeakArea(new FloatWithError(0.123)); break;
            default: throw new ArgumentOutOfRangeException(nameof(input));
        }
        Assert.Equal(AnalysisResultValidity.Invalid, result.ValidityReport.Status);
        Assert.Contains(result.ValidityReport.Reasons, value => value.Contains(reason));
    }

    [Theory]
    [InlineData(25, AnalysisResultValidity.Valid)]
    [InlineData(25.000000001, AnalysisResultValidity.Valid)]
    [InlineData(25.000001, AnalysisResultValidity.Invalid)]
    public void TemperatureComparisonToleratesOnlyNumericalRoundoff(double temperature, AnalysisResultValidity expected)
    {
        var result = Create(25);
        result.Solution.Solutions[0].Data.MeasuredTemperature = temperature;
        Assert.Equal(expected, result.ValidityReport.Status);
    }

    [Fact]
    public void AllChangedMembersWithoutTemperatureDependenceAreInvalid()
    {
        var result = Create(25, 25);
        foreach (var member in result.Solution.Solutions) member.Data.MeasuredTemperature = 35;
        Assert.Equal(AnalysisResultValidity.Invalid, result.ValidityReport.Status);
    }

    [Theory]
    [InlineData(true, AnalysisResultValidity.Invalid)]
    [InlineData(false, AnalysisResultValidity.PartialInvalid)]
    public async Task RoundTripRetainsCapturedTemperaturesAndDependenceStateDespiteChangedSpan(
        bool enabled, AnalysisResultValidity expected)
    {
        var result = Create(25, enabled ? 35 : 25);
        var originalSolution = result.Solution;
        var originalParameters = result.Solution.Solutions.Select(member => member.Parameters[ParameterType.Enthalpy1].Value).ToArray();
        var data = result.Solution.Solutions[1].Data;
        data.MeasuredTemperature = enabled ? 25 : 35;
        Assert.Equal(expected, result.ValidityReport.Status);

        using var package = await Save(result);
        FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(package);
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());

        Assert.Equal(enabled, restored.ValiditySnapshot.TemperatureDependenceEnabled);
        Assert.Equal(new double?[] { 25, enabled ? 35 : 25 }, restored.ValiditySnapshot.Experiments.Select(member => member.MeasuredTemperature));
        Assert.Equal(!enabled, restored.Model.TemperatureDependenceExposed);
        Assert.Equal(expected, restored.ValidityReport.Status);
        Assert.Contains(restored.ValidityReport.Reasons, reason => reason.EndsWith(": temperature changed."));
        Assert.Equal(originalParameters, restored.Solution.Solutions.Select(member => member.Parameters[ParameterType.Enthalpy1].Value));
        Assert.Equal(new[] { 298.15, enabled ? 308.15 : 298.15 },
            restored.Solution.Solutions.Select(member => member.Model.Parameters.ExperimentTemperature));
        Assert.Same(originalSolution, result.Solution);
        Assert.Equal(expected, result.ValidityReport.Status);
    }

    [Theory]
    [InlineData(false, false, AnalysisResultValidity.PartialInvalid)]
    [InlineData(true, false, AnalysisResultValidity.PartialInvalid)]
    [InlineData(false, true, AnalysisResultValidity.Invalid)]
    [InlineData(true, true, AnalysisResultValidity.Invalid)]
    public async Task MissingOrNullFieldsSkipTemperatureComparisonAndUseRestoredDependenceFlag(
        bool explicitNull, bool enabled, AnalysisResultValidity changedConcentrationStatus)
    {
        var result = Create(25, enabled ? 35 : 25);
        result.Solution.Solutions[1].Data.MeasuredTemperature = enabled ? 45 : 26;
        using var current = await Save(result);
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(current, (path, bytes) =>
        {
            if (path != "results/000000/result.json") return bytes;
            var json = JsonNode.Parse(bytes).AsObject();
            var validity = json["validity"].AsObject();
            RemoveOrNull(validity, "temperatureDependenceEnabled", explicitNull);
            foreach (var experiment in validity["experiments"].AsArray())
                RemoveOrNull(experiment.AsObject(), "measuredTemperature", explicitNull);
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        }, schemaMinor: 6);
        FtxtcJsonSchemaValidationTests.AssertPackageJsonValidates(historical);
        historical.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(historical)).OfType<AnalysisResult>());

        Assert.Null(restored.ValiditySnapshot.TemperatureDependenceEnabled);
        Assert.All(restored.ValiditySnapshot.Experiments, member => Assert.Null(member.MeasuredTemperature));
        Assert.Equal(enabled, restored.Model.TemperatureDependenceExposed);
        Assert.Equal(AnalysisResultValidity.Valid, restored.ValidityReport.Status);
        // The retained member-temperature map must not fill missing validity evidence.
        Assert.Equal(298.15, restored.Solution.Solutions[0].Model.Parameters.ExperimentTemperature);
        restored.Solution.Solutions[0].Data.CellConcentration = new FloatWithError(99e-6);
        Assert.Equal(changedConcentrationStatus, restored.ValidityReport.Status);
        Assert.DoesNotContain(restored.ValidityReport.Reasons, reason => reason.Contains("temperature changed"));

        using var resaved = await Save(restored);
        using var archive = new System.IO.Compression.ZipArchive(resaved, System.IO.Compression.ZipArchiveMode.Read);
        using var entry = archive.GetEntry("results/000000/result.json").Open();
        using var document = JsonDocument.Parse(entry);
        var resavedValidity = document.RootElement.GetProperty("validity");
        Assert.False(resavedValidity.TryGetProperty("temperatureDependenceEnabled", out _));
        Assert.All(resavedValidity.GetProperty("experiments").EnumerateArray(),
            experiment => Assert.False(experiment.TryGetProperty("measuredTemperature", out _)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSnapshotJsonRetainsOptionalFieldsAndReadsHistoricalSnapshots(bool explicitNull)
    {
        var result = Create(0, -5);
        var snapshot = AnalysisResultValiditySnapshot.FromJson(result.ValiditySnapshot.ToJson());
        Assert.True(snapshot.TemperatureDependenceEnabled);
        Assert.Equal(new double?[] { 0, -5 }, snapshot.Experiments.Select(member => member.MeasuredTemperature));
        var json = JsonNode.Parse(snapshot.ToJson()).AsObject();
        RemoveOrNull(json, "TemperatureDependenceEnabled", explicitNull);
        foreach (var experiment in json["Experiments"].AsArray())
            RemoveOrNull(experiment.AsObject(), "MeasuredTemperature", explicitNull);
        snapshot = AnalysisResultValiditySnapshot.FromJson(json.ToJsonString());
        result.Solution.Solutions[0].Data.MeasuredTemperature = 10;
        Assert.Null(snapshot.TemperatureDependenceEnabled);
        Assert.All(snapshot.Experiments, member => Assert.Null(member.MeasuredTemperature));
        Assert.Equal(AnalysisResultValidity.Valid, snapshot.Compare(result.Solution).Status);
    }

    [Theory]
    [InlineData(false, AnalysisResultHealth.PartialInvalid, "PARTIAL / STALE")]
    [InlineData(true, AnalysisResultHealth.Invalid, "INVALID / STALE")]
    public void HealthReasonsAndReportBadgesReflectTemperatureEditsWithoutBlockingReports(
        bool enabled, AnalysisResultHealth expectedHealth, string expectedBadge)
    {
        var result = Create(25, enabled ? 35 : 25);
        _ = result.PresentationData.Members;
        result.Solution.Solutions[0].Data.MeasuredTemperature = 30;
        Assert.Equal(expectedHealth, result.Health);
        Assert.Contains(result.HealthReasons, reason => reason.EndsWith(": temperature changed."));
        var document = AnalysisReportBuilder.Build(result);
        Assert.True(document.IsValid);
        Assert.Equal(expectedHealth, document.ResultHealth);
        Assert.Equal(expectedBadge, document.StatusBadgeText);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "result-1-result-health");
    }

    static void RemoveOrNull(JsonObject value, string key, bool explicitNull)
    {
        if (explicitNull) value[key] = null;
        else value.Remove(key);
    }

    static async Task<MemoryStream> Save(AnalysisResult result)
    {
        var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, result.Model.Models.Select(model => model.Data), new[] { result });
        stream.Position = 0;
        return stream;
    }

    internal static AnalysisResult Create(params double[] temperatures)
    {
        var models = temperatures.Select((temperature, index) =>
        {
            var model = InjectionProcessingMethodTests.FittedModel();
            model.Data.Name = "Experiment " + (index + 1);
            model.Data.MeasuredTemperature = temperature;
            model.Parameters.RestoreExperimentTemperature(temperature + 273.15);
            for (var i = 0; i < model.Data.Injections.Count; i++)
            {
                var source = model.Data.Injections[i];
                var injection = InjectionData.FromPEAQFile(model.Data, source.ID, source.Include,
                    60 + 180 * i, source.Volume, 180, 2, temperature);
                injection.ActualCellConcentration = source.ActualCellConcentration;
                injection.ActualTitrantConcentration = source.ActualTitrantConcentration;
                injection.Ratio = source.Ratio;
                injection.SetPeakArea(source.PeakArea);
                model.Data.Injections[i] = injection;
            }
            return model;
        }).ToList();
        var model = new GlobalModel(models);
        foreach (var member in models) model.Parameters.AddIndivdualParameter(member.Parameters);
        var solutions = models.Select(member => member.Solution).ToList();
        var convergence = SolverConvergence.FromMultiExperimentAnalysis(solutions.Select(member => member.Convergence).ToList());
        var global = new GlobalSolution(new GlobalSolver { Model = model }, solutions, convergence, reconstructBootstrap: false);
        model.Solution = global;
        return new AnalysisResult(global);
    }
}
