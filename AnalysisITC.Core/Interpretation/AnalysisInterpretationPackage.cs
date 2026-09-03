using System.Collections.Generic;

namespace AnalysisITC.Core.Interpretation
{
    public sealed class AnalysisInterpretationPackage
    {
        public string PackageSchemaVersion { get; set; } = AnalysisInterpretationPackageBuilder.PackageSchemaVersion;
        public InterpretationReportEvidence Report { get; set; }
        public InterpretationResultEvidence Result { get; set; }
        public AnalysisStudyContext StudyContext { get; set; }
        public AnalysisInterpretationOptions RequestedInterpretation { get; set; }
        public List<InterpretationEvidenceCatalogEntry> EvidenceCatalog { get; set; } = new List<InterpretationEvidenceCatalogEntry>();
        public InterpretationDataBoundary DataBoundary { get; set; } = new InterpretationDataBoundary();
    }

    public sealed class InterpretationDataBoundary
    {
        public bool ContainsRawThermogramSamples { get; set; }
        public bool ContainsBaselineArrays { get; set; }
        public bool ContainsBootstrapReplicateArrays { get; set; }
        public bool ContainsLocalPaths { get; set; }
        public string ModelObservationRestriction { get; set; } =
            "Thermogram and baseline data were not supplied; do not make observations about their shape or quality.";
    }

