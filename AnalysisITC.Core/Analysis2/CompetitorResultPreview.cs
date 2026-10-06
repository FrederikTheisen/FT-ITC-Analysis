using System;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Analysis
{
    public sealed class CompetitorResultPreview
    {
        public string Status { get; internal set; }
        public string Tooltip { get; internal set; }
        public string SourceName { get; internal set; }
    }

    /// <summary>Builds a read-only view of the competitor values that fitting can use.</summary>
    public static class CompetitorResultPreviewBuilder
    {
        public static CompetitorResultPreview Build(ExperimentAttribute attribute, ExperimentData experiment)
        {
            if (attribute == null || string.IsNullOrWhiteSpace(attribute.StringValue))
                return NoSelection();

            var source = DataManager.Results.FirstOrDefault(result => result.UniqueID == attribute.StringValue);
            return Build(attribute, experiment, source);
        }

        public static CompetitorResultPreview Build(ExperimentAttribute attribute, ExperimentData experiment, AnalysisResult source)
        {
            if (attribute == null || string.IsNullOrWhiteSpace(attribute.StringValue))
                return NoSelection();
            if (source == null)
            {
                var hasKd = Usable(attribute.CapturedAffinity);
                var hasH = Usable(attribute.CapturedEnthalpy);
                return new CompetitorResultPreview
                {
                    Status = "Missing",
                    Tooltip = hasKd || hasH
                        ? $"Source result missing\nSaved: {FormatValues(attribute.CapturedAffinity, attribute.CapturedEnthalpy, DefaultEnergyUnit)}"
                        : "Source result missing; no saved properties."
                };
            }

            var temperature = source.Model?.TemperatureDependenceExposed == true
                ? experiment?.MeasuredTemperature ?? 25
                : AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(source);
            string status;
            AnalysisResultValidityReport validity;
            try { validity = source.ValidityReport; }
            catch { validity = AnalysisResultValidityReport.Unknown("Validity could not be determined."); }
            var sourceSolutionChanged = !string.IsNullOrWhiteSpace(attribute.SourceSolutionId)
                && attribute.SourceSolutionId != source.Solution?.UniqueID;
            if (validity.Status == AnalysisResultValidity.Unknown) status = "Unknown";
            else if (validity.Status == AnalysisResultValidity.Invalid || validity.Status == AnalysisResultValidity.PartialInvalid) status = "Stale";
            else if (HasNonBindingMember(source)) status = "No binding";
            else if (sourceSolutionChanged) status = "Changed";
            else if (source.Health == AnalysisResultHealth.Warning) status = "Warning";
            else status = "Valid";

            try
            {
                var values = CompetitorResultAttributeResolver.EvaluateSummary(source, experiment);
                var capturedValuesChanged = Usable(attribute.CapturedAffinity) && Usable(attribute.CapturedEnthalpy)
                    && (!Same(attribute.CapturedAffinity, values.Kd) || !Same(attribute.CapturedEnthalpy, values.Enthalpy));
                if (status == "Valid" && capturedValuesChanged)
                    status = "Changed";
                var energyUnit = DefaultEnergyUnit;
                var tooltip = $"{source.Name}\n{FormatValues(values.Kd, values.Enthalpy, energyUnit)}";
                if (source.Model?.TemperatureDependenceExposed == true)
                    tooltip += $" at {temperature.ToString("G4", CultureInfo.CurrentCulture)} °C";
                if (source.Model?.TemperatureDependenceExposed != true && experiment != null
                    && Math.Abs(experiment.MeasuredTemperature - temperature) > 1.0)
                    tooltip += $"\nSummary at {temperature.ToString("G4", CultureInfo.CurrentCulture)} °C; experiment at {experiment.MeasuredTemperature.ToString("G4", CultureInfo.CurrentCulture)} °C";
                if ((sourceSolutionChanged || capturedValuesChanged)
                    && (Usable(attribute.CapturedAffinity) || Usable(attribute.CapturedEnthalpy)))
                    tooltip += $"\nSaved: {FormatValues(attribute.CapturedAffinity, attribute.CapturedEnthalpy, energyUnit)}";
                if (status == "Stale") tooltip += "\nSource result is stale.";
                tooltip += AssessmentNotes(source);
                return new CompetitorResultPreview { Status = status, Tooltip = tooltip, SourceName = source.Name };
            }
            catch (Exception ex)
            {
                return new CompetitorResultPreview { Status = "Unknown", Tooltip = $"{source.Name}\nSummary unavailable: {ex.Message}{AssessmentNotes(source)}" };
            }
        }

        static bool HasNonBindingMember(AnalysisResult source) =>
            source.MemberAssessments.Any(member => !BindingAssessmentInterpretation.TreatAsBinding(member.Assessment));

        /// <summary>Notes no-binding and inconclusive assessments, which result health does not fully reflect.</summary>
        static string AssessmentNotes(AnalysisResult source)
        {
            var members = source.MemberAssessments;
            var nonBinding = members.Count(member => !BindingAssessmentInterpretation.TreatAsBinding(member.Assessment));
            var inconclusive = members.Count(member => member.Assessment?.EffectiveOutcome == BindingAssessmentOutcome.Inconclusive);
            var counted = source.IsIndependentAssessmentCollection && members.Count > 1;
            var notes = "";
            if (nonBinding > 0)
                notes += counted && nonBinding < members.Count
                    ? $"\n{nonBinding} of {members.Count} experiments assessed as no binding; Kd and ΔH may not be meaningful."
                    : "\nAssessed as no binding; Kd and ΔH may not be meaningful.";
            if (inconclusive > 0)
                notes += counted && inconclusive < members.Count
                    ? $"\n{inconclusive} of {members.Count} experiments have an inconclusive binding assessment."
                    : "\nBinding assessment inconclusive.";
            return notes;
        }

        static CompetitorResultPreview NoSelection() => new CompetitorResultPreview
        {
            Status = "Select",
            Tooltip = "Select an Analysis Result."
        };

        static EnergyUnit DefaultEnergyUnit => EnergyUnitResolver.DefaultUnit(AppSettings.EnergyUnitFamily);

        static bool Usable(FloatWithError value) => !FloatWithError.IsNaN(value) && FWEMath.IsFinite(value.Value);
        static bool Same(FloatWithError first, FloatWithError second) => Usable(first) && Usable(second)
            && first.Value == second.Value && first.SD == second.SD && first.Lower == second.Lower && first.Upper == second.Upper;

        static string FormatValues(FloatWithError kd, FloatWithError enthalpy, EnergyUnit energyUnit) =>
            $"Kd {FormatKd(kd)} · ΔH {FormatEnthalpy(enthalpy, energyUnit)}";

        static string FormatKd(FloatWithError value) => Usable(value)
            ? value.AsFormattedConcentration(withunit: true, style: UncertaintyDisplayStyle.StandardDeviation)
            : "unavailable";

        static string FormatEnthalpy(FloatWithError value, EnergyUnit unit) => Usable(value)
            ? value.Energy.ToFormattedString(unit, permole: true, style: UncertaintyDisplayStyle.StandardDeviation)
            : "unavailable";
    }
}
