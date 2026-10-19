using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Interpretation;
using Xunit;
using Xunit.Abstractions;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Core.Tests;

public sealed class InterpretationEvidenceLevelTests
{
    readonly ITestOutputHelper output;
    public InterpretationEvidenceLevelTests(ITestOutputHelper output) => this.output = output;

    // Intentionally handwritten: these expectations do not consume the production policy or table definitions.
    static readonly Dictionary<string, string[]> Columns = new()
    {
        ["acquisition-v1"] = new[] { "injectionId", "timeSeconds", "durationSeconds", "volumeLitres", "injectionDelaySeconds", "filterPeriodSeconds", "activeCellConcentrationMolar", "activeTitrantConcentrationMolar" },
        ["heat-observations-v1"] = new[] { "injectionId", "included", "isIntegrated", "analysisAxisKind", "analysisAxisValue", "integratedHeatBeforeSubtractionJoules", "integratedHeatBeforeSubtractionErrorJoules", "integratedHeatJoules", "integratedHeatErrorJoules" },
        ["fit-v1"] = new[] { "injectionId", "observedHeatJoulesPerMole", "observedHeatErrorJoulesPerMole", "fittedHeatJoulesPerMole", "residualJoulesPerMole", "confidence95LowerJoulesPerMole", "confidence95UpperJoulesPerMole" },
        ["integration-v2"] = new[] { "injectionId", "integrationStartTimeSeconds", "integrationEndTimeSeconds", "integrationStartDelaySeconds", "integrationEndOffsetSeconds", "integrationLengthSeconds", "nonIntegratedTimeFraction", "nonIntegratedIntervalBeforeNextInjectionSeconds" },
        ["baseline-v1"] = new[] { "injectionId", "baselineAtIntegrationStartMicrowatts", "baselineAtIntegrationEndMicrowatts", "baselineChangeAcrossIntegrationMicrowatts", "integratedBaselineCorrectionJoules" },
        ["baseline-landmarks-v1"] = new[] { "timeSeconds", "powerMicrowatts" },
        ["baseline-spline-controls-v1"] = new[] { "timeSeconds", "powerMicrowatts", "slopeMicrowattsPerSecond", "userDefined" },
        ["baseline-segments-v1"] = new[] { "scope", "injectionId", "startTimeSeconds", "endTimeSeconds", "centerTimeSeconds", "coefficientsSi" },
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryLevelHasExplicitContentSchemasBoundariesAndNoBindingRestriction(int level)
    {
        var source = Source(level);
        var original = source.ToJsonString();
        var written = AnalysisInterpretationModelInputWriter.Write(original);
        using var json = JsonDocument.Parse(written);
        var root = json.RootElement;
        var result = root.GetProperty("results")[0];
        var expectedTables = level == 0 ? Array.Empty<string>() : level == 1
            ? new[] { "acquisition", "heatObservations", "fit" }
            : new[] { "acquisition", "integration", "heatObservations", "fit", "baseline" };
        var expectedSchemas = level == 0 ? Array.Empty<string>() : level == 1
            ? new[] { "acquisition-v1", "heat-observations-v1", "fit-v1" }
            : new[] { "acquisition-v1", "integration-v2", "heat-observations-v1", "fit-v1", "baseline-v1", "baseline-landmarks-v1", "baseline-spline-controls-v1", "baseline-segments-v1" };
        Assert.Equal(expectedSchemas.OrderBy(x => x), root.GetProperty("tableSchemas").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        foreach (var schema in expectedSchemas)
            Assert.Equal(Columns[schema], root.GetProperty("tableSchemas").GetProperty(schema).GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        foreach (var owner in new[] { result.GetProperty("experiments")[0], root.GetProperty("supportingExperiments")[0] })
        {
            var keys = new List<string> { "reportReference", "experimentId", "name", "residualDiagnostics", "baselineCompleted", "integrationCompleted", "initialDelaySeconds" };
            if (level > 0) keys.Add("injections");
            if (level >= 2) keys.AddRange(new[] { "baseline", "baselineProcessor", "processorLocked", "discardsIntegratedPointsForBaseline", "integrationLengthMode", "integrationLengthFactor" });
            if (level == 3) keys.Add("thermogram");
            Assert.Equal(keys.OrderBy(x => x), owner.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
            if (level > 0)
            {
                Assert.Equal(expectedTables.OrderBy(x => x), owner.GetProperty("injections").EnumerateObject().Select(p => p.Name).OrderBy(x => x));
                Assert.Equal(-12, owner.GetProperty("injections").GetProperty("fit").GetProperty("rows")[0][1].GetDouble());
            }
            if (level >= 2)
            {
                Assert.Equal(4, owner.GetProperty("baseline").GetProperty("netDriftMicrowatts").GetDouble());
                Assert.Equal(2, owner.GetProperty("baseline").GetProperty("segmented").GetProperty("segments").GetProperty("rows")[0][5][1].GetDouble());
            }
        }
        Assert.Equal(level >= 2, result.TryGetProperty("historicalFitInputs", out _));
        if (level >= 2) Assert.Equal(17, result.GetProperty("historicalFitInputs")[0].GetProperty("includedInjections")[0].GetProperty("volume").GetDouble());
        foreach (var group in new[] { "members", "nullMembers" })
            Assert.Equal(level > 0, result.GetProperty("bindingAssessment").GetProperty(group)[0].TryGetProperty("points", out _));
        var boundary = root.GetProperty("dataBoundary");
        Assert.Equal(level == 3, boundary.GetProperty("containsRawThermogramSamples").GetBoolean());
        Assert.Equal(level == 3, boundary.GetProperty("containsBaselineArrays").GetBoolean());
        Assert.Equal(level >= 2, boundary.GetProperty("containsBaselineSummary").GetBoolean());
        Assert.Equal(level >= 2, boundary.GetProperty("containsBaselineControlRepresentation").GetBoolean());
        Assert.False(boundary.GetProperty("containsBootstrapReplicateArrays").GetBoolean());
        Assert.False(boundary.GetProperty("containsLocalPaths").GetBoolean());
        var restriction = boundary.GetProperty("modelObservationRestriction").GetString();
        Assert.Contains("No binding detected assessment", restriction);
        Assert.Equal(1, restriction.Split("No binding detected assessment").Length - 1);
        Assert.Equal(level < 3, restriction.Contains("No raw thermogram"));
        Assert.All(root.GetProperty("omissions").EnumerateArray(), reason =>
        {
            if (level < 3) Assert.DoesNotContain("Thermogram omitted because", reason.GetString());
        });
        Assert.Equal(level == 0 ? 1 : 0, root.GetProperty("omissions").EnumerateArray()
            .Count(reason => reason.GetString() == "Saved Null comparison points omitted by user choice."));
        Assert.Equal(written, AnalysisInterpretationModelInputWriter.Write(written));
        Assert.Equal(original, source.ToJsonString());
        AssertSchemasResolve(root, root.GetProperty("tableSchemas"));
    }

    [Theory]
    [InlineData("members", 0)]
    [InlineData("members", 1)]
    [InlineData("members", 2)]
    [InlineData("nullMembers", 0)]
    [InlineData("nullMembers", 1)]
    [InlineData("nullMembers", 2)]
    public void ResultsDisclosesOnlySavedNullPointsThatWereSupplied(string group, int pointState)
    {
        var source = Source(0);
        var assessment = source["results"][0]["bindingAssessment"];
        foreach (var name in new[] { "members", "nullMembers" })
            assessment[name][0].AsObject().Remove("points");
        if (pointState > 0) assessment[group][0]["points"] = new JsonArray();
        if (pointState == 2) assessment[group][0]["points"].AsArray()
            .Add(new JsonObject { ["injectionNumber"] = 1, ["included"] = true });

        var written = AnalysisInterpretationModelInputWriter.Write(source.ToJsonString());
        using var json = JsonDocument.Parse(written);
        Assert.Equal(pointState == 2 ? 1 : 0, json.RootElement.GetProperty("omissions").EnumerateArray()
            .Count(reason => reason.GetString() == "Saved Null comparison points omitted by user choice."));
        Assert.Equal(written, AnalysisInterpretationModelInputWriter.Write(written));
    }

    [Fact]
    public void OldProcessingWithoutInjectionSelectionBecomesResultsAndGuidanceFlagsMatch()
    {
        var source = Source(3);
        source["requestedInterpretation"]["injectionRows"] = "none";
        using var json = JsonDocument.Parse(AnalysisInterpretationModelInputWriter.Write(source.ToJsonString()));
        var root = json.RootElement;
        var settings = root.GetProperty("requestedInterpretation");
        Assert.Equal("none", settings.GetProperty("injectionRows").GetString());
        Assert.False(settings.GetProperty("includeProcessingInformation").GetBoolean());
        Assert.False(settings.GetProperty("includeThermograms").GetBoolean());
        Assert.Empty(root.GetProperty("tableSchemas").EnumerateObject());
        Assert.False(root.GetProperty("results")[0].GetProperty("experiments")[0].TryGetProperty("baseline", out _));
        AssertSchemasResolve(root, root.GetProperty("tableSchemas"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncludedOnlyAndLaterDowngradeWorkInInlineAndSharedLayouts(bool shared)
    {
        var source = Source(3);
        source["supportingExperiments"] = new JsonArray();
        if (shared)
        {
            var first = source["results"][0]["experiments"][0];
            first["comments"] = new string('x', 3000);
            var second = first.DeepClone(); second["reportReference"] = "2A";
            source["results"].AsArray().Add(new JsonObject { ["reportReference"] = "2", ["experiments"] = new JsonArray(second) });
        }
        var compact = JsonNode.Parse(AnalysisInterpretationModelInputWriter.Write(source.ToJsonString()));
        Assert.Equal(shared ? "compact-tables-shared-evidence-v1" : "compact-tables-v1", compact["modelInputEncoding"].GetValue<string>());
        compact["requestedInterpretation"]["injectionRows"] = "includedOnly";
        var filtered = AnalysisInterpretationModelInputWriter.Write(compact.ToJsonString());
        using (var json = JsonDocument.Parse(filtered))
        {
            var root = json.RootElement;
            var experiment = root.GetProperty("results")[0].GetProperty("experiments")[0];
            Assert.Single(experiment.GetProperty("injections").GetProperty("fit").GetProperty("rows").EnumerateArray());
            foreach (var group in new[] { "members", "nullMembers" })
                Assert.Single(root.GetProperty("results")[0].GetProperty("bindingAssessment").GetProperty(group)[0].GetProperty("points").EnumerateArray());
            AssertSchemasResolve(root, root.GetProperty("tableSchemas"));
        }
        compact = JsonNode.Parse(filtered);
        compact["requestedInterpretation"]["injectionRows"] = "none";
        var downgraded = AnalysisInterpretationModelInputWriter.Write(compact.ToJsonString());
        using var reduced = JsonDocument.Parse(downgraded);
        var owners = reduced.RootElement.GetProperty("results").EnumerateArray().SelectMany(r => r.GetProperty("experiments").EnumerateArray());
        if (shared) owners = owners.Concat(reduced.RootElement.GetProperty("experimentEvidence").EnumerateArray());
        foreach (var owner in owners)
        {
            Assert.False(owner.TryGetProperty("injections", out _));
            Assert.False(owner.TryGetProperty("baseline", out _));
            Assert.False(owner.TryGetProperty("thermogram", out _));
        }
        Assert.Equal(downgraded, AnalysisInterpretationModelInputWriter.Write(downgraded));
        AssertSchemasResolve(reduced.RootElement, reduced.RootElement.GetProperty("tableSchemas"));
    }

    [Fact]
    public void RequestedLevelSurvivesCapabilitiesUntilUserChangesItAndIncludedOnlyIsPreserved()
    {
        var saved = new AnalysisInterpretationOptions { InjectionRows = AnalysisInterpretationInjectionRows.IncludedOnly, IncludeProcessingInformation = true, IncludeThermograms = true };
        var state = new AnalysisInterpretationEvidenceSelection(saved);
        Assert.Equal(AnalysisInterpretationEvidenceLevel.ProcessingDetails, state.RequestedLevel);
        foreach (var tier in new[] { "none", "standard", "advanced", "none", "advanced" })
        {
            state.ApplyCapabilities(tier == "none" ? null : new InterpretationOperatorOptionsResponse { AccessTier = tier });
            Assert.Equal(tier == "none" ? AnalysisInterpretationEvidenceLevel.Results : tier == "standard" ? AnalysisInterpretationEvidenceLevel.InjectionData : AnalysisInterpretationEvidenceLevel.ProcessingDetails, state.EffectiveLevel);
            Assert.Equal(AnalysisInterpretationEvidenceLevel.ProcessingDetails, state.RequestedLevel);
            Assert.Equal(tier != "none", state.IsVisible);
        }
        state.Select(AnalysisInterpretationEvidenceLevel.Thermograms);
        Assert.True(state.RequestOptions(saved).IncludeThermograms);
        state.ApplyCapabilities(null); state.ApplyCapabilities(new InterpretationOperatorOptionsResponse { AccessTier = "advanced" });
        Assert.Equal(AnalysisInterpretationEvidenceLevel.Thermograms, state.EffectiveLevel);
        state.Select(AnalysisInterpretationEvidenceLevel.Results);
        state.ApplyCapabilities(new InterpretationOperatorOptionsResponse { AccessTier = "advanced" });
        Assert.Equal(AnalysisInterpretationEvidenceLevel.Results, state.EffectiveLevel);
        state.Select(AnalysisInterpretationEvidenceLevel.InjectionData);
        Assert.Equal(AnalysisInterpretationInjectionRows.IncludedOnly, state.RequestOptions(saved).InjectionRows);
        Assert.True(saved.IncludeThermograms);
        Assert.DoesNotContain("evidenceLevel", JsonSerializer.Serialize(saved), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FrozenPreLayeredV2RecordBecomesStaleWhileFullCaptureRemainsLocallyAvailable()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Interpretation", "pre-layered-v2.ftxtc"));
        var loaded = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        var result = Assert.Single(loaded.Containers.OfType<AnalysisResult>());
        var report = Assert.Single(loaded.Reports);
        Assert.Equal("sha256:utf8:canonical-package-json-v2", report.ApprovedInterpretation.EvidenceFingerprintScheme);
        var oldOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        oldOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var oldCapture = JsonSerializer.Deserialize<AnalysisInterpretationPackage>(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Interpretation", "pre-layered-v2-canonical.json")), oldOptions);
        Assert.Equal(report.ApprovedInterpretation.InputFingerprint, AnalysisInterpretationPromptBuilder.Build(oldCapture).EvidenceFingerprint);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, AnalysisInterpretationService.EvaluateFreshness(report, result).Status);
        var originalFingerprint = report.ApprovedInterpretation.InputFingerprint;
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) AnalysisInterpretationService.EvaluateFreshness(report, result);
        output.WriteLine("Five full-capture freshness evaluations: {0:0.0} ms", timer.Elapsed.TotalMilliseconds);
        var settings = report.InterpretationSettings; settings.InjectionRows = AnalysisInterpretationInjectionRows.None;
        report.UpdateInterpretationSettings(settings);
        var full = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.NotNull(full.Result.Experiments[0].Thermogram);
        Assert.NotEmpty(full.Result.Experiments[0].Injections);
        Assert.DoesNotContain("Thermograms and sampled baselines omitted by user choice.", full.Omissions);
        using var sent = JsonDocument.Parse(AnalysisInterpretationModelInputWriter.Write(full));
        Assert.False(sent.RootElement.GetProperty("results")[0].GetProperty("experiments")[0].TryGetProperty("injections", out _));
        Assert.False(sent.RootElement.GetProperty("results")[0].GetProperty("experiments")[0].TryGetProperty("thermogram", out _));
        Assert.Equal(originalFingerprint, report.ApprovedInterpretation.InputFingerprint);
        Assert.Contains("Frozen development interpretation", report.ApprovedInterpretation.InterpretationMarkdown);
    }

    [Fact]
    public async Task CanonicalNoBindingRestrictionSurvivesMissingTracesAndEveryRequestLevel()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Interpretation", "pre-layered-v2.ftxtc"));
        var loaded = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        var result = Assert.Single(loaded.Containers.OfType<AnalysisResult>());
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        result.Solution.Solutions[0].Data.DataPoints.Clear();
        var report = Assert.Single(loaded.Reports);
        foreach (AnalysisInterpretationEvidenceLevel level in Enum.GetValues(typeof(AnalysisInterpretationEvidenceLevel)))
        {
            report.UpdateInterpretationSettings(AnalysisInterpretationEvidencePolicy.WithLevel(report.InterpretationSettings, level));
            var full = AnalysisInterpretationPackageBuilder.Build(report, result);
            Assert.False(full.DataBoundary.ContainsRawThermogramSamples);
            Assert.Contains("No binding detected assessment", full.DataBoundary.ModelObservationRestriction);
            AnalysisInterpretationThermograms.OmitTraces(full, "No traces");
            Assert.Contains("No binding detected assessment", full.DataBoundary.ModelObservationRestriction);
            using var sent = JsonDocument.Parse(AnalysisInterpretationModelInputWriter.Write(full));
            Assert.Contains("No binding detected assessment", sent.RootElement.GetProperty("dataBoundary").GetProperty("modelObservationRestriction").GetString());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FreshSharedLayoutRespectsEachLevel(int level)
    {
        var source = Source(level);
        source["supportingExperiments"] = new JsonArray();
        var first = source["results"][0]["experiments"][0];
        first["comments"] = new string('x', 3000);
        var second = first.DeepClone(); second["reportReference"] = "2A";
        source["results"].AsArray().Add(new JsonObject { ["reportReference"] = "2", ["experiments"] = new JsonArray(second) });
        var written = AnalysisInterpretationModelInputWriter.Write(source.ToJsonString());
        using var document = JsonDocument.Parse(written);
        var root = document.RootElement;
        Assert.Equal("compact-tables-shared-evidence-v1", root.GetProperty("modelInputEncoding").GetString());
        var shared = Assert.Single(root.GetProperty("experimentEvidence").EnumerateArray());
        Assert.Equal(level > 0, shared.TryGetProperty("injections", out _));
        Assert.Equal(level >= 2, shared.TryGetProperty("baseline", out _));
        Assert.Equal(level == 3, shared.TryGetProperty("thermogram", out _));
        Assert.Equal(written, AnalysisInterpretationModelInputWriter.Write(written));
        AssertSchemasResolve(root, root.GetProperty("tableSchemas"));
    }

    [Fact]
    public async Task MeasureRepresentativeProjectLoadFreshnessAndReportBuild()
    {
        var timer = Stopwatch.StartNew();
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "jors.ftxtc"));
        var loaded = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        var loadMs = timer.Elapsed.TotalMilliseconds;
        var results = loaded.Containers.OfType<AnalysisResult>().ToDictionary(result => result.UniqueID);
        Assert.NotEmpty(results);
        var report = new AnalysisReport(); report.SetResultIds(results.Keys);
        timer.Restart();
        var full = AnalysisInterpretationPackageBuilder.Build(report, id => results[id], _ => null);
        var prompt = AnalysisInterpretationPromptBuilder.Build(full);
        var captureMs = timer.Elapsed.TotalMilliseconds;
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        { InterpretationMarkdown = "## Overall interpretation\nTiming fixture.",
          InputFingerprint = prompt.EvidenceFingerprint, EvidenceFingerprintScheme = "sha256:utf8:canonical-package-json-v2" });
        timer.Restart();
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, id => results[id], _ => null).Status);
        var freshnessMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        AnalysisReportBuilder.Build(report, id => results[id], new AnalysisReportOptions());
        output.WriteLine("JORS fixture ({0} source samples): load {1:0.0} ms, capture/prompt {2:0.0} ms, freshness {3:0.0} ms, report {4:0.0} ms",
            loaded.Containers.OfType<ExperimentData>().Sum(data => data.DataPoints.Count), loadMs, captureMs, freshnessMs, timer.Elapsed.TotalMilliseconds);
    }

    static void AssertSchemasResolve(JsonElement node, JsonElement schemas)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("schema", out var schema) && node.TryGetProperty("rows", out var rows))
            {
                var count = schemas.GetProperty(schema.GetString()).GetProperty("columns").GetArrayLength();
                Assert.All(rows.EnumerateArray(), row => Assert.Equal(count, row.GetArrayLength()));
            }
            foreach (var property in node.EnumerateObject()) AssertSchemasResolve(property.Value, schemas);
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) AssertSchemasResolve(item, schemas);
    }

