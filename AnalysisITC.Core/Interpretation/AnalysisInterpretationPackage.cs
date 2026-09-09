using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Interpretation
{
    public sealed class AnalysisInterpretationPackage
    {
        public string PackageSchemaVersion { get; set; } = AnalysisInterpretationPackageBuilder.PackageSchemaVersion;
        public InterpretationReportEvidence Report { get; set; }
        public List<InterpretationResultEvidence> Results { get; set; } = new List<InterpretationResultEvidence>();
        public List<InterpretationExperimentEvidence> SupportingExperiments { get; set; } = new List<InterpretationExperimentEvidence>();
        public List<string> Omissions { get; set; } = new List<string>();
        [JsonIgnore]
        public InterpretationResultEvidence Result { get => Results?.FirstOrDefault(); set => Results = value == null ? new List<InterpretationResultEvidence>() : new List<InterpretationResultEvidence> { value }; }
        public AnalysisStudyContext StudyContext { get; set; }
        public AnalysisInterpretationOptions RequestedInterpretation { get; set; }
        public List<InterpretationEvidenceCatalogEntry> EvidenceCatalog { get; set; } = new List<InterpretationEvidenceCatalogEntry>();
        public InterpretationDataBoundary DataBoundary { get; set; } = new InterpretationDataBoundary();
    }

    public sealed class InterpretationThermogramEvidence
    {
        public string Encoding { get; set; } = "uniform-minmax-v1";
        public double BinWidthSeconds { get; set; } = 15;
        public double AnchorTimeSeconds { get; set; }
        public string PowerUnit { get; set; } = "µW";
        public double PowerOffsetWatts { get; set; }
        public string OffsetMethod { get; set; } = "Median of finite raw power samples; numerical centering only, not baseline subtraction";
        public string ReversalFormula { get; set; } = "powerWatts = pairValueMicrowatts / 1000000 + powerOffsetWatts; the same formula applies to baseline pair values";
        public int SourceSampleCount { get; set; }
        public int FiniteSampleCount { get; set; }
        public string Limitation { get; set; } = "Array position i represents the half-open interval [anchorTimeSeconds + i × binWidthSeconds, anchorTimeSeconds + (i + 1) × binWidthSeconds); the final interval may be partial. Each pair contains the minimum and maximum finite original values, with [null, null] for an interval without observations. Pair order is value order, not chronological order; exact occurrence times, within-interval order and waveform are unavailable. Do not infer precise settling or integration adequacy from extrema alone.";
        public List<double?[]> PowerMinMax { get; set; } = new List<double?[]>();
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<double?[]> BaselineMinMax { get; set; }
    }

    public sealed class InterpretationTandemSegmentEvidence
    {
        public int FirstInjectionId { get; set; }
        public double? StartTimeSeconds { get; set; }
        public double? EndTimeSeconds { get; set; }
        public double? InitialActiveCellConcentrationMolar { get; set; }
        public double? InitialActiveTitrantConcentrationMolar { get; set; }
    }

    public sealed class InterpretationProfileCoordinateEvidence
    {
        public string FittedCoordinateId { get; set; }
        public string Scope { get; set; }
        public string ExperimentId { get; set; }
        public double? BestFitValue { get; set; }
        public double? LowerBound { get; set; }
        public double? UpperBound { get; set; }
        public InterpretationProfileSideEvidence Lower { get; set; }
        public InterpretationProfileSideEvidence Upper { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class InterpretationProfileSideEvidence
    {
        public string Outcome { get; set; }
        public double? Endpoint { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class InterpretationDataBoundary
    {
        public bool ContainsRawThermogramSamples { get; set; }
        public bool ContainsBaselineArrays { get; set; }
        public bool ContainsBootstrapReplicateArrays { get; set; }
        public bool ContainsLocalPaths { get; set; }
        public bool ContainsBaselineSummary { get; set; } = true;
        public bool ContainsBaselineControlRepresentation { get; set; } = true;
        public string ModelObservationRestriction { get; set; } =
            "Compressed value bounds preserve original observations but omit exact occurrence times, within-segment order and waveform detail. Do not infer precise settling or integration adequacy from extrema alone. Use timing, fitted baseline and processing evidence where supplied.";
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
        public List<InterpretationReportReference> References { get; set; } = new List<InterpretationReportReference>();
    }

    public sealed class InterpretationReportReference
    {
        public string ReportReference { get; set; }
        public string Kind { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string ParentReference { get; set; }
    }

    public sealed class InterpretationResultEvidence
    {
        public string EvidenceId { get; set; }
        public string ReportReference { get; set; }
        public string ResultId { get; set; }
        public string Name { get; set; }
        public string DateUtc { get; set; }
        public string Comments { get; set; }
        public string Health { get; set; }
        public string ValidityStatus { get; set; }
        public List<string> ValidityReasons { get; set; } = new List<string>();
        public string MatchedFitDiagnosticsUnavailableReason { get; set; }
        public List<System.Text.Json.JsonElement> HistoricalFitInputs { get; set; } = new List<System.Text.Json.JsonElement>();
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
        // Absent in older evidence packages; do not infer a convention from that absence.
        public GaussianLikelihoodMode? LikelihoodMode { get; set; }
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
        public string ReportReference { get; set; }
        public string ExperimentId { get; set; }
        public string Name { get; set; }
        public string SourceFileBasename { get; set; }
        public InterpretationSolverEvidence Solver { get; set; }
        public string DateProvenance { get; set; }
        public string SourceStateFingerprint { get; set; }
        public string EvidenceBasis { get; set; } = "Current experiment and processing state";
        public string MatchedFitDiagnosticsUnavailableReason { get; set; }
        public InterpretationInformationCriteriaEvidence InformationCriteria { get; set; }
        public InterpretationThermogramEvidence Thermogram { get; set; }
        public string UnavailableDerivedParameterReason { get; set; }
        public List<InterpretationTandemSegmentEvidence> TandemSegments { get; set; } = new List<InterpretationTandemSegmentEvidence>();
        public string BlankReferenceExperimentId { get; set; }
        public string BlankSubtractionMethod { get; set; }
        public string DateUtc { get; set; }
        public string Comments { get; set; }
        public InterpretationInstrumentEvidence Instrument { get; set; }
        public double? TargetTemperatureKelvin { get; set; }
        public double? MeasuredTemperatureKelvin { get; set; }
        public double? TargetTemperatureCelsius { get; set; }
        public double? MeasuredTemperatureCelsius { get; set; }
        public double? CellConcentrationMolar { get; set; }
        public double? CellConcentrationSdMolar { get; set; }
        public double? SyringeConcentrationMolar { get; set; }
        public double? SyringeConcentrationSdMolar { get; set; }
        public string AnalysisAxis { get; set; }
        public bool BaselineCompleted { get; set; }
        public bool IntegrationCompleted { get; set; }
        public string BaselineProcessor { get; set; }
        public bool ProcessorLocked { get; set; }
        public bool DiscardsIntegratedPointsForBaseline { get; set; }
        public string IntegrationLengthMode { get; set; }
        public double? IntegrationLengthFactor { get; set; }
        public double? InitialDelaySeconds { get; set; }
        public InterpretationBaselineEvidence Baseline { get; set; }
        public InterpretationResidualDiagnosticsEvidence ResidualDiagnostics { get; set; }
        public List<InterpretationNamedValue> Attributes { get; set; } = new List<InterpretationNamedValue>();
        public List<InterpretationNamedValue> ModelOptions { get; set; } = new List<InterpretationNamedValue>();
        public List<InterpretationParameterEvidence> Parameters { get; set; } = new List<InterpretationParameterEvidence>();
        public List<InterpretationInjectionEvidence> Injections { get; set; } = new List<InterpretationInjectionEvidence>();
    }

    public sealed class InterpretationInstrumentEvidence
    {
        public string ModelId { get; set; }
        public string ModelName { get; set; }
        public double? CellVolumeLitres { get; set; }
        public string FeedbackMode { get; set; }
        public double? StirringSpeedRpm { get; set; }
        public double? FilterPeriodSeconds { get; set; }
        public bool FilterPeriodVariesByInjection { get; set; }
        public double? MinimumFilterPeriodSeconds { get; set; }
        public double? MaximumFilterPeriodSeconds { get; set; }
    }

    public sealed class InterpretationNamedValue
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
        public string TextValue { get; set; }
        public string DisplayValue { get; set; }
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
        public bool Confidence95Available { get; set; }
        public string Confidence95UnavailableReason { get; set; }
    }

    public sealed class InterpretationInjectionEvidence
    {
        public string EvidenceId { get; set; }
        public int InjectionId { get; set; }
        public bool Included { get; set; }
        public double? TimeSeconds { get; set; }
        public double? DurationSeconds { get; set; }
        public double? IntegrationStartTimeSeconds { get; set; }
        public double? IntegrationEndTimeSeconds { get; set; }
        public double? NonIntegratedIntervalBeforeNextInjectionSeconds { get; set; }
        public bool IsIntegrated { get; set; }
        public double? IntegratedHeatBeforeSubtractionJoules { get; set; }
        public double? IntegratedHeatBeforeSubtractionErrorJoules { get; set; }
        public double? VolumeLitres { get; set; }
        public double? ActiveCellConcentrationMolar { get; set; }
        public double? ActiveTitrantConcentrationMolar { get; set; }
        public double? InjectionDelaySeconds { get; set; }
        public double? FilterPeriodSeconds { get; set; }
        public double? IntegrationStartDelaySeconds { get; set; }
        public double? IntegrationEndOffsetSeconds { get; set; }
        public double? IntegrationLengthSeconds { get; set; }
        public double? IntegrationLengthFractionOfInjectionDelay { get; set; }
        public string AnalysisAxisKind { get; set; }
        public double? AnalysisAxisValue { get; set; }
        public double? IntegratedHeatJoules { get; set; }
        public double? IntegratedHeatErrorJoules { get; set; }
        public double? ObservedHeatJoulesPerMole { get; set; }
        public double? ObservedHeatErrorJoulesPerMole { get; set; }
        public double? FittedHeatJoulesPerMole { get; set; }
        public double? ResidualJoulesPerMole { get; set; }
        public double? Confidence95LowerJoulesPerMole { get; set; }
        public double? Confidence95UpperJoulesPerMole { get; set; }
        public double? BaselineAtIntegrationStartMicrowatts { get; set; }
        public double? BaselineAtIntegrationEndMicrowatts { get; set; }
        public double? BaselineChangeAcrossIntegrationMicrowatts { get; set; }
        public double? IntegratedBaselineCorrectionJoules { get; set; }
    }

    public sealed class InterpretationTemperatureDependenceEvidence
    {
        public string ParameterId { get; set; }
        public string SiUnit { get; set; }
        public double? ReferenceTemperatureKelvin { get; set; }
        public double? ReferenceTemperatureCelsius { get; set; }
        public double? InterceptSi { get; set; }
        public double? SlopeSiPerKelvin { get; set; }
    }

    public sealed class InterpretationBaselineEvidence
    {
        public string EvidenceId { get; set; }
        public string Method { get; set; }
        public bool Completed { get; set; }
        public bool Locked { get; set; }
        public bool IntegrationRegionsExcluded { get; set; }
        public bool IsAvailable { get; set; }
        public string UnavailableReason { get; set; }
        public double? TraceDurationSeconds { get; set; }
        public double? StartPowerMicrowatts { get; set; }
        public double? EndPowerMicrowatts { get; set; }
        public double? NetDriftMicrowatts { get; set; }
        public double? LinearDriftRateMicrowattsPerHour { get; set; }
        public string LinearTrendUnavailableReason { get; set; }
        public double? RangeMicrowatts { get; set; }
        public double? RmsDeviationFromLinearTrendMicrowatts { get; set; }
        public double? OutsideIntegrationRmsRawMinusBaselineMicrowatts { get; set; }
        public double? OutsideIntegrationMedianAbsoluteDeviationRawMinusBaselineMicrowatts { get; set; }
        public int OutsideIntegrationPointCount { get; set; }
        public string OutsideIntegrationStatisticsUnavailableReason { get; set; }
        public List<InterpretationBaselineLandmark> Landmarks { get; set; } = new List<InterpretationBaselineLandmark>();
        public InterpretationSplineBaselineControls Spline { get; set; }
        public InterpretationSegmentedBaselineControls Segmented { get; set; }
        public InterpretationPolynomialBaselineControls Polynomial { get; set; }
        public InterpretationAsymmetricLeastSquaresControls AsymmetricLeastSquares { get; set; }
    }

    public sealed class InterpretationBaselineLandmark
    {
        public double? TimeSeconds { get; set; }
        public double? PowerMicrowatts { get; set; }
    }

    public sealed class InterpretationSplineBaselineControls
    {
        public string Algorithm { get; set; }
        public string Density { get; set; }
        public string HandleMode { get; set; }
        public int PointsPerInjection { get; set; }
        public List<InterpretationSplineControlPoint> ControlPoints { get; set; } = new List<InterpretationSplineControlPoint>();
    }

    public sealed class InterpretationSplineControlPoint
    {
        public double? TimeSeconds { get; set; }
        public double? PowerMicrowatts { get; set; }
        public double? SlopeMicrowattsPerSecond { get; set; }
        public bool Locked { get; set; }
        public bool SlopeLocked { get; set; }
        public bool UserDefined { get; set; }
        public bool Linear { get; set; }
    }

    public sealed class InterpretationSegmentedBaselineControls
    {
        public int Degree { get; set; }
        public string CoefficientConvention { get; set; } = "Baseline power in watts equals sum coefficient[i]*(timeSeconds-centerTimeSeconds)^i.";
        public List<InterpretationBaselineSegment> Segments { get; set; } = new List<InterpretationBaselineSegment>();
    }

    public sealed class InterpretationBaselineSegment
    {
        public string Scope { get; set; }
        public int? InjectionId { get; set; }
        public double? StartTimeSeconds { get; set; }
        public double? EndTimeSeconds { get; set; }
        public double? CenterTimeSeconds { get; set; }
        public List<double?> CoefficientsSi { get; set; } = new List<double?>();
    }

    public sealed class InterpretationPolynomialBaselineControls
    {
        public int Degree { get; set; }
        public double? RejectionZLimit { get; set; }
        public string CoefficientAvailability { get; set; } = "Fitted coefficients are not exported because they are not preserved reliably after project reload.";
    }

    public sealed class InterpretationAsymmetricLeastSquaresControls
    {
        public int Iterations { get; set; }
        public double? Lambda { get; set; }
        public double? Asymmetry { get; set; }
    }

    public sealed class InterpretationResidualDiagnosticsEvidence
    {
        public string EvidenceId { get; set; }
        public bool IsAvailable { get; set; }
        public string UnavailableReason { get; set; }
        public int IncludedFiniteResidualCount { get; set; }
        public double? MeanResidualJoulesPerMole { get; set; }
        public double? RmsResidualJoulesPerMole { get; set; }
        public double? MeanAbsoluteResidualJoulesPerMole { get; set; }
        public double? MedianAbsoluteResidualJoulesPerMole { get; set; }
        public double? EarlyMeanResidualJoulesPerMole { get; set; }
        public double? MiddleMeanResidualJoulesPerMole { get; set; }
        public double? LateMeanResidualJoulesPerMole { get; set; }
        public string InjectionOrderGroupMeansUnavailableReason { get; set; }
        public double? ResidualSlopeAgainstAnalysisAxis { get; set; }
        public string ResidualSlopeUnavailableReason { get; set; }
        public double? LagOneAutocorrelation { get; set; }
        public string LagOneAutocorrelationUnavailableReason { get; set; }
        public int? SignRunCount { get; set; }
        public int? LongestSameSignRun { get; set; }
        public string SignRunsUnavailableReason { get; set; }
        public int StandardisedResidualCount { get; set; }
        public double? MaximumAbsoluteStandardisedResidual { get; set; }
        public int? CountAboveTwoInjectionErrors { get; set; }
        public int? CountAboveThreeInjectionErrors { get; set; }
        public string StandardisedResidualUnavailableReason { get; set; }
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
        public List<InterpretationProfileCoordinateEvidence> Coordinates { get; set; } = new List<InterpretationProfileCoordinateEvidence>();
        public string StandardDeviationSemantics { get; set; } = "Profile-derived equivalent SDs summarize likelihood intervals; they are not empirical Gaussian uncertainty distributions.";
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
