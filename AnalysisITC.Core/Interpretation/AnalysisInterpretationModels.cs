using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Core.Interpretation
{
    public enum AnalysisInterpretationAudience { MixedScientific, Specialist, GeneralScientific }
    public enum AnalysisInterpretationDetail { Concise, Detailed }
    public enum AnalysisInterpretationInjectionRows { All, IncludedOnly, None }
    public enum AnalysisInterpretationOrigin { AiGenerated, Manual }
    public enum AnalysisInterpretationSection
    {
        OverallInterpretation,
        ExperimentObservations,
        Limitations,
        SuggestedChecks,
        SuggestedInvestigations,
    }

    public sealed class AnalysisInterpretationOptions
    {
        public AnalysisInterpretationAudience Audience { get; set; } = AnalysisInterpretationAudience.MixedScientific;
        public AnalysisInterpretationDetail Detail { get; set; } = AnalysisInterpretationDetail.Detailed;
        public AnalysisInterpretationInjectionRows InjectionRows { get; set; } = AnalysisInterpretationInjectionRows.All;
        // Thermograms are an advanced, opt-in input because the compressed traces
        // are comparatively expensive and their extrema do not replace the full
        // acquisition. Privileged users can enable them in the report dialog.
        public bool IncludeThermograms { get; set; } = false;
        public bool AllowGeneralModelKnowledge { get; set; } = true;
        public List<AnalysisInterpretationSection> RequestedSections { get; set; } =
            Enum.GetValues(typeof(AnalysisInterpretationSection)).Cast<AnalysisInterpretationSection>().ToList();

        public static AnalysisInterpretationOptions Default() => new AnalysisInterpretationOptions();

        public AnalysisInterpretationOptions Copy() => new AnalysisInterpretationOptions
        {
            Audience = Audience,
            Detail = Detail,
            InjectionRows = InjectionRows,
            AllowGeneralModelKnowledge = AllowGeneralModelKnowledge,
            IncludeThermograms = IncludeThermograms,
            RequestedSections = (RequestedSections ?? new List<AnalysisInterpretationSection>()).Distinct().ToList(),
        };
    }

    public sealed class AnalysisStudyContext
    {
        public string ScientificQuestion { get; set; } = "";
        public string SystemDescription { get; set; } = "";
        public string ComponentTypes { get; set; } = "";
        public string InteractionConsiderations { get; set; } = "";
        public string ExpectedOutcome { get; set; } = "";
        public string CellContentsAndRole { get; set; } = "";
        public string SyringeContentsAndRole { get; set; } = "";
        public string RelatedSystemsOrConstructs { get; set; } = "";
        public string PreviousResultsAndControls { get; set; } = "";
        public string BufferConsiderations { get; set; } = "";
        public string TemperatureConsiderations { get; set; } = "";
        public string AdditionalNotes { get; set; } = "";
        public List<AnalysisExperimentContext> Experiments { get; set; } = new List<AnalysisExperimentContext>();
        public List<AnalysisUserReference> References { get; set; } = new List<AnalysisUserReference>();

        public AnalysisStudyContext Copy() => new AnalysisStudyContext
        {
            ScientificQuestion = ScientificQuestion ?? "",
            SystemDescription = SystemDescription ?? "",
            ComponentTypes = ComponentTypes ?? "",
            InteractionConsiderations = InteractionConsiderations ?? "",
            ExpectedOutcome = ExpectedOutcome ?? "",
            CellContentsAndRole = CellContentsAndRole ?? "",
            SyringeContentsAndRole = SyringeContentsAndRole ?? "",
            RelatedSystemsOrConstructs = RelatedSystemsOrConstructs ?? "",
            PreviousResultsAndControls = PreviousResultsAndControls ?? "",
            BufferConsiderations = BufferConsiderations ?? "",
            TemperatureConsiderations = TemperatureConsiderations ?? "",
            AdditionalNotes = AdditionalNotes ?? "",
            Experiments = (Experiments ?? new List<AnalysisExperimentContext>()).Select(value => value?.Copy()).Where(value => value != null).ToList(),
            References = (References ?? new List<AnalysisUserReference>()).Select(value => value?.Copy()).Where(value => value != null).ToList(),
        };
    }

    public sealed class AnalysisExperimentContext
    {
        public string ExperimentId { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string Annotation { get; set; } = "";
        internal AnalysisExperimentContext Copy() => new AnalysisExperimentContext
        { ExperimentId = ExperimentId ?? "", Purpose = Purpose ?? "", Annotation = Annotation ?? "" };
    }

    public sealed class AnalysisUserReference
    {
        public string Label { get; set; } = "";
        public string CitationOrUrl { get; set; } = "";
        public string Notes { get; set; } = "";
        internal AnalysisUserReference Copy() => new AnalysisUserReference
        { Label = Label ?? "", CitationOrUrl = CitationOrUrl ?? "", Notes = Notes ?? "" };
    }

    public sealed class AnalysisInterpretationRecord
    {
        public string TaskType { get; set; } = "interpretation";
        public AnalysisInterpretationOrigin Origin { get; set; } = AnalysisInterpretationOrigin.AiGenerated;
        public string InterpretationMarkdown { get; set; } = "";
        public string InputFingerprint { get; set; } = "";
        public string EffectiveInputFingerprint { get; set; } = "";
        public List<string> Omissions { get; set; } = new List<string>();
        public List<string> KnowledgeBaseIds { get; set; } = new List<string>();
        public List<string> RetrievedSourceIds { get; set; } = new List<string>();
        public string PromptVersion { get; set; } = "";
        public string OutputFormatVersion { get; set; } = "";
        public string EvidenceFingerprintScheme { get; set; } = "";
        public string ScientificGuidanceRevision { get; set; } = "";
        public string ScientificGuidanceVariant { get; set; } = "";
        public string ScientificInstructionsFingerprint { get; set; } = "";
        public string OutputInstructionsFingerprint { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string ReasoningEffort { get; set; } = "";
        public string EffectivePreset { get; set; } = "";
        public string PresetRevision { get; set; } = "";
        public string ServiceRequestId { get; set; } = "";
        public DateTime GeneratedAtUtc { get; set; }
        public DateTime ApprovedAtUtc { get; set; }
        public bool UserEdited { get; set; }

        public AnalysisInterpretationRecord Copy() => new AnalysisInterpretationRecord
        {
            Origin = Origin,
            EffectiveInputFingerprint = EffectiveInputFingerprint ?? "",
            Omissions = (Omissions ?? new List<string>()).ToList(),
            KnowledgeBaseIds = (KnowledgeBaseIds ?? new List<string>()).ToList(),
            RetrievedSourceIds = (RetrievedSourceIds ?? new List<string>()).ToList(),
            TaskType = string.IsNullOrWhiteSpace(TaskType) ? "interpretation" : TaskType,
            InterpretationMarkdown = InterpretationMarkdown ?? "", InputFingerprint = InputFingerprint ?? "",
            PromptVersion = PromptVersion ?? "", OutputFormatVersion = OutputFormatVersion ?? "",
            EvidenceFingerprintScheme = EvidenceFingerprintScheme ?? "", ScientificGuidanceRevision = ScientificGuidanceRevision ?? "",
            ScientificGuidanceVariant = ScientificGuidanceVariant ?? "",
            ScientificInstructionsFingerprint = ScientificInstructionsFingerprint ?? "", OutputInstructionsFingerprint = OutputInstructionsFingerprint ?? "",
            Provider = Provider ?? "", Model = Model ?? "", ReasoningEffort = ReasoningEffort ?? "", EffectivePreset = EffectivePreset ?? "", PresetRevision = PresetRevision ?? "", ServiceRequestId = ServiceRequestId ?? "",
            GeneratedAtUtc = GeneratedAtUtc, ApprovedAtUtc = ApprovedAtUtc, UserEdited = UserEdited,
        };
    }
}