    static JsonObject Source(int level)
    {
        var source = JsonNode.Parse("""
        {"requestedInterpretation":{},"omissions":["Experiment 1A: Thermogram omitted because it contains no finite time and raw-power samples."],
         "dataBoundary":{},"results":[{"reportReference":"1",
          "historicalFitInputs":[{"includedInjections":[{"volume":17,"peakArea":19,"integrationEndOffset":23}],"processing":{"baselineType":"Spline"}}],
          "bindingAssessment":{"effectiveOutcome":"NoBindingDetected",
           "members":[{"effectiveOutcome":"NoBindingDetected","points":[{"injectionNumber":1,"included":true},{"injectionNumber":2,"included":false}]}],
           "nullMembers":[{"points":[{"injectionNumber":1,"included":true},{"injectionNumber":2,"included":false}]}]},
          "experiments":[{"reportReference":"1A","experimentId":"source-1","name":"Run",
           "residualDiagnostics":{"meanResidualJoulesPerMole":2},"baselineCompleted":true,"integrationCompleted":true,"initialDelaySeconds":10,
           "baselineProcessor":"Spline","processorLocked":true,"discardsIntegratedPointsForBaseline":true,"integrationLengthMode":"Time","integrationLengthFactor":0.9,
           "baseline":{"isAvailable":true,"netDriftMicrowatts":4,"landmarks":[{"timeSeconds":0,"powerMicrowatts":2}],
            "spline":{"controlPoints":[{"timeSeconds":0,"powerMicrowatts":2,"slopeMicrowattsPerSecond":3,"userDefined":true}]},
            "segmented":{"segments":[{"scope":"Injection","injectionId":1,"startTimeSeconds":0,"endTimeSeconds":10,"centerTimeSeconds":5,"coefficientsSi":[1,2]}]}},
           "injections":[{"injectionId":1,"included":true,"observedHeatJoulesPerMole":-12},{"injectionId":2,"included":false,"observedHeatJoulesPerMole":-14}],
           "thermogram":{"powerMinMax":[[1,2]],"baselineMinMax":[[3,4]]}}]}],"supportingExperiments":[]}
        """).AsObject();
        source["requestedInterpretation"] = new JsonObject { ["injectionRows"] = level > 0 ? "all" : "none", ["includeProcessingInformation"] = level >= 2, ["includeThermograms"] = level == 3 };
        var supporting = source["results"][0]["experiments"][0].DeepClone();
        supporting["reportReference"] = "S1"; supporting["experimentId"] = "supporting";
        source["supportingExperiments"].AsArray().Add(supporting);
        return source;
    }
}
