using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalysisITC.Core.Interpretation
{
    public sealed class AnalysisInterpretationPrompt
    {
        public string PromptVersion { get; internal set; }
        public string SystemInstructions { get; internal set; }
        public string UserMessage { get; internal set; }
        public string OutputJsonSchema { get; internal set; }
        public string OutputSchemaVersion { get; internal set; }
        public string CanonicalPackageJson { get; internal set; }
        public string InputFingerprint { get; internal set; }
    }

    public static class AnalysisInterpretationPromptBuilder
    {
        public const string PromptVersion = "itc-interpretation-1.0";
        public const string OutputSchemaVersion = "itc-interpretation-output-1.0";

        internal static readonly JsonSerializerOptions CanonicalJsonOptions = CreateJsonOptions();

        public static AnalysisInterpretationPrompt Build(AnalysisInterpretationPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (package.PackageSchemaVersion != AnalysisInterpretationPackageBuilder.PackageSchemaVersion)
                throw new NotSupportedException("Unsupported interpretation package schema: " + package.PackageSchemaVersion);

            var canonical = JsonSerializer.Serialize(package, CanonicalJsonOptions);
            var system = BuildSystemInstructions(package);
            var schema = BuildOutputSchema();
            var user = "Interpret the supplied FT-ITC analysis package. Use only requested sections, omit unsupported subsections, " +
                "and return JSON conforming exactly to the output schema.\n\nPACKAGE_JSON\n" + canonical;
            return new AnalysisInterpretationPrompt
            {
                PromptVersion = PromptVersion,
                OutputSchemaVersion = OutputSchemaVersion,
                SystemInstructions = system,
                UserMessage = user,
                OutputJsonSchema = schema,
                CanonicalPackageJson = canonical,
                InputFingerprint = Sha256(PromptVersion + "\n" + OutputSchemaVersion + "\n" + system + "\n" + canonical),
            };
        }

        static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
                WriteIndented = false,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            return options;
        }

        static string BuildSystemInstructions(AnalysisInterpretationPackage package)
        {
            var model = package.Result?.Model?.Type ?? "unknown";
            var guidance = model switch
            {
                "one-set-of-sites" => "For a one-set-of-sites model, assess whether one class of equivalent independent sites is a scientifically adequate description.",
                "two-sets-of-sites" => "For a two-sets-of-sites model, consider identifiability, parameter exchange, and whether two site classes are supported rather than merely accommodated.",
                "sequential-binding-sites" => "For a sequential model, treat fitted steps as ordered macroscopic steps and assess identifiability across steps.",
                "competitive-binding" => "For a competitive model, account for supplied competitor concentration, affinity, enthalpy, and pre-equilibration assumptions.",
                "dissociation" => "For a dissociation model, interpret the injected preformed complex and dissociation-axis assumptions explicitly.",
                _ => "Do not infer model behavior that is not described by the package.",
            };
            var advanced = package.Result?.AdvancedAnalyses?.Count > 0
                ? "Completed advanced analyses may be interpreted only from their supplied evidence; do not treat availability as proof of a mechanism."
                : "No completed advanced-analysis evidence was supplied; do not infer advanced-analysis results.";
            var knowledge = package.RequestedInterpretation?.AllowGeneralModelKnowledge == true
                ? "General scientific/model knowledge may be used only as a hypothesis or recommendation, must use knowledgeBasis generalKnowledge or mixed, and must set requiresExternalVerification=true."
                : "Do not use general knowledge beyond definitions needed to read the supplied evidence.";

            return "You are assisting with scientific interpretation of a saved isothermal titration calorimetry analysis. " +
                "The original-data best-fit values are the reported estimates. Bootstrap and profile calculations describe uncertainty; their means or medians must never replace the reported estimate. " +
                "Weighted fitting status describes the optimization objective, while the supplied RMSD is explicitly unweighted; never conflate them. " +
                "Separate observations, interpretations, and hypotheses. Data-dependent claims must cite supplied evidence IDs. " +
                "Omit unsupported subsections and use missingInformation to request context needed for a sound interpretation. " +
                "The package excludes raw thermogram samples and baseline arrays, so never claim to observe thermogram shape, peaks, drift, baseline quality, or integration traces. " +
                knowledge + " Never invent, complete, or claim to verify literature citations; user-provided references may only be identified as supplied context. " +
                guidance + " " + advanced + " Return JSON only: no Markdown, HTML, prose wrapper, or extra keys.";
        }

        static string BuildOutputSchema()
        {
            object StringSchema() => new Dictionary<string, object> { ["type"] = "string" };
            object EnumSchema(params string[] values) => new Dictionary<string, object>
            {
                ["type"] = "string", ["enum"] = values,
            };
            object ArrayOf(string reference) => new Dictionary<string, object>
            {
                ["type"] = "array", ["items"] = new Dictionary<string, object> { ["$ref"] = reference },
            };
            var statement = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new[] { "text", "kind", "confidence", "knowledgeBasis", "requiresExternalVerification", "evidenceIds" },
                ["properties"] = new Dictionary<string, object>
                {
                    ["text"] = StringSchema(),
                    ["kind"] = EnumSchema("observation", "interpretation", "hypothesis"),
                    ["confidence"] = EnumSchema("high", "moderate", "low", "notAssessed"),
                    ["knowledgeBasis"] = EnumSchema("experimentalData", "userContext", "generalKnowledge", "mixed"),
                    ["requiresExternalVerification"] = new Dictionary<string, object> { ["type"] = "boolean" },
                    ["evidenceIds"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = StringSchema() },
                    ["experimentEvidenceId"] = StringSchema(),
                    ["parameterEvidenceId"] = StringSchema(),
                },
            };
            var recommendation = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new[] { "title", "rationale", "intendedQuestion", "priority", "evidenceIds", "knowledgeBasis", "requiresExternalVerification" },
                ["properties"] = new Dictionary<string, object>
                {
                    ["title"] = StringSchema(), ["rationale"] = StringSchema(), ["intendedQuestion"] = StringSchema(),
                    ["priority"] = EnumSchema("high", "medium", "low"),
                    ["evidenceIds"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = StringSchema() },
                    ["knowledgeBasis"] = EnumSchema("experimentalData", "userContext", "generalKnowledge", "mixed"),
                    ["requiresExternalVerification"] = new Dictionary<string, object> { ["type"] = "boolean" },
                },
            };
            var overallProperties = new Dictionary<string, object>();
            foreach (var name in new[] { "interaction", "studyQuestion", "expectedOutcome", "buffer", "temperature", "other" })
                overallProperties[name] = ArrayOf("#/$defs/statement");
            var schema = new Dictionary<string, object>
            {
                ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
                ["$id"] = OutputSchemaVersion,
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new Dictionary<string, object>
                {
                    ["overallInterpretation"] = new Dictionary<string, object>
                    {
                        ["type"] = "object", ["additionalProperties"] = false, ["properties"] = overallProperties,
                    },
                    ["fitQualityObservations"] = ArrayOf("#/$defs/statement"),
                    ["parameterObservations"] = ArrayOf("#/$defs/statement"),
                    ["experimentComments"] = ArrayOf("#/$defs/statement"),
                    ["limitations"] = ArrayOf("#/$defs/statement"),
                    ["suggestedChecks"] = ArrayOf("#/$defs/recommendation"),
                    ["suggestedInvestigations"] = ArrayOf("#/$defs/recommendation"),
                    ["missingInformation"] = ArrayOf("#/$defs/statement"),
                },
                ["$defs"] = new Dictionary<string, object> { ["statement"] = statement, ["recommendation"] = recommendation },
            };
            return JsonSerializer.Serialize(schema, CanonicalJsonOptions);
        }

        static string Sha256(string value)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2")));
        }
    }
}
