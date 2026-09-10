using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnalysisITC.Core.Interpretation
{
    /// <summary>Creates a compact model payload from canonical evidence without mutating the source.</summary>
    public static class AnalysisInterpretationModelInputWriter
    {
        public const string Encoding = "compact-tables-v1";

        static readonly TableDefinition[] Tables =
        {
            new TableDefinition("acquisition", "acquisition-v1", "Injection timing, volume, delay, filter and active concentrations.",
                "injectionId", "timeSeconds", "durationSeconds", "volumeLitres", "injectionDelaySeconds", "filterPeriodSeconds", "activeCellConcentrationMolar", "activeTitrantConcentrationMolar"),
            new TableDefinition("integration", "integration-v1", "Integration boundaries, delay, offset, length and nonintegrated interval.",
                "injectionId", "integrationStartTimeSeconds", "integrationEndTimeSeconds", "integrationStartDelaySeconds", "integrationEndOffsetSeconds", "integrationLengthSeconds", "integrationLengthFractionOfInjectionDelay", "nonIntegratedIntervalBeforeNextInjectionSeconds"),
            new TableDefinition("heatObservations", "heat-observations-v1", "Included status, integration status, analysis axis and integrated heats before and after subtraction.",
                "injectionId", "included", "isIntegrated", "analysisAxisKind", "analysisAxisValue", "integratedHeatBeforeSubtractionJoules", "integratedHeatBeforeSubtractionErrorJoules", "integratedHeatJoules", "integratedHeatErrorJoules"),
            new TableDefinition("fit", "fit-v1", "Observed and fitted molar heats, residuals and confidence endpoints.",
                "injectionId", "observedHeatJoulesPerMole", "observedHeatErrorJoulesPerMole", "fittedHeatJoulesPerMole", "residualJoulesPerMole", "confidence95LowerJoulesPerMole", "confidence95UpperJoulesPerMole"),
            new TableDefinition("baseline", "baseline-v1", "Baseline values at integration boundaries and integrated correction.",
                "injectionId", "baselineAtIntegrationStartMicrowatts", "baselineAtIntegrationEndMicrowatts", "baselineChangeAcrossIntegrationMicrowatts", "integratedBaselineCorrectionJoules"),
        };

        static readonly HashSet<string> NineDigitNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "timeSeconds", "durationSeconds", "integrationStartTimeSeconds", "integrationEndTimeSeconds",
            "nonIntegratedIntervalBeforeNextInjectionSeconds", "injectionDelaySeconds", "filterPeriodSeconds",
            "integrationStartDelaySeconds", "integrationEndOffsetSeconds", "integrationLengthSeconds",
            "integrationLengthFractionOfInjectionDelay", "baselineAtIntegrationStartMicrowatts",
            "baselineAtIntegrationEndMicrowatts", "baselineChangeAcrossIntegrationMicrowatts",
            "linearDriftRateMicrowattsPerHour", "startPowerMicrowatts", "endPowerMicrowatts", "netDriftMicrowatts",
            "rangeMicrowatts", "rmsDeviationFromLinearTrendMicrowatts", "outsideIntegrationRmsRawMinusBaselineMicrowatts",
            "outsideIntegrationMedianAbsoluteDeviationRawMinusBaselineMicrowatts", "powerMicrowatts",
            "slopeMicrowattsPerSecond", "residualSlopeAgainstAnalysisAxis", "initialDelaySeconds", "traceDurationSeconds",
            "startTimeSeconds", "endTimeSeconds", "centerTimeSeconds", "slopeSiPerKelvin",
            "integratedBaselineCorrectionJoules", "minimumFilterPeriodSeconds", "maximumFilterPeriodSeconds",
        };

        static readonly HashSet<string> FullPrecisionNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "anchorTimeSeconds", "binWidthSeconds", "powerOffsetWatts", "minusTwoLogLikelihood", "aic", "aicc",
            "fittedLowerBound", "fittedUpperBound", "lowerBound", "upperBound",
        };

        static readonly HashSet<string> ParameterNumbers = new HashSet<string>(StringComparer.Ordinal)
        {
            "bestFitValue", "standardDeviation", "confidence95Lower", "confidence95Upper",
            "fittedLowerBound", "fittedUpperBound", "lowerBound", "upperBound", "endpoint", "value",
        };

        public static string Write(AnalysisInterpretationPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            return Write(JsonSerializer.Serialize(package, AnalysisInterpretationPromptBuilder.CanonicalJsonOptions));
        }

        public static string Write(JsonElement fullEvidence)
        {
            if (fullEvidence.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Canonical evidence must be a JSON object.", nameof(fullEvidence));
            return Write(fullEvidence.GetRawText());
        }

        public static string Write(string fullEvidenceJson)
        {
            if (string.IsNullOrWhiteSpace(fullEvidenceJson))
                throw new ArgumentException("Canonical evidence JSON is required.", nameof(fullEvidenceJson));
            var root = JsonNode.Parse(fullEvidenceJson) as JsonObject;
            if (root == null) throw new ArgumentException("Canonical evidence must be a JSON object.", nameof(fullEvidenceJson));

            var mapping = BuildReportReferenceMapping(root);
            var extras = CollectExtraInjectionColumns(root);
            root.Remove("evidenceCatalog");
            root["modelInputEncoding"] = Encoding;
            root["tableSchemas"] = BuildTableSchemas(extras);
            root["modelInputDefinitions"] = new JsonObject
            {
                ["rows"] = "Each table row is a positional array in the declared column order; injectionId is present in every row.",
                ["nulls"] = "null means unavailable; excluded injections and source order are retained.",
                ["tables"] = "Each table's schema resolves through tableSchemas[schema].columns; reportReference identifies its experiment scope and units are expressed in column names.",
                ["precision"] = "Ordinary scientific values use six significant digits; time, duration, baseline, power, slopes, drift and thermogram extrema use nine. Thermogram anchors, bin widths and offsets, likelihoods, information criteria and parameter bounds retain full precision; narrow interval groups and imperfect correlations may retain extra precision.",
            };
            RewriteCorrelations(root, mapping);
            RewriteExperiments(root, extras);
            RemoveKnownEvidenceIds(root);
            RoundKnownNumbers(root);
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        static JsonObject BuildTableSchemas(IReadOnlyCollection<string> extras)
        {
            var schemas = new JsonObject();
            foreach (var table in Tables)
            {
                var columns = new JsonArray();
                foreach (var column in table.Columns) columns.Add(column);
                if (table.Name == "acquisition") foreach (var extra in extras) columns.Add(extra);
                schemas[table.SchemaName] = new JsonObject { ["description"] = table.Description, ["columns"] = columns };
            }
            return schemas;
        }

        static Dictionary<string, string> BuildReportReferenceMapping(JsonObject root)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            AddReference(root["report"] as JsonObject, map);
            foreach (var result in Objects(root["results"]))
            {
                AddReference(result, map);
                foreach (var experiment in Objects(result["experiments"])) AddReference(experiment, map);
            }
            foreach (var experiment in Objects(root["supportingExperiments"])) AddReference(experiment, map);
            return map;
        }

        static void AddReference(JsonObject value, IDictionary<string, string> map)
        {
            var id = String(value?["evidenceId"]); var reference = String(value?["reportReference"]);
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(reference)) map[id] = reference;
        }

        static List<string> CollectExtraInjectionColumns(JsonObject root)
        {
            var known = new HashSet<string>(Tables.SelectMany(t => t.Columns), StringComparer.Ordinal);
            var extras = new List<string>();
            foreach (var experiment in AllExperiments(root))
                foreach (var injection in Objects(experiment["injections"]))
                    foreach (var property in injection)
                        if (!property.Key.Equals("evidenceId", StringComparison.Ordinal) && !known.Contains(property.Key) && !extras.Contains(property.Key))
                            extras.Add(property.Key);
            return extras;
        }

        static void RewriteExperiments(JsonObject root, IReadOnlyCollection<string> extras)
        {
            foreach (var result in Objects(root["results"])) foreach (var experiment in Objects(result["experiments"])) RewriteExperiment(experiment, extras);
            foreach (var experiment in Objects(root["supportingExperiments"])) RewriteExperiment(experiment, extras);
        }

        static void RewriteExperiment(JsonObject experiment, IReadOnlyCollection<string> extras)
        {
            var rows = Objects(experiment["injections"]).ToList();
            var tables = new JsonObject();
            foreach (var table in Tables)
            {
                var tableRows = new JsonArray();
                foreach (var source in rows)
                {
                    var row = new JsonArray();
                    foreach (var column in table.Columns) row.Add(Clone(source[column]));
                    if (table.Name == "acquisition") foreach (var extra in extras) row.Add(Clone(source[extra]));
                    tableRows.Add(row);
                }
                tables[table.Name] = new JsonObject { ["schema"] = table.SchemaName, ["reportReference"] = String(experiment["reportReference"]), ["rows"] = tableRows };
            }
            experiment["injections"] = tables;
        }

        static void RemoveKnownEvidenceIds(JsonObject root)
        {
            Remove(root["report"] as JsonObject);
            foreach (var result in Objects(root["results"]))
            {
                Remove(result);
                foreach (var experiment in Objects(result["experiments"])) RemoveExperimentEvidenceIds(experiment);
            }
            foreach (var experiment in Objects(root["supportingExperiments"])) RemoveExperimentEvidenceIds(experiment);
        }

        static void RemoveExperimentEvidenceIds(JsonObject experiment)
        {
            Remove(experiment);
            foreach (var parameter in Objects(experiment["parameters"])) Remove(parameter);
            Remove(experiment["baseline"] as JsonObject);
            Remove(experiment["residualDiagnostics"] as JsonObject);
        }

        static void Remove(JsonObject value) { value?.Remove("evidenceId"); }

        static void RewriteCorrelations(JsonObject root, IDictionary<string, string> map)
        {
            foreach (var result in Objects(root["results"]))
            {
                RewriteCorrelation(result["bootstrapCorrelation"] as JsonObject, map);
                foreach (var correlation in Objects(result["bootstrapCorrelations"])) RewriteCorrelation(correlation, map);
            }
        }

        static void RewriteCorrelation(JsonObject correlation, IDictionary<string, string> map)
        {
            if (correlation == null) return;
            var source = String(correlation["scopeEvidenceId"]);
            if (!string.IsNullOrEmpty(source) && map.TryGetValue(source, out var reference))
            {
                correlation.Remove("scopeEvidenceId");
                correlation["scopeReportReference"] = reference;
            }
        }

        static void RoundKnownNumbers(JsonObject root)
        {
            foreach (var result in Objects(root["results"])) RoundResult(result);
            foreach (var experiment in Objects(root["supportingExperiments"])) RoundExperiment(experiment);
        }

        static void RoundResult(JsonObject result)
        {
            RoundSolver(result["solver"] as JsonObject);
            foreach (var option in Objects((result["model"] as JsonObject)?["options"])) RoundFields(option, "numericValue");
            RoundCorrelation(result["bootstrapCorrelation"] as JsonObject);
            foreach (var correlation in Objects(result["bootstrapCorrelations"])) RoundCorrelation(correlation);
            foreach (var experiment in Objects(result["experiments"])) RoundExperiment(experiment);
            foreach (var item in Objects(result["temperatureDependence"])) RoundFields(item, "referenceTemperatureKelvin", "referenceTemperatureCelsius", "interceptSi", "slopeSiPerKelvin");
            foreach (var item in Objects(result["advancedAnalyses"])) foreach (var value in Objects(item["values"])) RoundAdvancedValue(value);
        }

        static void RoundExperiment(JsonObject experiment)
        {
            RoundSolver(experiment["solver"] as JsonObject);
            RoundFields(experiment, "targetTemperatureKelvin", "measuredTemperatureKelvin", "targetTemperatureCelsius", "measuredTemperatureCelsius", "cellConcentrationMolar", "cellConcentrationSdMolar", "syringeConcentrationMolar", "syringeConcentrationSdMolar", "integrationLengthFactor", "initialDelaySeconds");
            RoundInstrument(experiment["instrument"] as JsonObject);
            RoundBaseline(experiment["baseline"] as JsonObject);
            RoundResidualDiagnostics(experiment["residualDiagnostics"] as JsonObject);
            foreach (var item in Objects(experiment["attributes"])) RoundFields(item, "numericValue");
            foreach (var item in Objects(experiment["modelOptions"])) RoundFields(item, "numericValue");
            foreach (var parameter in Objects(experiment["parameters"])) RoundParameterGroup(parameter);
            RoundThermogram(experiment["thermogram"] as JsonObject);
            RoundTableRows(experiment["injections"] as JsonObject);
            foreach (var segment in Objects(experiment["tandemSegments"])) RoundFields(segment, "startTimeSeconds", "endTimeSeconds", "initialActiveCellConcentrationMolar", "initialActiveTitrantConcentrationMolar");
        }

        static void RoundSolver(JsonObject solver)
        {
            if (solver == null) return;
            RoundFields(solver, "unweightedRmsdMicrojoules", "unweightedMolarRmsdJoulesPerMole");
            var profile = solver["profileLikelihood"] as JsonObject;
            if (profile != null) foreach (var coordinate in Objects(profile["coordinates"])) RoundProfileCoordinate(coordinate);
        }

        static void RoundInstrument(JsonObject value)
        {
            RoundFields(value, "cellVolumeLitres", "stirringSpeedRpm", "filterPeriodSeconds", "minimumFilterPeriodSeconds", "maximumFilterPeriodSeconds");
        }

        static void RoundBaseline(JsonObject value)
        {
            if (value == null) return;
            RoundFields(value, "traceDurationSeconds", "startPowerMicrowatts", "endPowerMicrowatts", "netDriftMicrowatts", "linearDriftRateMicrowattsPerHour", "rangeMicrowatts", "rmsDeviationFromLinearTrendMicrowatts", "outsideIntegrationRmsRawMinusBaselineMicrowatts", "outsideIntegrationMedianAbsoluteDeviationRawMinusBaselineMicrowatts", "rejectionZLimit", "lambda", "asymmetry");
            foreach (var landmark in Objects(value["landmarks"])) RoundFields(landmark, "timeSeconds", "powerMicrowatts");
            var spline = value["spline"] as JsonObject;
            if (spline != null) foreach (var point in Objects(spline["controlPoints"])) RoundFields(point, "timeSeconds", "powerMicrowatts", "slopeMicrowattsPerSecond");
            var segmented = value["segmented"] as JsonObject;
            if (segmented != null) foreach (var segment in Objects(segmented["segments"]))
            {
                RoundFields(segment, "startTimeSeconds", "endTimeSeconds", "centerTimeSeconds");
                if (segment["coefficientsSi"] is JsonArray coefficients)
                    for (var i = 0; i < coefficients.Count; i++) RoundNumber(coefficients, i, 9);
            }
            RoundFields(value["polynomial"] as JsonObject, "rejectionZLimit");
            RoundFields(value["asymmetricLeastSquares"] as JsonObject, "lambda", "asymmetry");
        }

        static void RoundResidualDiagnostics(JsonObject value)
        {
            if (value == null) return;
            foreach (var name in new[] { "meanResidualJoulesPerMole", "rmsResidualJoulesPerMole", "meanAbsoluteResidualJoulesPerMole", "medianAbsoluteResidualJoulesPerMole", "earlyMeanResidualJoulesPerMole", "middleMeanResidualJoulesPerMole", "lateMeanResidualJoulesPerMole", "maximumAbsoluteStandardisedResidual" }) RoundField(value, name, 6);
            RoundCorrelationField(value, "lagOneAutocorrelation");
            RoundField(value, "residualSlopeAgainstAnalysisAxis", 9);
        }

        static void RoundThermogram(JsonObject value)
        {
            if (value == null) return;
            RoundMinMax(value["powerMinMax"] as JsonArray); RoundMinMax(value["baselineMinMax"] as JsonArray);
        }

        static void RoundCorrelation(JsonObject correlation)
        {
            if (correlation == null) return;
            var matrix = correlation["pearsonMatrix"] as JsonArray ?? correlation["matrix"] as JsonArray;
            if (matrix == null) return;
            for (var rowIndex = 0; rowIndex < matrix.Count; rowIndex++)
            {
                var row = matrix[rowIndex] as JsonArray;
                if (row == null) continue;
                for (var i = 0; i < row.Count; i++) RoundCorrelationNumber(row, i);
            }
        }

        static void RoundCorrelationField(JsonObject value, string name)
        {
            if (value == null || !(value[name] is JsonValue number) || !TryDouble(number, out var original) || IsIntegerLiteral(number)) return;
            var rounded = ParseNumber(NumberText(original, 6));
            if (Math.Abs(original) < 1 && Math.Abs(rounded) >= 1) return;
            value[name] = JsonNode.Parse(NumberText(original, 6));
        }

        static void RoundCorrelationNumber(JsonArray row, int index)
        {
            if (!(row[index] is JsonValue number) || !TryDouble(number, out var original) || IsIntegerLiteral(number)) return;
            var rounded = ParseNumber(NumberText(original, 6));
            if (Math.Abs(original) < 1 && Math.Abs(rounded) >= 1) return;
            row[index] = JsonNode.Parse(NumberText(original, 6));
        }

        static void RoundMinMax(JsonArray values)
        {
            if (values == null) return;
            for (var pairIndex = 0; pairIndex < values.Count; pairIndex++)
            {
                var pair = values[pairIndex] as JsonArray;
                if (pair == null) continue;
                for (var i = 0; i < pair.Count; i++) RoundNumber(pair, i, 9);
            }
        }

        static void RoundTableRows(JsonObject injections)
        {
            if (injections == null) return;
            foreach (var table in Tables)
            {
                var rows = (injections[table.Name] as JsonObject)?["rows"] as JsonArray;
                if (rows == null) continue;
                foreach (var row in rows.OfType<JsonArray>())
                {
                    var preserveFitInterval = table.Name == "fit" && FitIntervalNeedsPreservation(row, table.Columns);
                    for (var i = 0; i < table.Columns.Length && i < row.Count; i++)
                        if (table.Columns[i] != "injectionId" && !(preserveFitInterval && IsFitIntervalColumn(table.Columns[i]))) RoundNumber(row, i, Precision(table.Columns[i]));
                }
            }
        }

        static bool IsFitIntervalColumn(string name)
        {
            return name == "fittedHeatJoulesPerMole" || name == "confidence95LowerJoulesPerMole" || name == "confidence95UpperJoulesPerMole";
        }

        static bool FitIntervalNeedsPreservation(JsonArray row, string[] columns)
        {
            var values = new List<NumericField>();
            for (var i = 0; i < columns.Length && i < row.Count; i++)
            {
                if (!IsFitIntervalColumn(columns[i]) || !(row[i] is JsonValue number) || !TryDouble(number, out var original)) continue;
                values.Add(new NumericField(columns[i], original, IsIntegerLiteral(number) ? original : ParseNumber(NumberText(original, 6))));
            }
            return HasCollisionOrOrderChange(values);
        }

        static void RoundAdvancedValue(JsonObject value)
        {
            RoundParameterGroup(value);
        }

        static void RoundParameterGroup(JsonObject value)
        {
            if (value == null) return;
            var numbers = new List<NumericField>();
            foreach (var property in value) if (ParameterNumbers.Contains(property.Key) && TryDouble(property.Value as JsonValue, out var original)) numbers.Add(new NumericField(property.Key, original, FullPrecisionNames.Contains(property.Key) || IsIntegerLiteral(property.Value as JsonValue) ? original : ParseNumber(NumberText(original, 6))));
            if (HasCollisionOrOrderChange(numbers)) return;
            foreach (var item in numbers) RoundField(value, item.Name, 6);
        }

        static void RoundProfileCoordinate(JsonObject value)
        {
            if (value == null) return;
            var numbers = new List<NumericField>();
            foreach (var property in value) if (ParameterNumbers.Contains(property.Key) && TryDouble(property.Value as JsonValue, out var original)) numbers.Add(new NumericField(property.Key, original, FullPrecisionNames.Contains(property.Key) || IsIntegerLiteral(property.Value as JsonValue) ? original : ParseNumber(NumberText(original, 6))));
            foreach (var sideName in new[] { "lower", "upper" }) { var side = value[sideName] as JsonObject; if (TryDouble(side?["endpoint"] as JsonValue, out var endpoint)) numbers.Add(new NumericField(sideName + ".endpoint", endpoint, IsIntegerLiteral(side["endpoint"] as JsonValue) ? endpoint : ParseNumber(NumberText(endpoint, 6)))); }
            if (HasCollisionOrOrderChange(numbers)) return;
            foreach (var item in numbers) { var parts = item.Name.Split('.'); RoundField(parts.Length == 1 ? value : value[parts[0]] as JsonObject, parts.Length == 1 ? item.Name : parts[1], 6); }
        }

        static bool HasCollisionOrOrderChange(List<NumericField> values)
        {
            return values.Any(a => values.Any(b => a.Name != b.Name && a.Original != b.Original && a.Rounded == b.Rounded)) || ChangesOrdering(values);
        }

        static void RoundFields(JsonObject value, params string[] names) { if (value != null) foreach (var name in names) RoundField(value, name, Precision(name)); }

        static void RoundField(JsonObject value, string name, int digits)
        {
            if (FullPrecisionNames.Contains(name)) return;
            if (value == null || !(value[name] is JsonValue number) || !TryDouble(number, out var original)) return;
            value[name] = RoundedNumber(number, original, digits);
        }

        static void RoundNumber(JsonArray parent, int index, int digits)
        {
            var value = index >= 0 && index < parent.Count ? parent[index] : null;
            if (!(value is JsonValue number) || !TryDouble(number, out var original) || IsIntegerLiteral(number)) return;
            parent[index] = RoundedNumber(number, original, digits);
        }

        static JsonNode RoundedNumber(JsonValue source, double original, int digits) => IsIntegerLiteral(source) ? source.DeepClone() : JsonNode.Parse(NumberText(original, digits));
        static bool IsIntegerLiteral(JsonValue value) { var raw = value.ToJsonString(); return raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0; }
        static int Precision(string name) => NineDigitNames.Contains(name) ? 9 : 6;

        static bool ChangesOrdering(List<NumericField> values)
        {
            for (var i = 0; i < values.Count; i++) for (var j = i + 1; j < values.Count; j++) if (Math.Sign(values[i].Original - values[j].Original) != Math.Sign(values[i].Rounded - values[j].Rounded)) return true;
            return false;
        }

        static bool TryDouble(JsonValue value, out double result) { if (value != null && value.TryGetValue<double>(out result)) return !double.IsNaN(result) && !double.IsInfinity(result); result = 0; return false; }
        static string NumberText(double value, int digits) => value.ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        static double ParseNumber(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        static JsonNode Clone(JsonNode value) => value?.DeepClone();
        static string String(JsonNode value) => value is JsonValue json && json.TryGetValue<string>(out var result) ? result : null;
        static IEnumerable<JsonObject> Objects(JsonNode value) => (value as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();
        static IEnumerable<JsonObject> AllExperiments(JsonObject root) { foreach (var result in Objects(root["results"])) foreach (var experiment in Objects(result["experiments"])) yield return experiment; foreach (var experiment in Objects(root["supportingExperiments"])) yield return experiment; }

        sealed class NumericField { public readonly string Name; public readonly double Original; public readonly double Rounded; public NumericField(string name, double original, double rounded) { Name = name; Original = original; Rounded = rounded; } }
        sealed class TableDefinition { public readonly string Name, SchemaName, Description; public readonly string[] Columns; public TableDefinition(string name, string schemaName, string description, params string[] columns) { Name = name; SchemaName = schemaName; Description = description; Columns = columns; } }
    }
}
