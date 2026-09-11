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
        public const string SharedEvidenceEncoding = "compact-tables-shared-evidence-v1";

        // These fields describe the observations and processing state that can be
        // reused by several fits.  Fit/solver/uncertainty fields deliberately stay
        // on the result member.  The source projection below is also used only for
        // equality; the full canonical JSON remains the freshness authority.
        static readonly string[] SourceFields =
        {
            "experimentId", "name", "sourceFileBasename", "dateProvenance", "sourceStateFingerprint",
            "thermogram", "tandemSegments", "blankReferenceExperimentId", "blankSubtractionMethod", "dateUtc",
            "comments", "instrument", "targetTemperatureKelvin", "measuredTemperatureKelvin",
            "targetTemperatureCelsius", "measuredTemperatureCelsius", "cellConcentrationMolar",
            "cellConcentrationSdMolar", "syringeConcentrationMolar", "syringeConcentrationSdMolar",
            "analysisAxis", "baselineCompleted", "integrationCompleted", "baselineProcessor", "processorLocked",
            "discardsIntegratedPointsForBaseline", "integrationLengthMode", "integrationLengthFactor",
            "initialDelaySeconds", "baseline", "attributes",
        };

        static readonly string[] SharedInjectionTables = { "acquisition", "integration", "heatObservations", "baseline" };

        static readonly HashSet<string> FitInjectionFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "observedHeatJoulesPerMole", "observedHeatErrorJoulesPerMole", "fittedHeatJoulesPerMole",
            "residualJoulesPerMole", "confidence95LowerJoulesPerMole", "confidence95UpperJoulesPerMole",
        };

        static readonly TableDefinition[] Tables =
        {
            new TableDefinition("acquisition", "acquisition-v1", "Injection timing, volume, delay, filter and active concentrations.",
                "injectionId", "timeSeconds", "durationSeconds", "volumeLitres", "injectionDelaySeconds", "filterPeriodSeconds", "activeCellConcentrationMolar", "activeTitrantConcentrationMolar"),
            new TableDefinition("integration", "integration-v2", "Integration boundaries, delay, offset, length and nonintegrated interval.",
                "injectionId", "integrationStartTimeSeconds", "integrationEndTimeSeconds", "integrationStartDelaySeconds", "integrationEndOffsetSeconds", "integrationLengthSeconds", "nonIntegratedTimeFraction", "nonIntegratedIntervalBeforeNextInjectionSeconds"),
            new TableDefinition("heatObservations", "heat-observations-v1", "Included status, integration status, analysis axis and integrated heats before and after subtraction.",
                "injectionId", "included", "isIntegrated", "analysisAxisKind", "analysisAxisValue", "integratedHeatBeforeSubtractionJoules", "integratedHeatBeforeSubtractionErrorJoules", "integratedHeatJoules", "integratedHeatErrorJoules"),
            new TableDefinition("fit", "fit-v1", "Observed and fitted molar heats, residuals and confidence endpoints.",
                "injectionId", "observedHeatJoulesPerMole", "observedHeatErrorJoulesPerMole", "fittedHeatJoulesPerMole", "residualJoulesPerMole", "confidence95LowerJoulesPerMole", "confidence95UpperJoulesPerMole"),
            new TableDefinition("baseline", "baseline-v1", "Baseline values at integration boundaries and integrated correction.",
                "injectionId", "baselineAtIntegrationStartMicrowatts", "baselineAtIntegrationEndMicrowatts", "baselineChangeAcrossIntegrationMicrowatts", "integratedBaselineCorrectionJoules"),
        };

        // Baseline controls remain attached to each experiment, but use the same
        // schema/table representation as injection evidence. Unknown point fields
        // are retained in a table-level extension map.
        static readonly TableDefinition[] BaselineTables =
        {
            new TableDefinition("landmarks", "baseline-landmarks-v1", "Sampled fitted-baseline landmarks at explicit times.",
                "timeSeconds", "powerMicrowatts"),
            new TableDefinition("controlPoints", "baseline-spline-controls-v1", "Spline baseline control points; omitted control flags are not implied to be false.",
                "timeSeconds", "powerMicrowatts", "slopeMicrowattsPerSecond", "userDefined"),
            new TableDefinition("segments", "baseline-segments-v1", "Segmented baseline bounds and polynomial coefficients in SI units and centred-time convention.",
                "scope", "injectionId", "startTimeSeconds", "endTimeSeconds", "centerTimeSeconds", "coefficientsSi"),
        };

        static readonly HashSet<string> NineDigitNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "timeSeconds", "durationSeconds", "integrationStartTimeSeconds", "integrationEndTimeSeconds",
            "nonIntegratedIntervalBeforeNextInjectionSeconds", "injectionDelaySeconds", "filterPeriodSeconds",
            "integrationStartDelaySeconds", "integrationEndOffsetSeconds", "integrationLengthSeconds",
            "nonIntegratedTimeFraction", "integrationLengthFractionOfInjectionDelay", "baselineAtIntegrationStartMicrowatts",
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

            // Shared model input is already a complete compact representation.  In
            // particular, do not attempt to treat its positional tables as the
            // source arrays used for grouping.
            if (String(root["modelInputEncoding"]) == SharedEvidenceEncoding
                && root["experimentEvidence"] is JsonArray)
                return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            if (String(root["modelInputEncoding"]) == Encoding && HasPositionalInjectionTables(root))
                return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

            // Group membership is decided from the untouched, full-precision
            // source projection.  This prevents transmitted rounding or omitted
            // spline flags from making distinct processing states look identical.
            var sourceGroups = BuildSourceGroups(root.DeepClone() as JsonObject);

            var mapping = BuildReportReferenceMapping(root);
            var extras = CollectExtraInjectionColumns(root);
            root.Remove("evidenceCatalog");
            root["modelInputEncoding"] = Encoding;
            root["tableSchemas"] = BuildTableSchemas(extras);
            RewriteBaselineTables(root);
            root["modelInputDefinitions"] = new JsonObject
            {
                ["rows"] = "Each injection table row is a positional array in the declared column order and includes injectionId; baseline landmark and spline-control rows use explicit time, while segmented-baseline rows retain scope and may have a null injectionId.",
                ["nulls"] = "null means unavailable; excluded injections and source order are retained.",
                ["tables"] = "Each table's schema resolves through tableSchemas[schema].columns; member-local tables carry the experiment reportReference, while tables in experimentEvidence carry that record's evidenceReference. Baseline tables may also contain an extensions map keyed by row index for unknown source properties.",
                ["baselineControls"] = "Baseline landmark, spline-control and segment tables contain fitted-baseline evidence, not raw signal observations. Spline control flags other than userDefined are intentionally omitted; the control table is not a complete specification for reconstructing the exact interpolated baseline, and omitted flags must not be interpreted as false.",
                ["precision"] = "Ordinary scientific values use six significant digits; time, duration, baseline, power, slopes, drift and thermogram extrema use nine. Thermogram anchors, bin widths and offsets, likelihoods, information criteria and parameter bounds retain full precision; narrow interval groups and imperfect correlations may retain extra precision.",
            };
            RewriteCorrelations(root, mapping);
            RewriteExperiments(root, extras);
            RemoveKnownEvidenceIds(root);
            RoundKnownNumbers(root);
            var inline = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

            // A shared layout is useful only when its complete envelope is smaller.
            // This keeps unrelated, one-off experiments in the simpler established
            // representation and makes references a bounded optimisation.
            if (sourceGroups.Count > 0 && sourceGroups.Any(group => group.ReportReferences.Count > 1))
            {
                var sharedRoot = root.DeepClone() as JsonObject;
                if (ApplySharedEvidence(sharedRoot, sourceGroups))
                {
                    var shared = sharedRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    if (System.Text.Encoding.UTF8.GetByteCount(shared) < System.Text.Encoding.UTF8.GetByteCount(inline)) return shared;
                }
            }
            return inline;
        }

        /// <summary>Reads the representation identifier emitted by <see cref="Write"/>.</summary>
        public static string ReadEncoding(string modelPackageJson)
        {
            if (string.IsNullOrWhiteSpace(modelPackageJson)) return Encoding;
            using var document = JsonDocument.Parse(modelPackageJson);
            return document.RootElement.TryGetProperty("modelInputEncoding", out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : Encoding;
        }

        static List<SourceGroup> BuildSourceGroups(JsonObject root)
        {
            var groups = new List<SourceGroup>();
            if (root == null || HasPositionalInjectionTables(root)) return groups;
            var byKey = new Dictionary<string, SourceGroup>(StringComparer.Ordinal);
            foreach (var member in AllMembers(root))
            {
                var reference = String(member["reportReference"]);
                if (string.IsNullOrEmpty(reference)) continue;

                // An absent experiment identity cannot establish that two records
                // are the same source.  Keep such members separate even if all
                // other exported values happen to match.
                var identity = String(member["experimentId"]);
                var key = ComparisonText(BuildSourceProjection(member));
                if (string.IsNullOrEmpty(identity)) key += "|member:" + reference;

                if (!byKey.TryGetValue(key, out var group))
                {
                    group = new SourceGroup("E" + (groups.Count + 1).ToString(CultureInfo.InvariantCulture));
                    byKey.Add(key, group); groups.Add(group);
                }
                group.ReportReferences.Add(reference);
            }
            return groups;
        }

        static bool HasPositionalInjectionTables(JsonObject root)
        {
            foreach (var member in AllMembers(root))
                if (member["injections"] is JsonObject) return true;
            return false;
        }

        static JsonObject BuildSourceProjection(JsonObject member)
        {
            var source = new JsonObject();
            foreach (var field in SourceFields)
            {
                if (field == "baseline")
                {
                    if (member["baseline"] != null)
                    {
                        var baseline = Clone(member["baseline"]);
                        // The baseline object's evidenceId is an internal
                        // navigation identifier. Unknown nested properties are
                        // scientific evidence and must still participate in
                        // equality and remain present.
                        (baseline as JsonObject)?.Remove("evidenceId");
                        source[field] = baseline;
                    }
                    else if (member.ContainsKey("baseline"))
                        source[field] = null;
                }
                else if (member[field] != null)
                    source[field] = Clone(member[field]);
                else if (member.ContainsKey(field))
                    source[field] = null;
            }

            if (member["injections"] is JsonArray injections)
            {
                var sourceRows = new JsonArray();
                foreach (var injection in injections.OfType<JsonObject>())
                {
                    var sourceRow = new JsonObject();
                    foreach (var property in injection)
                    {
                        if (property.Key == "evidenceId" || FitInjectionFields.Contains(property.Key)) continue;
                        sourceRow[property.Key] = Clone(property.Value);
                    }
                    sourceRows.Add(sourceRow);
                }
                source["injections"] = sourceRows;
            }
            else if (member.ContainsKey("injections"))
                source["injections"] = Clone(member["injections"]);
            return source;
        }

        static string ComparisonText(JsonNode value)
        {
            if (value == null) return "null";
            if (value is JsonObject obj)
                return "{" + string.Join(",", obj.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => JsonSerializer.Serialize(item.Key) + ":" + ComparisonText(item.Value))) + "}";
            if (value is JsonArray array) return "[" + string.Join(",", array.Select(ComparisonText)) + "]";
            return value.ToJsonString();
        }

        static bool ApplySharedEvidence(JsonObject root, IReadOnlyList<SourceGroup> groups)
        {
            if (root == null || groups == null || groups.Count == 0) return false;
            var byReference = new Dictionary<string, SourceGroup>(StringComparer.Ordinal);
            foreach (var group in groups)
                foreach (var reference in group.ReportReferences)
                    if (byReference.ContainsKey(reference)) return false;
                    else byReference.Add(reference, group);

            var records = new JsonArray();
            foreach (var group in groups)
            {
                var first = FindMember(root, group.ReportReferences[0]);
                if (first == null) return false;
                var record = BuildSharedSourceRecord(first, group.Reference, group.ReportReferences);
                records.Add(record);
            }

            foreach (var member in AllMembers(root).ToList())
            {
                var reference = String(member["reportReference"]);
                SourceGroup group;
                if (string.IsNullOrEmpty(reference) || !byReference.TryGetValue(reference, out group)) return false;
                MoveSourceToRecord(member, group.Reference);
            }

            root["experimentEvidence"] = records;
            root["modelInputEncoding"] = SharedEvidenceEncoding;
            if (root["modelInputDefinitions"] is JsonObject definitions)
                definitions["sharedEvidence"] = "When present, each member's experimentEvidenceRef resolves directly to one complete experimentEvidence record. Reuse means identical exported acquisition and processing evidence from the same experiment, not independent replication or equal fits. reportReferences lists the member labels using the record; cite those Result/Experiment labels rather than the internal evidenceReference. Fit tables, parameters, uncertainty, validity and constraints remain result-specific. Different processing states have separate records, and reuse does not verify that a historical fit used the current processing.";
            return true;
        }

        static JsonObject BuildSharedSourceRecord(JsonObject member, string evidenceReference, IReadOnlyList<string> reportReferences)
        {
            var record = new JsonObject
            {
                ["evidenceReference"] = evidenceReference,
                ["reportReferences"] = new JsonArray(reportReferences.Select(reference => (JsonNode)reference).ToArray()),
            };
            foreach (var field in SourceFields)
                if (member[field] != null) record[field] = Clone(member[field]);
                else if (member.ContainsKey(field)) record[field] = null;

            var sourceTables = new JsonObject();
            var injections = member["injections"] as JsonObject;
            foreach (var tableName in SharedInjectionTables)
            {
                if (injections?[tableName] is not JsonObject table) continue;
                var copy = table.DeepClone() as JsonObject;
                copy.Remove("reportReference"); copy["evidenceReference"] = evidenceReference;
                sourceTables[tableName] = copy;
            }
            record["injections"] = sourceTables;
            SetBaselineTableOwners(record["baseline"], evidenceReference);
            return record;
        }

        static void SetBaselineTableOwners(JsonNode baseline, string evidenceReference)
        {
            if (baseline is not JsonObject value) return;
            if (value["landmarks"] is JsonObject landmarks)
            {
                landmarks.Remove("reportReference"); landmarks["evidenceReference"] = evidenceReference;
            }
            if (value["spline"] is JsonObject spline && spline["controlPoints"] is JsonObject controls)
            {
                controls.Remove("reportReference"); controls["evidenceReference"] = evidenceReference;
            }
            if (value["segmented"] is JsonObject segmented && segmented["segments"] is JsonObject segments)
            {
                segments.Remove("reportReference"); segments["evidenceReference"] = evidenceReference;
            }
        }

        static void MoveSourceToRecord(JsonObject member, string evidenceReference)
        {
            foreach (var field in SourceFields)
                if (field != "experimentId" && field != "name") member.Remove(field);
            if (member["injections"] is JsonObject injections)
                foreach (var tableName in SharedInjectionTables) injections.Remove(tableName);
            member["experimentEvidenceRef"] = evidenceReference;
        }

        static JsonObject FindMember(JsonObject root, string reportReference) =>
            AllMembers(root).FirstOrDefault(member => string.Equals(String(member["reportReference"]), reportReference, StringComparison.Ordinal));

        static JsonObject BuildTableSchemas(IReadOnlyCollection<string> extras)
        {
            var schemas = new JsonObject();
            foreach (var table in Tables.Concat(BaselineTables))
            {
                var columns = new JsonArray();
                foreach (var column in table.Columns) columns.Add(column);
                if (table.Name == "acquisition") foreach (var extra in extras) columns.Add(extra);
                var schema = new JsonObject { ["description"] = table.Description, ["columns"] = columns };
                if (table.SchemaName == "integration-v2")
                    schema["columnDefinitions"] = new JsonObject
                    {
                        ["nonIntegratedTimeFraction"] = "1 - integrationLengthSeconds / injectionDelaySeconds. Fraction of the stored nominal injection interval outside integration. Low values prompt checking whether enough signal remains to define the baseline."
                    };
                schemas[table.SchemaName] = schema;
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
                        if (!property.Key.Equals("evidenceId", StringComparison.Ordinal)
                            && !property.Key.Equals("integrationLengthFractionOfInjectionDelay", StringComparison.Ordinal)
                            && !known.Contains(property.Key) && !extras.Contains(property.Key))
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
            // A compact package can be passed through the writer again by an export
            // or transport fallback. Its injection tables are already positional.
            if (experiment["injections"] is JsonObject existing && existing["acquisition"] is JsonObject)
                return;
            var rows = Objects(experiment["injections"]).ToList();
            var tables = new JsonObject();
            foreach (var table in Tables)
            {
                var tableRows = new JsonArray();
                foreach (var source in rows)
                {
                    var row = new JsonArray();
                    foreach (var column in table.Columns)
                        row.Add(column == "nonIntegratedTimeFraction" ? ComplementFraction(source) : Clone(source[column]));
                    if (table.Name == "acquisition") foreach (var extra in extras) row.Add(Clone(source[extra]));
                    tableRows.Add(row);
                }
                tables[table.Name] = new JsonObject { ["schema"] = table.SchemaName, ["reportReference"] = String(experiment["reportReference"]), ["rows"] = tableRows };
            }
            experiment["injections"] = tables;
        }

        static JsonNode ComplementFraction(JsonObject source)
        {
            if (source == null || source["integrationLengthSeconds"] == null || source["injectionDelaySeconds"] == null)
                return null;
            if (!double.TryParse(source["integrationLengthSeconds"]?.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var length)
                || !double.TryParse(source["injectionDelaySeconds"]?.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var delay)
                || delay == 0) return null;
            return JsonValue.Create(1d - length / delay);
        }

        static void RewriteBaselineTables(JsonObject root)
        {
            foreach (var experiment in AllExperiments(root))
            {
                var reference = String(experiment["reportReference"]);
                var baseline = experiment["baseline"] as JsonObject;
                if (baseline == null || string.IsNullOrEmpty(reference)) continue;

                if (baseline["landmarks"] is JsonArray landmarks)
                    baseline["landmarks"] = BuildBaselineTable(BaselineTables[0], reference, landmarks,
                        new HashSet<string>(BaselineTables[0].Columns, StringComparer.Ordinal));

                var spline = baseline["spline"] as JsonObject;
                if (spline?["controlPoints"] is JsonArray controlPoints)
                    spline["controlPoints"] = BuildBaselineTable(BaselineTables[1], reference, controlPoints,
                        new HashSet<string>(BaselineTables[1].Columns, StringComparer.Ordinal),
                        new HashSet<string>(new[] { "locked", "slopeLocked", "linear" }, StringComparer.Ordinal));

                var segmented = baseline["segmented"] as JsonObject;
                if (segmented?["segments"] is JsonArray segments)
                    segmented["segments"] = BuildBaselineTable(BaselineTables[2], reference, segments,
                        new HashSet<string>(BaselineTables[2].Columns, StringComparer.Ordinal));
            }
        }

        static JsonObject BuildBaselineTable(TableDefinition table, string reportReference, JsonArray sourceRows,
            ISet<string> standardColumns, ISet<string> omittedColumns = null)
        {
            var rows = new JsonArray();
            var extensions = new JsonArray();
            omittedColumns ??= new HashSet<string>(StringComparer.Ordinal);
            for (var rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
            {
                var source = sourceRows[rowIndex] as JsonObject;
                var row = new JsonArray();
                foreach (var column in table.Columns) row.Add(Clone(source?[column]));
                rows.Add(row);

                var unknown = new JsonObject();
                if (source != null)
                    foreach (var property in source)
                        if (!standardColumns.Contains(property.Key) && !omittedColumns.Contains(property.Key))
                            unknown[property.Key] = Clone(property.Value);
                if (unknown.Count > 0)
                    extensions.Add(new JsonObject { ["rowIndex"] = rowIndex, ["values"] = unknown });
            }

            var result = new JsonObject
            {
                ["schema"] = table.SchemaName,
                ["reportReference"] = reportReference,
                ["rows"] = rows,
            };
            if (extensions.Count > 0) result["extensions"] = extensions;
            return result;
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
            RoundBaselineTable(value["landmarks"] as JsonObject, BaselineTables[0]);
            var spline = value["spline"] as JsonObject;
            RoundBaselineTable(spline?["controlPoints"] as JsonObject, BaselineTables[1]);
            var segmented = value["segmented"] as JsonObject;
            RoundBaselineTable(segmented?["segments"] as JsonObject, BaselineTables[2]);
            RoundFields(value["polynomial"] as JsonObject, "rejectionZLimit");
            RoundFields(value["asymmetricLeastSquares"] as JsonObject, "lambda", "asymmetry");
        }

        static void RoundBaselineTable(JsonObject tableObject, TableDefinition table)
        {
            var rows = tableObject?["rows"] as JsonArray;
            if (rows == null) return;
            foreach (var row in rows.OfType<JsonArray>())
                for (var i = 0; i < table.Columns.Length && i < row.Count; i++)
                {
                    var column = table.Columns[i];
                    if (column == "scope" || column == "injectionId" || column == "userDefined") continue;
                    if (column == "coefficientsSi" && row[i] is JsonArray coefficients)
                        for (var coefficient = 0; coefficient < coefficients.Count; coefficient++) RoundNumber(coefficients, coefficient, 9);
                    else RoundNumber(row, i, Precision(column));
                }
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
        static IEnumerable<JsonObject> AllMembers(JsonObject root) { foreach (var result in Objects(root["results"])) foreach (var experiment in Objects(result["experiments"])) yield return experiment; foreach (var experiment in Objects(root["supportingExperiments"])) yield return experiment; }

        sealed class NumericField { public readonly string Name; public readonly double Original; public readonly double Rounded; public NumericField(string name, double original, double rounded) { Name = name; Original = original; Rounded = rounded; } }
        sealed class TableDefinition { public readonly string Name, SchemaName, Description; public readonly string[] Columns; public TableDefinition(string name, string schemaName, string description, params string[] columns) { Name = name; SchemaName = schemaName; Description = description; Columns = columns; } }
        sealed class SourceGroup
        {
            public readonly string Reference;
            public readonly List<string> ReportReferences = new List<string>();
            public SourceGroup(string reference) { Reference = reference; }
        }
    }
}
