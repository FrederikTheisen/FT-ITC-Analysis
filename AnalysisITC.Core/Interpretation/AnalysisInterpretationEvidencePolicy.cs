using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Core.Interpretation
{
    // This enum is deliberately not a property of AnalysisInterpretationOptions:
    // the existing three option fields remain the serialized representation.
    public enum AnalysisInterpretationEvidenceLevel { Results, InjectionData, ProcessingDetails, Thermograms }

    /// <summary>Cumulative model-input evidence, independent of full local capture.</summary>
    public static class AnalysisInterpretationEvidencePolicy
    {
        // Evidence ownership. E = results[].experiments[], supportingExperiments[], experimentEvidence[].
        // | JSON paths/evidence | First transmitted level |
        // | report, studyContext; results[].{reportReference,resultId,name,dateUtc,comments,health,validityStatus,validityReasons,matchedFitDiagnosticsUnavailableReason,model,solver,informationCriteria}; assessment except points | Results |
        // | result correlations, temperatureDependence, advancedAnalyses | Results |
        // | results[].experiments[].{reportReference,experimentId,name,sourceFileBasename,dateProvenance,dateUtc,comments,sourceStateFingerprint,traceability} | Results |
        // | results[].experiments[].{solver,informationCriteria,evidenceBasis,matchedFitDiagnosticsUnavailableReason,unavailableDerivedParameterReason} | Results |
        // | results[].experiments[].{concentrationMethod,heatMethod,fittedHeatMethod,analysisAxis,instrument} | Results |
        // | results[].experiments[].{temperatures,concentrations,attributes,modelOptions,parameters,cValues,tandem and blank relationships} | Results |
        // | results[].experiments[].initialDelaySeconds, baselineCompleted, integrationCompleted, residualDiagnostics | Results |
        // | supportingExperiments: source, identity, names, comments, conditions, concentrations, instrument, attributes, provenance and relationships | Results |
        // | supportingExperiments: injections, thermograms and baseline controls are always omitted; processing settings and scalar baseline statistics/reasons | ProcessingDetails |
        // | results[].experiments[].injections.{acquisition,heatObservations,fit}; bindingAssessment.{members,nullMembers}[].points | InjectionData |
        // | results[].historicalFitInputs (whole saved snapshots) | ProcessingDetails |
        // | E.baseline (statistics, availability and controls); E.injections.{integration,baseline} | ProcessingDetails |
        // | E.{baselineProcessor,processorLocked,discardsIntegratedPointsForBaseline,integrationLengthMode,integrationLengthFactor} | ProcessingDetails |
        // | E.thermogram, aligned baseline arrays and trace omission reasons | Thermograms |
        // | requestedInterpretation, omissions, dataBoundary, tableSchemas, modelInputDefinitions | Describe retained evidence at every level |
        public const string SelectorTooltip = "Controls how much evidence is sent";
        public const string InjectionOmission = "Injection tables omitted by user choice.";
        public const string NullPointsOmission = "Saved Null comparison points omitted by user choice.";
        public const string ProcessingOmission = "Detailed processing information omitted by user choice.";
        public const string ThermogramOmission = "Thermograms and sampled baselines omitted by user choice.";
        internal const string SupportingExperimentOmission = "Supporting experiments contain summaries only; injection rows, baseline controls and thermograms are not supplied.";
        public const string TraceRestriction = "Compressed value bounds preserve original observations but omit exact occurrence times, within-segment order and waveform detail. Do not infer precise settling or integration adequacy from extrema alone. Use timing, fitted baseline and processing evidence where supplied.";
        public const string NoTraceRestriction = "No raw thermogram or sampled fitted-baseline arrays were supplied. Assess available summaries, controls and injection evidence only; do not claim to observe peak shape or settling.";
        public const string NoBindingRestriction = "A No binding detected assessment is result-level evidence based on the saved comparison; supplied attempted-model parameters are diagnostic context, not established binding findings. Use each member assessment when interpreting its parameters. Use its saved Null model predictions and observations as the null-model evidence. Do not replace missing comparison values with binding-fit parameters.";

        public static AnalysisInterpretationEvidenceLevel Level(AnalysisInterpretationOptions options)
        {
            options = options ?? AnalysisInterpretationOptions.Default();
            if (options.InjectionRows == AnalysisInterpretationInjectionRows.None) return AnalysisInterpretationEvidenceLevel.Results;
            if (!options.IncludeProcessingInformation) return AnalysisInterpretationEvidenceLevel.InjectionData;
            return options.IncludeThermograms ? AnalysisInterpretationEvidenceLevel.Thermograms : AnalysisInterpretationEvidenceLevel.ProcessingDetails;
        }

        public static AnalysisInterpretationOptions WithLevel(AnalysisInterpretationOptions options,
            AnalysisInterpretationEvidenceLevel level, AnalysisInterpretationInjectionRows rows = AnalysisInterpretationInjectionRows.All)
        {
            var result = (options ?? AnalysisInterpretationOptions.Default()).Copy();
            result.InjectionRows = level >= AnalysisInterpretationEvidenceLevel.InjectionData
                ? rows == AnalysisInterpretationInjectionRows.IncludedOnly ? rows : AnalysisInterpretationInjectionRows.All
                : AnalysisInterpretationInjectionRows.None;
            result.IncludeProcessingInformation = level >= AnalysisInterpretationEvidenceLevel.ProcessingDetails;
            result.IncludeThermograms = level >= AnalysisInterpretationEvidenceLevel.Thermograms;
            return result;
        }

        public static IReadOnlyList<AnalysisInterpretationEvidenceLevel> AvailableLevels(InterpretationOperatorOptionsResponse capabilities)
        {
            var maximum = !InterpretationAccessDisplay.CanIncludeInjectionTables(capabilities) ? AnalysisInterpretationEvidenceLevel.Results
                : !InterpretationAccessDisplay.CanIncludeProcessingInformation(capabilities) ? AnalysisInterpretationEvidenceLevel.InjectionData
                : !InterpretationAccessDisplay.CanIncludeThermograms(capabilities) ? AnalysisInterpretationEvidenceLevel.ProcessingDetails
                : AnalysisInterpretationEvidenceLevel.Thermograms;
            return Enum.GetValues(typeof(AnalysisInterpretationEvidenceLevel)).Cast<AnalysisInterpretationEvidenceLevel>()
                .Where(level => level <= maximum).ToArray();
        }

        public static string Label(AnalysisInterpretationEvidenceLevel level) => level switch
        {
            AnalysisInterpretationEvidenceLevel.Results => "Results",
            AnalysisInterpretationEvidenceLevel.InjectionData => "Injection data",
            AnalysisInterpretationEvidenceLevel.ProcessingDetails => "Processing details",
            AnalysisInterpretationEvidenceLevel.Thermograms => "Thermograms",
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

        public static string Description(AnalysisInterpretationEvidenceLevel level) => level switch
        {
            AnalysisInterpretationEvidenceLevel.Results => "Context, conditions, fitted values, uncertainty, assessments and fit diagnostics.",
            AnalysisInterpretationEvidenceLevel.InjectionData => "Results plus acquisition details, injection heats, fit comparisons and saved Null comparison points.",
            AnalysisInterpretationEvidenceLevel.ProcessingDetails => "Injection data plus integration boundaries, baseline statistics and controls, and available historical fit inputs.",
            AnalysisInterpretationEvidenceLevel.Thermograms => "Processing details plus compressed thermograms and available aligned baselines.",
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

        public static string ObservationRestriction(bool hasTraces, bool noBinding) =>
            (hasTraces ? TraceRestriction : NoTraceRestriction) + (noBinding ? " " + NoBindingRestriction : "");
    }

    /// <summary>Dialog-owned intent survives capability changes; traces require a fresh opening opt-in.</summary>
    public sealed class AnalysisInterpretationEvidenceSelection
    {
        readonly AnalysisInterpretationInjectionRows rowPreference;
        public AnalysisInterpretationEvidenceLevel RequestedLevel { get; private set; }
        public AnalysisInterpretationEvidenceLevel EffectiveLevel { get; private set; }
        public IReadOnlyList<AnalysisInterpretationEvidenceLevel> AvailableLevels { get; private set; }
        public bool IsVisible => AvailableLevels.Count > 1;

        public AnalysisInterpretationEvidenceSelection(AnalysisInterpretationOptions saved)
        {
            RequestedLevel = AnalysisInterpretationEvidencePolicy.Level(saved);
            if (RequestedLevel == AnalysisInterpretationEvidenceLevel.Thermograms)
                RequestedLevel = AnalysisInterpretationEvidenceLevel.ProcessingDetails;
            rowPreference = saved?.InjectionRows == AnalysisInterpretationInjectionRows.IncludedOnly
                ? AnalysisInterpretationInjectionRows.IncludedOnly : AnalysisInterpretationInjectionRows.All;
            ApplyCapabilities(null);
        }

        public void ApplyCapabilities(InterpretationOperatorOptionsResponse capabilities)
        {
            AvailableLevels = AnalysisInterpretationEvidencePolicy.AvailableLevels(capabilities);
            EffectiveLevel = (AnalysisInterpretationEvidenceLevel)Math.Min((int)RequestedLevel, (int)AvailableLevels.Last());
        }

        public void Select(AnalysisInterpretationEvidenceLevel level)
        {
            RequestedLevel = level;
            EffectiveLevel = (AnalysisInterpretationEvidenceLevel)Math.Min((int)level, (int)AvailableLevels.Last());
        }

        public AnalysisInterpretationOptions RequestOptions(AnalysisInterpretationOptions saved) =>
            AnalysisInterpretationEvidencePolicy.WithLevel(saved, EffectiveLevel, rowPreference);
    }
}
