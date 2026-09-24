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
            else if (sourceSolutionChanged) status = "Changed";
            else if (source.Solution?.Solutions?.Any(solution => solution?.ParameterBoundaryHit == true
                || solution?.BootstrapParameterBoundaryHit == true
                || solution?.Convergence?.HasErrorEstimationLimitWarnings == true) == true) status = "Warning";
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
                    && Math.Abs(experiment.MeasuredTemperature - temperature) > 1e-6)
                    tooltip += $"\nSummary at {temperature.ToString("G4", CultureInfo.CurrentCulture)} °C; experiment at {experiment.MeasuredTemperature.ToString("G4", CultureInfo.CurrentCulture)} °C";
                if ((sourceSolutionChanged || capturedValuesChanged)
                    && (Usable(attribute.CapturedAffinity) || Usable(attribute.CapturedEnthalpy)))
                    tooltip += $"\nSaved: {FormatValues(attribute.CapturedAffinity, attribute.CapturedEnthalpy, energyUnit)}";
                if (status == "Stale") tooltip += "\nSource result is stale.";
                return new CompetitorResultPreview { Status = status, Tooltip = tooltip, SourceName = source.Name };
            }
            catch (Exception ex)
            {
                return new CompetitorResultPreview { Status = "Unknown", Tooltip = $"{source.Name}\nSummary unavailable: {ex.Message}" };
            }
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