    public sealed class InterpretationEvidenceCatalogEntry
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Label { get; set; }
        public string ParentId { get; set; }
    }

    public sealed class InterpretationReportEvidence
    {
        public string EvidenceId { get; set; }
        public string ReportId { get; set; }
        public string Name { get; set; }
        public string DateUtc { get; set; }
        public string AuthorComments { get; set; }
        public List<string> ResultIds { get; set; } = new List<string>();
    }

    public sealed class InterpretationResultEvidence
    {
        public string EvidenceId { get; set; }
        public string ResultId { get; set; }
        public string Name { get; set; }
        public string DateUtc { get; set; }
        public string Comments { get; set; }
        public string Health { get; set; }
        public string ValidityStatus { get; set; }
        public List<string> ValidityReasons { get; set; } = new List<string>();
        public InterpretationModelEvidence Model { get; set; }
        public InterpretationSolverEvidence Solver { get; set; }
        public InterpretationInformationCriteriaEvidence InformationCriteria { get; set; }
        public List<InterpretationExperimentEvidence> Experiments { get; set; } = new List<InterpretationExperimentEvidence>();
        public List<InterpretationTemperatureDependenceEvidence> TemperatureDependence { get; set; } = new List<InterpretationTemperatureDependenceEvidence>();
        public List<InterpretationAdvancedAnalysisEvidence> AdvancedAnalyses { get; set; } = new List<InterpretationAdvancedAnalysisEvidence>();
        public InterpretationCorrelationEvidence BootstrapCorrelation { get; set; }
        public List<InterpretationCorrelationEvidence> BootstrapCorrelations { get; set; } = new List<InterpretationCorrelationEvidence>();
    }

    public sealed class InterpretationModelEvidence
    {
        public string Type { get; set; }
        public bool IsGlobal { get; set; }
        public bool UsesWeightedFitting { get; set; }
        public List<InterpretationNamedValue> Options { get; set; } = new List<InterpretationNamedValue>();
        public List<InterpretationConstraintEvidence> Constraints { get; set; } = new List<InterpretationConstraintEvidence>();
    }

    public sealed class InterpretationSolverEvidence
    {
        public string Algorithm { get; set; }
        public string Termination { get; set; }
        public string FailureReason { get; set; }
        public int Iterations { get; set; }
        public bool UsesWeightedObjective { get; set; }
        public double? UnweightedRmsdMicrojoules { get; set; }
        public double? UnweightedMolarRmsdJoulesPerMole { get; set; }
        public string ErrorEstimationMethod { get; set; }
        public string ErrorEstimationOutcome { get; set; }
        public string ErrorEstimationSummary { get; set; }
        public int BootstrapIterationCount { get; set; }
        public int? AttemptedUncertaintyRefits { get; set; }
        public int? SuccessfulUncertaintyRefits { get; set; }
        public int? FailedUncertaintyRefits { get; set; }
        public int ExcludedLimitTerminations { get; set; }
        public InterpretationProfileLikelihoodEvidence ProfileLikelihood { get; set; }
    }

    public sealed class InterpretationInformationCriteriaEvidence
    {
        public int ObservationCount { get; set; }
        public int FittedParameterCount { get; set; }
        public int LikelihoodParameterCount { get; set; }
        public bool UsesKnownObservationSigmas { get; set; }
        public InterpretationAvailableNumber MinusTwoLogLikelihood { get; set; }
        public InterpretationAvailableNumber Aic { get; set; }
        public InterpretationAvailableNumber Aicc { get; set; }
    }

    public sealed class InterpretationAvailableNumber
    {
        public bool IsAvailable { get; set; }
        public double? Value { get; set; }
        public string UnavailableReason { get; set; }
    }

    public sealed class InterpretationExperimentEvidence
    {
        public string EvidenceId { get; set; }
        public string ExperimentId { get; set; }
        public string Name { get; set; }
        public string SourceFileBasename { get; set; }
        public string DateUtc { get; set; }
        public string Comments { get; set; }
        public string Instrument { get; set; }
        public double? TargetTemperatureKelvin { get; set; }
        public double? MeasuredTemperatureKelvin { get; set; }
        public double? CellConcentrationMolar { get; set; }
        public double? CellConcentrationSdMolar { get; set; }
        public double? SyringeConcentrationMolar { get; set; }
        public double? SyringeConcentrationSdMolar { get; set; }
        public double? CellVolumeLitres { get; set; }
        public double? StirringSpeedRpm { get; set; }
        public string FeedbackMode { get; set; }
        public string AnalysisAxis { get; set; }
        public bool BaselineCompleted { get; set; }
        public bool IntegrationCompleted { get; set; }
        public string BaselineProcessor { get; set; }
        public bool ProcessorLocked { get; set; }
        public bool DiscardsIntegratedPointsForBaseline { get; set; }
        public string IntegrationLengthMode { get; set; }
        public double? IntegrationLengthFactor { get; set; }
        public double? InitialDelaySeconds { get; set; }
        public List<InterpretationNamedValue> Attributes { get; set; } = new List<InterpretationNamedValue>();
        public List<InterpretationNamedValue> ModelOptions { get; set; } = new List<InterpretationNamedValue>();
        public List<InterpretationParameterEvidence> Parameters { get; set; } = new List<InterpretationParameterEvidence>();
        public List<InterpretationInjectionEvidence> Injections { get; set; } = new List<InterpretationInjectionEvidence>();
    }

    public sealed class InterpretationNamedValue
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string TextValue { get; set; }
        public double? NumericValue { get; set; }
        public bool? BooleanValue { get; set; }
    }

    public sealed class InterpretationConstraintEvidence
    {
        public string FittedCoordinateId { get; set; }
        public string Constraint { get; set; }
    }

    public sealed class InterpretationParameterEvidence
    {
        public string EvidenceId { get; set; }
        public string QuantityId { get; set; }
        public string FittedCoordinateId { get; set; }
        public string Name { get; set; }
        public string SiUnit { get; set; }
        public double? BestFitValue { get; set; }
        public double? StandardDeviation { get; set; }
        public double? Confidence95Lower { get; set; }
        public double? Confidence95Upper { get; set; }
        public bool IsFittedCoordinate { get; set; }
        public bool IsDerived { get; set; }
        public bool IsLocked { get; set; }
        public string Constraint { get; set; }
        public bool BoundaryWarning { get; set; }
        public double? FittedLowerBound { get; set; }
        public double? FittedUpperBound { get; set; }
        public string UncertaintyMethod { get; set; }
    }

    public sealed class InterpretationInjectionEvidence
    {
        public string EvidenceId { get; set; }
        public int InjectionId { get; set; }
        public bool Included { get; set; }
        public double? VolumeLitres { get; set; }
        public double? ActualCellConcentrationMolar { get; set; }
        public double? ActualTitrantConcentrationMolar { get; set; }
        public string AnalysisAxisKind { get; set; }
        public double? AnalysisAxisValue { get; set; }
        public double? ObservedHeatJoulesPerMole { get; set; }
        public double? ObservedUncertaintyJoulesPerMole { get; set; }
        public double? FittedHeatJoulesPerMole { get; set; }
        public double? ResidualJoulesPerMole { get; set; }
        public double? Confidence95LowerJoulesPerMole { get; set; }
        public double? Confidence95UpperJoulesPerMole { get; set; }
    }

    public sealed class InterpretationTemperatureDependenceEvidence
    {
        public string ParameterId { get; set; }
        public string SiUnit { get; set; }
        public double? ReferenceTemperatureKelvin { get; set; }
        public double? InterceptSi { get; set; }
        public double? SlopeSiPerKelvin { get; set; }
    }

    public sealed class InterpretationAdvancedAnalysisEvidence
    {
        public string Type { get; set; }
        public string Status { get; set; }
        public int CompletedIterations { get; set; }
        public string CompletedAtUtc { get; set; }
        public string UncertaintyMethod { get; set; }
        public List<InterpretationAdvancedValue> Values { get; set; } = new List<InterpretationAdvancedValue>();
    }

    public sealed class InterpretationAdvancedValue
    {
        public string Id { get; set; }
        public string SiUnit { get; set; }
        public double? Value { get; set; }
        public double? StandardDeviation { get; set; }
        public double? Confidence95Lower { get; set; }
        public double? Confidence95Upper { get; set; }
    }

    public sealed class InterpretationProfileLikelihoodEvidence
    {
        public string Calibration { get; set; }
        public string Outcome { get; set; }
        public double? ConfidenceLevel { get; set; }
        public int ObservationCount { get; set; }
        public int ParameterCount { get; set; }
        public int DegreesOfFreedom { get; set; }
        public int AttemptedSolverCalls { get; set; }
        public int CompleteIntervalCount { get; set; }
        public int CoordinateCount { get; set; }
        public List<string> Diagnostics { get; set; } = new List<string>();
    }

    public sealed class InterpretationCorrelationEvidence
    {
        public string ScopeEvidenceId { get; set; }
        public string Availability { get; set; }
        public string Reason { get; set; }
        public int CompleteReplicateCount { get; set; }
        public List<string> CoordinateLabels { get; set; } = new List<string>();
        public List<List<double?>> PearsonMatrix { get; set; } = new List<List<double?>>();
        public bool RankLimited { get; set; }
        public bool CoarseMonteCarloPrecision { get; set; }
        public bool FrequentFailures { get; set; }
        public int UncertainSignPairCount { get; set; }
    }
}
