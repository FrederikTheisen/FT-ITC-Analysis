using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Core.Interpretation
{
    public enum AnalysisInterpretationAudience { MixedScientific, Specialist, GeneralScientific }
    public enum AnalysisInterpretationDetail { Concise, Detailed }
    public enum AnalysisInterpretationInjectionRows { All, IncludedOnly, None }
    public enum AnalysisInterpretationSection
    {
        OverallInterpretation,
        FitQualityObservations,
        ParameterObservations,
        ExperimentComments,
        Limitations,
        SuggestedChecks,
        SuggestedInvestigations,
        MissingInformation,
    }

    public sealed class AnalysisInterpretationOptions
    {
        public AnalysisInterpretationAudience Audience { get; set; } = AnalysisInterpretationAudience.MixedScientific;
        public AnalysisInterpretationDetail Detail { get; set; } = AnalysisInterpretationDetail.Detailed;
        public AnalysisInterpretationInjectionRows InjectionRows { get; set; } = AnalysisInterpretationInjectionRows.All;
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

    public enum InterpretationStatementKind { Observation, Interpretation, Hypothesis }
    public enum InterpretationConfidence { High, Moderate, Low, NotAssessed }
    public enum InterpretationKnowledgeBasis { ExperimentalData, UserContext, GeneralKnowledge, Mixed }
    public enum InterpretationPriority { High, Medium, Low }

    public sealed class AnalysisInterpretationStatement
    {
        public string Text { get; set; } = "";
        public InterpretationStatementKind Kind { get; set; }
        public InterpretationConfidence Confidence { get; set; } = InterpretationConfidence.NotAssessed;
        public InterpretationKnowledgeBasis KnowledgeBasis { get; set; }
        public bool RequiresExternalVerification { get; set; }
        public List<string> EvidenceIds { get; set; } = new List<string>();
        public string ExperimentEvidenceId { get; set; }
        public string ParameterEvidenceId { get; set; }

        internal AnalysisInterpretationStatement Copy() => new AnalysisInterpretationStatement
        {
            Text = Text ?? "", Kind = Kind, Confidence = Confidence, KnowledgeBasis = KnowledgeBasis,
            RequiresExternalVerification = RequiresExternalVerification,
            EvidenceIds = (EvidenceIds ?? new List<string>()).ToList(),
            ExperimentEvidenceId = ExperimentEvidenceId, ParameterEvidenceId = ParameterEvidenceId,
        };
    }

    public sealed class AnalysisInterpretationRecommendation
    {
        public string Title { get; set; } = "";
        public string Rationale { get; set; } = "";
        public string IntendedQuestion { get; set; } = "";
        public InterpretationPriority Priority { get; set; } = InterpretationPriority.Medium;
        public List<string> EvidenceIds { get; set; } = new List<string>();
        public InterpretationKnowledgeBasis KnowledgeBasis { get; set; }
        public bool RequiresExternalVerification { get; set; }
        internal AnalysisInterpretationRecommendation Copy() => new AnalysisInterpretationRecommendation
        {
            Title = Title ?? "", Rationale = Rationale ?? "", IntendedQuestion = IntendedQuestion ?? "",
            Priority = Priority, EvidenceIds = (EvidenceIds ?? new List<string>()).ToList(),
            KnowledgeBasis = KnowledgeBasis, RequiresExternalVerification = RequiresExternalVerification,
        };
    }

    public sealed class AnalysisOverallInterpretation
    {
        public List<AnalysisInterpretationStatement> Interaction { get; set; } = new List<AnalysisInterpretationStatement>();
        public List<AnalysisInterpretationStatement> StudyQuestion { get; set; } = new List<AnalysisInterpretationStatement>();
        public List<AnalysisInterpretationStatement> ExpectedOutcome { get; set; } = new List<AnalysisInterpretationStatement>();
        public List<AnalysisInterpretationStatement> Buffer { get; set; } = new List<AnalysisInterpretationStatement>();
        public List<AnalysisInterpretationStatement> Temperature { get; set; } = new List<AnalysisInterpretationStatement>();
        public List<AnalysisInterpretationStatement> Other { get; set; } = new List<AnalysisInterpretationStatement>();
        internal AnalysisOverallInterpretation Copy() => new AnalysisOverallInterpretation
        {
            Interaction = Copy(Interaction), StudyQuestion = Copy(StudyQuestion), ExpectedOutcome = Copy(ExpectedOutcome),
            Buffer = Copy(Buffer), Temperature = Copy(Temperature), Other = Copy(Other),
        };
        static List<AnalysisInterpretationStatement> Copy(IEnumerable<AnalysisInterpretationStatement> values) =>
            (values ?? Enumerable.Empty<AnalysisInterpretationStatement>()).Select(value => value?.Copy()).Where(value => value != null).ToList();
    }

    public sealed class AnalysisInterpretationDocument
    {
        public AnalysisOverallInterpretation OverallInterpretation { get; set; }
        public List<AnalysisInterpretationStatement> FitQualityObservations { get; set; }
        public List<AnalysisInterpretationStatement> ParameterObservations { get; set; }
        public List<AnalysisInterpretationStatement> ExperimentComments { get; set; }
        public List<AnalysisInterpretationStatement> Limitations { get; set; }
        public List<AnalysisInterpretationRecommendation> SuggestedChecks { get; set; }
        public List<AnalysisInterpretationRecommendation> SuggestedInvestigations { get; set; }
        public List<AnalysisInterpretationStatement> MissingInformation { get; set; }

        public AnalysisInterpretationDocument Copy() => new AnalysisInterpretationDocument
        {
            OverallInterpretation = OverallInterpretation?.Copy(),
            FitQualityObservations = Copy(FitQualityObservations), ParameterObservations = Copy(ParameterObservations),
            ExperimentComments = Copy(ExperimentComments), Limitations = Copy(Limitations),
            SuggestedChecks = Copy(SuggestedChecks), SuggestedInvestigations = Copy(SuggestedInvestigations),
            MissingInformation = Copy(MissingInformation),
        };
        internal IEnumerable<AnalysisInterpretationStatement> AllStatements()
        {
            if (OverallInterpretation != null)
            {
                foreach (var item in OverallInterpretation.Interaction ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
                foreach (var item in OverallInterpretation.StudyQuestion ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
                foreach (var item in OverallInterpretation.ExpectedOutcome ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
                foreach (var item in OverallInterpretation.Buffer ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
                foreach (var item in OverallInterpretation.Temperature ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
                foreach (var item in OverallInterpretation.Other ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
            }
            foreach (var item in FitQualityObservations ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
            foreach (var item in ParameterObservations ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
            foreach (var item in ExperimentComments ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
            foreach (var item in Limitations ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
            foreach (var item in MissingInformation ?? Enumerable.Empty<AnalysisInterpretationStatement>()) yield return item;
        }
        static List<AnalysisInterpretationStatement> Copy(IEnumerable<AnalysisInterpretationStatement> values) => values?.Select(value => value?.Copy()).Where(value => value != null).ToList();
        static List<AnalysisInterpretationRecommendation> Copy(IEnumerable<AnalysisInterpretationRecommendation> values) => values?.Select(value => value?.Copy()).Where(value => value != null).ToList();
    }

    public sealed class AnalysisInterpretationRecord
    {
        public AnalysisInterpretationDocument Interpretation { get; set; }
        public string InputFingerprint { get; set; } = "";
        public string PromptVersion { get; set; } = "";
        public string OutputSchemaVersion { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string ServiceRequestId { get; set; } = "";
        public DateTime GeneratedAtUtc { get; set; }
        public DateTime ApprovedAtUtc { get; set; }
        public bool UserEdited { get; set; }

        public AnalysisInterpretationRecord Copy() => new AnalysisInterpretationRecord
        {
            Interpretation = Interpretation?.Copy(), InputFingerprint = InputFingerprint ?? "",
            PromptVersion = PromptVersion ?? "", OutputSchemaVersion = OutputSchemaVersion ?? "",
            Provider = Provider ?? "", Model = Model ?? "", ServiceRequestId = ServiceRequestId ?? "",
            GeneratedAtUtc = GeneratedAtUtc, ApprovedAtUtc = ApprovedAtUtc, UserEdited = UserEdited,
        };
    }
}
