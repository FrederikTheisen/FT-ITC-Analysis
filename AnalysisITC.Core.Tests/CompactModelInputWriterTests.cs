using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class CompactModelInputWriterTests
{
    [Fact]
    public void ThermogramsAreOptInByDefault()
    {
        Assert.False(AnalysisInterpretationOptions.Default().IncludeThermograms);
        Assert.False(InterpretationAccessDisplay.CanIncludeThermograms(new InterpretationOperatorOptionsResponse { AccessTier = "public" }));
        Assert.False(InterpretationAccessDisplay.CanIncludeThermograms(new InterpretationOperatorOptionsResponse { AccessTier = "standard" }));
        Assert.True(InterpretationAccessDisplay.CanIncludeThermograms(new InterpretationOperatorOptionsResponse { AccessTier = "advanced" }));
        Assert.True(InterpretationAccessDisplay.CanIncludeThermograms(new InterpretationOperatorOptionsResponse { AccessTier = "administrator" }));
    }

    static JsonDocument Compact(string source)
    {
        using var original = JsonDocument.Parse(source);
        return JsonDocument.Parse(AnalysisInterpretationModelInputWriter.Write(original.RootElement));
    }

    [Fact]
    public void UnknownAndHistoricalEvidenceIsPreservedWithoutNameBasedStripping()
    {
        const string source = """
        {"packageSchemaVersion":"2.0","evidenceCatalog":[],
         "studyContext":{"evidenceId":"user-context-id","timeSeconds":0.1234567890123456789},
         "futureEvidence":{"evidenceId":"future-id","bestFitValue":1.1234567890123456789},
         "results":[{"evidenceId":"r1","reportReference":"1",
           "historicalFitInputs":[{"evidenceId":"historical-id","value":0.1234567890123456789}],
           "experiments":[]}],"supportingExperiments":[]}
        """;
        using var original = JsonDocument.Parse(source);
        using var compact = Compact(source);
        Assert.False(compact.RootElement.TryGetProperty("evidenceCatalog", out _));
        Assert.False(compact.RootElement.GetProperty("results")[0].TryGetProperty("evidenceId", out _));
        foreach (var name in new[] { "studyContext", "futureEvidence" })
        {
            Assert.Equal(original.RootElement.GetProperty(name).GetProperty("evidenceId").GetString(),
                compact.RootElement.GetProperty(name).GetProperty("evidenceId").GetString());
            var number = name == "studyContext" ? "timeSeconds" : "bestFitValue";
            Assert.Equal(original.RootElement.GetProperty(name).GetProperty(number).GetRawText(),
                compact.RootElement.GetProperty(name).GetProperty(number).GetRawText());
        }
        Assert.Equal("historical-id", compact.RootElement.GetProperty("results")[0]
            .GetProperty("historicalFitInputs")[0].GetProperty("evidenceId").GetString());
    }

    [Fact]
    public void TablesPreserveEveryInjectionFieldAndExplicitScope()
    {
        var injection = new InterpretationInjectionEvidence
        {
            EvidenceId = "r1/e1/i4", InjectionId = 4, Included = false, IsIntegrated = true,
            TimeSeconds = 450, DurationSeconds = 4, VolumeLitres = 2e-6,
            InjectionDelaySeconds = 150, FilterPeriodSeconds = 5,
            ActiveCellConcentrationMolar = 0.0002, ActiveTitrantConcentrationMolar = 0.000048,
            IntegrationStartTimeSeconds = 452, IntegrationEndTimeSeconds = 590,
            IntegrationStartDelaySeconds = 2, IntegrationEndOffsetSeconds = 140,
            IntegrationLengthSeconds = 138, IntegrationLengthFractionOfInjectionDelay = 0.92,
            NonIntegratedIntervalBeforeNextInjectionSeconds = 10,
            AnalysisAxisKind = "MolarRatio", AnalysisAxisValue = 0.24,
            IntegratedHeatBeforeSubtractionJoules = -0.00014,
            IntegratedHeatBeforeSubtractionErrorJoules = 3e-7,
            IntegratedHeatJoules = -0.00013, IntegratedHeatErrorJoules = 4e-7,
            ObservedHeatJoulesPerMole = -32500, ObservedHeatErrorJoulesPerMole = 100,
            FittedHeatJoulesPerMole = -32600, ResidualJoulesPerMole = 100,
            Confidence95LowerJoulesPerMole = -32700, Confidence95UpperJoulesPerMole = null,
            BaselineAtIntegrationStartMicrowatts = 41.25,
            BaselineAtIntegrationEndMicrowatts = 41.26,
            BaselineChangeAcrossIntegrationMicrowatts = 0.01,
            IntegratedBaselineCorrectionJoules = 0.0057,
        };
        var package = new AnalysisInterpretationPackage
        {
            Results = new List<InterpretationResultEvidence>
            {
                new() { EvidenceId = "r1", ReportReference = "1", Experiments = new()
                { new() { EvidenceId = "r1/e1", ReportReference = "1A", Injections = new() { injection } } } },
            },
            SupportingExperiments = new()
            { new() { EvidenceId = "support-1", ReportReference = "S1", Injections = new() { injection } } },
        };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        using var expected = JsonDocument.Parse(JsonSerializer.Serialize(injection, options));
        using var compact = Compact(JsonSerializer.Serialize(package, options));
        var schemas = compact.RootElement.GetProperty("tableSchemas");
        var experiments = new[] { compact.RootElement.GetProperty("results")[0].GetProperty("experiments")[0],
            compact.RootElement.GetProperty("supportingExperiments")[0] };
        foreach (var experiment in experiments)
        {
            var reconstructed = new Dictionary<string, JsonElement>();
            var tables = experiment.GetProperty("injections");
            Assert.Equal(5, tables.EnumerateObject().Count());
            foreach (var table in tables.EnumerateObject())
            {
                Assert.Equal(experiment.GetProperty("reportReference").GetString(), table.Value.GetProperty("reportReference").GetString());
                var schema = schemas.GetProperty(table.Value.GetProperty("schema").GetString()!);
                var columns = (schema.ValueKind == JsonValueKind.Array ? schema : schema.GetProperty("columns"))
                    .EnumerateArray().Select(c => c.GetString()!).ToArray();
                var row = Assert.Single(table.Value.GetProperty("rows").EnumerateArray());
                Assert.Equal(columns.Length, row.GetArrayLength());
                Assert.Equal(4, row[Array.IndexOf(columns, "injectionId")].GetInt32());
                for (var i = 0; i < columns.Length; i++)
                    if (columns[i] != "injectionId") Assert.True(reconstructed.TryAdd(columns[i], row[i]));
            }
            foreach (var field in expected.RootElement.EnumerateObject().Where(p => p.Name is not "evidenceId" and not "injectionId"))
            {
                Assert.True(reconstructed.Remove(field.Name, out var actual), field.Name);
                if (field.Value.ValueKind == JsonValueKind.Number) Assert.Equal(field.Value.GetDouble(), actual.GetDouble());
                else Assert.Equal(field.Value.GetRawText(), actual.GetRawText());
            }
            Assert.Empty(reconstructed);
        }
    }

    [Fact]
    public void ProtectsParameterIntervalGroupsAndCorrelationEndpoints()
    {
        const string source = """
        {"packageSchemaVersion":"2.0","results":[{"evidenceId":"r1","reportReference":"1",
         "bootstrapCorrelations":[{"scopeEvidenceId":"r1/e1","pearsonMatrix":[[1,0.99999999],[-0.99999999,1]]}],
         "experiments":[{"evidenceId":"r1/e1","reportReference":"1A","injections":[],
           "parameters":[{"bestFitValue":1.00000002,"confidence95Lower":1.00000001,
             "confidence95Upper":1.00000003,"fittedLowerBound":0.00000000123456789,"fittedUpperBound":9.87654321}]}]}],"supportingExperiments":[]}
        """;
        using var compact = Compact(source);
        var result = compact.RootElement.GetProperty("results")[0];
        var parameter = result.GetProperty("experiments")[0].GetProperty("parameters")[0];
        Assert.Equal(1.00000002, parameter.GetProperty("bestFitValue").GetDouble());
        Assert.Equal(1.00000001, parameter.GetProperty("confidence95Lower").GetDouble());
        Assert.Equal(1.00000003, parameter.GetProperty("confidence95Upper").GetDouble());
        Assert.Equal(9.87654321, parameter.GetProperty("fittedUpperBound").GetDouble());
        var correlation = result.GetProperty("bootstrapCorrelations")[0];
        Assert.Equal("1A", correlation.GetProperty("scopeReportReference").GetString());
        Assert.Equal(0.99999999, correlation.GetProperty("pearsonMatrix")[0][1].GetDouble());
        Assert.Equal(-0.99999999, correlation.GetProperty("pearsonMatrix")[1][0].GetDouble());
    }

    [Fact]
    public void AppliesIndependentPrecisionExpectationsWithoutRoundingProtectedValues()
    {
        const string source = """
        {"packageSchemaVersion":"2.0","results":[{"reportReference":"1",
         "solver":{"iterations":123456789},
         "informationCriteria":{"aicc":-1343.2763850798904,"minusTwoLogLikelihood":-1371.7154094701343},
         "experiments":[{"reportReference":"1A","injections":[],
          "parameters":[{"bestFitValue":2.123456789,"standardDeviation":0.000123456789,
            "confidence95Lower":1.111111119,"confidence95Upper":3.222222229}],
          "baseline":{"startPowerMicrowatts":41.1234567899,"endPowerMicrowatts":41.1234597899},
          "thermogram":{"anchorTimeSeconds":0.12345678912345,"binWidthSeconds":15.000000001,
            "powerOffsetWatts":0.0000411234567899123,
            "powerMinMax":[[0.0001234567899,0.0001234569999],[null,null]]}}]}],"supportingExperiments":[]}
        """;
        using var original = JsonDocument.Parse(source);
        using var compact = Compact(source);
        var result = compact.RootElement.GetProperty("results")[0];
        Assert.Equal(123456789, result.GetProperty("solver").GetProperty("iterations").GetInt32());
        Assert.Equal(-1343.2763850798904, result.GetProperty("informationCriteria").GetProperty("aicc").GetDouble());
        var experiment = result.GetProperty("experiments")[0];
        var parameter = experiment.GetProperty("parameters")[0];
        Assert.Equal(2.12346, parameter.GetProperty("bestFitValue").GetDouble());
        Assert.Equal(0.000123457, parameter.GetProperty("standardDeviation").GetDouble());
        Assert.Equal(1.11111, parameter.GetProperty("confidence95Lower").GetDouble());
        Assert.Equal(3.22222, parameter.GetProperty("confidence95Upper").GetDouble());
        Assert.Equal(41.1234568, experiment.GetProperty("baseline").GetProperty("startPowerMicrowatts").GetDouble());
        Assert.Equal(41.1234598, experiment.GetProperty("baseline").GetProperty("endPowerMicrowatts").GetDouble());
        var trace = experiment.GetProperty("thermogram");
        var originalTrace = original.RootElement.GetProperty("results")[0].GetProperty("experiments")[0].GetProperty("thermogram");
        foreach (var key in new[] { "anchorTimeSeconds", "binWidthSeconds", "powerOffsetWatts" })
            Assert.Equal(originalTrace.GetProperty(key).GetRawText(), trace.GetProperty(key).GetRawText());
        Assert.Equal(0.00012345679, trace.GetProperty("powerMinMax")[0][0].GetDouble());
        Assert.Equal(0.000123457, trace.GetProperty("powerMinMax")[0][1].GetDouble());
        Assert.Equal(JsonValueKind.Null, trace.GetProperty("powerMinMax")[1][0].ValueKind);
    }

    [Fact]
    public void PreservesBoundsAndNarrowProfilePredictionAndDerivedIntervals()
    {
        const string source = """
        {"results":[{"reportReference":"1",
         "solver":{"profileLikelihood":{"coordinates":[
          {"bestFitValue":1.00000002,"lowerBound":1.00000001,"upperBound":8.123456789,
           "lower":{"endpoint":1.000000015},"upper":{"endpoint":1.00000003}}]}},
         "advancedAnalyses":[{"values":[{"value":1.00000002,"standardDeviation":0.0123456789,"confidence95Lower":1.00000001,"confidence95Upper":1.00000003}]}],
         "experiments":[{"reportReference":"1A",
          "parameters":[{"bestFitValue":2.123456789,"confidence95Lower":1.111111119,
            "confidence95Upper":3.222222229,"fittedLowerBound":0.1234567890123456789,"fittedUpperBound":9.876543210987654321}],
          "injections":[{"injectionId":11,"observedHeatJoulesPerMole":2.123456789,
            "fittedHeatJoulesPerMole":1.00000002,"confidence95LowerJoulesPerMole":1.00000001,"confidence95UpperJoulesPerMole":1.00000003}]}]}]}
        """;
        using var original = JsonDocument.Parse(source);
        using var compact = Compact(source);
        var result = compact.RootElement.GetProperty("results")[0];
        var experiment = result.GetProperty("experiments")[0];
        var parameter = experiment.GetProperty("parameters")[0];
        Assert.Equal(2.12346, parameter.GetProperty("bestFitValue").GetDouble());
        foreach (var key in new[] { "fittedLowerBound", "fittedUpperBound" })
            Assert.Equal(original.RootElement.GetProperty("results")[0].GetProperty("experiments")[0]
                .GetProperty("parameters")[0].GetProperty(key).GetRawText(), parameter.GetProperty(key).GetRawText());
        var coordinate = result.GetProperty("solver").GetProperty("profileLikelihood").GetProperty("coordinates")[0];
        Assert.Equal(1.00000002, coordinate.GetProperty("bestFitValue").GetDouble());
        Assert.Equal(1.000000015, coordinate.GetProperty("lower").GetProperty("endpoint").GetDouble());
        Assert.Equal(1.00000001, coordinate.GetProperty("lowerBound").GetDouble());
        Assert.Equal(8.123456789, coordinate.GetProperty("upperBound").GetDouble());
        Assert.Equal(1.00000002, result.GetProperty("advancedAnalyses")[0].GetProperty("values")[0].GetProperty("value").GetDouble());
        Assert.Equal(0.0123456789, result.GetProperty("advancedAnalyses")[0].GetProperty("values")[0].GetProperty("standardDeviation").GetDouble());
        var row = experiment.GetProperty("injections").GetProperty("fit").GetProperty("rows")[0];
        Assert.Equal(1.00000002, row[3].GetDouble());
        Assert.Equal(1.00000001, row[5].GetDouble());
        Assert.Equal(1.00000003, row[6].GetDouble());
    }

    [Fact]
    public void KeepsUnknownInjectionFieldsAndNonSequentialRowOrder()
    {
        const string source = """
        {"results":[{"reportReference":"2","evidenceId":"r2",
         "bootstrapCorrelation":{"scopeEvidenceId":"r2","pearsonMatrix":[]},
         "experiments":[{"reportReference":"2C","experimentId":"same-C","blankReferenceExperimentId":"blank",
          "residualDiagnostics":{"meanFutureDiagnostic":1.1234567890123456789},
          "injections":[{"injectionId":14,"included":false,"analysisAxisKind":"FutureAxis","futureNumeric":1.1234567890123456789},
            {"injectionId":2,"included":true,"futureNumeric":null}]},
          {"reportReference":"2A","injections":[{"injectionId":37,"included":true}]}]}],
         "supportingExperiments":[{"reportReference":"S1","experimentId":"blank","injections":[{"injectionId":4,"included":false}]}]}
        """;
        using var compact = Compact(source);
        var root = compact.RootElement;
        var result = root.GetProperty("results")[0];
        Assert.Equal("2", result.GetProperty("bootstrapCorrelation").GetProperty("scopeReportReference").GetString());
        var experiment = result.GetProperty("experiments")[0];
        Assert.Equal("2C", experiment.GetProperty("reportReference").GetString());
        Assert.Equal("same-C", experiment.GetProperty("experimentId").GetString());
        Assert.Equal("blank", experiment.GetProperty("blankReferenceExperimentId").GetString());
        Assert.Equal("1.1234567890123456789", experiment.GetProperty("residualDiagnostics").GetProperty("meanFutureDiagnostic").GetRawText());
        foreach (var table in experiment.GetProperty("injections").EnumerateObject())
            Assert.Equal(new[] { 14, 2 }, table.Value.GetProperty("rows").EnumerateArray().Select(row => row[0].GetInt32()));
        var columns = root.GetProperty("tableSchemas").GetProperty("acquisition-v1").GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToArray();
        var rows = experiment.GetProperty("injections").GetProperty("acquisition").GetProperty("rows");
        Assert.Equal("1.1234567890123456789", rows[0][Array.IndexOf(columns, "futureNumeric")].GetRawText());
        Assert.Equal(JsonValueKind.Null, rows[1][Array.IndexOf(columns, "futureNumeric")].ValueKind);
        Assert.Equal("FutureAxis", experiment.GetProperty("injections").GetProperty("heatObservations").GetProperty("rows")[0][3].GetString());
        Assert.Equal(4, root.GetProperty("supportingExperiments")[0].GetProperty("injections").GetProperty("fit").GetProperty("rows")[0][0].GetInt32());
        Assert.Equal(AnalysisInterpretationModelInputWriter.Write(source), AnalysisInterpretationModelInputWriter.Write(source));
    }

    [Fact]
    public void CompactsBaselineControlsOnEachExperimentAndPreservesExtensions()
    {
        const string source = """
        {
          "results": [
            {
              "reportReference": "1",
              "experiments": [
                {
                  "reportReference": "1A",
                  "injections": [],
                  "baseline": {
                    "locked": true,
                    "landmarks": [
                      {"timeSeconds": 1.1234567899, "powerMicrowatts": 2.1234567899, "futurePoint": "kept"}
                    ],
                    "spline": {
                      "algorithm": "Smooth",
                      "controlPoints": [
                        {"timeSeconds": 3.1234567899, "powerMicrowatts": 4.1234567899, "slopeMicrowattsPerSecond": 5.1234567899, "locked": true, "slopeLocked": false, "linear": true, "userDefined": true, "futurePoint": 7.1234567899}
                      ]
                    },
                    "segmented": {
                      "degree": 2,
                      "coefficientConvention": "centred",
                      "segments": [
                        {"scope": "Injection", "injectionId": 3, "startTimeSeconds": 6.1234567899, "endTimeSeconds": 8.1234567899, "centerTimeSeconds": 7.1234567899, "coefficientsSi": [0.1234567899, 1.1234567899, 2.1234567899], "futureSegment": false},
                        {"scope": "InitialDelay", "injectionId": null, "startTimeSeconds": 9.1234567899, "endTimeSeconds": 10.1234567899, "centerTimeSeconds": 9.6234567899, "coefficientsSi": [null, 0.00000000123456789, 2.5]}
                      ]
                    }
                  }
                }
              ]
            }
          ],
          "supportingExperiments": [
            {"reportReference": "S1", "injections": [], "baseline": {"landmarks": [{"timeSeconds": 11.1234567899, "powerMicrowatts": 12.1234567899}]}}
          ]
        }
        """;
        using var original = JsonDocument.Parse(source);
        using var compact = Compact(source);
        var root = compact.RootElement;
        var schemas = root.GetProperty("tableSchemas");
        Assert.Equal(new[] { "timeSeconds", "powerMicrowatts" },
            schemas.GetProperty("baseline-landmarks-v1").GetProperty("columns").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "timeSeconds", "powerMicrowatts", "slopeMicrowattsPerSecond", "userDefined" },
            schemas.GetProperty("baseline-spline-controls-v1").GetProperty("columns").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "scope", "injectionId", "startTimeSeconds", "endTimeSeconds", "centerTimeSeconds", "coefficientsSi" },
            schemas.GetProperty("baseline-segments-v1").GetProperty("columns").EnumerateArray().Select(item => item.GetString()).ToArray());

        var experiment = root.GetProperty("results")[0].GetProperty("experiments")[0];
        var baseline = experiment.GetProperty("baseline");
        Assert.True(baseline.GetProperty("locked").GetBoolean());

        var landmarkTable = baseline.GetProperty("landmarks");
        Assert.Equal("baseline-landmarks-v1", landmarkTable.GetProperty("schema").GetString());
        Assert.Equal("1A", landmarkTable.GetProperty("reportReference").GetString());
        var landmark = Assert.Single(landmarkTable.GetProperty("rows").EnumerateArray());
        Assert.Equal(1.12345679, landmark[0].GetDouble());
        Assert.Equal(2.12345679, landmark[1].GetDouble());
        Assert.Equal("kept", landmarkTable.GetProperty("extensions")[0].GetProperty("values").GetProperty("futurePoint").GetString());

        var splineTable = baseline.GetProperty("spline").GetProperty("controlPoints");
        Assert.Equal("1A", splineTable.GetProperty("reportReference").GetString());
        var spline = Assert.Single(splineTable.GetProperty("rows").EnumerateArray());
        Assert.Equal(4, spline.GetArrayLength());
        Assert.Equal(3.12345679, spline[0].GetDouble());
        Assert.Equal(5.12345679, spline[2].GetDouble());
        Assert.True(spline[3].GetBoolean());
        Assert.Equal(7.1234567899, splineTable.GetProperty("extensions")[0].GetProperty("values").GetProperty("futurePoint").GetDouble());

        var segmentTable = baseline.GetProperty("segmented").GetProperty("segments");
        var segmentRows = segmentTable.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, segmentRows.Length);
        Assert.Equal(3, segmentRows[0][1].GetInt32());
        Assert.Equal(JsonValueKind.Null, segmentRows[1][1].ValueKind);
        Assert.Equal(6.12345679, segmentRows[0][2].GetDouble());
        Assert.Equal(8.12345679, segmentRows[0][3].GetDouble());
        Assert.Equal(7.12345679, segmentRows[0][4].GetDouble());
        Assert.Equal(2.12345679, segmentRows[0][5][2].GetDouble());
        Assert.Equal(JsonValueKind.Null, segmentRows[1][5][0].ValueKind);
        Assert.Equal(0.00000000123456789, segmentRows[1][5][1].GetDouble());
        Assert.Equal("centred", baseline.GetProperty("segmented").GetProperty("coefficientConvention").GetString());
        Assert.Equal("false", segmentTable.GetProperty("extensions")[0].GetProperty("values").GetProperty("futureSegment").GetRawText());

        var supporting = root.GetProperty("supportingExperiments")[0].GetProperty("baseline").GetProperty("landmarks");
        Assert.Equal("S1", supporting.GetProperty("reportReference").GetString());
        Assert.Equal(11.1234568, supporting.GetProperty("rows")[0][0].GetDouble());
        Assert.True(original.RootElement.GetProperty("results")[0].GetProperty("experiments")[0].GetProperty("baseline").GetProperty("spline").GetProperty("controlPoints")[0].GetProperty("locked").GetBoolean());
    }

    [Fact]
    public void RewritingCompactPackageRetainsBaselineTables()
    {
        const string source = """
        {"results":[{"reportReference":"1","experiments":[{"reportReference":"1A","injections":[],
          "baseline":{"landmarks":[{"timeSeconds":1,"powerMicrowatts":2}],"spline":{"controlPoints":[]}}}]}],"supportingExperiments":[]}
        """;
        var first = AnalysisInterpretationModelInputWriter.Write(source);
        var second = AnalysisInterpretationModelInputWriter.Write(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void OmittedSplineFlagsStillAffectFullEvidenceFreshness()
    {
        var point = new InterpretationSplineControlPoint
        {
            TimeSeconds = 1, PowerMicrowatts = 2, SlopeMicrowattsPerSecond = 0.000001,
            UserDefined = true, Locked = false, SlopeLocked = false, Linear = false,
        };
        var package = new AnalysisInterpretationPackage
        {
            Results = new List<InterpretationResultEvidence>
            {
                new() { ReportReference = "1", Experiments = new List<InterpretationExperimentEvidence>
                {
                    new() { ReportReference = "1A", Baseline = new InterpretationBaselineEvidence
                    {
                        Spline = new InterpretationSplineBaselineControls { ControlPoints = new List<InterpretationSplineControlPoint> { point } },
                    } },
                } },
            },
        };

        var first = AnalysisInterpretationPromptBuilder.Build(package);
        point.Locked = true;
        var second = AnalysisInterpretationPromptBuilder.Build(package);

        Assert.NotEqual(first.CanonicalPackageJson, second.CanonicalPackageJson);
        Assert.NotEqual(first.EvidenceFingerprint, second.EvidenceFingerprint);
        Assert.Equal(first.ModelPackageJson, second.ModelPackageJson);
    }

    [Fact]
    public void ChangesBelowModelPrecisionStillChangeFullEvidenceFingerprint()
    {
        var parameter = new InterpretationParameterEvidence { BestFitValue = 0.1234567891 };
        var package = new AnalysisInterpretationPackage { Results = new()
        { new() { ReportReference = "1", Experiments = new()
        { new() { ReportReference = "1A", Parameters = new() { parameter } } } } } };
        var first = AnalysisInterpretationPromptBuilder.Build(package);
        parameter.BestFitValue = 0.1234567892;
        var second = AnalysisInterpretationPromptBuilder.Build(package);
        Assert.Equal(first.ModelPackageJson, second.ModelPackageJson);
        Assert.NotEqual(first.CanonicalPackageJson, second.CanonicalPackageJson);
        Assert.NotEqual(first.EvidenceFingerprint, second.EvidenceFingerprint);
        Assert.Equal(0.1234567892, parameter.BestFitValue);
    }
}
