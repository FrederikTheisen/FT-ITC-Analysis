using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisReportBuilder
    {
        const double SupportingFigureWidthCentimeters = 5.0;
        const double SupportingFigureHeightCentimeters = 7.7;
        const double FinalFigureWidthCentimeters = 6.0;
        const double FinalFigureHeightCentimeters = 10.0;
        const double CoverFigureHeightCentimeters = 10.5;

        public static AnalysisReportDocument Build(
            AnalysisResult result,
            AnalysisReportOptions options = null)
        {
            return Build(new[] { result }, options);
        }

        static AnalysisReportDocument BuildResultChapter(
            AnalysisResult result,
            AnalysisReportOptions options,
            int resultIndex,
            IReadOnlyDictionary<string, string> previousExperimentLabels = null)
        {
            options = ApplyTraceabilityPolicy((options ?? new AnalysisReportOptions()).Copy());
            if (options.ExperimentReferenceLabels == null)
                options.ExperimentReferenceLabels = ExperimentReferenceLabels(new[] { result });
            if (options.EnergyUnitOverride.HasValue)
                EnergyUnitResolver.ValidateOverride(options.EnergyUnitOverride.Value);

            var document = CreateReportDocument(new[] { result }, options, resultIndex);
            var validation = Validate(result, options.OutputPurpose);
            foreach (var diagnostic in validation.Diagnostics)
                document.AddDiagnostic(diagnostic.Severity, diagnostic.Code, diagnostic.Message);
            if (!validation.IsValid) return document;

            if (result.IsIndependentAssessmentCollection
                && !ResultOutputPolicy.IsCombinedBindingOutputAllowed(result, options.OutputPurpose)
                && options.OutputPurpose == ResultOutputPurpose.Standard)
            {
                BuildIndependentMixedOutput(document, result, options, resultIndex, previousExperimentLabels);
                return document;
            }

            if (ResultOutputPolicy.SuppressBindingOutputs(result, options.OutputPurpose))
            {
                BuildNoBindingSections(document, result, options, resultIndex);
                return document;
            }

            var overview = AnalysisResultOverviewTable.Build(
                result,
                options.EnergyUnitFamily,
                options.EnergyUnitOverride,
                options.UseKelvin,
                options.UncertaintyDisplayStyle);
            var members = result.Solution.Solutions
                .Where(solution => solution?.Data != null)
                .ToList();
            var labels = members
                .Select((_, index) => AnalysisReportReferenceLabels.Experiment(resultIndex, index))
                .ToList();

            BuildCover(document, result, members, labels, options, resultIndex);
            BuildSummary(document, result, overview, labels, options);
            BuildExperimentSections(document, result, members, labels, overview, options,
                previousExperimentLabels);
            BuildAdvancedSections(document, result, options);
            AddResultDiagnostics(document, result);

            return document;
        }

        public static AnalysisReportDocument Build(
            IReadOnlyList<AnalysisResult> results,
            AnalysisReportOptions options = null)
        {
            return InReportCulture(() =>
            {
                var effective = ApplyTraceabilityPolicy((options ?? new AnalysisReportOptions()).Copy());
                var document = Build(results, effective, false);
                AddReportAppendix(document, results, null, effective);
                return document;
            });
        }

        // Report text always uses dot decimals, independent of the calling thread's culture.
        static AnalysisReportDocument InReportCulture(Func<AnalysisReportDocument> build)
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try { return build(); }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        static AnalysisReportDocument Build(
            IReadOnlyList<AnalysisResult> results,
            AnalysisReportOptions options,
            bool includeInterpretationEntry)
        {
            options = ApplyTraceabilityPolicy((options ?? new AnalysisReportOptions()).Copy());
            if (options.ExperimentReferenceLabels == null)
                options.ExperimentReferenceLabels = ExperimentReferenceLabels(results);
            if (options.EnergyUnitOverride.HasValue)
                EnergyUnitResolver.ValidateOverride(options.EnergyUnitOverride.Value);

            var selected = (results ?? Array.Empty<AnalysisResult>()).ToList();
            var document = CreateReportDocument(selected, options);
            var validation = Validate(selected, options.OutputPurpose);
            foreach (var diagnostic in validation.Diagnostics)
                document.AddDiagnostic(diagnostic.Severity, diagnostic.Code, diagnostic.Message);
            if (!validation.IsValid) return document;

            BuildFrontPage(document, selected, includeInterpretationEntry, options);
            var reportAdvancedKinds = new HashSet<AnalysisReportAdvancedSectionKind>(selected
                .SelectMany(GetAvailableAdvancedSections)
                .Select(descriptor => descriptor.Request.Kind));
            var previousExperimentLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < selected.Count; index++)
            {
                var childOptions = CopyOptionsForResult(options, selected[index], reportAdvancedKinds);
                var child = BuildResultChapter(selected[index], childOptions, index,
                    previousExperimentLabels);
                foreach (var diagnostic in child.Diagnostics)
                    document.AddDiagnostic(diagnostic.Severity,
                        "result-" + (index + 1).ToString(CultureInfo.InvariantCulture) + "-" + diagnostic.Code,
                        selected[index].Name + ": " + diagnostic.Message);
                foreach (var section in child.Sections)
                    document.AddSection(CloneResultSection(section, index, selected[index]));
                var members = selected[index].Solution?.Solutions ?? new List<SolutionInterface>();
                for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
                {
                    var id = members[memberIndex]?.Data?.UniqueID;
                    if (!string.IsNullOrWhiteSpace(id) && !previousExperimentLabels.ContainsKey(id))
                        previousExperimentLabels[id] = AnalysisReportReferenceLabels.Experiment(index, memberIndex);
                }
            }
            return document;
        }

        public static AnalysisReportDocument Build(
            AnalysisITC.Core.Data.AnalysisReport report,
            Func<string, AnalysisResult> resultResolver,
            AnalysisReportOptions options = null)
        {
            return Build(report, resultResolver, _ => null, options);
        }

        public static AnalysisReportDocument Build(
            AnalysisITC.Core.Data.AnalysisReport report,
            Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver,
            AnalysisReportOptions options = null)
        {
            return InReportCulture(() => BuildReport(report, resultResolver, experimentResolver, options));
        }

        static AnalysisReportDocument BuildReport(
            AnalysisITC.Core.Data.AnalysisReport report,
            Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver,
            AnalysisReportOptions options)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (resultResolver == null) throw new ArgumentNullException(nameof(resultResolver));
            if (experimentResolver == null) throw new ArgumentNullException(nameof(experimentResolver));
            var useSavedSettings = options == null;
            options = ApplyTraceabilityPolicy((options ?? report.PresentationSettings).Copy());
            options.ExperimentResolver = experimentResolver;
            if (useSavedSettings)
            {
                options.Author = AppSettings.UserName ?? "";
                options.GeneratedAtUtc = DateTime.UtcNow;
                options.ApplicationVersion = AppVersion.FullVersionString;
            }
            if (string.IsNullOrWhiteSpace(options.Title)) options.Title = report.Name;

            var results = new List<AnalysisResult>();
            var unresolvedIds = new List<string>();
            var duplicateIds = report.ResultIds
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            foreach (var id in report.ResultIds)
            {
                var resolved = resultResolver(id);
                if (resolved != null) results.Add(resolved);
                else unresolvedIds.Add(id);
            }
            var supporting = new List<ExperimentData>();
            var unresolvedExperimentIds = new List<string>();
            var duplicateExperimentIds = report.SupportingExperimentIds
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            foreach (var id in report.SupportingExperimentIds)
            {
                var resolved = experimentResolver(id);
                if (resolved != null) supporting.Add(resolved);
                else unresolvedExperimentIds.Add(id);
            }
            var memberIds = new HashSet<string>(results
                .Where(result => result?.Solution?.Solutions != null)
                .SelectMany(result => result.Solution.Solutions)
                .Select(solution => solution?.Data?.UniqueID)
                .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
            var effectiveSupporting = supporting
                .Where(experiment => !memberIds.Contains(experiment.UniqueID))
                .ToList();
            options.ExperimentReferenceLabels = ExperimentReferenceLabels(results, effectiveSupporting);
            var hasInterpretation = !string.IsNullOrWhiteSpace(
                report.ApprovedInterpretation?.InterpretationMarkdown);
            var document = report.ResultIds.Count == results.Count && duplicateIds.Count == 0
                ? Build(results, options, hasInterpretation)
                : CreateReportDocument(results, options);
            var cover = document.Sections.FirstOrDefault(item => item.Kind == AnalysisReportSectionKind.Cover);
            if (cover != null && !string.IsNullOrWhiteSpace(report.AuthorComments))
                cover.Add(new AnalysisReportTextBlock("Report comments", report.AuthorComments,
                    AnalysisReportLayoutPolicy.KeepTogether));
            foreach (var duplicateId in duplicateIds)
                document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Error, "duplicate-report-result",
                    "The report references result ID '" + duplicateId + "' more than once.");
            foreach (var id in unresolvedIds)
                document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Error, "unresolved-report-result",
                    "The report's referenced analysis result '" + id + "' is missing or unresolved.");
            foreach (var duplicateId in duplicateExperimentIds)
                document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Error, "duplicate-supporting-experiment",
                    "The report references supporting experiment ID '" + duplicateId + "' more than once.");
            foreach (var id in unresolvedExperimentIds)
                document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Error, "unresolved-supporting-experiment",
                    "The report's supporting experiment '" + id + "' is missing or unresolved.");
            if (report.ResultIds.Count == 0)
                document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Error, "missing-report-results",
                    "The report does not reference any analysis results.");

            if (hasInterpretation)
            {
                report.SetInterpretationFreshness(AnalysisInterpretationService.EvaluateFreshness(report, resultResolver, experimentResolver));
                var section = BuildInterpretationSection(report, results);
                var contentsIndex = document.Sections.ToList().FindIndex(item =>
                    item.Blocks.Any(block => block is AnalysisReportTableOfContentsBlock));
                document.InsertSection(contentsIndex < 0 ? 1 : contentsIndex + 1, section);
            }
            AddSupportingExperiments(document, results, effectiveSupporting, options);
            AddReportAppendix(document, results, effectiveSupporting, options);
            return document;
        }

        static void AddCoverTraceability(
            AnalysisReportSection section,
            AnalysisReportDocument document,
            AnalysisReportOptions options)
        {
            // Preparer, generation time and signature describe the whole report, so they belong on the front page only.
            if (!options.ExtraTraceability || !options.IncludeCoverSignature) return;
            var author = string.IsNullOrWhiteSpace(document.Author) ? "Not recorded" : document.Author;
            section.Add(new AnalysisReportKeyValueBlock("Report preparation", new[]
            {
                Item("Prepared by", author),
                Item("Generated at", document.ExportDateText),
            }));
            section.Add(new AnalysisReportTextBlock("Signature", "Report ID: " + ReportIdText(document)
                + "\nSignature for " + author + ": ____________________    Date: ____________________",
                AnalysisReportLayoutPolicy.KeepTogether));
        }

        static AnalysisReportOptions ApplyTraceabilityPolicy(AnalysisReportOptions options)
        {
            options.ExtraTraceability = options.ExtraTraceability || AppSettings.TraceabilityModeEnabled;
            return options;
        }

        static string ReportIdText(AnalysisReportDocument document) =>
            string.IsNullOrWhiteSpace(document.ReportId) ? "Not recorded" : document.ReportId;

        public static bool NeedsPreparerRefresh(AnalysisReportDocument document)
        {
            if (document == null) return true;
            return !string.Equals(document.Author?.Trim() ?? "", AppSettings.UserName?.Trim() ?? "", StringComparison.Ordinal);
        }

        static AnalysisReportSection BuildInterpretationSection(
            AnalysisITC.Core.Data.AnalysisReport report,
            IReadOnlyList<AnalysisResult> results)
        {
            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.Interpretation,
                "interpretation",
                "Interpretation",
                AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
            var record = report.ApprovedInterpretation;
            var freshness = report.InterpretationFreshness;
            if (record.Origin == AnalysisInterpretationOrigin.AiGenerated
                && freshness != AnalysisInterpretationFreshness.Current)
                section.Add(new AnalysisReportNoticeBlock(
                    freshness == AnalysisInterpretationFreshness.Stale ? "Out-of-date interpretation" : "Interpretation freshness unknown",
                    report.InterpretationFreshnessReason + " The approved text has been retained and should be reviewed before use.",
                    AnalysisReportNoticeLevel.Warning));
            AddMarkdownBlocks(section, record.InterpretationMarkdown,
                record.Origin == AnalysisInterpretationOrigin.AiGenerated);
            AddInterpretationProvenance(section, record);
            return section;
        }

        public static AnalysisReportDocument BuildInterpretationPreview(string markdown, AnalysisInterpretationRecord provenance = null)
        {
            var document = new AnalysisReportDocument { Title = "Interpretation", GeneratedAtUtc = DateTime.UtcNow };
            var section = new AnalysisReportSection(AnalysisReportSectionKind.Interpretation, "interpretation", "Interpretation",
                AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
            AddMarkdownBlocks(section, markdown, true);
            AddInterpretationProvenance(section, provenance ?? new AnalysisInterpretationRecord
            { Provider = "openai", Model = "configured model", ServiceRequestId = new string('0', 32), GeneratedAtUtc = DateTime.UtcNow, ApprovedAtUtc = DateTime.UtcNow });
            document.AddSection(section);
            return document;
        }

        static void AddInterpretationProvenance(AnalysisReportSection section, AnalysisInterpretationRecord record)
        {
            var provenance = record.Origin == AnalysisInterpretationOrigin.Manual
                ? $"Interpretation written by the user; saved: {FormatUtc(record.ApprovedAtUtc)}."
                : "Automatically generated interpretation" + (record.UserEdited ? ", user edited" : ", not marked as user-edited") +
                    $". Provider: {Empty(record.Provider)}; model: {Empty(record.Model)}; reasoning: {Empty(record.ReasoningEffort)}; " +
                    $"scientific guidance: {FormatGuidance(record.ScientificGuidanceRevision)}; generated: {FormatUtc(record.GeneratedAtUtc)}; " +
                    $"approved: {FormatUtc(record.ApprovedAtUtc)}; request: {Empty(record.ServiceRequestId)}.";
            section.Add(new AnalysisReportNoticeBlock("Source and editing history", provenance, AnalysisReportNoticeLevel.Information));
        }

        static string FormatGuidance(string revision) =>
            string.Equals(revision, "none", StringComparison.OrdinalIgnoreCase)
                ? "none (minimal evidence boundary only)"
                : Empty(revision);

        static void AddMarkdownBlocks(AnalysisReportSection section, string markdown, bool validateAiResponse)
        {
            var normalized = validateAiResponse
                ? AnalysisInterpretationResponseParser.Parse(markdown)
                : AnalysisInterpretationResponseParser.ParseManual(markdown);
            var lines = normalized.Split('\n');
            var paragraph = new List<string>();
            var bullets = new List<string>();
            void FlushParagraph()
            {
                if (paragraph.Count == 0) return;
                section.Add(new AnalysisReportTextBlock("", string.Join(" ", paragraph),
                    AnalysisReportLayoutPolicy.KeepTogether, inlineMarkdown: true));
                paragraph.Clear();
            }
            void FlushBullets()
            {
                if (bullets.Count == 0) return;
                section.Add(new AnalysisReportTextBlock("", string.Join("\n", bullets.Select(item => "• " + item)),
                    AnalysisReportLayoutPolicy.KeepTogether, inlineMarkdown: true));
                bullets.Clear();
            }
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (AnalysisReportMarkdownTable.TryRead(lines, index, out var table, out var end))
                {
                    FlushParagraph(); FlushBullets();
                    section.Add(table);
                    index = end;
                    continue;
                }
                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    FlushParagraph(); FlushBullets();
                    section.Add(new AnalysisReportHeadingBlock(line.Substring(4), 3));
                }
                else if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    FlushParagraph(); FlushBullets();
                    section.Add(new AnalysisReportHeadingBlock(line.Substring(3), 2));
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    FlushParagraph(); bullets.Add(line.Substring(2));
                }
                else if (line.Length == 0) { FlushParagraph(); FlushBullets(); }
                else { FlushBullets(); paragraph.Add(line); }
            }
            FlushParagraph(); FlushBullets();
        }

        static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "not supplied" : value;
        static string FormatUtc(DateTime value) => value == default(DateTime)
            ? "not supplied"
            : (value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime()).ToString("u", CultureInfo.InvariantCulture);

        public static IReadOnlyList<AnalysisReportAdvancedSectionDescriptor> GetAvailableAdvancedSections(
            AnalysisResult result)
        {
            var output = new List<AnalysisReportAdvancedSectionDescriptor>();
            if (result?.Solution?.Solutions == null) return output;

            if (CanBuildTemperaturePlot(result))
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.TemperatureDependence,
                    "Temperature dependence",
                    "Thermodynamic parameters and their saved temperature dependences."));
            }

            if (result.SpolarRecordAnalysis?.Result != null)
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.SpolarRecord,
                    "Spolar Record",
                    "Saved hydration, conformational, and residue estimates."));
            }

            if (CanBuildAffinitySaltPlot(result))
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.AffinityVersusSalt,
                    "Affinity versus salt",
                    "Reported affinity as a function of salt concentration."));
            }

            if (result.ElectrostaticsAnalysis?.Calculated == true
                && result.ElectrostaticsAnalysis.IonicStrengthDependenceFit != null)
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.DebyeHuckel,
                    "Debye-Huckel dependence",
                    "Saved ionic-strength dependence and fitted curve."));
            }

            if (result.ElectrostaticsAnalysis?.Calculated == true
                && result.ElectrostaticsAnalysis.CounterIonReleaseFit != null)
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.CounterIonRelease,
                    "Counter-ion release",
                    "Saved counter-ion release dependence and fitted line."));
            }

            if (result.ProtonationAnalysis?.Fit is LinearFitWithError)
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.Protonation,
                    "Protonation dependence",
                    "Saved buffer-protonation dependence and fitted line."));
            }

            AddCorrelationDescriptors(output, result);
            return output;
        }

        static AnalysisReportDocument CreateReportDocument(
            IReadOnlyList<AnalysisResult> results,
            AnalysisReportOptions options,
            int firstResultIndex = 0)
        {
            var selected = results ?? Array.Empty<AnalysisResult>();
            var defaultTitle = selected.Count == 1 && selected[0] != null ? selected[0].Name ?? "" : "Analysis report";
            var document = new AnalysisReportDocument
            {
                DocumentLabel = options.DocumentLabel?.Trim() ?? "",
                Title = string.IsNullOrWhiteSpace(options.Title) ? defaultTitle : options.Title.Trim(),
                GeneratedAtUtc = options.GeneratedAtUtc.Kind == DateTimeKind.Utc
                    ? options.GeneratedAtUtc
                    : options.GeneratedAtUtc.ToUniversalTime(),
                ResultHealth = AggregateHealth(selected),
                Creator = MarkdownStrings.AppName,
                Author = options.Author?.Trim() ?? "",
                ReportId = options.ReportId ?? "",
                ApplicationVersion = options.ApplicationVersion ?? "",
            };
            foreach (var item in selected.Select((result, index) => new { result, index })
                .Where(item => item.result != null))
                document.AddResult(ResultReference(item.result, firstResultIndex + item.index));
            return document;
        }

        static AnalysisReportResultReference ResultReference(AnalysisResult result, int resultIndex) =>
            new AnalysisReportResultReference(
                result.UniqueID, AnalysisReportReferenceLabels.Result(resultIndex),
                result.Name, result.Date, ModelName(result),
                result.Solution?.Solutions?.Count(solution => solution?.Data != null) ?? 0,
                result.Health);

        static AnalysisResultHealth AggregateHealth(IEnumerable<AnalysisResult> results)
        {
            var health = AnalysisResultHealth.Valid;
            foreach (var result in results ?? Enumerable.Empty<AnalysisResult>())
            {
                if (result == null) return AnalysisResultHealth.Invalid;
                if (HealthRank(result.Health) > HealthRank(health)) health = result.Health;
            }
            return health;
        }

        static int HealthRank(AnalysisResultHealth health) => health switch
        {
            AnalysisResultHealth.Invalid => 4,
            AnalysisResultHealth.PartialInvalid => 3,
            AnalysisResultHealth.Warning => 2,
            AnalysisResultHealth.Unknown => 1,
            _ => 0,
        };

        static void BuildFrontPage(
            AnalysisReportDocument document,
            IReadOnlyList<AnalysisResult> results,
            bool includeInterpretation,
            AnalysisReportOptions options)
        {
            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.Cover, "cover", document.Title,
                AnalysisReportLayoutPolicy.KeepTogether | AnalysisReportLayoutPolicy.ShrinkToSinglePage);
            if (!string.IsNullOrWhiteSpace(document.DocumentLabel))
                section.Add(new AnalysisReportTextBlock("", document.DocumentLabel,
                    AnalysisReportLayoutPolicy.KeepTogether));
            AddCoverTraceability(section, document, options);
            section.Add(new AnalysisReportKeyValueBlock("Report scope", new[]
            {
                Item("Analysis results", results.Count.ToString(CultureInfo.CurrentCulture)),
                Item("Distinct result experiments", CountDistinctExperiments(results)
                    .ToString(CultureInfo.CurrentCulture)),
            }));
            section.Add(new AnalysisReportTableBlock("Included results", new[]
            {
                new AnalysisReportTableColumn("result", "Result"),
                new AnalysisReportTableColumn("model", "Model"),
                new AnalysisReportTableColumn("created", "Analysis date"),
                new AnalysisReportTableColumn("experiments", "Experiments"),
                new AnalysisReportTableColumn("status", "Status"),
                new AnalysisReportTableColumn("assessment", "Binding assessment"),
            }, results.Select((result, index) => new AnalysisReportTableRow(new[]
            {
                AnalysisReportReferenceLabels.Result(index) + ". " + result.Name,
                ResultOutputPolicy.FormatModelName(result, ModelName(result), options.OutputPurpose),
                FormatDate(result.Date),
                result.Solution.Solutions.Count(solution => solution?.Data != null).ToString(CultureInfo.CurrentCulture),
                HealthText(result.Health),
                NullModelComparisonPresentation.OutcomeText(result.CollectionAssessmentOutcome)
                    + (result.BindingAssessment?.IsManual == true ? " (manual)" : ""),
            })), AnalysisReportLayoutPolicy.AllowContinuation, 7.5, 2));
            section.Add(new AnalysisReportTableOfContentsBlock("Contents",
                TableOfContentsEntries(results, includeInterpretation)));
            var reportSnapshots = results.SelectMany(result => result?.ValiditySnapshot?.Experiments
                ?? new List<ExperimentFitInputSnapshot>()).ToList();
            if (HasMixedBookkeeping(reportSnapshots))
                section.Add(new AnalysisReportNoticeBlock("Bookkeeping conventions",
                    "This report contains results using different bookkeeping conventions.",
                    AnalysisReportNoticeLevel.Warning));
            document.AddSection(section);
        }

        public static int CountDistinctExperiments(IEnumerable<AnalysisResult> results)
        {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var unidentified = new HashSet<ExperimentData>();
            foreach (var data in (results ?? Enumerable.Empty<AnalysisResult>())
                .Where(result => result?.Solution?.Solutions != null)
                .SelectMany(result => result.Solution.Solutions)
                .Select(solution => solution?.Data)
                .Where(data => data != null))
            {
                if (!string.IsNullOrWhiteSpace(data.UniqueID)) identifiers.Add(data.UniqueID);
                else unidentified.Add(data);
            }
            return identifiers.Count + unidentified.Count;
        }

        public static bool HasRepeatedExperiments(IEnumerable<AnalysisResult> results)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in (results ?? Enumerable.Empty<AnalysisResult>())
                .Where(result => result?.Solution?.Solutions != null)
                .SelectMany(result => result.Solution.Solutions)
                .Select(solution => solution?.Data?.UniqueID)
                .Where(id => !string.IsNullOrWhiteSpace(id)))
                if (!seen.Add(id)) return true;
            return false;
        }

        static IReadOnlyList<AnalysisReportTableOfContentsEntry> TableOfContentsEntries(
            IReadOnlyList<AnalysisResult> results,
            bool includeInterpretation)
        {
            var entries = new List<AnalysisReportTableOfContentsEntry>();
            if (includeInterpretation)
                entries.Add(new AnalysisReportTableOfContentsEntry("Interpretation", "interpretation"));
            entries.AddRange(results.Select((result, index) =>
                new AnalysisReportTableOfContentsEntry(
                    "Result " + (index + 1).ToString(CultureInfo.CurrentCulture) + ". " + result.Name,
                    "result-" + (index + 1).ToString(CultureInfo.InvariantCulture) + "-overview")));
            return entries;
        }

        static AnalysisReportOptions CopyOptionsForResult(
            AnalysisReportOptions options,
            AnalysisResult result,
            ISet<AnalysisReportAdvancedSectionKind> reportAdvancedKinds)
        {
            var copy = new AnalysisReportOptions
            {
                DocumentLabel = "",
                Title = result.Name,
                EnergyUnitFamily = options.EnergyUnitFamily,
                EnergyUnitOverride = options.EnergyUnitOverride,
                UseKelvin = options.UseKelvin,
                IncludeInjectionTables = options.IncludeInjectionTables,
                CondenseRepeatedExperiments = options.CondenseRepeatedExperiments,
                ExpandedExplanations = options.ExpandedExplanations,
                UncertaintyDisplayStyle = options.UncertaintyDisplayStyle,
                GeneratedAtUtc = options.GeneratedAtUtc,
                ApplicationVersion = options.ApplicationVersion,
                ExtraTraceability = options.ExtraTraceability,
                IncludeCoverSignature = false,
                Author = options.Author,
                ReportId = options.ReportId,
                ExperimentReferenceLabels = options.ExperimentReferenceLabels,
                ExperimentResolver = options.ExperimentResolver,
                OutputPurpose = options.OutputPurpose,
            };
            var availableKinds = new HashSet<AnalysisReportAdvancedSectionKind>(
                GetAvailableAdvancedSections(result)
                    .Select(descriptor => descriptor.Request.Kind));
            // A section available for another result is omitted silently; one that no result can provide is passed
            // through so the chapter reports it as omitted.
            foreach (var request in options.AdvancedSections
                .Where(request => request != null
                    && (availableKinds.Contains(request.Kind) || !reportAdvancedKinds.Contains(request.Kind))))
                copy.AdvancedSections.Add(new AnalysisReportAdvancedSectionRequest(
                    request.Kind, request.CorrelationMemberIndex));
            return copy;
        }

        static AnalysisReportSection CloneResultSection(
            AnalysisReportSection source,
            int resultIndex,
            AnalysisResult result)
        {
            var ordinal = resultIndex + 1;
            var prefix = "result-" + ordinal.ToString(CultureInfo.InvariantCulture) + "-";
            var isOverview = source.Kind == AnalysisReportSectionKind.Cover;
            var section = new AnalysisReportSection(
                isOverview ? AnalysisReportSectionKind.ResultOverview : source.Kind,
                prefix + (isOverview ? "overview" : source.Id),
                isOverview
                    ? "Result " + ordinal.ToString(CultureInfo.CurrentCulture) + ". " + result.Name
                    : source.Title,
                source.Layout | (isOverview ? AnalysisReportLayoutPolicy.StartOnNewPage : AnalysisReportLayoutPolicy.None),
                result.Name,
                isOverview ? result.Health : (AnalysisResultHealth?)null);
            foreach (var block in source.Blocks) section.Add(block);
            return section;
        }

        static string HealthText(AnalysisResultHealth health) => health switch
        {
            AnalysisResultHealth.Valid => "Valid",
            AnalysisResultHealth.Warning => "Warnings",
            AnalysisResultHealth.PartialInvalid => "Partial / stale",
            AnalysisResultHealth.Invalid => "Invalid / stale",
            _ => "Unknown",
        };

        public static AnalysisReportValidationResult Validate(AnalysisResult result,
            ResultOutputPurpose purpose = ResultOutputPurpose.Standard)
        {
            var diagnostics = new List<AnalysisReportDiagnostic>();
            if (result == null)
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "missing-result", "No saved analysis result was supplied."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            if (result.Solution?.Model == null)
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "missing-solution", "The saved analysis result has no usable fitted model."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            if (result.Solution.Solutions == null
                || !result.Solution.Solutions.Any(solution => solution?.Data != null))
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "missing-members", "The saved analysis result contains no usable experiment fits."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            if (!result.IsIndependentAssessmentCollection
                && ResultOutputPolicy.SuppressBindingOutputs(result, purpose))
                return new AnalysisReportValidationResult(diagnostics);

            var reportValues = result.Solution.Solutions
                .Where(solution => solution?.Data != null)
                .Where(solution => ResultOutputPolicy.IsMemberBindingOutputAllowed(result, solution, purpose))
                .SelectMany(solution => solution.ReportParameters?.Values
                    ?? Enumerable.Empty<FloatWithError>())
                .ToList();
            if (reportValues.Count == 0)
            {
                if (result.IsIndependentAssessmentCollection
                    && !result.Solution.Solutions.Any(solution => solution?.Data != null
                        && ResultOutputPolicy.IsMemberBindingOutputAllowed(result, solution, purpose)))
                    return new AnalysisReportValidationResult(diagnostics);
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                        "missing-parameters", "The saved analysis result contains no reportable fitted parameters."));
                return new AnalysisReportValidationResult(diagnostics);
            }
            if (reportValues.Any(value => FloatWithError.IsNaN(value) || !IsFinite(value.Value)))
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "non-finite-parameters", "The saved analysis result contains non-finite reported parameter values."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            return new AnalysisReportValidationResult(diagnostics);
        }

        static void BuildIndependentMixedOutput(AnalysisReportDocument document, AnalysisResult result,
            AnalysisReportOptions options, int resultIndex,
            IReadOnlyDictionary<string, string> previousExperimentLabels)
        {
            var overview = AnalysisResultOverviewTable.Build(result, options.EnergyUnitFamily,
                options.EnergyUnitOverride, options.UseKelvin, options.UncertaintyDisplayStyle);
            var members = (result.Solution?.Solutions ?? new List<SolutionInterface>())
                .Where(solution => solution?.Data != null).ToList();
            var labels = members.Select((_, index) => AnalysisReportReferenceLabels.Experiment(resultIndex, index)).ToList();
            BuildCover(document, result, members, labels, options, resultIndex);

            var summary = new AnalysisReportSection(AnalysisReportSectionKind.AnalysisSummary,
                "analysis-summary", "Analysis summary",
                AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
            AddBookkeepingNotice(summary, result, labels);
            document.AddSection(summary);

            for (var index = 0; index < members.Count; index++)
            {
                if (ResultOutputPolicy.IsMemberBindingOutputAllowed(result, members[index], options.OutputPurpose))
                    BuildExperimentSections(document, result, members, labels, overview, options,
                        previousExperimentLabels, selectedMemberIndexes: new[] { index });
                else
                    BuildIndependentSuppressedMemberSections(document, result, options, index, members, labels);
            }
            AddResultDiagnostics(document, result);
        }

        static void BuildNoBindingSections(AnalysisReportDocument document, AnalysisResult result,
            AnalysisReportOptions options, int resultIndex)
        {
            var members = (result.Solution?.Solutions ?? new List<SolutionInterface>())
                .Where(solution => solution?.Data != null).ToList();
            var labels = members.Select((_, index) => AnalysisReportReferenceLabels.Experiment(resultIndex, index)).ToList();
            BuildCover(document, result, members, labels, options, resultIndex);

            var comparison = result.NullComparison;
            var section = new AnalysisReportSection(AnalysisReportSectionKind.AnalysisSummary,
                "analysis-summary", "Analysis summary",
                AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
            var assessment = result.BindingAssessment;
            var labelsById = members.Select((member, index) => (member.Data.UniqueID, Label: labels[index]))
                .Where(item => !string.IsNullOrWhiteSpace(item.UniqueID))
                .GroupBy(item => item.UniqueID, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);
            section.Add(new AnalysisReportKeyValueBlock("Binding assessment", new[]
            {
                Item("Conclusion", NullModelComparisonPresentation.OutcomeText(assessment?.EffectiveOutcome
                    ?? BindingAssessmentOutcome.NotAssessed)),
                Item(ResultOutputPolicy.SuppressBindingOutputs(result, options.OutputPurpose)
                    ? "Attempted binding model" : "Binding model", ModelName(result)),
                Item("Null model", NullModelComparisonPresentation.NullModel(comparison)),
                Item("Null fit", FormatNullFit(result, comparison, labelsById, options)),
                Item("ΔAICc", FormatDeltaAicc(comparison)),
            }));
            AddBookkeepingNotice(section, result, labels);
            document.AddSection(section);

            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                var label = AnalysisReportReferenceLabels.Experiment(resultIndex, index);
                var memberSection = new AnalysisReportSection(AnalysisReportSectionKind.Experiment,
                    "experiment-" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    label + ". " + member.Data.Name,
                    AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
                var figureOptions = FinalFigureOptions(options);
                figureOptions.ShowFitParameters = false;
                figureOptions.ShowConfidenceBand = false;
                var figure = PublicationFigureBuilder.Build(
                    new PublicationFigureSource(member.Data, member, result, options.OutputPurpose), figureOptions);
                memberSection.Add(new AnalysisReportKeyValueBlock("Experiment details",
                    BuildExperimentMetadata(member.Data, options, result)));
                memberSection.Add(new AnalysisReportKeyValueBlock("Processing and integration",
                    BuildProcessingItems(member.Data, result)));
                memberSection.Add(new AnalysisReportFigureBlock("Offset comparison evidence", label,
                    figure, AnalysisReportLayoutPolicy.KeepTogether));
                if (!string.IsNullOrWhiteSpace(member.Data.Comments))
                    memberSection.Add(new AnalysisReportTextBlock("Comments", member.Data.Comments,
                        AnalysisReportLayoutPolicy.AllowContinuation));
                if (options.IncludeInjectionTables)
                    memberSection.Add(BuildNullEvidenceTable(result, member, options));
                if (!member.Data.HasThermogram)
                    memberSection.Add(new AnalysisReportNoticeBlock("Raw processing unavailable",
                        "This saved experiment contains integrated heats without a raw thermogram.",
                        AnalysisReportNoticeLevel.Information));
                document.AddSection(memberSection);
            }
            AddResultDiagnostics(document, result);
        }

        static void BuildIndependentSuppressedMemberSections(AnalysisReportDocument document,
            AnalysisResult result, AnalysisReportOptions options, int memberIndex,
            IReadOnlyList<SolutionInterface> members, IReadOnlyList<string> labels)
        {
                var index = memberIndex;
                var member = members[index];
                if (ResultOutputPolicy.IsMemberBindingOutputAllowed(result, member, options.OutputPurpose)) return;
                var assessment = result.GetMemberBindingAssessment(member);
                var comparison = result.GetMemberNullComparison(member);
                var outcome = assessment?.EffectiveOutcome ?? BindingAssessmentOutcome.NotAssessed;
                var label = labels[index];
                var section = new AnalysisReportSection(AnalysisReportSectionKind.Experiment,
                    "experiment-" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    label + ". " + member.Data.Name,
                    AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
                section.Add(new AnalysisReportKeyValueBlock("Binding assessment", new[]
                {
                    Item("Conclusion", NullModelComparisonPresentation.OutcomeText(outcome)),
                    Item("ΔAICc", FormatDeltaAicc(comparison)),
                }));
                var figureOptions = FinalFigureOptions(options);
                figureOptions.ShowFitParameters = false;
                figureOptions.ShowConfidenceBand = false;
                var figure = PublicationFigureBuilder.Build(new PublicationFigureSource(member.Data, member, result,
                    options.OutputPurpose), figureOptions);
                section.Add(new AnalysisReportKeyValueBlock("Experiment details",
                    BuildExperimentMetadata(member.Data, options, result)));
                section.Add(new AnalysisReportKeyValueBlock("Processing and integration",
                    BuildProcessingItems(member.Data, result)));
                section.Add(new AnalysisReportFigureBlock("Offset comparison evidence", label, figure,
                    AnalysisReportLayoutPolicy.KeepTogether));
                if (!string.IsNullOrWhiteSpace(member.Data.Comments))
                    section.Add(new AnalysisReportTextBlock("Comments", member.Data.Comments,
                        AnalysisReportLayoutPolicy.AllowContinuation));
                if (options.IncludeInjectionTables)
                    section.Add(BuildNullEvidenceTable(result, member, options));
                if (!member.Data.HasThermogram)
                    section.Add(new AnalysisReportNoticeBlock("Raw processing unavailable",
                        "This saved experiment contains integrated heats without a raw thermogram.",
                        AnalysisReportNoticeLevel.Information));
                document.AddSection(section);
        }

        static string FormatNullFit(AnalysisResult result, NullModelComparison comparison,
            IReadOnlyDictionary<string, string> labelsById, AnalysisReportOptions options)
        {
            var offsets = FormatNullOffsets(result, comparison, options.EnergyUnitFamily, options.EnergyUnitOverride, labelsById);
            var rmsd = NullModelComparisonPresentation.NullRmsd(comparison, options.EnergyUnitFamily);
            if (rmsd == "Unavailable") return offsets;
            var rmsdText = "RMSD " + rmsd + " " + ThermogramUnits.IntegratedHeatUnit(options.EnergyUnitFamily);
            return offsets == "Unavailable" ? rmsdText : offsets + "; " + rmsdText;
        }

        static string FormatDeltaAicc(NullModelComparison comparison)
        {
            var delta = NullModelComparisonPresentation.Delta(comparison);
            if (comparison?.DeltaAicc is not double value || delta == "Unavailable" || delta == "Not calculated")
                return delta;
            var binding = comparison.BindingInformationCriteria;
            var nullCriteria = comparison.NullInformationCriteria;
            var text = (value > 0 ? "+" : string.Empty) + value.ToString("F1", CultureInfo.CurrentCulture);
            if (binding?.IsAiccAvailable != true || nullCriteria?.IsAiccAvailable != true) return text;
            return text + " (binding " + binding.Aicc.Value.ToString("F1", CultureInfo.CurrentCulture)
                + ", null " + nullCriteria.Aicc.Value.ToString("F1", CultureInfo.CurrentCulture) + ")";
        }

        static string FormatNullOffsets(AnalysisResult result, NullModelComparison comparison, EnergyUnitFamily family,
            EnergyUnit? unitOverride, IReadOnlyDictionary<string, string> labelsById = null)
        {
            if (comparison?.NullFitSucceeded != true || comparison.Members == null || comparison.Members.Count == 0)
                return "Unavailable";
            var members = comparison.Members.Where(member => !double.IsNaN(member.Offset)
                && !double.IsInfinity(member.Offset)).ToList();
            if (members.Count == 0) return "Unavailable";
            var unit = EnergyUnitResolver.Resolve(family, unitOverride, members.Select(member => member.Offset));
            return string.Join("; ", members.Select(member =>
            {
                var value = new Energy(member.Offset).ToString(unit, "G6", withunit: true, permole: true);
                if (labelsById != null && member.ExperimentId != null
                    && labelsById.TryGetValue(member.ExperimentId, out var label))
                    return label + ": " + value;
                var name = result?.Solution?.Solutions?.FirstOrDefault(solution =>
                    solution?.Data?.UniqueID == member.ExperimentId)?.Data?.Name ?? member.ExperimentId;
                return name + " (" + (member.Scope ?? "local") + "): " + value;
            }));
        }

        static AnalysisReportTableBlock BuildNullEvidenceTable(AnalysisResult result,
            SolutionInterface solution, AnalysisReportOptions options)
        {
            var data = solution.Data;
            var comparison = result?.GetMemberNullComparison(solution);
            var member = comparison?.Members?.FirstOrDefault(item =>
                string.Equals(item.ExperimentId, data?.UniqueID, StringComparison.Ordinal));
            var savedPoints = (member?.Points ?? new List<NullModelComparisonPoint>())
                .Where(point => point.InjectionMass > 0 && IsFinite(point.ObservedHeatJoules))
                .OrderBy(point => point.InjectionId).ToList();
            var saved = savedPoints.Count > 0;
            var showPrediction = saved && comparison?.NullFitSucceeded == true
                && savedPoints.Any(point => IsFinite(point.PredictedHeatJoules));
            var unit = ThermogramUnits.IntegratedHeatUnit(options.EnergyUnitFamily);
            var scale = ThermogramUnits.IntegratedHeatScale(options.EnergyUnitFamily);
            var rows = saved
                ? savedPoints.Select(point => new AnalysisReportTableRow(new[]
                {
                    (point.InjectionId + 1).ToString(CultureInfo.CurrentCulture),
                    point.Included ? "Included" : "Excluded",
                    FormatFinite(point.Ratio, "G6"),
                    FormatFinite(point.InjectionMass, "G6"),
                    FormatFinite(point.ObservedHeatJoules * scale, "G6"),
                    showPrediction && IsFinite(point.PredictedHeatJoules)
                        ? FormatFinite(point.PredictedHeatJoules * scale, "G6") : "",
                    showPrediction && IsFinite(point.PredictedHeatJoules)
                        ? FormatFinite((point.ObservedHeatJoules - point.PredictedHeatJoules) * scale, "G6") : "",
                })).ToList()
                : data.Injections.Where(injection => injection.IsIntegrated).Select(injection =>
                    new AnalysisReportTableRow(new[]
                    {
                        (injection.ID + 1).ToString(CultureInfo.CurrentCulture),
                        injection.Include ? "Included" : "Excluded",
                        FormatFinite(injection.Ratio, "G6"),
                        FormatFinite(injection.InjectionMass, "G6"),
                        FormatFinite(injection.PeakArea.Value * scale, "G6"), "", "",
                    })).ToList();
            return new AnalysisReportTableBlock(
                saved ? "Saved null comparison injection evidence" : "Injection table — Current experiment data",
                new[]
                {
                    new AnalysisReportTableColumn("injection", "Injection"),
                    new AnalysisReportTableColumn("included", "Use"),
                    new AnalysisReportTableColumn("ratio", "Saved ratio"),
                    new AnalysisReportTableColumn("amount", "Injected amount (mol)"),
                    new AnalysisReportTableColumn("observed", "Observed heat (" + unit + ")"),
                    new AnalysisReportTableColumn("prediction", "Offset prediction (" + unit + ")"),
                    new AnalysisReportTableColumn("residual", "Residual (" + unit + ")"),
                }, rows, AnalysisReportLayoutPolicy.AllowContinuation,
                fontSize: 7.5, verticalCellPadding: 1.5);
        }

        static string ComparisonReasonForReport(NullModelComparison comparison)
        {
            if (comparison == null) return NullModelComparisonPresentation.ComparisonReason(null);
            if (!comparison.BindingFitSucceeded)
                return string.IsNullOrWhiteSpace(comparison.BindingFitReason)
                    ? "The binding model fit failed." : comparison.BindingFitReason;
            if (!comparison.NullFitSucceeded)
                return NullModelComparisonPresentation.NullFitReason(comparison);
            if (comparison.DeltaAicc.HasValue
                && !double.IsNaN(comparison.DeltaAicc.Value) && !double.IsInfinity(comparison.DeltaAicc.Value))
                return "Saved binding and Offset comparison.";
            return NullModelComparisonPresentation.ComparisonReason(comparison);
        }

        public static AnalysisReportValidationResult Validate(IReadOnlyList<AnalysisResult> results,
            ResultOutputPurpose purpose = ResultOutputPurpose.Standard)
        {
            var diagnostics = new List<AnalysisReportDiagnostic>();
            if (results == null || results.Count == 0)
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "missing-results", "Select at least one saved analysis result."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            var duplicateIds = results.Where(result => result != null && !string.IsNullOrWhiteSpace(result.UniqueID))
                .GroupBy(result => result.UniqueID, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);
            foreach (var group in duplicateIds)
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "duplicate-result", "Analysis result '" + group.First().Name + "' is selected more than once."));

            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                var name = result == null || string.IsNullOrWhiteSpace(result.Name)
                    ? "Result " + (index + 1).ToString(CultureInfo.CurrentCulture)
                    : result.Name;
                foreach (var diagnostic in Validate(result, purpose).Diagnostics)
                    diagnostics.Add(new AnalysisReportDiagnostic(diagnostic.Severity,
                        "result-" + (index + 1).ToString(CultureInfo.InvariantCulture) + "-" + diagnostic.Code,
                        name + ": " + diagnostic.Message));
            }
            return new AnalysisReportValidationResult(diagnostics);
        }

        public static AnalysisReportValidationResult Validate(
            AnalysisITC.Core.Data.AnalysisReport report,
            Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver,
            ResultOutputPurpose purpose = ResultOutputPurpose.Standard)
        {
            if (report == null)
                return new AnalysisReportValidationResult(new[]
                {
                    new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                        "missing-report", "No analysis report definition was supplied.")
                });
            if (resultResolver == null) throw new ArgumentNullException(nameof(resultResolver));
            if (experimentResolver == null) throw new ArgumentNullException(nameof(experimentResolver));

            var diagnostics = new List<AnalysisReportDiagnostic>();
            var results = report.ResultIds.Select(resultResolver).ToList();
            diagnostics.AddRange(Validate(results, purpose).Diagnostics);
            foreach (var group in report.SupportingExperimentIds.GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "duplicate-supporting-experiment",
                    "Supporting experiment '" + group.Key + "' is referenced more than once."));
            foreach (var id in report.SupportingExperimentIds.Distinct(StringComparer.Ordinal))
                if (experimentResolver(id) == null)
                    diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                        "unresolved-supporting-experiment",
                        "Supporting experiment '" + id + "' is missing or unresolved."));
            return new AnalysisReportValidationResult(diagnostics);
        }

        static void BuildCover(
            AnalysisReportDocument document,
            AnalysisResult result,
            IReadOnlyList<SolutionInterface> members,
            IReadOnlyList<string> labels,
            AnalysisReportOptions options,
            int resultIndex)
        {
            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.Cover,
                "cover",
                document.Title,
                AnalysisReportLayoutPolicy.KeepTogether
                    | AnalysisReportLayoutPolicy.ShrinkToSinglePage);

            if (!string.IsNullOrWhiteSpace(document.DocumentLabel))
                section.Add(new AnalysisReportTextBlock("", document.DocumentLabel,
                    AnalysisReportLayoutPolicy.KeepTogether));
            AddCoverTraceability(section, document, options);

            var analysisItems = new List<AnalysisReportKeyValueItem>
            {
                Item("Analysis date", FormatDate(result.Date)),
                Item(ResultOutputPolicy.SuppressBindingOutputs(result, options.OutputPurpose)
                    ? "Attempted binding model" : "Model", ModelName(result)),
                Item("Experiments", members.Count.ToString(CultureInfo.CurrentCulture)),
            };
            // The assessment is shown only where it changes the output: standard output omits binding results,
            // or diagnostic output shows binding results that standard output would omit.
            if (ResultOutputPolicy.SuppressBindingOutputs(result))
                analysisItems.Insert(2, Item("Binding assessment", NullModelComparisonPresentation.OutcomeText(
                    result.CollectionAssessmentOutcome)));
            if (options.ExtraTraceability)
                analysisItems.Insert(1, Item("Analysis operator", string.IsNullOrWhiteSpace(result.OperatorName) ? "Not recorded" : result.OperatorName));
            if (!string.Equals(document.Title, result.Name, StringComparison.Ordinal))
                analysisItems.Insert(0, Item("Result", result.Name));
            section.Add(new AnalysisReportKeyValueBlock("Analysis", analysisItems));

            if (result.Health != AnalysisResultHealth.Valid || result.ValidityReport?.Reasons?.Count > 0)
                AddValidityNotice(section, result);
            if (!string.IsNullOrWhiteSpace(result.Comments))
                section.Add(new AnalysisReportTextBlock("Comments", result.Comments,
                    AnalysisReportLayoutPolicy.KeepTogether));

            var canvasOptions = CoverCanvasOptions(members.Count, resultIndex);
            var figureOptions = SupportingFigureOptions(options, canvasOptions);
            var canvas = PublicationFigureCanvasBuilder.Build(
                new ITCDataContainer[] { result }, figureOptions, canvasOptions, options.OutputPurpose);
            section.Add(new AnalysisReportFigureCanvasBlock("Experiment overview", canvas));

            document.AddSection(section);
        }

        static void BuildSummary(
            AnalysisReportDocument document,
            AnalysisResult result,
            AnalysisResultOverviewTable overview,
            IReadOnlyList<string> labels,
            AnalysisReportOptions options)
        {
            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.AnalysisSummary,
                "analysis-summary",
                "Analysis summary",
                AnalysisReportLayoutPolicy.StartOnNewPage
                    | AnalysisReportLayoutPolicy.AllowContinuation);

            var evaluationTemperature = AnalysisResultParameterEvaluator
                .DefaultEvaluationTemperatureCelsius(result);
            var evaluation = AnalysisResultParameterEvaluator.Evaluate(
                result,
                evaluationTemperature,
                options.EnergyUnitFamily,
                options.EnergyUnitOverride,
                options.UncertaintyDisplayStyle);
            if (options.OutputPurpose == ResultOutputPurpose.Diagnostic)
            {
                var assessmentItems = new List<AnalysisReportKeyValueItem>
                {
                    Item("Conclusion", NullModelComparisonPresentation.OutcomeText(
                        result.CollectionAssessmentOutcome)),
                };
                if (result.IsIndependentAssessmentCollection)
                {
                    var counts = result.MemberAssessments
                        .GroupBy(member => member.Assessment?.EffectiveOutcome ?? BindingAssessmentOutcome.NotAssessed)
                        .OrderBy(group => group.Key);
                    foreach (var group in counts)
                        assessmentItems.Add(Item(NullModelComparisonPresentation.OutcomeText(group.Key),
                            group.Count().ToString(CultureInfo.CurrentCulture)));
                }
                else
                {
                    var comparison = result.NullComparison;
                    assessmentItems.Add(Item("Null model", NullModelComparisonPresentation.NullModel(comparison)));
                    assessmentItems.Add(Item("Null fit", NullModelComparisonPresentation.NullStatus(comparison)));
                    assessmentItems.Add(Item("Null offsets", FormatNullOffsets(result, comparison,
                        options.EnergyUnitFamily, options.EnergyUnitOverride)));
                    assessmentItems.Add(Item("Null RMSD", NullModelComparisonPresentation.NullRmsd(comparison,
                        options.EnergyUnitFamily) + " " + ThermogramUnits.IntegratedHeatUnit(options.EnergyUnitFamily)));
                    assessmentItems.Add(Item("Binding AICc", NullModelComparisonPresentation.Aicc(comparison?.BindingInformationCriteria)));
                    assessmentItems.Add(Item("Null AICc", NullModelComparisonPresentation.Aicc(comparison?.NullInformationCriteria)));
                    assessmentItems.Add(Item("ΔAICc", NullModelComparisonPresentation.Delta(comparison)));
                    assessmentItems.Add(Item("Comparison reason", ComparisonReasonForReport(comparison)));
                }
                section.Add(new AnalysisReportKeyValueBlock("Binding assessment and null comparison", assessmentItems));
                if (result.IsIndependentAssessmentCollection)
                    AddPooledComparisonDiagnostics(section, result, options);
            }
            var summaryPlot = BuildThermodynamicSummaryPlot(result, labels, options);
            if (summaryPlot != null) section.Add(summaryPlot);

            section.Add(OverviewTableBlock(overview, result, options, labels));
            AddBookkeepingNotice(section, result, labels);
            var subtractionSummary = SavedSubtractionSummary(result, labels, options);
            if (!string.IsNullOrWhiteSpace(subtractionSummary))
                section.Add(new AnalysisReportKeyValueBlock("Buffer subtraction",
                    new[] { Item("Saved fit", subtractionSummary) }));
            if (result.IsTemperatureDependenceEnabled && evaluation.IsAvailable)
            {
                section.Add(new AnalysisReportKeyValueBlock(
                    "Reported parameters at " + FormatTemperature(evaluationTemperature, options.UseKelvin),
                    evaluation.Rows.Select(row => Item(row.Label, row.Value))));
                AddSummaryUncertaintyExplanation(section, evaluation, options);
            }

            section.Add(new AnalysisReportKeyValueBlock("Model", BuildModelItems(result)));
            var fixedItems = BuildFixedParameterItems(result, null, overview, options);
            if (fixedItems.Count > 0)
                section.Add(new AnalysisReportKeyValueBlock("Fixed parameters", fixedItems));
            section.Add(new AnalysisReportKeyValueBlock("Fit details", BuildFitDiagnosticItems(result)));
            if (options.ExpandedExplanations && result.Solution.UseWeightedFitting)
                section.Add(new AnalysisReportNoticeBlock("Reading fit diagnostics",
                    "The fit uses injection uncertainties to weight its residuals. The RMSD shown here is unweighted, so it describes the typical heat residual but is not the score minimized during fitting.",
                    AnalysisReportNoticeLevel.Information));
            if (CorrelationRequested(options, null)) AddCorrelation(section, result, null, options);
            document.AddSection(section);
        }

        static void BuildExperimentSections(
            AnalysisReportDocument document,
            AnalysisResult result,
            IReadOnlyList<SolutionInterface> members,
            IReadOnlyList<string> labels,
            AnalysisResultOverviewTable overview,
            AnalysisReportOptions options,
            IReadOnlyDictionary<string, string> previousExperimentLabels,
            bool skipSuppressedIndependentMembers = false,
            IReadOnlyList<int> selectedMemberIndexes = null)
        {
            foreach (var index in selectedMemberIndexes ?? Enumerable.Range(0, members.Count).ToList())
            {
                if (skipSuppressedIndependentMembers
                    && !ResultOutputPolicy.IsMemberBindingOutputAllowed(result, members[index], options.OutputPurpose))
                    continue;
                var solution = members[index];
                var data = solution.Data;
                var label = labels[index];
                string previousLabel = null;
                var isRepeated = options.CondenseRepeatedExperiments
                    && !string.IsNullOrWhiteSpace(data.UniqueID)
                    && previousExperimentLabels != null
                    && previousExperimentLabels.TryGetValue(data.UniqueID, out previousLabel);
                var section = new AnalysisReportSection(
                    AnalysisReportSectionKind.Experiment,
                    "experiment-" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    label + ". " + data.Name,
                    AnalysisReportLayoutPolicy.StartOnNewPage
                        | AnalysisReportLayoutPolicy.AllowContinuation);

                var fitFigure = PublicationFigureBuilder.Build(
                    new PublicationFigureSource(data, solution, result, options.OutputPurpose),
                    FinalFigureOptions(options));
                if (data.HasThermogram)
                {
                    var processingOptions = FinalFigureOptions(options);
                    processingOptions.PlotWidthCentimeters = 8.5;
                    processingOptions.PlotHeightCentimeters = FinalFigureHeightCentimeters;
                    processingOptions.ShowFitPanel = false;
                    processingOptions.ShowResiduals = false;
                    processingOptions.DrawBaselineCorrected = false;
                    processingOptions.ShowBaseline = true;
                    processingOptions.ShowIntegrationRegions = true;
                    processingOptions.IntegrationRegionStyle = PublicationIntegrationRegionStyle.Line;
                    processingOptions.FocusThermogramOnBaseline = true;
                    processingOptions.ShowExperimentDetails = false;
                    processingOptions.ShowFitParameters = false;
                    var processingFigure = PublicationFigureBuilder.Build(
                        new PublicationFigureSource(data, solution, result, options.OutputPurpose), processingOptions);
                    section.Add(new AnalysisReportFigurePairBlock(
                        "Experiment figures", "Baseline and integration windows", processingFigure,
                        "Final fit", fitFigure));
                }
                else
                {
                    section.Add(new AnalysisReportNoticeBlock(
                        "Raw processing unavailable",
                        "This saved experiment does not contain a raw thermogram.",
                        AnalysisReportNoticeLevel.Information));
                    section.Add(new AnalysisReportFigureBlock(
                        "Final fit", label, fitFigure, AnalysisReportLayoutPolicy.KeepTogether));
                }
                section.Add(new AnalysisReportKeyValueBlock(
                    isRepeated ? "Experiment details — condensed" : "Experiment details",
                    isRepeated
                        ? BuildCondensedExperimentMetadata(data, options, previousLabel, result)
                        : BuildExperimentMetadata(data, options, result),
                    // Continue below the figures instead of moving the whole list to the next page.
                    AnalysisReportLayoutPolicy.AllowContinuation));
                if (!isRepeated)
                    section.Add(new AnalysisReportKeyValueBlock(
                        "Processing and integration", BuildProcessingItems(data, result)));
                section.Add(BuildParameterTable(
                    "Fitted and derived parameters", result, solution, overview, options));
                if (members.Count > 1 && CorrelationRequested(options, index))
                    AddCorrelation(section, result, index, options);
                var fitItems = BuildMemberFitItems(result, solution).ToList();
                if (result.IsIndependentAssessmentCollection)
                    AddMemberAssessmentItems(fitItems, result, solution);
                section.Add(new AnalysisReportKeyValueBlock("Fit details", fitItems));
                if (!string.IsNullOrWhiteSpace(data.Comments))
                    section.Add(new AnalysisReportTextBlock("Comments", data.Comments,
                        AnalysisReportLayoutPolicy.AllowContinuation));
                if (options.IncludeInjectionTables)
                    section.Add(BuildInjectionTable(data, options));

                document.AddSection(section);
            }
        }

        static void AddSupportingExperiments(
            AnalysisReportDocument document,
            IReadOnlyList<AnalysisResult> results,
            IReadOnlyList<ExperimentData> experiments,
            AnalysisReportOptions options)
        {
            if (document == null || experiments == null || experiments.Count == 0) return;
            for (var index = 0; index < experiments.Count; index++)
                document.AddSupportingExperiment(new AnalysisReportSupportingExperimentReference(
                    experiments[index].UniqueID,
                    AnalysisReportReferenceLabels.SupportingExperiment(index),
                    experiments[index].Name));

            var cover = document.Sections.FirstOrDefault(section => section.Kind == AnalysisReportSectionKind.Cover);
            cover?.Add(new AnalysisReportKeyValueBlock("Supporting evidence", new[]
            {
                Item("Supporting experiments", experiments.Count.ToString(CultureInfo.CurrentCulture)),
                Item("Distinct experiments in report", CountDistinctExperiments(results, experiments)
                    .ToString(CultureInfo.CurrentCulture)),
            }));
            var contents = cover?.Blocks.OfType<AnalysisReportTableOfContentsBlock>().FirstOrDefault();
            contents?.AddEntry(new AnalysisReportTableOfContentsEntry(
                "Supporting experiments", "supporting-experiments"));

            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.SupportingData,
                "supporting-experiments",
                "Supporting experiments",
                AnalysisReportLayoutPolicy.StartOnNewPage | AnalysisReportLayoutPolicy.AllowContinuation);
            for (var index = 0; index < experiments.Count; index++)
            {
                var data = experiments[index];
                var label = AnalysisReportReferenceLabels.SupportingExperiment(index);
                section.Add(new AnalysisReportHeadingBlock(label + ". " + data.Name, 2));
                AddSupportingFigures(section, data, options);
                section.Add(new AnalysisReportKeyValueBlock(
                    "Experiment details", BuildSupportingExperimentMetadata(data, options)));
                var processingNotes = BuildSupportingProcessingItems(data, results).ToList();
                if (processingNotes.Count > 0)
                    section.Add(new AnalysisReportKeyValueBlock(
                        "Notes", processingNotes));
                if (data.Solution?.Convergence?.Failed == true)
                {
                    var reason = data.Solution.Convergence.FailureReason;
                    section.Add(new AnalysisReportNoticeBlock("Attached fit unsuccessful",
                        string.IsNullOrWhiteSpace(reason)
                            ? "The attached fit did not complete successfully; fitted parameters are not reported."
                            : reason.Trim() + " Fitted parameters are not reported.",
                        AnalysisReportNoticeLevel.Warning));
                }
                if (!string.IsNullOrWhiteSpace(data.Comments))
                    section.Add(new AnalysisReportTextBlock("Comments", data.Comments,
                        AnalysisReportLayoutPolicy.AllowContinuation));
                if (options.IncludeInjectionTables)
                    section.Add(BuildInjectionTable(data, options));
            }
            document.AddSection(section);
        }

        static void AddSupportingFigures(
            AnalysisReportSection section,
            ExperimentData data,
            AnalysisReportOptions options)
        {
            PublicationFigureDocument processing = null;
            if (data.HasThermogram)
            {
                var processingOptions = FinalFigureOptions(options);
                processingOptions.PlotWidthCentimeters = 8.5;
                processingOptions.ShowFitPanel = false;
                processingOptions.ShowResiduals = false;
                processingOptions.DrawBaselineCorrected = false;
                processingOptions.ShowBaseline = true;
                processingOptions.ShowIntegrationRegions = true;
                processingOptions.IntegrationRegionStyle = PublicationIntegrationRegionStyle.Line;
                processingOptions.FocusThermogramOnBaseline = true;
                processingOptions.ShowExperimentDetails = false;
                processingOptions.ShowFitParameters = false;
                processingOptions.IntegratedInjectionsOnly = true;
                processing = PublicationFigureBuilder.Build(new PublicationFigureSource(data), processingOptions);
            }

            var hasIntegratedHeats = (data.Injections ?? new List<InjectionData>())
                .Any(injection => injection != null && injection.IsIntegrated && IsFinite(injection.Enthalpy));
            PublicationFigureDocument heats = null;
            if (hasIntegratedHeats)
            {
                var heatOptions = FinalFigureOptions(options);
                heatOptions.ShowThermogram = false;
                heatOptions.ShowFitPanel = true;
                heatOptions.ShowResiduals = false;
                heatOptions.ShowFitLine = false;
                heatOptions.ShowConfidenceBand = false;
                heatOptions.DrawFitOffsetCorrected = false;
                heatOptions.ShowExperimentDetails = false;
                heatOptions.ShowFitParameters = false;
                heatOptions.IntegratedInjectionsOnly = true;
                heats = PublicationFigureBuilder.Build(new PublicationFigureSource(data), heatOptions);
            }

            if (processing != null && heats != null)
                section.Add(new AnalysisReportFigurePairBlock("Current experiment data — observations",
                    "Baseline and integration windows", processing,
                    "Integrated heats — no fit", heats));
            else if (processing != null)
                section.Add(new AnalysisReportFigureBlock("Current experiment data — baseline and integration windows", "", processing,
                    AnalysisReportLayoutPolicy.KeepTogether));
            else if (heats != null)
                    section.Add(new AnalysisReportFigureBlock("Integrated heats — Current experiment data; no fit", "", heats,
                    AnalysisReportLayoutPolicy.KeepTogether));
            else
                section.Add(new AnalysisReportNoticeBlock("Experimental plots unavailable",
                    "This experiment contains neither a raw thermogram nor finite saved integrated heats.",
                    AnalysisReportNoticeLevel.Information));

            if (!data.HasThermogram)
                section.Add(new AnalysisReportNoticeBlock("Raw thermogram unavailable",
                    "This saved experiment does not contain a raw thermogram.",
                    AnalysisReportNoticeLevel.Information));
            if (!hasIntegratedHeats)
                section.Add(new AnalysisReportNoticeBlock("Integrated heats unavailable",
                    "No finite saved integrated heats are available; the report did not integrate the experiment.",
                    AnalysisReportNoticeLevel.Information));
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildSupportingExperimentMetadata(
            ExperimentData data,
            AnalysisReportOptions options)
        {
            return BuildExperimentMetadata(data, options);
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildSupportingProcessingItems(
            ExperimentData data,
            IReadOnlyList<AnalysisResult> results)
        {
            var items = new List<AnalysisReportKeyValueItem>();
            var injections = data.Injections ?? new List<InjectionData>();
            var integrated = injections.Count(injection => injection?.IsIntegrated == true);
            if (data.HasThermogram && data.Processor?.BaselineCompleted != true)
                items.Add(Item("Baseline", "Incomplete"));
            if (injections.Count > 0 && integrated != injections.Count)
                items.Add(Item("Integration", integrated.ToString(CultureInfo.CurrentCulture)
                    + " of " + injections.Count.ToString(CultureInfo.CurrentCulture) + " injections integrated"));

            var subtraction = data.BufferSubtractionSettings;
            if (subtraction != null)
            {
                var reference = data.ReferenceExperiment?.Name ?? "Missing reference experiment";
                items.Add(Item("Integrated heats", data.ReferenceExperiment == null
                    ? "Stored corrected heats; configured reference " + reference + " is unavailable ("
                        + subtraction.MethodDisplayName + ")"
                    : "Stored corrected heats; configured reference " + reference + " (" + subtraction.MethodDisplayName + ")"));
            }

            var targets = new List<string>();
            foreach (var (result, resultIndex) in (results ?? Array.Empty<AnalysisResult>()).Select((result, index) => (result, index)))
            {
                var snapshots = result?.ValiditySnapshot?.Experiments;
                if (snapshots == null) continue;
                var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
                for (var memberIndex = 0; memberIndex < solutions.Count; memberIndex++)
                {
                    var experimentId = solutions[memberIndex]?.Data?.UniqueID;
                    var savedReferenceId = snapshots.FirstOrDefault(item => item.ExperimentID == experimentId)
                        ?.Attributes?.FirstOrDefault(attribute => attribute.Key == AttributeKey.BufferSubtraction)?.StringValue;
                    if (savedReferenceId == data.UniqueID)
                        targets.Add(AnalysisReportReferenceLabels.Experiment(resultIndex, memberIndex));
                }
            }
            if (targets.Count > 0)
                items.Add(Item("Used as subtraction reference by", string.Join(", ", targets)));
            return items;
        }

        public static int CountDistinctExperiments(
            IEnumerable<AnalysisResult> results,
            IEnumerable<ExperimentData> supportingExperiments)
        {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var unidentified = new HashSet<ExperimentData>();
            foreach (var data in (results ?? Enumerable.Empty<AnalysisResult>())
                .Where(result => result?.Solution?.Solutions != null)
                .SelectMany(result => result.Solution.Solutions)
                .Select(solution => solution?.Data)
                .Concat(supportingExperiments ?? Enumerable.Empty<ExperimentData>())
                .Where(data => data != null))
            {
                if (!string.IsNullOrWhiteSpace(data.UniqueID)) identifiers.Add(data.UniqueID);
                else unidentified.Add(data);
            }
            return identifiers.Count + unidentified.Count;
        }

        static void BuildAdvancedSections(
            AnalysisReportDocument document,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var available = GetAvailableAdvancedSections(result)
                .ToDictionary(item => item.Request.Key, item => item);
            var requests = (options.AdvancedSections
                    ?? new List<AnalysisReportAdvancedSectionRequest>())
                .Where(request => request != null)
                .GroupBy(request => request.Key)
                .Select(group => group.First())
                .ToList();
            var temperaturePlotAdded = false;

            foreach (var request in requests)
            {
                if (request.Kind == AnalysisReportAdvancedSectionKind.Correlation) continue;
                if (!available.TryGetValue(request.Key, out var descriptor))
                {
                    var message = UnavailableAdvancedMessage(result, request);
                    document.AddDiagnostic(
                        AnalysisReportDiagnosticSeverity.Warning,
                        "advanced-section-omitted",
                        message);
                    continue;
                }

                var section = new AnalysisReportSection(
                    AnalysisReportSectionKind.AdvancedAnalysis,
                    "advanced-" + NormalizeId(request.Key),
                    descriptor.Title,
                    AnalysisReportLayoutPolicy.StartOnNewPage
                        | AnalysisReportLayoutPolicy.AllowContinuation);

                switch (request.Kind)
                {
                    case AnalysisReportAdvancedSectionKind.TemperatureDependence:
                        if (!temperaturePlotAdded)
                        {
                            section.Add(BuildTemperaturePlot(result, options));
                            temperaturePlotAdded = true;
                            AddTemperatureParameters(section, result, options);
                        }
                        break;
                    case AnalysisReportAdvancedSectionKind.SpolarRecord:
                        AddSpolarRecord(section, result, options);
                        break;
                    case AnalysisReportAdvancedSectionKind.AffinityVersusSalt:
                        section.Add(BuildAffinitySaltPlot(result));
                        break;
                    case AnalysisReportAdvancedSectionKind.DebyeHuckel:
                        section.Add(BuildDebyeHuckelPlot(result));
                        AddElectrostaticsParameters(section, result, options);
                        break;
                    case AnalysisReportAdvancedSectionKind.CounterIonRelease:
                        section.Add(BuildCounterIonReleasePlot(result));
                        AddElectrostaticsParameters(section, result, options);
                        break;
                    case AnalysisReportAdvancedSectionKind.Protonation:
                        section.Add(BuildProtonationPlot(result, options));
                        AddProtonationParameters(section, result, options);
                        break;
                    case AnalysisReportAdvancedSectionKind.Correlation:
                        AddCorrelation(section, result, request.CorrelationMemberIndex, options);
                        break;
                }

                if (section.Blocks.Count > 0) document.AddSection(section);
            }
        }

        static void AddReportAppendix(
            AnalysisReportDocument document,
            IReadOnlyList<AnalysisResult> results,
            IReadOnlyList<ExperimentData> supporting,
            AnalysisReportOptions options)
        {
            if (document == null || document.Sections.Count == 0) return;
            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.Appendix,
                "appendix",
                "Appendix",
                AnalysisReportLayoutPolicy.StartOnNewPage
                    | AnalysisReportLayoutPolicy.AllowContinuation);

            section.Add(BuildExperimentSourcesTable(results, supporting, options));

            if (document.Warnings.Count > 0)
            {
                section.Add(new AnalysisReportNoticeBlock(
                    "Report warnings",
                    string.Join(Environment.NewLine, document.Warnings),
                    AnalysisReportNoticeLevel.Warning));
            }

            var reportDetails = new List<AnalysisReportKeyValueItem>
            {
                Item("Software", document.Creator),
                Item("Application version", document.ApplicationVersion),
            };
            // Preparer and generation time are on the front page; the appendix does not repeat them.
            if (options.ExtraTraceability)
            {
                reportDetails.Add(Item("Report identifier", ReportIdText(document)));
                reportDetails.Add(Item("Result identifiers", string.Join("; ", document.Results.Select(item => item.Label + ": " + item.Id))));
            }
            section.Add(new AnalysisReportKeyValueBlock("Report details", reportDetails));

            document.AddSection(section);
            document.Sections.FirstOrDefault(item => item.Kind == AnalysisReportSectionKind.Cover)?
                .Blocks.OfType<AnalysisReportTableOfContentsBlock>().FirstOrDefault()?
                .AddEntry(new AnalysisReportTableOfContentsEntry("Appendix", "appendix"));
        }

        static void AddPooledComparisonDiagnostics(AnalysisReportSection section, AnalysisResult result,
            AnalysisReportOptions options)
        {
            var comparison = result.PooledNullComparison;
            section.Add(new AnalysisReportHeadingBlock("Pooled Comparison Diagnostics", 2));
            section.Add(new AnalysisReportTextBlock("",
                "Pooled across the independently fitted experiments with one common variance; diagnostic only, it does not determine member assessments.",
                AnalysisReportLayoutPolicy.AllowContinuation));
            var delta = FormatDeltaAicc(comparison);
            var rmsd = NullModelComparisonPresentation.NullRmsd(comparison, options.EnergyUnitFamily);
            var nullFit = comparison?.NullFitSucceeded != true
                ? NullModelComparisonPresentation.NullStatus(comparison)
                : rmsd == "Unavailable" ? "Converged"
                : "RMSD " + rmsd + " " + ThermogramUnits.IntegratedHeatUnit(options.EnergyUnitFamily);
            section.Add(new AnalysisReportKeyValueBlock("", new[]
            {
                Item("ΔAICc", delta == "Unavailable" || delta == "Not calculated"
                    ? ComparisonReasonForReport(comparison) : delta),
                Item("Null fit", nullFit),
            }));
        }

        static AnalysisReportTableBlock OverviewTableBlock(
            AnalysisResultOverviewTable overview, AnalysisResult result, AnalysisReportOptions options,
            IReadOnlyList<string> labels)
        {
            var columns = overview.Columns.Select(column => new AnalysisReportTableColumn(
                column.Id, column.Title, column.Alignment,
                column.Id == "Experiment" ? 1.35
                    : column.Id == "Loss" ? .55
                    : column.Id == "InformationCriteria" ? .72
                    : 1));
            var rows = overview.Rows.Select((row, index) => new AnalysisReportTableRow(
                overview.Columns.Select(column => column.Parameter.HasValue
                    ? ParameterFitStatus(result, row.Solution, column.Parameter.Value) == "Fixed"
                        ? FormatParameter(column.Parameter.Value,
                            row.Solution?.ReportParameters != null && row.Solution.ReportParameters.TryGetValue(column.Parameter.Value, out var fixedValue)
                                ? fixedValue
                                : row.Solution?.Parameters != null && row.Solution.Parameters.TryGetValue(column.Parameter.Value, out var parameterValue)
                                    ? parameterValue
                                    : new FloatWithError(double.NaN), overview, options.Copy(), noUncertainty: true).value + " (fixed)"
                        : PutConfidenceIntervalOnNewLine(row[column.Id])
                    : column.Id == "Experiment"
                        ? LabeledExperimentName(labels, index, row[column.Id])
                        : row[column.Id])));
            return new AnalysisReportTableBlock(
                "Experiment parameter overview",
                columns,
                rows,
                AnalysisReportLayoutPolicy.KeepTogether
                    | AnalysisReportLayoutPolicy.ShrinkToSinglePage,
                SummaryTableFontSize(overview.Columns.Count(column => column.Parameter.HasValue)));
        }

        static List<AnalysisReportKeyValueItem> BuildFixedParameterItems(
            AnalysisResult result, SolutionInterface onlySolution,
            AnalysisResultOverviewTable overview, AnalysisReportOptions options)
        {
            var solutions = onlySolution == null
                ? result?.Solution?.Solutions ?? new List<SolutionInterface>()
                : new List<SolutionInterface> { onlySolution };
            var items = new List<AnalysisReportKeyValueItem>();
            foreach (var solution in solutions)
            {
                var parameterKeys = (solution?.ReportParameters?.Keys ?? Enumerable.Empty<ParameterType>())
                    .Concat(solution?.Parameters?.Keys ?? Enumerable.Empty<ParameterType>())
                    .Distinct();
                foreach (var parameter in parameterKeys)
                {
                    if (ParameterFitStatus(result, solution, parameter) != "Fixed") continue;
                    var value = solution?.ReportParameters != null && solution.ReportParameters.TryGetValue(parameter, out var reported)
                        ? reported
                        : solution?.Parameters != null && solution.Parameters.TryGetValue(parameter, out var fitted)
                            ? fitted : new FloatWithError(double.NaN);
                    var fixedOptions = options.Copy();
                    fixedOptions.UncertaintyDisplayStyle = UncertaintyDisplayStyle.None;
                    var formatted = FormatParameter(parameter, value, overview, fixedOptions, noUncertainty: true);
                    var suffix = solution.Data?.Name;
                    var label = ParameterLabel(parameter) + (string.IsNullOrWhiteSpace(suffix) ? "" : " — " + suffix);
                    if (!items.Any(item => item.Label == label))
                        items.Add(Item(label, formatted.value + (string.IsNullOrWhiteSpace(formatted.unit) ? "" : " " + formatted.unit)));
                }
            }
            foreach (var entry in result?.Model?.Parameters?.GlobalTable ?? new Dictionary<ParameterType, Parameter>())
            {
                if (entry.Value?.IsLocked != true) continue;
                var parameter = entry.Key;
                var value = new FloatWithError(entry.Value.Value);
                if (parameter.GetProperties()?.ParentType == ParameterType.Affinity1)
                    value = new FloatWithError(Math.Pow(10, -entry.Value.Value));
                var fixedOptions = options.Copy();
                fixedOptions.UncertaintyDisplayStyle = UncertaintyDisplayStyle.None;
                var formatted = FormatParameter(parameter, value, overview, fixedOptions, noUncertainty: true);
                var label = "Shared " + ParameterLabel(parameter);
                if (!items.Any(item => item.Label == label))
                    items.Add(Item(label, formatted.value + (string.IsNullOrWhiteSpace(formatted.unit) ? "" : " " + formatted.unit)));
            }
            return items;
        }

        static void AddBookkeepingNotice(AnalysisReportSection section, AnalysisResult result,
            IReadOnlyList<string> labels)
        {
            var members = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            var entries = members.Select((solution, index) => new
            {
                Solution = solution,
                Label = index < (labels?.Count ?? 0) ? labels[index]
                    : AnalysisReportReferenceLabels.Experiment(0, index),
                Convention = SavedBookkeeping(result, solution?.Data?.UniqueID),
            }).Where(entry => !string.IsNullOrWhiteSpace(entry.Convention)).ToList();
            if (entries.Select(entry => entry.Convention).Distinct(StringComparer.Ordinal).Count() < 2) return;

            section.Add(new AnalysisReportNoticeBlock("Bookkeeping conventions",
                "This result contains experiments using different bookkeeping conventions.",
                AnalysisReportNoticeLevel.Warning));
            section.Add(new AnalysisReportKeyValueBlock("Bookkeeping conventions",
                entries.Select(entry => Item(entry.Label + " — " + (entry.Solution?.Data?.Name ?? "Experiment"),
                    entry.Convention))));
        }

        static bool HasMixedBookkeeping(IEnumerable<ExperimentFitInputSnapshot> snapshots)
        {
            var entries = snapshots?.ToList() ?? new List<ExperimentFitInputSnapshot>();
            return entries.Where(item => item.AppliedDilutionMethod.HasValue)
                    .Select(item => item.AppliedDilutionMethod.Value).Distinct().Count() > 1
                || entries.Select(item => item.HeatMethod).Distinct().Count() > 1;
        }

        static string BookkeepingDescription(DilutionMethod? concentrationMethod, InjectionHeatMethod heatMethod)
        {
            var heat = HeatMethodName(heatMethod);
            if (concentrationMethod is DilutionMethod concentration
                && InjectionBookkeeping.HeatMethodFor(concentration) == heatMethod)
                return concentration.DisplayName();
            var concentrationName = concentrationMethod?.DisplayName() ?? "Unknown concentration method";
            return concentrationMethod.HasValue
                ? concentrationName + " concentrations; " + heat + " heat"
                : concentrationName + "; " + heat + " heat";
        }

        static string HeatMethodName(InjectionHeatMethod method) => method switch
        {
            InjectionHeatMethod.MicroCal => "MicroCal",
            InjectionHeatMethod.IdealContinuousMixing => "Ideal continuous mixing",
            InjectionHeatMethod.DiscreteDisplacement => "Discrete displacement",
            _ => "Unknown",
        };

        static string SavedBookkeeping(AnalysisResult result, string experimentId)
        {
            var saved = result?.ValiditySnapshot?.Experiments?.FirstOrDefault(item => item.ExperimentID == experimentId);
            if (saved != null) return BookkeepingDescription(saved.AppliedDilutionMethod, saved.HeatMethod);
            var solution = result?.Solution?.Solutions?.FirstOrDefault(item => item?.Data?.UniqueID == experimentId);
            return solution == null ? "" : BookkeepingDescription(null, solution.Model?.HeatMethod ?? InjectionHeatMethod.MicroCal);
        }

        static string SavedSubtractionSummary(AnalysisResult result, IReadOnlyList<string> labels, AnalysisReportOptions options)
        {
            var saved = result?.ValiditySnapshot?.Experiments ?? new List<ExperimentFitInputSnapshot>();
            var entries = new List<string>();
            var members = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            foreach (var member in members)
            {
                var snapshot = saved.FirstOrDefault(item => item.ExperimentID == member?.Data?.UniqueID);
                var attribute = snapshot?.Attributes?.FirstOrDefault(item => item.Key == AttributeKey.BufferSubtraction);
                if (attribute == null) continue;
                var reference = ResolveReferenceName(member?.Data, attribute.StringValue, options);
                if (string.IsNullOrWhiteSpace(reference)) reference = "Reference experiment unavailable";
                var subtractionMethod = (BufferSubtractionMethod)attribute.IntValue;
                var method = subtractionMethod == BufferSubtractionMethod.MatchedInjection
                    ? "matched injections" : subtractionMethod.GetDisplayName().ToLowerInvariant();
                var memberIndex = members.IndexOf(member);
                var label = memberIndex >= 0 && memberIndex < labels.Count ? labels[memberIndex] : member?.Data?.Name ?? "Experiment";
                entries.Add(label + ": " + reference + "; " + method);
            }
            return string.Join(" | ", entries);
        }

        internal static double SummaryTableFontSize(int parameterCount) =>
            parameterCount > 7 ? 5.5 : 7.5;

        static AnalysisReportTableBlock BuildParameterTable(
            string title,
            AnalysisResult result,
            SolutionInterface solution,
            AnalysisResultOverviewTable overview,
            AnalysisReportOptions options)
        {
            var parameters = new Dictionary<ParameterType, FloatWithError>(
                solution?.ReportParameters ?? new Dictionary<ParameterType, FloatWithError>());
            if (solution?.Parameters != null
                && solution.Parameters.TryGetValue(ParameterType.Offset, out var offset))
                parameters[ParameterType.Offset] = offset;
            var fixedOptions = options.Copy();
            fixedOptions.UncertaintyDisplayStyle = UncertaintyDisplayStyle.None;

            var columns = new[]
            {
                new AnalysisReportTableColumn("Parameter", "Parameter", AnalysisResultColumnAlignment.Left, 2),
                new AnalysisReportTableColumn("Type", "Type", AnalysisResultColumnAlignment.Left, 1),
                new AnalysisReportTableColumn("Value", "Value", AnalysisResultColumnAlignment.Right, 2),
                new AnalysisReportTableColumn("Unit", "Unit", AnalysisResultColumnAlignment.Left, 1),
            };
            var rows = parameters
                .OrderBy(item => ParameterOrder(item.Key))
                .Select(item =>
                {
                    var status = ParameterFitStatus(result, solution, item.Key);
                    var formatted = FormatParameter(item.Key, item.Value, overview,
                        status == "Fixed" ? fixedOptions : options);
                    return new AnalysisReportTableRow(new[]
                    {
                        ParameterLabel(item.Key),
                        status,
                        formatted.value,
                        formatted.unit,
                    });
                });
            return new AnalysisReportTableBlock(
                title, columns, rows, AnalysisReportLayoutPolicy.AllowContinuation,
                verticalCellPadding: 1.5);
        }

        static string ParameterFitStatus(AnalysisResult result, SolutionInterface solution, ParameterType parameter)
        {
            if (IsDerivedParameter(parameter)) return "Derived";
            var key = parameter == ParameterType.ApparentAffinity ? ParameterType.Affinity1 : parameter;
            var global = result?.Model?.Parameters;
            if (global != null)
            {
                var constraint = global.GetConstraintForParameter(key);
                if (constraint != VariableConstraint.None)
                {
                    if (constraint == VariableConstraint.ThermodynamicallyLinked
                        && key.GetProperties().ParentType == ParameterType.Affinity1)
                        return "Derived";
                    var coordinates = GlobalConstraintSemantics.CoordinateKeys(key, constraint);
                    if (coordinates.Count > 0)
                        return coordinates.All(coordinate => global.GlobalTable.TryGetValue(coordinate, out var value) && value.IsLocked)
                            ? "Fixed" : "Fitted";
                    if (global.GlobalTable.TryGetValue(key, out var shared))
                        return shared.IsLocked ? "Fixed" : "Fitted";
                }
            }
            return solution?.Model?.Parameters?.Table != null
                && solution.Model.Parameters.Table.TryGetValue(key, out var local) && local.IsLocked
                ? "Fixed" : "Fitted";
        }

        static AnalysisReportTableBlock BuildInjectionTable(
            ExperimentData experiment,
            AnalysisReportOptions options,
            string title = "Injection table")
        {
            var overview = ExperimentOverviewTable.Build(
                experiment,
                options.EnergyUnitFamily,
                options.EnergyUnitOverride);
            var visibleColumns = overview.Columns.Where(column => column.IsVisible).ToList();
            var columns = visibleColumns.Select(column => new AnalysisReportTableColumn(
                column.Id,
                column.Title,
                column.Alignment switch
                {
                    ExperimentOverviewColumnAlignment.Center => AnalysisResultColumnAlignment.Center,
                    ExperimentOverviewColumnAlignment.Right => AnalysisResultColumnAlignment.Right,
                    _ => AnalysisResultColumnAlignment.Left,
                }));
            var rows = overview.Rows.Select(row => new AnalysisReportTableRow(
                visibleColumns.Select(column => row[column.Id])));

            return new AnalysisReportTableBlock(
                title,
                columns,
                rows,
                AnalysisReportLayoutPolicy.AllowContinuation,
                fontSize: visibleColumns.Count > 7 ? 5.75 : 7.5,
                verticalCellPadding: 1.5);
        }

        static string PutConfidenceIntervalOnNewLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var intervalStart = value.IndexOf('[');
            if (intervalStart <= 0 || value[intervalStart - 1] == '\n') return value;
            return value.Substring(0, intervalStart).TrimEnd()
                + "\n"
                + value.Substring(intervalStart);
        }

        static AnalysisReportTableBlock BuildExperimentSourcesTable(
            IReadOnlyList<AnalysisResult> results,
            IReadOnlyList<ExperimentData> supporting,
            AnalysisReportOptions options)
        {
            var rows = new List<(ExperimentData Data, List<string> Labels)>();
            void AddRow(ExperimentData data, string label)
            {
                var existing = rows.FirstOrDefault(row => ReferenceEquals(row.Data, data)
                    || (!string.IsNullOrWhiteSpace(data.UniqueID)
                        && string.Equals(row.Data.UniqueID, data.UniqueID, StringComparison.Ordinal)));
                if (existing.Data == null) rows.Add((data, new List<string> { label }));
                else existing.Labels.Add(label);
            }
            var memberLabels = new List<(SolutionInterface Member, AnalysisResult Result, string Label)>();
            for (var resultIndex = 0; resultIndex < (results?.Count ?? 0); resultIndex++)
            {
                var members = results[resultIndex]?.Solution?.Solutions?
                    .Where(member => member?.Data != null).ToList() ?? new List<SolutionInterface>();
                for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
                {
                    var label = AnalysisReportReferenceLabels.Experiment(resultIndex, memberIndex);
                    memberLabels.Add((members[memberIndex], results[resultIndex], label));
                    AddRow(members[memberIndex].Data, label);
                }
            }
            for (var index = 0; index < (supporting?.Count ?? 0); index++)
                if (supporting[index] != null)
                    AddRow(supporting[index], AnalysisReportReferenceLabels.SupportingExperiment(index));

            string Role(ExperimentData data)
            {
                if (string.IsNullOrWhiteSpace(data.UniqueID)) return "";
                var targets = memberLabels.Where(item => item.Result?.ValiditySnapshot?.Experiments?
                        .FirstOrDefault(snapshot => snapshot.ExperimentID == item.Member.Data.UniqueID)?
                        .Attributes?.FirstOrDefault(attribute => attribute.Key == AttributeKey.BufferSubtraction)?
                        .StringValue == data.UniqueID)
                    .Select(item => item.Label).Distinct().ToList();
                return targets.Count == 0 ? "" : "Buffer reference for " + string.Join(", ", targets);
            }

            var columns = new[]
            {
                new AnalysisReportTableColumn("ReportedAs", "Reported as", AnalysisResultColumnAlignment.Left, .8),
                new AnalysisReportTableColumn("Experiment", "Experiment", AnalysisResultColumnAlignment.Left, 1.5),
                new AnalysisReportTableColumn("File", "Source file", AnalysisResultColumnAlignment.Left, 1.7),
                new AnalysisReportTableColumn("Temperature", options.UseKelvin ? "T (K)" : "T (°C)", AnalysisResultColumnAlignment.Right, .8),
                new AnalysisReportTableColumn("Cell", "Cell", AnalysisResultColumnAlignment.Right, .9),
                new AnalysisReportTableColumn("Syringe", "Syringe", AnalysisResultColumnAlignment.Right, .9),
                new AnalysisReportTableColumn("Injections", "Inj.", AnalysisResultColumnAlignment.Right, .7),
                new AnalysisReportTableColumn("Role", "Role", AnalysisResultColumnAlignment.Left, 1.3),
            };
            return new AnalysisReportTableBlock("Experiment sources", columns, rows.Select(row => new AnalysisReportTableRow(new[]
            {
                string.Join(", ", row.Labels),
                row.Data.Name,
                row.Data.FileName,
                FormatTemperature(row.Data.MeasuredTemperature, options.UseKelvin, includeUnit: false),
                row.Data.CellConcentration.AsFormattedConcentration(true),
                row.Data.SyringeConcentration.AsFormattedConcentration(true),
                row.Data.InjectionCount.ToString(CultureInfo.CurrentCulture),
                Role(row.Data),
            })), AnalysisReportLayoutPolicy.AllowContinuation, 7.5);
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildModelItems(AnalysisResult result)
        {
            var properties = result.Model.ModelType.GetProperties();
            var items = new List<AnalysisReportKeyValueItem>
            {
                Item("Model", properties?.Name ?? result.Model.ModelType.ToString()),
                Item("Analysis", result.Model.Parameters.RequiresGlobalFitting ? "Global" : "Individual"),
            };
            foreach (var option in result.Model.ModelOptions
                ?? new Dictionary<AttributeKey, ExperimentAttribute>())
            {
                if (IsRoutineDefaultModelOption(option.Key, option.Value)) continue;
                items.Add(Item(
                    "Option: " + (option.Value?.GetDisplayName()
                        ?? option.Key.GetProperties()?.Name
                        ?? option.Key.ToString()),
                    option.Value?.GetDisplayValue() ?? "Unavailable"));
            }
            foreach (var constraint in result.Model.Parameters.Constraints
                .Where(item => item.Value != VariableConstraint.None))
            {
                items.Add(Item(
                    "Constraint: " + ParameterLabel(constraint.Key),
                    ConstraintPresentation.Description(constraint.Key, constraint.Value)));
            }
            return items;
        }

        static bool IsRoutineDefaultModelOption(AttributeKey key, ExperimentAttribute option)
        {
            if (option == null) return false;
            if (key == AttributeKey.UseSyringeActiveFraction) return !option.BoolValue;
            if (key == AttributeKey.NumberOfSites1) return Math.Abs(option.DoubleValue - 1) < 1e-9;
            return false;
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildFitDiagnosticItems(AnalysisResult result)
        {
            var solution = result.Solution;
            var convergence = solution.Convergence;
            var items = new List<AnalysisReportKeyValueItem> { RmsdItem(solution.UnweightedRmsd, solution.MolarRMSD) };
            if (convergence != null)
                items.Add(Item("Uncertainty", UncertaintyText(solution.ErrorEstimationMethod, convergence)));

            var criteria = result.IsIndependentAssessmentCollection ? null : result.InformationCriteria;
            if (criteria != null)
                items.Add(Item("AIC / AICc",
                    (criteria.IsAicAvailable ? FormatFinite(criteria.Aic.Value, "F1") : criteria.AicUnavailableReason)
                    + " / "
                    + (criteria.IsAiccAvailable ? FormatFinite(criteria.Aicc.Value, "F1") : criteria.AiccUnavailableReason)));
            if (convergence != null)
                items.Add(Item("Solver", SolverText(convergence, solution.UseWeightedFitting)));
            return items;
        }

        static AnalysisReportKeyValueItem RmsdItem(double rmsd, Energy? molarRmsd) => molarRmsd.HasValue
            ? Item("RMSD / Molar RMSD", FormatFinite(rmsd, "G5") + " µJ / "
                + molarRmsd.Value.ToFormattedString(EnergyUnit.KiloJoule, withunit: true, permole: true))
            : Item("RMSD", FormatFinite(rmsd, "G5") + " µJ");

        static string UncertaintyText(ErrorEstimationMethod method, SolverConvergence convergence)
        {
            if (method == ErrorEstimationMethod.None) return method.Description();
            var parts = new List<string> { method.Description() };
            if (convergence == null) return parts[0];
            parts.Add(LowerFirst(convergence.ErrorEstimationOutcome.GetEnumDescription()));
            if (convergence.ErrorEstimationAttemptedRefits.HasValue
                || convergence.ErrorEstimationSucceededRefits.HasValue
                || convergence.ErrorEstimationFailedRefits.HasValue)
            {
                var attempted = convergence.ErrorEstimationAttemptedRefits?.ToString(CultureInfo.CurrentCulture) ?? "unknown";
                var succeeded = convergence.ErrorEstimationSucceededRefits?.ToString(CultureInfo.CurrentCulture) ?? "unknown";
                var refits = succeeded + " of " + attempted + " refits succeeded";
                if (convergence.ErrorEstimationFailedRefits is int failed && failed > 0)
                    refits += " (" + failed.ToString(CultureInfo.CurrentCulture) + " failed)";
                parts.Add(refits);
            }
            else if (!string.IsNullOrWhiteSpace(convergence.ErrorEstimationSummary))
                parts.Add(convergence.ErrorEstimationSummary.Trim());
            return string.Join("; ", parts);
        }

        static string SolverText(SolverConvergence convergence, bool weighted)
        {
            var algorithm = convergence.Algorithm.GetProperties()?.Name ?? convergence.Algorithm.ToString();
            var text = algorithm + "; " + LowerFirst(convergence.Termination.GetEnumDescription())
                + " after " + convergence.Iterations.ToString(CultureInfo.CurrentCulture)
                + (convergence.Iterations == 1 ? " iteration" : " iterations")
                + " (" + FormatDuration(convergence.TotalTime) + ")";
            return weighted ? text + "; weighted by injection SDs" : text;
        }

        static string LowerFirst(string value) => string.IsNullOrEmpty(value)
            ? value ?? ""
            : char.ToLowerInvariant(value[0]) + value.Substring(1);

        static IEnumerable<AnalysisReportKeyValueItem> BuildExperimentMetadata(
            ExperimentData data,
            AnalysisReportOptions options,
            AnalysisResult result = null)
        {
            var instrument = data.Instrument.GetProperties()?.Name;
            var items = new List<AnalysisReportKeyValueItem>
            {
                Item("Source file", data.FileName),
                Item("Temperature", FormatExperimentTemperature(data, options.UseKelvin)),
                Item("Cell concentration", data.CellConcentration.AsFormattedConcentration(true)),
                Item("Syringe concentration", data.SyringeConcentration.AsFormattedConcentration(true)),
                Item("Injections", data.InjectionCount.ToString(CultureInfo.CurrentCulture)),
            };
            AddExperimentIdentifiers(items, data);
            AddExperimentDateItems(items, data, 0, options);
            items.Add(new AnalysisReportKeyValueItem("Experiment settings", ""));
            items.Add(Item("Instrument", string.IsNullOrWhiteSpace(instrument) ? "Unknown" : instrument, 1));
            items.Add(Item("Cell volume", FormatFinite(1_000_000 * data.CellVolume, "G5") + " µL", 1));
            if (IsFinite(data.StirringSpeed) && data.StirringSpeed >= 0)
                items.Add(Item("Stirring speed", FormatFinite(data.StirringSpeed, "G5") + " rpm", 1));
            if (data.FeedBackMode != FeedbackMode.Null)
                items.Add(Item("Feedback", data.FeedBackMode.GetProperties()?.Name
                    ?? data.FeedBackMode.ToString(), 1));
            if (IsFinite(data.InitialDelay) && data.InitialDelay > 0)
                items.Add(Item("Initial delay", FormatFinite(data.InitialDelay, "G5") + " s", 1));
            AddExperimentAttributes(items, data, options, result);
            if (!string.IsNullOrWhiteSpace(data.TandemMergeDescription))
                items.Add(Item("Tandem merge origin", data.TandemMergeDescription));
            return items;
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildCondensedExperimentMetadata(
            ExperimentData data,
            AnalysisReportOptions options,
            string previousLabel,
            AnalysisResult result = null)
        {
            var instrument = data.Instrument.GetProperties()?.Name;
            var items = new List<AnalysisReportKeyValueItem>
            {
                Item("Previously reported as", previousLabel),
                Item("Source file", data.FileName),
                Item("Instrument", string.IsNullOrWhiteSpace(instrument) ? "Unknown" : instrument),
                Item("Temperature", FormatExperimentTemperature(data, options.UseKelvin)),
                Item("Cell concentration", data.CellConcentration.AsFormattedConcentration(true)),
                Item("Syringe concentration", data.SyringeConcentration.AsFormattedConcentration(true)),
            };
            AddExperimentIdentifiers(items, data);
            AddExperimentDateItems(items, data, 0, options);
            AddExperimentAttributes(items, data, options, result);
            if (!string.IsNullOrWhiteSpace(data.TandemMergeDescription))
                items.Add(Item("Tandem merge origin", data.TandemMergeDescription));
            var bookkeeping = SavedBookkeeping(result, data.UniqueID);
            if (!string.IsNullOrWhiteSpace(bookkeeping))
                items.Add(Item("Bookkeeping convention", bookkeeping));
            return items;
        }

        static Dictionary<string, string> ExperimentReferenceLabels(
            IReadOnlyList<AnalysisResult> results,
            IReadOnlyList<ExperimentData> supporting = null)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var resultIndex = 0; resultIndex < (results?.Count ?? 0); resultIndex++)
            {
                var members = results[resultIndex]?.Solution?.Solutions?
                    .Where(member => member?.Data != null).ToList();
                for (var memberIndex = 0; memberIndex < (members?.Count ?? 0); memberIndex++)
                {
                    var id = members[memberIndex].Data.UniqueID;
                    if (!string.IsNullOrWhiteSpace(id) && !labels.ContainsKey(id))
                        labels.Add(id, AnalysisReportReferenceLabels.Experiment(resultIndex, memberIndex));
                }
            }
            for (var index = 0; index < (supporting?.Count ?? 0); index++)
            {
                var id = supporting[index]?.UniqueID;
                if (!string.IsNullOrWhiteSpace(id) && !labels.ContainsKey(id))
                    labels.Add(id, AnalysisReportReferenceLabels.SupportingExperiment(index));
            }
            return labels;
        }

        static string ResolveReferenceName(ExperimentData data, string referenceId, AnalysisReportOptions options)
        {
            var resolved = options?.ExperimentResolver?.Invoke(referenceId);
            if (!string.IsNullOrWhiteSpace(resolved?.Name)) return resolved.Name;
            if (options?.ExperimentResolver != null) return null;
            resolved = DataManager.Data.FirstOrDefault(item => item.UniqueID == referenceId);
            if (!string.IsNullOrWhiteSpace(resolved?.Name)) return resolved.Name;
            if (data?.BufferSubtractionSettings?.ReferenceExperimentId == referenceId
                && !string.IsNullOrWhiteSpace(data.ReferenceExperiment?.Name))
                return data.ReferenceExperiment.Name;
            return null;
        }

        static void AddExperimentAttributes(List<AnalysisReportKeyValueItem> items, ExperimentData data,
            AnalysisReportOptions options, AnalysisResult result)
        {
            var attributes = (data.Attributes ?? new List<ExperimentAttribute>())
                .Where(attribute => attribute != null)
                .ToList();
            if (attributes.Count == 0) return;

            items.Add(new AnalysisReportKeyValueItem("Attributes", ""));
            foreach (var attribute in attributes)
            {
                var value = attribute.Key == AttributeKey.CompetitorResult
                    ? FormatCompetitorProperties(attribute, data, options, result)
                    : attribute.Key == AttributeKey.BufferSubtraction
                        ? FormatBufferSubtraction(attribute, data, options)
                    : attribute.GetDisplayValue(data);
                items.Add(Item(attribute.Key == AttributeKey.CompetitorResult ? "Competitor properties"
                    : attribute.Key == AttributeKey.BufferSubtraction ? "Buffer subtraction"
                    : attribute.GetDisplayName(), value, 1));
            }
        }

        // Names the reference by its report label when the reference experiment is part of the report.
        static string FormatBufferSubtraction(ExperimentAttribute attribute, ExperimentData data, AnalysisReportOptions options)
        {
            var settings = BufferSubtractionSettings.FromAttribute(attribute);
            if (settings == null || string.IsNullOrWhiteSpace(settings.ReferenceExperimentId)
                || options?.ExperimentReferenceLabels == null
                || !options.ExperimentReferenceLabels.TryGetValue(settings.ReferenceExperimentId, out var label))
                return attribute.GetDisplayValue(data);
            var name = data?.ReferenceExperiment?.Name
                ?? ResolveReferenceName(data, settings.ReferenceExperimentId, options)
                ?? "Missing reference experiment";
            return name + " (Experiment " + label + "; " + settings.MethodDisplayName + ")";
        }

        static string FormatCompetitorProperties(ExperimentAttribute attribute, ExperimentData data,
            AnalysisReportOptions options, AnalysisResult result)
        {
            var saved = result?.ValiditySnapshot?.Experiments?
                .FirstOrDefault(item => item.ExperimentID == data?.UniqueID)?.Attributes?
                .FirstOrDefault(item => item.Key == AttributeKey.CompetitorResult);
            var affinity = saved == null ? attribute.CapturedAffinity : new FloatWithError(
                saved.CapturedAffinity, saved.CapturedAffinitySD, saved.CapturedAffinityLower, saved.CapturedAffinityUpper);
            var enthalpy = saved == null ? attribute.CapturedEnthalpy : new FloatWithError(
                saved.CapturedEnthalpy, saved.CapturedEnthalpySD, saved.CapturedEnthalpyLower, saved.CapturedEnthalpyUpper);
            var sourceId = saved?.StringValue ?? attribute.StringValue;
            var kdText = IsUsableCompetitorAffinity(affinity)
                ? affinity.AsFormattedConcentration(withunit: true, style: options.UncertaintyDisplayStyle)
                : "Unavailable";
            var enthalpyText = IsUsableCompetitorEnthalpy(enthalpy)
                ? new Energy(enthalpy).ToFormattedString(
                    EnergyUnitResolver.Resolve(options.EnergyUnitFamily, options.EnergyUnitOverride,
                        new[] { enthalpy }),
                    permole: true, style: options.UncertaintyDisplayStyle)
                : "Unavailable";
            var source = DataManager.Results.FirstOrDefault(item => item.UniqueID == sourceId);
            var attribution = source == null
                ? string.IsNullOrWhiteSpace(sourceId) ? "source result not recorded" : "source result unavailable"
                : "from result \"" + source.Name + "\"";
            return "Kd = " + kdText + "; ΔH = " + enthalpyText + " (" + attribution + ")";
        }

        static bool IsUsableCompetitorAffinity(FloatWithError value) =>
            !FloatWithError.IsNaN(value) && IsFinite(value.Value) && value.Value > 0;

        static bool IsUsableCompetitorEnthalpy(FloatWithError value) =>
            !FloatWithError.IsNaN(value) && IsFinite(value.Value);

        static void AddExperimentIdentifiers(List<AnalysisReportKeyValueItem> items, ExperimentData data)
        {
            var hasExternalId = !string.IsNullOrWhiteSpace(data?.ExternalExperimentId);
            var hasCellId = !string.IsNullOrWhiteSpace(data?.CellSampleId);
            var hasSyringeId = !string.IsNullOrWhiteSpace(data?.SyringeSampleId);
            if (!hasExternalId && !hasCellId && !hasSyringeId) return;
            if (hasExternalId)
                items.Add(Item("External experiment ID", data.ExternalExperimentId));
            if (hasCellId)
                items.Add(Item("Cell sample/batch ID", data.CellSampleId));
            if (hasSyringeId)
                items.Add(Item("Syringe sample/batch ID", data.SyringeSampleId));
        }

        static string FormatExperimentTemperature(ExperimentData data, bool useKelvin)
        {
            var measured = FormatTemperature(data.MeasuredTemperature, useKelvin);
            var target = FormatTemperature(data.TargetTemperature, useKelvin);
            if (measured == "Unavailable") return "Target " + target;
            if (target == "Unavailable") return "Measured " + measured;
            return "Measured " + measured + "; target " + target;
        }

        static bool IsTrustedExperimentDateSource(ExperimentDateSource source) =>
            source == ExperimentDateSource.DataFile
            || source == ExperimentDateSource.UserModified;

        static void AddExperimentDateItems(List<AnalysisReportKeyValueItem> items, ExperimentData data, int index,
            AnalysisReportOptions options)
        {
            if (data.DateSource == ExperimentDateSource.DataFile)
                items.Insert(index, Item("Experiment date", FormatDate(data.Date) + " (data file)"));
            else if (data.DateSource == ExperimentDateSource.UserModified)
                items.Insert(index, Item("Experiment date", FormatDate(data.Date) + " (user provided)"));
            // A filesystem timestamp does not establish the experiment date, so it is only reported for traceability.
            else if (!options.ExtraTraceability) return;
            else if (data.DateSource == ExperimentDateSource.FileSystem)
                items.Insert(index, Item("Experiment date", FormatDate(data.Date) + " (file system date)"));
            else items.Insert(index, Item("Experiment date", "Not recorded"));
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildProcessingItems(ExperimentData data,
            AnalysisResult result = null)
        {
            var injections = data.Injections ?? new List<InjectionData>();
            var included = injections.Count(injection => injection.Include);
            var excluded = injections.Where(injection => !injection.Include)
                .Select(injection => (injection.ID + 1).ToString(CultureInfo.CurrentCulture))
                .ToList();
            var integrated = injections.Count(injection => injection.IsIntegrated);
            var ranges = IntegrationRanges(injections);
            var items = new List<AnalysisReportKeyValueItem>
            {
                Item("Baseline method", data.Processor?.BaselineType.ToString() ?? BaselineInterpolatorTypes.None.ToString()),
                Item("Injection use", included.ToString(CultureInfo.CurrentCulture) + " included; " +
                    (excluded.Count == 0 ? "none excluded" : "excluded: " + string.Join(", ", excluded))),
                new AnalysisReportKeyValueItem("Integration regions", ""),
                Item("Start after injection", ranges.start, 1),
                Item("End after injection", ranges.end, 1),
            };
            if (data.Processor?.BaselineCompleted != true)
                items.Insert(1, Item("Baseline status", "Incomplete"));
            if (data.Processor?.IntegrationLengthMode != InjectionData.IntegrationLengthMode.Time)
                items.Insert(2, Item("Integration mode", data.Processor?.IntegrationLengthMode.ToString() ?? "Unavailable"));
            if (integrated != injections.Count)
                items.Insert(2, Item("Integrated injections", integrated + " of " + injections.Count));
            var bookkeeping = SavedBookkeeping(result, data?.UniqueID);
            if (!string.IsNullOrWhiteSpace(bookkeeping))
                items.Insert(0, Item("Bookkeeping convention", bookkeeping));
            return items;
        }

        static AnalysisReportThermodynamicSummaryBlock BuildThermodynamicSummaryPlot(
            AnalysisResult result,
            IReadOnlyList<string> labels,
            AnalysisReportOptions options)
        {
            var members = result?.Solution?.Solutions?
                .Where(solution => solution?.ReportParameters != null)
                .ToList() ?? new List<SolutionInterface>();
            if (members.Count == 0) return null;
            var keys = members.SelectMany(solution => solution.ReportParameters.Keys).Distinct();
            var parameters = ThermodynamicParameterSlots.OrderedKeys(
                keys,
                ThermodynamicParameterFamily.Enthalpy,
                ThermodynamicParameterFamily.EntropyContribution,
                ThermodynamicParameterFamily.Gibbs).ToList();
            if (parameters.Count == 0) return null;
            var unit = ResolveMolarEnergyUnit(result, options);
            var scale = Energy.ScaleFactor(unit);
            var familyCounts = parameters
                .Where(parameter => ThermodynamicParameterSlots.TryResolve(parameter, out _, out _))
                .GroupBy(parameter =>
                {
                    ThermodynamicParameterSlots.TryResolve(parameter, out _, out var family);
                    return family;
                })
                .ToDictionary(group => group.Key, group => group.Count());
            var categories = parameters.Select(parameter => ThermodynamicSummaryLabel(parameter, familyCounts)).ToList();
            var series = new List<AnalysisReportThermodynamicSeries>();
            for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                var member = members[memberIndex];
                var bars = new List<AnalysisReportThermodynamicBar>();
                for (var index = 0; index < parameters.Count; index++)
                {
                    if (!member.ReportParameters.TryGetValue(parameters[index], out var value)
                        || !IsFinite(value.Value)) continue;
                    var bounds = UncertaintyBounds(value, scale);
                    bars.Add(new AnalysisReportThermodynamicBar(
                        categories[index], value.Value * scale,
                        bounds.sdLower, bounds.sdUpper,
                        bounds.ciLower, bounds.ciUpper));
                }
                if (bars.Count > 0)
                    series.Add(new AnalysisReportThermodynamicSeries(
                        labels != null && memberIndex < labels.Count
                            ? labels[memberIndex]
                            : AnalysisReportReferenceLabels.Experiment(0, memberIndex), bars));
            }
            if (series.Count == 0) return null;
            const UncertaintyDisplayStyle summaryUncertainty = UncertaintyDisplayStyle.ConfidenceInterval;
            return new AnalysisReportThermodynamicSummaryBlock(
                "Thermodynamic summary",
                unit.GetUnit() + "/mol",
                summaryUncertainty,
                ThermodynamicUncertaintyNote(result.Solution.ErrorEstimationMethod,
                    summaryUncertainty),
                categories,
                series);
        }

        static string ThermodynamicUncertaintyNote(
            ErrorEstimationMethod method,
            UncertaintyDisplayStyle style)
        {
            var showsSd = style == UncertaintyDisplayStyle.Automatic
                || style == UncertaintyDisplayStyle.StandardDeviation
                || style == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
            var showsCi = style == UncertaintyDisplayStyle.ConfidenceInterval
                || style == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
            if (!showsSd && !showsCi) return "";

            var intervalSource = method == ErrorEstimationMethod.ProfileLikelihood
                ? "profile-likelihood calculation"
                : method == ErrorEstimationMethod.BootstrapResiduals
                    ? "solution distribution"
                    : method == ErrorEstimationMethod.LeaveOneOut
                        ? "leave-one-out solution distribution"
                        : "saved uncertainty analysis";

            if (showsSd && showsCi)
                return "Bars: inner caps = ±1 SD (symmetric approximation); outer whiskers = saved 95% CI from the "
                    + intervalSource + ".";
            if (showsSd)
                return "Bars: ±1 SD is a symmetric approximation about the best fit.";
            return "Bars: 95% CI is the saved interval from the " + intervalSource + ".";
        }

        static void AddMemberAssessmentItems(List<AnalysisReportKeyValueItem> items, AnalysisResult result,
            SolutionInterface solution)
        {
            var outcome = result.GetMemberBindingAssessment(solution)?.EffectiveOutcome ?? BindingAssessmentOutcome.NotAssessed;
            if (outcome == BindingAssessmentOutcome.NotAssessed) return;
            items.Add(Item("Binding assessment", NullModelComparisonPresentation.OutcomeText(outcome)));
            items.Add(Item("ΔAICc", FormatDeltaAicc(result.GetMemberNullComparison(solution))));
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildMemberFitItems(AnalysisResult result, SolutionInterface solution)
        {
            var items = new List<AnalysisReportKeyValueItem> { RmsdItem(solution.UnweightedRmsd, solution.MolarRMSD) };
            var cValues = AnalysisCValueCalculator.Calculate(solution);
            foreach (var cValue in cValues)
                items.Add(Item(cValue.Label,
                    cValue.IsAvailable ? cValue.Estimate.Value.ToString() : "Unavailable"));
            if (cValues.Any(item => item.ConcentrationBasis == "initial-tandem-segment"))
                items.Add(Item("c-value concentration basis", "Initial tandem segment"));
            var isGlobal = result?.Model?.Parameters?.RequiresGlobalFitting == true
                || result?.Model?.Parameters?.Constraints?.Any(item => item.Value != VariableConstraint.None) == true;
            if (!isGlobal)
                items.Add(Item("Uncertainty", UncertaintyText(solution.ErrorMethod, solution.Convergence)));
            return items;
        }

        static void AddValidityNotice(AnalysisReportSection section, AnalysisResult result)
        {
            var report = result.ValidityReport;
            var reasons = report?.Reasons == null || report.Reasons.Count == 0
                ? "No validity details were recorded."
                : string.Join(Environment.NewLine, report.Reasons);
            var level = result.Health == AnalysisResultHealth.Valid
                ? AnalysisReportNoticeLevel.Information
                : result.Health == AnalysisResultHealth.Invalid
                    ? AnalysisReportNoticeLevel.Error
                    : AnalysisReportNoticeLevel.Warning;
            section.Add(new AnalysisReportNoticeBlock(
                HealthLabel(result.Health), reasons, level));
        }

        static void AddResultDiagnostics(AnalysisReportDocument document, AnalysisResult result)
        {
            if (result.Health != AnalysisResultHealth.Valid)
            {
                document.AddDiagnostic(
                    AnalysisReportDiagnosticSeverity.Warning,
                    "result-health",
                    "The saved result is reported with status: " + HealthLabel(result.Health) + ".");
            }

            foreach (var solution in result.Solution.Solutions.Where(solution => solution != null))
            {
                if (solution.ParameterBoundaryHit)
                    document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Warning,
                        "parameter-boundary", (solution.Data?.Name ?? "Experiment") +
                        " has a fitted parameter at a boundary.");
                if (solution.BootstrapParameterBoundaryHit)
                    document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Warning,
                        "bootstrap-boundary", (solution.Data?.Name ?? "Experiment") +
                        " has bootstrap estimates at a parameter boundary.");
                if (solution.Convergence?.HasErrorEstimationLimitWarnings == true)
                    document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Warning,
                        "uncertainty-limit", (solution.Data?.Name ?? "Experiment") +
                        " has limit-terminated uncertainty refits.");
            }
        }

        static void AddCorrelationDescriptors(
            ICollection<AnalysisReportAdvancedSectionDescriptor> output,
            AnalysisResult result)
        {
            try
            {
                var available = false;
                var shared = new BootstrapCorrelationAnalyzer().Analyze(result);
                available = shared?.IsAvailable == true;

                if (result.Solution.Solutions.Count > 1)
                {
                    for (var index = 0; index < result.Solution.Solutions.Count; index++)
                    {
                        var member = new BootstrapCorrelationAnalyzer().Analyze(result, index);
                        available |= member?.IsAvailable == true;
                    }
                }

                if (!available) return;
                output.Add(new AnalysisReportAdvancedSectionDescriptor(
                    new AnalysisReportAdvancedSectionRequest(
                        AnalysisReportAdvancedSectionKind.Correlation),
                    result.Solution.Solutions.Count > 1
                        ? "Parameter correlations"
                        : "Parameter correlation",
                    result.Solution.Solutions.Count > 1
                        ? "Include all saved shared and experiment parameter-correlation matrices."
                        : "Include the parameter correlation calculated from saved residual-bootstrap fits."));
            }
            catch
            {
                // Correlation is optional. Damaged legacy bootstrap content must not
                // prevent the rest of the report from being built.
            }
        }

        static void AddCorrelation(
            AnalysisReportSection section,
            AnalysisResult result,
            int? memberIndex,
            AnalysisReportOptions options)
        {
            var correlation = memberIndex.HasValue
                ? new BootstrapCorrelationAnalyzer().Analyze(result, memberIndex.Value)
                : new BootstrapCorrelationAnalyzer().Analyze(result);
            if (correlation?.IsAvailable != true) return;
            var labels = correlation.Parameters.Select(parameter =>
            {
                var prefix = parameter.IsShared ? "Global · "
                    : parameter.IsMember ? "Experiment · " : "";
                return prefix + parameter.Label;
            });
            var notes = BootstrapCorrelationDiagnosticFormatter.ReliabilityWarnings(correlation).ToList();
            notes.Insert(0, "Residual bootstrap (Pearson); " +
                correlation.UsedReplicateCount.ToString(CultureInfo.CurrentCulture) +
                " complete replicates.");
            section.Add(new AnalysisReportCorrelationMatrixBlock(
                memberIndex.HasValue ? "Parameter correlation" : "Shared parameter correlation",
                labels, correlation.CorrelationMatrix, notes));
            if (options.ExpandedExplanations)
                section.Add(new AnalysisReportNoticeBlock("Reading parameter correlations",
                    "These values show how parameter estimates varied together across saved residual-bootstrap fits. Values near +1 or −1 indicate strong co-variation and can signal that the fit has difficulty separating those parameters; they do not establish a causal relationship.",
                    AnalysisReportNoticeLevel.Information));
        }

        static bool CorrelationRequested(AnalysisReportOptions options, int? memberIndex) =>
            (options?.AdvancedSections ?? Array.Empty<AnalysisReportAdvancedSectionRequest>())
                .Any(request => request?.Kind == AnalysisReportAdvancedSectionKind.Correlation
                    && (!request.CorrelationMemberIndex.HasValue
                        || request.CorrelationMemberIndex == memberIndex));

        static AnalysisReportPlotBlock BuildTemperaturePlot(
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var dependences = result.Solution.TemperatureDependence;
            var parameters = ThermodynamicParameterSlots.OrderedKeys(
                dependences.Keys,
                ThermodynamicParameterFamily.Enthalpy,
                ThermodynamicParameterFamily.EntropyContribution,
                ThermodynamicParameterFamily.Gibbs);
            var unit = ResolveMolarEnergyUnit(result, options);
            var scale = Energy.ScaleFactor(unit);
            var series = new List<AnalysisReportPlotSeries>();
            var summaries = new AnalysisResultAggregateSummaryCalculator(result);
            foreach (var parameter in parameters)
            {
                if (!dependences.ContainsKey(parameter)) continue;
                var group = parameter.ToString();
                var points = result.Solution.Solutions
                    .Where(solution => solution != null
                        && IsFinite(solution.Temp)
                        && solution.ReportParameters.ContainsKey(parameter)
                        && IsFinite(solution.ReportParameters[parameter].Value))
                    .OrderBy(solution => solution.Temp)
                    .Select(solution => PlotPointForDisplay(
                        DisplayTemperature(solution.Temp, options.UseKelvin),
                        solution.ReportParameters[parameter],
                        scale,
                        solution.Data?.Name,
                        options.UncertaintyDisplayStyle))
                    .ToList();
                if (points.Count == 0) continue;

                var label = ParameterLabel(parameter);
                series.Add(new AnalysisReportPlotSeries(
                    label, AnalysisReportPlotSeriesKind.Points, points, group));
                var domain = PlotDomain(points.Select(point => point.X));
                var displayXs = Sample(domain.min, domain.max, 81);
                var modelXs = options.UseKelvin
                    ? displayXs.Select(value => value - 273.15).ToArray()
                    : displayXs;
                var envelope = summaries.BuildEnvelope(parameter, modelXs);
                var includeConfidenceBand = options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.ConfidenceInterval
                    || options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
                series.Add(new AnalysisReportPlotSeries(
                    label,
                    AnalysisReportPlotSeriesKind.Line,
                    envelope.Select(point => new AnalysisReportPlotPoint(
                        DisplayTemperature(point.X, options.UseKelvin),
                        point.Center * scale,
                        includeConfidenceBand && point.HasBand ? (double?)(point.Lower * scale) : null,
                        includeConfidenceBand && point.HasBand ? (double?)(point.Upper * scale) : null)),
                    group));
            }

            return new AnalysisReportPlotBlock(
                "Temperature dependence",
                options.UseKelvin ? "Temperature (K)" : "Temperature (°C)",
                "Thermodynamic parameter (" + unit.GetUnit() + "/mol)",
                series,
                options.UncertaintyDisplayStyle);
        }

        static void AddTemperatureParameters(
            AnalysisReportSection section,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var temperature = AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);
            var evaluation = AnalysisResultParameterEvaluator.Evaluate(
                result, temperature, options.EnergyUnitFamily,
                options.EnergyUnitOverride, options.UncertaintyDisplayStyle);
            if (evaluation.IsAvailable)
            {
                section.Add(new AnalysisReportKeyValueBlock(
                    "Parameters at " + FormatTemperature(temperature, options.UseKelvin),
                    evaluation.Rows.Select(row => Item(row.Label, row.Value))));
                AddSummaryUncertaintyExplanation(section, evaluation, options);
            }
        }

        static void AddSummaryUncertaintyExplanation(AnalysisReportSection section,
            AnalysisResultParameterEvaluation evaluation, AnalysisReportOptions options)
        {
            if (options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.None
                || options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.StandardDeviation) return;
            if (!evaluation.Rows.Any(row => row.Tooltip.Contains("Approximate propagated interval"))) return;
            if (options.ExpandedExplanations)
                section.Add(new AnalysisReportNoticeBlock("Summary uncertainty",
                    "Local summary intervals are constructed from individual 95% intervals and observed spread. "
                    + "Lower and upper uncertainty are propagated separately. These intervals have not been shown to provide 95% coverage, and covariance between experiments is omitted. "
                    + "Shared/model-estimated intervals retain their CI95 meaning.", AnalysisReportNoticeLevel.Information));
            else
                section.Add(new AnalysisReportTextBlock("",
                    "Local summary intervals are approximate: 95% coverage is not established, and covariance between experiments is omitted.",
                    AnalysisReportLayoutPolicy.KeepTogether));
        }

        static void AddSpolarRecord(
            AnalysisReportSection section,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var analysis = result.SpolarRecordAnalysis;
            var output = analysis.Result;
            var temperature = output.ReferenceTemperature.Value;
            var unit = ResolveMolarEnergyUnit(result, options);
            section.Add(new AnalysisReportKeyValueBlock("Saved result", new[]
            {
                Item("Folded mode", SpolarFoldedMode(analysis)),
                Item("Temperature mode", SpolarTemperatureMode(analysis)),
                Item(SpolarTemperatureLabel(analysis), FormatTemperature(
                    output.ReferenceTemperature,
                    options.UseKelvin,
                    options.UncertaintyDisplayStyle)),
                Item("Hydration contribution", new Energy(output.HydrationContribution(temperature))
                    .ToFormattedString(unit, permole: true, style: options.UncertaintyDisplayStyle)),
                Item("Conformational contribution", new Energy(output.ConformationalContribution(temperature))
                    .ToFormattedString(unit, permole: true, style: options.UncertaintyDisplayStyle)),
                Item("Residue estimate", output.Rvalue.AsNumber(options.UncertaintyDisplayStyle)),
                Item("Uncertainty", AdvancedAnalysisUncertaintyDescription()),
                Item("Completed", FormatNullableDate(analysis.CompletedAtUtc)),
            }));
        }

        static AnalysisReportPlotBlock BuildAffinitySaltPlot(AnalysisResult result)
        {
            var unit = result.AppropriateAffinityUnit;
            var points = result.Solution.Solutions
                .Where(solution => solution?.Data != null
                    && solution.ReportParameters.ContainsKey(ParameterType.Affinity1))
                .Select(solution =>
                {
                    var salt = solution.Data.Attributes
                        .Find(attribute => attribute.Key == AttributeKey.Salt);
                    return salt == null ? null : PlotPoint(
                        1000 * salt.ParameterValue.Value,
                        solution.ReportParameters[ParameterType.Affinity1],
                        unit.GetMod(), solution.Data.Name);
                })
                .Where(point => point != null)
                .OrderBy(point => point.X)
                .ToList();
            return new AnalysisReportPlotBlock(
                "Affinity versus salt", "Salt concentration (mM)",
                "Kd (" + unit.GetName() + ")",
                new[] { new AnalysisReportPlotSeries(
                    "Saved observations", AnalysisReportPlotSeriesKind.Points, points) });
        }

        static AnalysisReportPlotBlock BuildDebyeHuckelPlot(AnalysisResult result)
        {
            var analysis = result.ElectrostaticsAnalysis;
            var points = result.Solution.Solutions
                .Where(solution => solution?.Data != null
                    && solution.ReportParameters.ContainsKey(ParameterType.Affinity1)
                    && solution.ReportParameters[ParameterType.Affinity1].Value > 0)
                .Select(solution =>
                {
                    var x = Math.Sqrt(Math.Max(0, BufferAttribute.GetIonicStrength(solution.Data)));
                    var kd = FWEMath.Log10(solution.ReportParameters[ParameterType.Affinity1]);
                    return PlotPoint(x, kd, 1, solution.Data.Name);
                })
                .OrderBy(point => point.X)
                .ToList();
            var series = new List<AnalysisReportPlotSeries>
            {
                new AnalysisReportPlotSeries("Saved observations", AnalysisReportPlotSeriesKind.Points, points)
            };
            if (points.Count > 0 && analysis.IonicStrengthDependenceFit != null)
            {
                var domain = PlotDomain(points.Select(point => point.X));
                series.Add(SampledSeries(
                    "Saved fit",
                    Sample(domain.min, domain.max, 81),
                    x =>
                    {
                        // The evaluator takes sqrt(I) and already returns log10(Kd).
                        return analysis.IonicStrengthDependenceFit.Evaluate(x);
                    },
                    1));
            }
            return new AnalysisReportPlotBlock(
                "Debye-Huckel dependence", "sqrt(Ionic strength / M)", "log10(Kd / M)", series);
        }

        static AnalysisReportPlotBlock BuildCounterIonReleasePlot(AnalysisResult result)
        {
            var analysis = result.ElectrostaticsAnalysis;
            var points = result.Solution.Solutions
                .Where(solution => solution?.Data != null
                    && solution.ReportParameters.ContainsKey(ParameterType.Affinity1))
                .Select(solution =>
                {
                    var activity = SaltAttribute.GetIonActivity(solution.Data);
                    var affinity = solution.ReportParameters[ParameterType.Affinity1];
                    return activity > 0 && affinity.Value > 0
                        ? PlotPoint(Math.Log(activity), FWEMath.Log(affinity), 1, solution.Data.Name)
                        : null;
                })
                .Where(point => point != null)
                .OrderBy(point => point.X)
                .ToList();
            var series = new List<AnalysisReportPlotSeries>
            {
                new AnalysisReportPlotSeries("Saved observations", AnalysisReportPlotSeriesKind.Points, points)
            };
            if (points.Count > 0 && analysis.CounterIonReleaseFit != null)
            {
                var domain = PlotDomain(points.Select(point => point.X));
                series.Add(LinearSeries("Saved fit", analysis.CounterIonReleaseFit,
                    Sample(domain.min, domain.max, 81), 1));
            }
            return new AnalysisReportPlotBlock(
                "Counter-ion release", "ln(Salt activity)", "ln(Kd / M)", series);
        }

        static AnalysisReportPlotBlock BuildProtonationPlot(
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var analysis = result.ProtonationAnalysis;
            var fit = analysis.Fit as LinearFitWithError;
            var unit = ResolveMolarEnergyUnit(result, options);
            var scale = Energy.ScaleFactor(unit);
            var points = analysis.DataPoints
                .Where(point => point != null && IsFinite(point.Item1) && IsFinite(point.Item2.Value))
                .OrderBy(point => point.Item1)
                .Select(point => PlotPoint(point.Item1 * scale, point.Item2, scale))
                .ToList();
            var series = new List<AnalysisReportPlotSeries>
            {
                new AnalysisReportPlotSeries("Saved observations", AnalysisReportPlotSeriesKind.Points, points)
            };
            if (fit != null && points.Count > 0)
            {
                var domain = PlotDomain(points.Select(point => point.X));
                series.Add(LinearSeries("Saved fit", fit,
                    Sample(domain.min, domain.max, 81), scale, xScale: scale));
            }
            return new AnalysisReportPlotBlock(
                "Protonation dependence",
                "Buffer protonation enthalpy (" + unit.GetUnit() + "/mol)",
                "Observed enthalpy (" + unit.GetUnit() + "/mol)",
                series);
        }

        static void AddElectrostaticsParameters(
            AnalysisReportSection section,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var analysis = result.ElectrostaticsAnalysis;
            if (analysis == null) return;
            var unit = result.AppropriateAffinityUnit;
            var fit = analysis.IonicStrengthDependenceFit;
            var items = new List<AnalysisReportKeyValueItem>();
            if (fit != null)
            {
                items.Add(Item("Kd at zero ionic strength", fit.Kd0.AsFormattedConcentration(
                    unit, withunit: true, style: options.UncertaintyDisplayStyle)));
                items.Add(Item("Salt sensitivity", fit.SaltSensitivity.ToString("G5", options.UncertaintyDisplayStyle)));
                if (fit.UsesCurvature)
                    items.Add(Item("Curvature", fit.Curvature.ToString("G5", options.UncertaintyDisplayStyle)));
            }
            if (!FloatWithError.IsNaN(analysis.CounterIonRelease))
                items.Add(Item("Counter-ion release", analysis.CounterIonRelease.ToString("G5", options.UncertaintyDisplayStyle)));
            items.Add(Item("Uncertainty", AdvancedAnalysisUncertaintyDescription()));
            items.Add(Item("Completed", FormatNullableDate(analysis.CompletedAtUtc)));
            section.Add(new AnalysisReportKeyValueBlock("Saved result", items));
        }

        static void AddProtonationParameters(
            AnalysisReportSection section,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var analysis = result.ProtonationAnalysis;
            var unit = ResolveMolarEnergyUnit(result, options);
            section.Add(new AnalysisReportKeyValueBlock("Saved result", new[]
            {
                Item("Binding enthalpy", analysis.BindingEnthalpy.ToFormattedString(
                    unit, permole: true, style: options.UncertaintyDisplayStyle)),
                Item("Protonation change", analysis.ProtonationChange.ToString("G5", options.UncertaintyDisplayStyle)),
                Item("Uncertainty", AdvancedAnalysisUncertaintyDescription()),
                Item("Completed", FormatNullableDate(analysis.CompletedAtUtc)),
            }));
        }

        static string AdvancedAnalysisUncertaintyDescription() =>
            "Repeated random sampling of saved input uncertainties.";

        static AnalysisReportAdvancedSectionDescriptor Descriptor(
            AnalysisReportAdvancedSectionKind kind,
            string title,
            string description)
        {
            return new AnalysisReportAdvancedSectionDescriptor(
                new AnalysisReportAdvancedSectionRequest(kind), title, description);
        }

        static string UnavailableAdvancedMessage(
            AnalysisResult result,
            AnalysisReportAdvancedSectionRequest request)
        {
            var title = request.Kind.ToString();
            var reason = request.Kind switch
            {
                AnalysisReportAdvancedSectionKind.TemperatureDependence =>
                    "No saved temperature-dependence fit with usable plot data is available.",
                AnalysisReportAdvancedSectionKind.SpolarRecord =>
                    result?.SpolarRecordAnalysisUnavailableReason,
                AnalysisReportAdvancedSectionKind.AffinityVersusSalt =>
                    result?.ElectrostaticsAnalysisUnavailableReason,
                AnalysisReportAdvancedSectionKind.DebyeHuckel =>
                    "No completed saved Debye-Huckel analysis is available.",
                AnalysisReportAdvancedSectionKind.CounterIonRelease =>
                    "No completed saved counter-ion release analysis is available.",
                AnalysisReportAdvancedSectionKind.Protonation =>
                    result?.ProtonationAnalysisUnavailableReason,
                AnalysisReportAdvancedSectionKind.Correlation =>
                    "No usable residual-bootstrap correlation is available for the requested scope.",
                _ => "The requested saved analysis is unavailable.",
            };
            if (string.IsNullOrWhiteSpace(reason)) reason = "The requested saved analysis is unavailable.";
            return title + " was omitted: " + reason;
        }

        static bool CanBuildTemperaturePlot(AnalysisResult result)
        {
            if (result?.IsTemperatureDependenceEnabled != true) return false;
            var dependences = result?.Solution?.TemperatureDependence;
            if (dependences == null || dependences.Count == 0) return false;
            return result.Solution.Solutions.Any(solution => solution?.ReportParameters != null
                && dependences.Keys.Any(key => solution.ReportParameters.ContainsKey(key)));
        }

        static bool CanBuildAffinitySaltPlot(AnalysisResult result)
        {
            return result?.IsElectrostaticsAnalysisDependenceEnabled == true
                && result.Solution.Solutions.Any(solution => solution?.Data?.Attributes != null
                    && solution.Data.Attributes.Any(attribute => attribute.Key == AttributeKey.Salt)
                    && solution.ReportParameters.ContainsKey(ParameterType.Affinity1));
        }

        static PublicationFigureCanvasOptions CoverCanvasOptions(int count, int resultIndex)
        {
            var columns = 5;
            var scale = 3.0 / columns;
            for (var candidate = 3; candidate <= 5; candidate++)
            {
                var candidateScale = 3.0 / candidate;
                var rows = Math.Max(1, (int)Math.Ceiling(count / (double)candidate));
                if (rows * SupportingFigureHeightCentimeters * candidateScale > CoverFigureHeightCentimeters)
                    continue;
                columns = candidate;
                scale = candidateScale;
                break;
            }

            var rowCount = Math.Max(1, (int)Math.Ceiling(count / (double)columns));
            return new PublicationFigureCanvasOptions
            {
                PlotWidthCentimeters = SupportingFigureWidthCentimeters * scale,
                PlotHeightCentimeters = SupportingFigureHeightCentimeters * scale,
                FontSize = 9,
                SymbolSize = 4,
                StrokeWidth = 1,
                Columns = columns,
                Rows = rowCount,
                ShowPanelLetters = true,
                ShowPanelTitles = true,
                // Panel headings are wider than the panels at four or more columns.
                PanelTitleMaximumCharacters = columns <= 3 ? 25 : columns == 4 ? 20 : 18,
                PanelLabelPrefix = AnalysisReportReferenceLabels.Result(resultIndex),
                GroupResultFigures = false,
                ShowInformationBoxes = false,
            };
        }

        static PublicationFigureOptions SupportingFigureOptions(
            AnalysisReportOptions options,
            PublicationFigureCanvasOptions canvas)
        {
            return ReportFigureOptions(
                options,
                canvas.PlotWidthCentimeters,
                canvas.PlotHeightCentimeters,
                canvas.FontSize);
        }

        static PublicationFigureOptions FinalFigureOptions(AnalysisReportOptions options) =>
            ReportFigureOptions(options, FinalFigureWidthCentimeters, FinalFigureHeightCentimeters, 14);

        static PublicationFigureOptions ReportFigureOptions(
            AnalysisReportOptions options,
            double width,
            double height,
            double fontSize)
        {
            return new PublicationFigureOptions
            {
                PlotWidthCentimeters = width,
                PlotHeightCentimeters = height,
                PointsPerCentimeter = PublicationFigureOptions.DefaultPointsPerCentimeter,
                FontSize = fontSize,
                Font = PublicationFont.LiberationSans,
                EnergyUnitFamily = options.EnergyUnitFamily,
                EnergyUnitOverride = options.EnergyUnitOverride,
                TimeUnit = TimeUnit.Minute,
                ShowThermogram = true,
                ShowResiduals = true,
                ShowErrorBars = true,
                ShowConfidenceBand = true,
                ShowExperimentDetails = false,
                ShowFitParameters = false,
                ShowAxisTitles = true,
                ShowFitLine = true,
                DrawFitOffsetCorrected = true,
                ShowBadData = true,
                ShowBadDataErrorBars = false,
                AutoAxesIgnoresBadData = true,
                IncludeResidualGraphGap = true,
                SanitizeTicks = true,
                DrawBaselineCorrected = true,
                ShowBaseline = false,
                BaselineStyle = PublicationBaselineStyle.Solid,
                BaselineLayer = PublicationBaselineLayer.OverData,
                BaselineWidth = 1,
                ShowIntegrationRegions = false,
                IntegrationRegionStyle = PublicationIntegrationRegionStyle.Fill,
                ShowZeroLine = true,
                DataXTickCount = 5,
                DataYTickCount = 5,
                FitXTickCount = 5,
                FitYTickCount = 5,
                ResidualYTickCount = 3,
                ResidualPanelFraction = 0.2,
                InformationBoxPlacement = PublicationInfoBoxPlacement.Auto,
                SymbolShape = PublicationSymbolShape.Circle,
                SymbolSize = fontSize <= 6 ? 3 : 5,
                FitLineWidth = 1.5,
                FitLineSmoothness = LineSmoothness.Linear,
                PowerAxisTitle = "Differential Power (<unit>)",
                TimeAxisTitle = "Time (<unit>)",
                EnthalpyAxisTitle = "<unit> of injectant",
                XAxisTitle = null,
                DisplayParameters = FinalFigureDisplayParameters.None,
                AttributeOptions = DisplayAttributeOptions.None,
                TextUncertaintyStyle = options.UncertaintyDisplayStyle,
            };
        }

        static AnalysisReportKeyValueItem Item(string label, string value, int indentLevel = 0)
        {
            return new AnalysisReportKeyValueItem(
                label,
                string.IsNullOrWhiteSpace(value) ? "Unavailable" : value,
                indentLevel);
        }

        static string ModelName(AnalysisResult result)
        {
            var modelType = result?.Model?.ModelType;
            return modelType?.GetProperties()?.Name ?? modelType?.ToString() ?? "Unavailable";
        }

        static string HealthLabel(AnalysisResultHealth health)
        {
            return health switch
            {
                AnalysisResultHealth.Valid => "Valid",
                AnalysisResultHealth.Warning => "Valid with warnings",
                AnalysisResultHealth.PartialInvalid => "Partially invalid or stale",
                AnalysisResultHealth.Invalid => "Invalid or stale",
                _ => "Validity unknown",
            };
        }

        static string FormatDate(DateTime value)
        {
            return value == default
                ? "Unavailable"
                : value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }

        static string FormatNullableDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)
                : "Unavailable";
        }

        static string FormatTemperature(double celsius, bool useKelvin, bool includeUnit = true)
        {
            var value = FormatFinite(DisplayTemperature(celsius, useKelvin), "F2");
            if (!includeUnit || value == "Unavailable") return value;
            return value + (useKelvin ? " K" : " °C");
        }

        static string FormatTemperature(
            FloatWithError celsius,
            bool useKelvin,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            if (FloatWithError.IsNaN(celsius) || !IsFinite(celsius.Value)) return "Unavailable";
            var displayed = useKelvin ? celsius + 273.15 : celsius;
            return displayed.AsNumber(uncertaintyStyle) + (useKelvin ? " K" : " °C");
        }

        static double DisplayTemperature(double celsius, bool useKelvin)
        {
            return useKelvin ? celsius + 273.15 : celsius;
        }

        static string FormatFinite(double value, string format)
        {
            return IsFinite(value)
                ? value.ToString(format, CultureInfo.CurrentCulture)
                : "Unavailable";
        }

        static string FormatDuration(TimeSpan value)
        {
            if (value < TimeSpan.Zero) return "Unavailable";
            if (value.TotalMinutes < 1)
                return value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            return value.TotalHours >= 1
                ? ((int)value.TotalHours).ToString(CultureInfo.InvariantCulture) + " h " + value.Minutes.ToString(CultureInfo.InvariantCulture) + " min"
                : value.Minutes.ToString(CultureInfo.InvariantCulture) + " min " + value.Seconds.ToString(CultureInfo.InvariantCulture) + " s";
        }

        static (string start, string end) IntegrationRanges(IEnumerable<InjectionData> injections)
        {
            var ranges = (injections ?? Enumerable.Empty<InjectionData>())
                .Where(injection => injection != null && injection.IsIntegrated)
                .Select(injection => new
                {
                    Start = (double)injection.IntegrationStartDelay,
                    End = (double)injection.IntegrationEndOffset,
                })
                .Where(range => IsFinite(range.Start) && IsFinite(range.End))
                .ToList();
            if (ranges.Count == 0) return ("Unavailable", "Unavailable");

            var minimumStart = ranges.Min(range => range.Start);
            var maximumStart = ranges.Max(range => range.Start);
            var minimumEnd = ranges.Min(range => range.End);
            var maximumEnd = ranges.Max(range => range.End);
            return (FormatObservedInterval(minimumStart, maximumStart),
                FormatObservedInterval(minimumEnd, maximumEnd));
        }

        static string FormatObservedInterval(double minimum, double maximum) =>
            (Math.Abs(maximum - minimum) < 1e-9
                ? FormatFinite(minimum, "G4")
                : FormatFinite(minimum, "G4") + "–" + FormatFinite(maximum, "G4")) + " s";

        static (string value, string unit) FormatParameter(
            ParameterType parameter,
            FloatWithError value,
            AnalysisResultOverviewTable overview,
            AnalysisReportOptions options,
            bool noUncertainty = false)
        {
            var uncertaintyStyle = noUncertainty ? UncertaintyDisplayStyle.None : options.UncertaintyDisplayStyle;
            var parent = parameter.GetProperties().ParentType;
            string formattedValue;
            string unit;
            if (parent == ParameterType.Affinity1 || parameter == ParameterType.ApparentAffinity)
            {
                var concentrationUnit = ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(Math.Abs(value.Value));
                formattedValue = value.AsFormattedConcentration(
                    concentrationUnit,
                    withunit: false,
                    style: uncertaintyStyle);
                unit = concentrationUnit.GetName();
            }
            else if (parent == ParameterType.Enthalpy1
                || parent == ParameterType.Gibbs1
                || parent == ParameterType.EntropyContribution1
                || parent == ParameterType.Offset
                || parent == ParameterType.HeatCapacity1
                || parent == ParameterType.Entropy1)
            {
                var energyUnit = parent == ParameterType.HeatCapacity1
                    ? overview.ResolvedHeatCapacityUnit
                    : overview.ResolvedEnergyUnit;
                formattedValue = value.Energy.ToFormattedString(
                    energyUnit,
                    withunit: false,
                    perK: parent == ParameterType.HeatCapacity1 || parent == ParameterType.Entropy1,
                    style: uncertaintyStyle);
                unit = energyUnit.GetUnit() + "/mol";
                if (parent == ParameterType.HeatCapacity1 || parent == ParameterType.Entropy1)
                    unit += "·K⁻¹";
            }
            else
            {
                formattedValue = value.AsNumber(uncertaintyStyle);
                unit = "";
            }

            return (formattedValue, unit);
        }

        static string ParameterLabel(ParameterType parameter)
        {
            return parameter.GetProperties()?.Name ?? parameter.ToString();
        }

        static string ThermodynamicSummaryLabel(
            ParameterType parameter,
            IReadOnlyDictionary<ThermodynamicParameterFamily, int> familyCounts)
        {
            if (!ThermodynamicParameterSlots.TryResolve(parameter, out var slot, out var family))
                return ParameterLabel(parameter);
            var symbol = family == ThermodynamicParameterFamily.Enthalpy ? "ΔH"
                : family == ThermodynamicParameterFamily.EntropyContribution ? "−TΔS"
                : family == ThermodynamicParameterFamily.Gibbs ? "ΔG"
                : ParameterLabel(parameter);
            return familyCounts.TryGetValue(family, out var count) && count > 1
                ? symbol + " " + slot.Index.ToString(CultureInfo.CurrentCulture)
                : symbol;
        }

        static string LabeledExperimentName(
            IReadOnlyList<string> labels,
            int index,
            string name,
            int maximumNameCharacters = 0)
        {
            var label = labels != null && index >= 0 && index < labels.Count
                ? labels[index]
                : "";
            name = CompactLabelText(name, maximumNameCharacters);
            return string.IsNullOrWhiteSpace(label)
                ? name ?? ""
                : label + ". " + (name ?? "");
        }

        static string CompactLabelText(string text, int maximumCharacters)
        {
            text = text ?? "";
            if (maximumCharacters <= 0 || text.Length <= maximumCharacters) return text;
            if (maximumCharacters == 1) return "…";
            return text.Substring(0, maximumCharacters - 1).TrimEnd() + "…";
        }

        static (double? sdLower, double? sdUpper, double? ciLower, double? ciUpper) UncertaintyBounds(
            FloatWithError value,
            double scale)
        {
            if (!value.HasError) return (null, null, null, null);
            var sdA = (value.Value - value.SD) * scale;
            var sdB = (value.Value + value.SD) * scale;
            var ciA = value.Lower * scale;
            var ciB = value.Upper * scale;
            return (Math.Min(sdA, sdB), Math.Max(sdA, sdB),
                Math.Min(ciA, ciB), Math.Max(ciA, ciB));
        }

        static int ParameterOrder(ParameterType parameter)
        {
            var values = (ParameterType[])Enum.GetValues(typeof(ParameterType));
            var index = Array.IndexOf(values, parameter);
            return index < 0 ? int.MaxValue : index;
        }

        static bool IsDerivedParameter(ParameterType parameter)
        {
            var parent = parameter.GetProperties().ParentType;
            return parent == ParameterType.Gibbs1
                || parent == ParameterType.Entropy1
                || parent == ParameterType.EntropyContribution1
                || parent == ParameterType.HeatCapacity1;
        }

        static EnergyUnit ResolveMolarEnergyUnit(AnalysisResult result, AnalysisReportOptions options)
        {
            var values = result?.Solution?.Solutions?
                .Where(solution => solution?.ReportParameters != null)
                .SelectMany(solution => solution.ReportParameters)
                .Where(item =>
                {
                    var parent = item.Key.GetProperties().ParentType;
                    return parent == ParameterType.Enthalpy1
                        || parent == ParameterType.Gibbs1
                        || parent == ParameterType.EntropyContribution1
                        || parent == ParameterType.Offset;
                })
                .Select(item => item.Value.Value)
                ?? Enumerable.Empty<double>();
            return EnergyUnitResolver.Resolve(options.EnergyUnitFamily, options.EnergyUnitOverride, values);
        }

        static AnalysisReportPlotPoint PlotPoint(
            double x,
            FloatWithError value,
            double scale,
            string label = "")
        {
            var center = value.Value * scale;
            var lower = value.HasError ? (double?)(value.Lower * scale) : null;
            var upper = value.HasError ? (double?)(value.Upper * scale) : null;
            if (scale < 0)
            {
                var temporary = lower;
                lower = upper;
                upper = temporary;
            }
            return new AnalysisReportPlotPoint(x, center, lower, upper, label);
        }

        static AnalysisReportPlotPoint PlotPointForDisplay(
            double x,
            FloatWithError value,
            double scale,
            string label,
            UncertaintyDisplayStyle style)
        {
            var center = value.Value * scale;
            if (!value.HasError || style == UncertaintyDisplayStyle.None)
                return new AnalysisReportPlotPoint(x, center, label: label);
            var bounds = UncertaintyBounds(value, scale);
            var useConfidenceInterval = style == UncertaintyDisplayStyle.ConfidenceInterval
                || style == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
            var lower = useConfidenceInterval ? bounds.ciLower : bounds.sdLower;
            var upper = useConfidenceInterval ? bounds.ciUpper : bounds.sdUpper;
            return new AnalysisReportPlotPoint(x, center, lower, upper, label)
            {
                StandardDeviationLower = bounds.sdLower,
                StandardDeviationUpper = bounds.sdUpper,
                ConfidenceLower = bounds.ciLower,
                ConfidenceUpper = bounds.ciUpper,
            };
        }

        static AnalysisReportPlotPoint PlotPoint(
            double x,
            double value,
            double scale,
            string label = "")
        {
            return new AnalysisReportPlotPoint(x, value * scale, label: label);
        }

        static AnalysisReportPlotSeries SampledSeries(
            string label,
            IEnumerable<double> xs,
            Func<double, FloatWithError> evaluate,
            double yScale,
            double xScale = 1)
        {
            var envelope = FitEnvelopeBuilder.Build(
                xs.Where(IsFinite).Select(x => x / xScale),
                x =>
                {
                    var value = evaluate(x);
                    return (value.Value, value.Lower, value.Upper);
                });
            return EnvelopeSeries(label, envelope, yScale, xScale);
        }

        static AnalysisReportPlotSeries LinearSeries(
            string label,
            LinearFitWithError fit,
            IEnumerable<double> displayXs,
            double yScale,
            double xScale = 1)
        {
            return EnvelopeSeries(
                label,
                FitEnvelopeBuilder.Build(fit, null, displayXs.Select(x => x / xScale)),
                yScale,
                xScale);
        }

        static AnalysisReportPlotSeries EnvelopeSeries(
            string label,
            IReadOnlyList<FitEnvelopePoint> envelope,
            double yScale,
            double xScale)
        {
            return new AnalysisReportPlotSeries(
                label,
                AnalysisReportPlotSeriesKind.Line,
                envelope.Select(point => new AnalysisReportPlotPoint(
                    point.X * xScale,
                    point.Center * yScale,
                    point.HasBand ? (double?)(point.Lower * yScale) : null,
                    point.HasBand ? (double?)(point.Upper * yScale) : null)));
        }

        static (double min, double max) PlotDomain(IEnumerable<double> values)
        {
            var finite = (values ?? Enumerable.Empty<double>()).Where(IsFinite).ToList();
            if (finite.Count == 0) return (0, 1);
            var min = finite.Min();
            var max = finite.Max();
            if (Math.Abs(max - min) < 1e-12)
            {
                var padding = Math.Max(1, Math.Abs(min) * 0.05);
                return (min - padding, max + padding);
            }
            var extension = 0.05 * (max - min);
            return (min - extension, max + extension);
        }

        static IEnumerable<double> Sample(double min, double max, int count)
        {
            if (count <= 1) return new[] { min };
            return Enumerable.Range(0, count)
                .Select(index => min + (max - min) * index / (count - 1.0));
        }

        static string SpolarFoldedMode(FTSRMethod analysis)
        {
            return (analysis.CompletedFoldedMode ?? analysis.FoldedMode) switch
            {
                FTSRMethod.SRFoldedMode.Glob => "Globular",
                FTSRMethod.SRFoldedMode.Intermediate => "Intermediate",
                FTSRMethod.SRFoldedMode.ID => "Intrinsically disordered",
                _ => "Unavailable",
            };
        }

        static string SpolarTemperatureMode(FTSRMethod analysis)
        {
            return (analysis.CompletedTempMode ?? analysis.TempMode) switch
            {
                FTSRMethod.SRTempMode.IsoEntropicPoint => "Iso-entropic point",
                FTSRMethod.SRTempMode.MeanTemperature => "Mean experimental temperature",
                FTSRMethod.SRTempMode.ReferenceTemperature => "Reference temperature",
                _ => "Unavailable",
            };
        }

        static string SpolarTemperatureLabel(FTSRMethod analysis)
        {
            return (analysis.CompletedTempMode ?? analysis.TempMode) switch
            {
                FTSRMethod.SRTempMode.IsoEntropicPoint => "Iso-entropic temperature",
                FTSRMethod.SRTempMode.MeanTemperature => "Mean temperature",
                _ => "Reference temperature",
            };
        }

        static string NormalizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "section";
            return new string(value.Trim().ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray()).Trim('-');
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
