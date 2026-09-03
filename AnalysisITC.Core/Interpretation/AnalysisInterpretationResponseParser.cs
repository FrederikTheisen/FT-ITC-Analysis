using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AnalysisITC.Core.Interpretation
{
    public sealed class AnalysisInterpretationValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }
        public AnalysisInterpretationValidationException(IEnumerable<string> errors)
            : base("The interpretation response is invalid: " + string.Join("; ", errors ?? Enumerable.Empty<string>()))
        {
            Errors = (errors ?? Enumerable.Empty<string>()).ToList();
        }
    }

    public static class AnalysisInterpretationResponseParser
    {
        static readonly HashSet<string> RootProperties = Set(
            "overallInterpretation", "fitQualityObservations", "parameterObservations",
            "experimentComments", "limitations", "suggestedChecks", "suggestedInvestigations", "missingInformation");
        static readonly HashSet<string> OverallProperties = Set("interaction", "studyQuestion", "expectedOutcome", "buffer", "temperature", "other");
        static readonly HashSet<string> StatementProperties = Set(
            "text", "kind", "confidence", "knowledgeBasis", "requiresExternalVerification", "evidenceIds",
            "experimentEvidenceId", "parameterEvidenceId");
        static readonly HashSet<string> RecommendationProperties = Set(
            "title", "rationale", "intendedQuestion", "priority", "evidenceIds", "knowledgeBasis", "requiresExternalVerification");
        static readonly string[] RequiredStatementProperties =
            { "text", "kind", "confidence", "knowledgeBasis", "requiresExternalVerification", "evidenceIds" };
        static readonly string[] RequiredRecommendationProperties =
            { "title", "rationale", "intendedQuestion", "priority", "evidenceIds", "knowledgeBasis", "requiresExternalVerification" };

        public static AnalysisInterpretationDocument Parse(string json, AnalysisInterpretationPackage package)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new AnalysisInterpretationValidationException(new[] { "Response JSON is empty." });
            if (package == null) throw new ArgumentNullException(nameof(package));
            var errors = new List<string>();
            try
            {
                using var parsed = JsonDocument.Parse(json);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                    errors.Add("The response root must be an object.");
                else
                {
                    ValidateShape(parsed.RootElement, errors);
                    ValidateRequestedSections(parsed.RootElement, package, errors);
                }
            }
            catch (JsonException ex)
            {
                throw new AnalysisInterpretationValidationException(new[] { "Malformed JSON: " + ex.Message });
            }
            if (errors.Count > 0) throw new AnalysisInterpretationValidationException(errors);

            AnalysisInterpretationDocument document;
            try
            {
                document = JsonSerializer.Deserialize<AnalysisInterpretationDocument>(json, AnalysisInterpretationPromptBuilder.CanonicalJsonOptions);
            }
            catch (JsonException ex)
            {
                throw new AnalysisInterpretationValidationException(new[] { "Invalid value or enum: " + ex.Message });
            }
            if (document == null) throw new AnalysisInterpretationValidationException(new[] { "Response document is empty." });
            ValidateScience(document, package, errors);
            if (errors.Count > 0) throw new AnalysisInterpretationValidationException(errors);
            return document;
        }

        static void ValidateShape(JsonElement root, List<string> errors)
        {
            Unknown(root, RootProperties, "response", errors);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "overallInterpretation")
                {
                    if (property.Value.ValueKind != JsonValueKind.Object) { errors.Add("overallInterpretation must be an object."); continue; }
                    Unknown(property.Value, OverallProperties, property.Name, errors);
                    foreach (var subsection in property.Value.EnumerateObject()) ValidateArray(subsection.Value, StatementProperties, RequiredStatementProperties, subsection.Name, errors);
                }
                else if (property.Name == "suggestedChecks" || property.Name == "suggestedInvestigations")
                    ValidateArray(property.Value, RecommendationProperties, RequiredRecommendationProperties, property.Name, errors);
                else ValidateArray(property.Value, StatementProperties, RequiredStatementProperties, property.Name, errors);
            }
        }

        static void ValidateArray(JsonElement value, HashSet<string> allowed, IEnumerable<string> required, string path, List<string> errors)
        {
            if (value.ValueKind != JsonValueKind.Array) { errors.Add(path + " must be an array."); return; }
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) errors.Add($"{path}[{index}] must be an object.");
                else
                {
                    Unknown(item, allowed, $"{path}[{index}]", errors);
                    foreach (var name in required)
                        if (!item.TryGetProperty(name, out _)) errors.Add($"Missing required property {path}[{index}].{name}.");
                    if (item.TryGetProperty("evidenceIds", out var evidenceIds) && evidenceIds.ValueKind != JsonValueKind.Array)
                        errors.Add($"{path}[{index}].evidenceIds must be an array.");
                    if (item.TryGetProperty("requiresExternalVerification", out var verification)
                        && verification.ValueKind != JsonValueKind.True && verification.ValueKind != JsonValueKind.False)
                        errors.Add($"{path}[{index}].requiresExternalVerification must be Boolean.");
                }
                index++;
            }
        }

        static void Unknown(JsonElement value, HashSet<string> allowed, string path, List<string> errors)
        {
            foreach (var property in value.EnumerateObject())
                if (!allowed.Contains(property.Name)) errors.Add($"Unknown property {path}.{property.Name}.");
        }

        static void ValidateScience(AnalysisInterpretationDocument document, AnalysisInterpretationPackage package, List<string> errors)
        {
            var evidence = package.EvidenceCatalog.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var statement in document.AllStatements())
            {
                if (statement == null) { errors.Add("Null interpretation statement."); continue; }
                RequiredPlain(statement.Text, "statement text", errors);
                ValidateKnowledge(statement.KnowledgeBasis, statement.RequiresExternalVerification, "statement", errors);
                ValidateEvidence(statement.EvidenceIds, evidence, "statement", errors);
                if ((statement.KnowledgeBasis == InterpretationKnowledgeBasis.ExperimentalData
                    || statement.KnowledgeBasis == InterpretationKnowledgeBasis.UserContext
                    || statement.KnowledgeBasis == InterpretationKnowledgeBasis.Mixed)
                    && (statement.EvidenceIds == null || statement.EvidenceIds.Count == 0))
                    errors.Add("A data-dependent statement must cite at least one evidence ID.");
                if (statement.KnowledgeBasis == InterpretationKnowledgeBasis.GeneralKnowledge
                    && statement.Kind != InterpretationStatementKind.Hypothesis)
                    errors.Add("A statement based only on general knowledge must be labelled as a hypothesis.");
                ValidateScope(statement, evidence, errors);
            }
            foreach (var recommendation in (document.SuggestedChecks ?? new List<AnalysisInterpretationRecommendation>())
                .Concat(document.SuggestedInvestigations ?? new List<AnalysisInterpretationRecommendation>()))
            {
                if (recommendation == null) { errors.Add("Null recommendation."); continue; }
                RequiredPlain(recommendation.Title, "recommendation title", errors);
                RequiredPlain(recommendation.Rationale, "recommendation rationale", errors);
                RequiredPlain(recommendation.IntendedQuestion, "recommendation intended question", errors);
                ValidateKnowledge(recommendation.KnowledgeBasis, recommendation.RequiresExternalVerification, "recommendation", errors);
                ValidateEvidence(recommendation.EvidenceIds, evidence, "recommendation", errors);
                if ((recommendation.KnowledgeBasis == InterpretationKnowledgeBasis.ExperimentalData
                    || recommendation.KnowledgeBasis == InterpretationKnowledgeBasis.UserContext
                    || recommendation.KnowledgeBasis == InterpretationKnowledgeBasis.Mixed)
                    && (recommendation.EvidenceIds == null || recommendation.EvidenceIds.Count == 0))
                    errors.Add("A data-dependent recommendation must cite at least one evidence ID.");
            }
        }

        static void ValidateRequestedSections(JsonElement root, AnalysisInterpretationPackage package, List<string> errors)
        {
            var requested = new HashSet<AnalysisInterpretationSection>(
                package.RequestedInterpretation?.RequestedSections
                    ?? Enum.GetValues(typeof(AnalysisInterpretationSection)).Cast<AnalysisInterpretationSection>());
            foreach (var property in root.EnumerateObject())
            {
                if (TrySection(property.Name, out var section) && !requested.Contains(section))
                    errors.Add("The response includes unrequested section " + property.Name + ".");
            }
        }

        static bool TrySection(string name, out AnalysisInterpretationSection section)
        {
            switch (name)
            {
                case "overallInterpretation": section = AnalysisInterpretationSection.OverallInterpretation; return true;
                case "fitQualityObservations": section = AnalysisInterpretationSection.FitQualityObservations; return true;
                case "parameterObservations": section = AnalysisInterpretationSection.ParameterObservations; return true;
                case "experimentComments": section = AnalysisInterpretationSection.ExperimentComments; return true;
                case "limitations": section = AnalysisInterpretationSection.Limitations; return true;
                case "suggestedChecks": section = AnalysisInterpretationSection.SuggestedChecks; return true;
                case "suggestedInvestigations": section = AnalysisInterpretationSection.SuggestedInvestigations; return true;
                case "missingInformation": section = AnalysisInterpretationSection.MissingInformation; return true;
                default: section = default(AnalysisInterpretationSection); return false;
            }
        }

        static void ValidateScope(AnalysisInterpretationStatement statement, IReadOnlyDictionary<string, InterpretationEvidenceCatalogEntry> evidence, List<string> errors)
        {
            InterpretationEvidenceCatalogEntry experiment = null;
            InterpretationEvidenceCatalogEntry parameter = null;
            if (!string.IsNullOrWhiteSpace(statement.ExperimentEvidenceId))
            {
                if (!evidence.TryGetValue(statement.ExperimentEvidenceId, out experiment) || experiment.Kind != "experiment")
                    errors.Add("experimentEvidenceId does not identify an experiment.");
                if (!(statement.EvidenceIds ?? new List<string>()).Contains(statement.ExperimentEvidenceId))
                    errors.Add("experimentEvidenceId must also occur in evidenceIds.");
            }
            if (!string.IsNullOrWhiteSpace(statement.ParameterEvidenceId))
            {
                if (!evidence.TryGetValue(statement.ParameterEvidenceId, out parameter) || parameter.Kind != "parameter")
                    errors.Add("parameterEvidenceId does not identify a parameter.");
                if (!(statement.EvidenceIds ?? new List<string>()).Contains(statement.ParameterEvidenceId))
                    errors.Add("parameterEvidenceId must also occur in evidenceIds.");
            }
            if (experiment != null && parameter != null && parameter.ParentId != experiment.Id)
                errors.Add("Parameter and experiment scopes do not match.");
        }

        static void ValidateKnowledge(InterpretationKnowledgeBasis basis, bool verify, string label, List<string> errors)
        {
            if ((basis == InterpretationKnowledgeBasis.GeneralKnowledge || basis == InterpretationKnowledgeBasis.Mixed) && !verify)
                errors.Add(label + " based on general knowledge must require external verification.");
        }

        static void ValidateEvidence(IEnumerable<string> ids, IReadOnlyDictionary<string, InterpretationEvidenceCatalogEntry> evidence, string label, List<string> errors)
        {
            foreach (var id in ids ?? Enumerable.Empty<string>())
                if (string.IsNullOrWhiteSpace(id) || !evidence.ContainsKey(id)) errors.Add(label + " cites unknown evidence ID '" + (id ?? "") + "'.");
        }

        static void RequiredPlain(string value, string label, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value)) { errors.Add(label + " is required."); return; }
            var lines = value.Replace("\r", "").Split('\n').Select(line => line.TrimStart()).ToList();
            if (value.IndexOf('<') >= 0 || value.IndexOf('>') >= 0 || value.Contains("`")
                || value.Contains("**") || value.Contains("](" )
                || lines.Any(line => line.StartsWith("#", StringComparison.Ordinal)
                    || line.StartsWith("- ", StringComparison.Ordinal)
                    || line.StartsWith("* ", StringComparison.Ordinal)
                    || line.StartsWith("> ", StringComparison.Ordinal)
                    || System.Text.RegularExpressions.Regex.IsMatch(line, "^[0-9]+\\.\\s")))
                errors.Add(label + " must be plain text without Markdown or HTML.");
        }

        static HashSet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);
    }
}
