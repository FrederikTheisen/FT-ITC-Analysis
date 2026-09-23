using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    public sealed class AnalysisResultParameterEvaluationRow
    {
        public AnalysisResultParameterEvaluationRow(string label, string value, string tooltip)
        {
            Label = label ?? "";
            Value = value ?? "";
            Tooltip = tooltip ?? "";
        }

        public string Label { get; }
        public string Value { get; }
        public string Tooltip { get; }
    }

    public sealed class AnalysisResultParameterEvaluation
    {
        AnalysisResultParameterEvaluation(double temperatureCelsius, List<AnalysisResultParameterEvaluationRow> rows, string message)
        {
            TemperatureCelsius = temperatureCelsius;
            Rows = rows;
            Message = message ?? "";
        }

        public double TemperatureCelsius { get; }
        public IReadOnlyList<AnalysisResultParameterEvaluationRow> Rows { get; }
        public string Message { get; }
        public bool IsAvailable => Rows.Count > 0;

        public static AnalysisResultParameterEvaluation Unavailable(double temperatureCelsius, string message)
        {
            return new AnalysisResultParameterEvaluation(temperatureCelsius, new List<AnalysisResultParameterEvaluationRow>(), message);
        }

        public static AnalysisResultParameterEvaluation Available(double temperatureCelsius, List<AnalysisResultParameterEvaluationRow> rows)
        {
            return new AnalysisResultParameterEvaluation(temperatureCelsius, rows ?? new List<AnalysisResultParameterEvaluationRow>(), "");
        }
    }

    internal enum AggregateUncertaintyKind
    {
        ModelEstimated,
        ReplicateStandardDeviation,
        TemperatureTrendStandardDeviation,
        RegressionSlopeStandardError,
        InsufficientReplicates,
    }

    internal sealed class AggregateParameterSummary
    {
        internal AggregateParameterSummary(
            FloatWithError value,
            AggregateUncertaintyKind kind,
            int count = 0)
        {
            Value = value;
            Kind = kind;
            Count = count;
        }

        internal FloatWithError Value { get; }
        internal AggregateUncertaintyKind Kind { get; }
        internal int Count { get; }
    }

    /// <summary>Calculates transient summary uncertainty without changing stored fits.</summary>
    public sealed class AnalysisResultAggregateSummaryCalculator
    {
        readonly AnalysisResult result;

        public AnalysisResultAggregateSummaryCalculator(AnalysisResult result) { this.result = result; }

        internal AggregateParameterSummary Evaluate(ParameterType parameter, double temperatureCelsius)
        {
            var summary = BuildDependence(parameter);
            return summary == null ? null : new AggregateParameterSummary(summary.Evaluate(temperatureCelsius), summary.Kind, summary.Count);
        }

        /// <summary>
        /// Returns the numerical value used by every summary surface. Thermodynamic
        /// quantities must be evaluated through the saved dependence rather than
        /// re-aggregating their per-experiment report values.
        /// </summary>
        internal AggregateParameterSummary EvaluateSummaryParameter(ParameterType parameter, double temperatureCelsius)
        {
            if (!ThermodynamicParameterSlots.TryResolve(parameter, out var slot, out var family))
                return Evaluate(parameter, temperatureCelsius);

            if (family == ThermodynamicParameterFamily.HeatCapacity)
                return EvaluateHeatCapacity(slot);

            if (family != ThermodynamicParameterFamily.Affinity)
                return Evaluate(parameter, temperatureCelsius);

            var dependence = BuildDependence(slot.Gibbs);
            var kelvin = temperatureCelsius + 273.15;
            if (dependence == null || kelvin <= 0) return null;
            var gibbs = dependence.Evaluate(temperatureCelsius);
            var affinity = dependence.Replicates.Count > 0
                ? new FloatWithError(dependence.Replicates.Select(curve => Math.Exp(curve.Evaluate(temperatureCelsius).Value / (kelvin * Energy.R))),
                    Math.Exp(gibbs.Value / (kelvin * Energy.R)))
                : FWEMath.Exp(gibbs / (kelvin * Energy.R));
            return new AggregateParameterSummary(affinity, dependence.Kind, dependence.Count);
        }

        internal SummaryDependence BuildDependence(ParameterType parameter)
        {
            if (!result.Solution.TemperatureDependence.TryGetValue(parameter, out var dependence)) return null;
            if (ThermodynamicParameterSlots.TryResolve(parameter, out var linkedSlot, out var linkedFamily)
                && (linkedFamily == ThermodynamicParameterFamily.Gibbs
                    || linkedFamily == ThermodynamicParameterFamily.EntropyContribution
                    || linkedFamily == ThermodynamicParameterFamily.Enthalpy)
                && result.Model.Parameters.GetConstraintForParameter(linkedSlot.Affinity)
                    == VariableConstraint.ThermodynamicallyLinked)
                return LinkedThermodynamicEvaluation.Build(result.Solution, linkedSlot, linkedFamily);
            if (!IsLocallyAggregated(parameter)) return SummaryUncertainty.Model(dependence);
            var observations = Observations(parameter);
            var values = observations.Select(item => item.value).ToArray();
            var summary = result.Model.TemperatureDependenceExposed
                ? SummaryUncertainty.Trend(dependence, values, observations.Select(item => item.data.MeasuredTemperature).ToArray())
                : SummaryUncertainty.Mean(values, dependence.Intercept.Value);
            if (!result.Model.TemperatureDependenceExposed)
            {
                summary.ReferenceTemperature = dependence.ReferenceT;
                summary.Slope = dependence.Slope.Value;
                summary.SlopeUncertainty = dependence.Slope;
            }
            if (SummaryUncertainty.HasDuplicateExperiments(observations.Select(item => item.data)))
                summary.MakeUncertaintyUnavailable();
            return summary;
        }

        /// <summary>
        /// Samples the saved temperature relationship using the same evaluator
        /// for linear and thermodynamically linked parameters.
        /// </summary>
        public IReadOnlyList<FitEnvelopePoint> BuildEnvelope(
            ParameterType parameter,
            IEnumerable<double> temperaturesCelsius)
        {
            if (result?.Solution?.TemperatureDependence == null
                || temperaturesCelsius == null
                || !result.Solution.TemperatureDependence.TryGetValue(parameter, out var fit))
                return Array.Empty<FitEnvelopePoint>();

            var dependence = BuildDependence(parameter);
            if (dependence == null) return Array.Empty<FitEnvelopePoint>();

            if (IsThermodynamicallyLinked(parameter))
                return FitEnvelopeBuilder.Build(temperaturesCelsius, temperature =>
                {
                    var value = dependence.Evaluate(temperature);
                    return (value.Value, value.Lower, value.Upper);
                });

            var bootstrapFits = result.Solution.BootstrapSolutions?
                .Where(solution => solution?.TemperatureDependence?.ContainsKey(parameter) == true)
                .Select(solution => solution.TemperatureDependence[parameter])
                .ToList();
            return FitEnvelopeBuilder.Build(fit, bootstrapFits, temperaturesCelsius);
        }

        bool IsThermodynamicallyLinked(ParameterType parameter)
        {
            return ThermodynamicParameterSlots.TryResolve(parameter, out var slot, out var family)
                && (family == ThermodynamicParameterFamily.Enthalpy
                    || family == ThermodynamicParameterFamily.Gibbs
                    || family == ThermodynamicParameterFamily.EntropyContribution)
                && result.Model.Parameters.GetConstraintForParameter(slot.Affinity)
                    == VariableConstraint.ThermodynamicallyLinked;
        }

        internal AggregateParameterSummary EvaluateHeatCapacity(ThermodynamicParameterSlot slot)
        {
            if (result.Model.Parameters.GetConstraintForParameter(slot.Affinity) == VariableConstraint.ThermodynamicallyLinked)
            {
                var linked = LinkedThermodynamicEvaluation.Build(result.Solution, slot, ThermodynamicParameterFamily.Enthalpy);
                return new AggregateParameterSummary(linked.SlopeUncertainty, AggregateUncertaintyKind.ModelEstimated);
            }
            if (!result.Solution.TemperatureDependence.TryGetValue(slot.Enthalpy, out var dependence)) return null;
            if (!IsLocallyFitted(slot.Enthalpy) || !result.Model.TemperatureDependenceExposed)
                return new AggregateParameterSummary(dependence.Slope, AggregateUncertaintyKind.ModelEstimated);
            var summary = BuildDependence(slot.Enthalpy);
            return new AggregateParameterSummary(summary.SlopeUncertainty,
                AggregateUncertaintyKind.RegressionSlopeStandardError, summary.Count);
        }

        internal bool IsLocallyAggregated(ParameterType parameter)
        {
            if (!ThermodynamicParameterSlots.TryResolve(parameter, out var slot, out var family))
                return IsLocallyFitted(parameter);
            switch (family)
            {
                case ThermodynamicParameterFamily.Gibbs:
                    return IsLocallyFitted(slot.Affinity);
                case ThermodynamicParameterFamily.EntropyContribution:
                    return IsLocallyFitted(slot.Enthalpy) || IsLocallyFitted(slot.Affinity);
                case ThermodynamicParameterFamily.HeatCapacity:
                    return IsLocallyFitted(slot.Enthalpy);
                default:
                    return IsLocallyFitted(parameter);
            }
        }

        bool IsLocallyFitted(ParameterType parameter) =>
            result.Model.Parameters.GetConstraintForParameter(parameter) == VariableConstraint.None;

        List<(ExperimentData data, FloatWithError value)> Observations(ParameterType parameter)
        {
            var dependency = result.Solution.Solutions
                .SelectMany(solution => solution?.DependenciesToReport ?? new List<Tuple<ParameterType, Func<SolutionInterface, FloatWithError>>>())
                .FirstOrDefault(item => item.Item1 == parameter);
            if (dependency == null) return new List<(ExperimentData, FloatWithError)>();
            return result.Solution.Solutions.Where(solution => solution?.Data != null)
                .Select(solution => (data: solution.Data, value: dependency.Item2(solution)))
                .Where(item => SummaryUncertainty.HasValue(item.value)
                    && (!result.Model.TemperatureDependenceExposed || SummaryUncertainty.IsFinite(item.data.MeasuredTemperature)))
                .ToList();
        }
    }

    public static class AnalysisResultParameterEvaluator
    {
        const double AbsoluteZeroCelsius = -273.15;

        public static double DefaultEvaluationTemperatureCelsius(AnalysisResult result)
        {
            if (result?.Model == null || result.Solution == null) return AppSettings.ReferenceTemperature;

            return result.Model.TemperatureDependenceExposed
                ? AppSettings.ReferenceTemperature
                : MeanModelTemperature(result);
        }

        public static AnalysisResultParameterEvaluation Evaluate(
            AnalysisResult result,
            double temperatureCelsius,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            EnergyUnitResolver.ValidateOverride(energyUnit);
            return EvaluateInternal(result, temperatureCelsius, energyUnit, energyUnit, uncertaintyStyle);
        }

        public static AnalysisResultParameterEvaluation Evaluate(
            AnalysisResult result,
            double temperatureCelsius,
            EnergyUnitFamily family,
            EnergyUnit? energyUnitOverride,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            temperatureCelsius = Math.Max(AbsoluteZeroCelsius, temperatureCelsius);
            var units = ResolveEnergyUnits(result, temperatureCelsius, family, energyUnitOverride);
            return EvaluateInternal(result, temperatureCelsius, units.molar, units.heatCapacity, uncertaintyStyle);
        }

        public static AnalysisResultParameterEvaluation Evaluate(
            AnalysisResult result,
            double temperatureCelsius,
            EnergyUnitFamily family,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            return Evaluate(result, temperatureCelsius, family, null, uncertaintyStyle);
        }

        static AnalysisResultParameterEvaluation EvaluateInternal(
            AnalysisResult result,
            double temperatureCelsius,
            EnergyUnit molarEnergyUnit,
            EnergyUnit heatCapacityUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            temperatureCelsius = Math.Max(AbsoluteZeroCelsius, temperatureCelsius);

            if (result?.Solution?.TemperatureDependence == null)
                return AnalysisResultParameterEvaluation.Unavailable(temperatureCelsius, "Parameter evaluation unavailable.");

            var summaries = new AnalysisResultAggregateSummaryCalculator(result);
            var rows = new List<AnalysisResultParameterEvaluationRow>();
            AddHeatCapacityRows(result, summaries, rows, heatCapacityUnit, uncertaintyStyle);
            foreach (var slot in PresentSlots(result))
                AddInteractionRows(result, summaries, rows, slot, temperatureCelsius, molarEnergyUnit, uncertaintyStyle);

            return rows.Count == 0
                ? AnalysisResultParameterEvaluation.Unavailable(temperatureCelsius, "Parameter evaluation unavailable for this result.")
                : AnalysisResultParameterEvaluation.Available(temperatureCelsius, rows);
        }

        public static List<Tuple<string, string>> EvaluateDefaultList(AnalysisResult result)
        {
            var temperatureCelsius = DefaultEvaluationTemperatureCelsius(result);
            var evaluation = Evaluate(result, temperatureCelsius, AppSettings.EnergyUnitFamily, null, AppSettings.UncertaintyDisplayStyle);

            return evaluation.Rows
                .Select(row => new Tuple<string, string>(row.Label, row.Value))
                .ToList();
        }

        public static List<Tuple<string, string>> EvaluateDefaultList(
            AnalysisResult result,
            EnergyUnitFamily family,
            EnergyUnit? energyUnitOverride,
            UncertaintyDisplayStyle? uncertaintyStyle = null)
        {
            var temperatureCelsius = DefaultEvaluationTemperatureCelsius(result);
            var evaluation = Evaluate(
                result,
                temperatureCelsius,
                family,
                energyUnitOverride,
                uncertaintyStyle ?? AppSettings.UncertaintyDisplayStyle);

            return evaluation.Rows
                .Select(row => new Tuple<string, string>(row.Label, row.Value))
                .ToList();
        }

        static void AddHeatCapacityRows(
            AnalysisResult result,
            AnalysisResultAggregateSummaryCalculator summaries,
            List<AnalysisResultParameterEvaluationRow> rows,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            if (result?.IsTemperatureDependenceEnabled != true) return;

            var slots = PresentSlots(result).ToList();
            foreach (var slot in slots)
                AddHeatCapacityRow(result, summaries, rows, slot, slots.Count > 1, energyUnit, uncertaintyStyle);
        }

        static void AddHeatCapacityRow(
            AnalysisResult result,
            AnalysisResultAggregateSummaryCalculator summaries,
            List<AnalysisResultParameterEvaluationRow> rows,
            ThermodynamicParameterSlot slot,
            bool includeIndex,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            if (!result.Solution.TemperatureDependence.TryGetValue(slot.Enthalpy, out var dependence)) return;

            var summary = summaries.EvaluateHeatCapacity(slot);
            if (summary == null) return;

            var slope = summary.Value;
            if (Math.Abs(slope.Value) <= 0) return;

            var heatCapacity = new Energy(slope);
            var suffix = includeIndex ? slot.Index.ToString() : string.Empty;
            var label = $"Heat capacity change{(includeIndex ? " " + slot.Index : string.Empty)} (∆Cp{suffix})";

            rows.Add(new AnalysisResultParameterEvaluationRow(
                label,
                heatCapacity.ToFormattedString(energyUnit, withunit: true, permole: true, perK: true, style: uncertaintyStyle),
                ErrorTooltip(
                    "∆Cp",
                    heatCapacity.FloatWithError * energyUnit.GetMod(),
                    energyUnit.GetUnit() + "/mol·K",
                    summary,
                    IsProfileResult(result),
                    HasDirectSharedProfileCoordinate(result, slot.HeatCapacity))));
        }

        static void AddInteractionRows(
            AnalysisResult result,
            AnalysisResultAggregateSummaryCalculator summaries,
            List<AnalysisResultParameterEvaluationRow> rows,
            ThermodynamicParameterSlot slot,
            double temperatureCelsius,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            var enthalpyKey = slot.Enthalpy;
            var entropyKey = slot.EntropyContribution;
            var gibbsKey = slot.Gibbs;
            var affinityKey = slot.Affinity;
            var profile = IsProfileResult(result);

            if (TryEvaluateEnergy(summaries, enthalpyKey, temperatureCelsius, out var enthalpy, out var enthalpySummary))
            {
                var directProfile = result.Solution.TemperatureDependence.TryGetValue(enthalpyKey, out var dependence)
                    && HasDirectEnthalpyProfileCoordinate(result, slot, dependence, temperatureCelsius);
                rows.Add(EnergyRow(ParameterName(enthalpyKey), "∆H", enthalpy, enthalpySummary, energyUnit, uncertaintyStyle, profile, directProfile));
            }

            if (TryEvaluateEnergy(summaries, entropyKey, temperatureCelsius, out var entropy, out var entropySummary))
                rows.Add(EnergyRow(ParameterName(entropyKey), "-T∆S", entropy, entropySummary, energyUnit, uncertaintyStyle, profile));

            if (TryEvaluateEnergy(summaries, gibbsKey, temperatureCelsius, out var gibbs, out var gibbsSummary))
            {
                var directProfile = result.Solution.Model.Parameters.GetConstraintForParameter(affinityKey)
                    == VariableConstraint.TemperatureDependent
                    && HasDirectSharedProfileCoordinate(result, gibbsKey);
                rows.Add(EnergyRow(ParameterName(gibbsKey), "∆G", gibbs, gibbsSummary, energyUnit, uncertaintyStyle, profile, directProfile));

                var kelvin = temperatureCelsius + 273.15;

                if (kelvin <= 0) return;

                var kdExponent = gibbs / (kelvin * Energy.R);
                var kd = FWEMath.Exp(kdExponent.FloatWithError);
                rows.Add(new AnalysisResultParameterEvaluationRow(
                    ParameterName(affinityKey),
                    kd.AsFormattedConcentration(withunit: true, style: uncertaintyStyle),
                    ConcentrationTooltip("Kd", kd, gibbsSummary, profile)));
            }
        }

        static (EnergyUnit molar, EnergyUnit heatCapacity) ResolveEnergyUnits(
            AnalysisResult result,
            double temperatureCelsius,
            EnergyUnitFamily family,
            EnergyUnit? energyUnitOverride)
        {
            var molarValues = new List<double>();
            var heatCapacityValues = new List<double>();
            var dependences = result?.Solution?.TemperatureDependence;
            if (dependences != null)
            {
                foreach (var slot in PresentSlots(result))
                {
                    if (dependences.TryGetValue(slot.Enthalpy, out var enthalpy))
                    {
                        molarValues.Add(enthalpy.Evaluate(temperatureCelsius).Value);
                        heatCapacityValues.Add(enthalpy.Slope.Value);
                    }
                    if (dependences.TryGetValue(slot.EntropyContribution, out var entropy))
                        molarValues.Add(entropy.Evaluate(temperatureCelsius).Value);
                    if (dependences.TryGetValue(slot.Gibbs, out var gibbs))
                        molarValues.Add(gibbs.Evaluate(temperatureCelsius).Value);
                }
            }

            return (
                EnergyUnitResolver.Resolve(family, energyUnitOverride, molarValues),
                EnergyUnitResolver.Resolve(family, energyUnitOverride, heatCapacityValues));
        }

        static bool TryEvaluateEnergy(
            AnalysisResultAggregateSummaryCalculator summaries,
            ParameterType key,
            double temperatureCelsius,
            out Energy value,
            out AggregateParameterSummary summary)
        {
            value = new Energy(0);
            summary = summaries.Evaluate(key, temperatureCelsius);
            if (summary == null)
            {
                return false;
            }

            value = new Energy(summary.Value);
            return true;
        }

        static AnalysisResultParameterEvaluationRow EnergyRow(
            string label,
            string tooltipPrefix,
            Energy value,
            AggregateParameterSummary summary,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle,
            bool profile,
            bool directProfile = false)
        {
            return new AnalysisResultParameterEvaluationRow(
                label,
                value.ToFormattedString(energyUnit, permole: true, style: uncertaintyStyle),
                ErrorTooltip(
                    tooltipPrefix,
                    value.FloatWithError * energyUnit.GetMod(),
                    energyUnit.GetUnit() + "/mol",
                    summary,
                    profile,
                    directProfile));
        }

        static string ConcentrationTooltip(
            string prefix,
            FloatWithError value,
            AggregateParameterSummary summary = null,
            bool profile = false)
        {
            var unit = ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(value.Value);
            return ErrorTooltip(prefix, value * unit.GetMod(), unit.GetName(), summary, profile);
        }

        static string ErrorTooltip(
            string prefix,
            FloatWithError value,
            string unit,
            AggregateParameterSummary summary = null,
            bool profile = false,
            bool directProfile = false)
        {
            var suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit;
            var central = IsFinite(value.Value) ? FormatTooltipNumber(value.Value) : "unavailable";
            var sd = IsFinite(value.SD) ? FormatTooltipNumber(value.SD) : "unavailable";
            var lower = IsFinite(value.Lower) ? FormatTooltipNumber(value.Lower) : "unavailable";
            var upper = IsFinite(value.Upper) ? FormatTooltipNumber(value.Upper) : "unavailable";
            var isAggregate = summary != null && summary.Kind != AggregateUncertaintyKind.ModelEstimated;

            var statistic = "SD";
            if (isAggregate)
            {
                switch (summary.Kind)
                {
                    case AggregateUncertaintyKind.ReplicateStandardDeviation:
                    case AggregateUncertaintyKind.InsufficientReplicates:
                        statistic = $"Combined SD (n = {summary.Count})";
                        break;
                    case AggregateUncertaintyKind.TemperatureTrendStandardDeviation:
                        statistic = "Combined SD around temperature trend";
                        break;
                    case AggregateUncertaintyKind.RegressionSlopeStandardError:
                        statistic = "Propagated slope SD";
                        break;
                }
            }
            var interval = isAggregate
                ? $"Approximate propagated interval: {lower} to {upper}{suffix}. Constructed from individual 95% intervals and observed spread; 95% coverage is not established. Covariance between experiments is omitted."
                : profile
                    ? $"95% CI: {lower} to {upper}{suffix} ({(directProfile ? "direct profile" : "propagated")})"
                    : $"95% confidence interval: {lower} to {upper}{suffix}";

            return string.Join(
                Environment.NewLine,
                $"{prefix} (value ± {statistic}): {central} ± {sd}{suffix}",
                interval);
        }

        static bool IsProfileResult(AnalysisResult result) =>
            result?.Solution?.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood;

        static bool HasDirectSharedProfileCoordinate(AnalysisResult result, ParameterType parameter) =>
            result?.Solution?.ProfileLikelihoodRun is ProfileLikelihoodRunResult run
            && run.Outcome != ErrorEstimationOutcome.CompleteFailure
            && run.Coordinates?.Any(coordinate =>
                coordinate.Id.Scope == ParameterBoundaryScope.Shared
                && coordinate.Id.Parameter == parameter
                && coordinate.HasCompleteInterval) == true;

        static bool HasDirectEnthalpyProfileCoordinate(
            AnalysisResult result,
            ThermodynamicParameterSlot slot,
            LinearFitWithError dependence,
            double temperatureCelsius)
        {
            if (!HasDirectSharedProfileCoordinate(result, slot.Enthalpy)) return false;

            switch (result.Solution.Model.Parameters.GetConstraintForParameter(slot.Enthalpy))
            {
                case VariableConstraint.SameForAll:
                    return true;
                case VariableConstraint.TemperatureDependent:
                    return Math.Abs(temperatureCelsius - dependence.ReferenceT) <= 1e-9;
                default:
                    return false;
            }
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static string FormatTooltipNumber(double value)
        {
            return value.ToString("G5", CultureInfo.CurrentCulture);
        }

        static string ParameterName(ParameterType key)
        {
            var properties = key.GetProperties();
            return $"{properties.Name} ({PlainSymbol(properties.SymbolName)})";
        }

        static IEnumerable<ThermodynamicParameterSlot> PresentSlots(AnalysisResult result)
        {
            var dependences = result?.Solution?.TemperatureDependence;
            if (dependences == null) return Enumerable.Empty<ThermodynamicParameterSlot>();
            return ThermodynamicParameterSlots.All.Where(slot =>
                dependences.ContainsKey(slot.Enthalpy)
                || dependences.ContainsKey(slot.EntropyContribution)
                || dependences.ContainsKey(slot.Gibbs));
        }

        static double MeanModelTemperature(AnalysisResult result)
        {
            if (result?.Model?.Models != null && result.Model.Models.Count > 0)
                return result.Model.Models.Average(model => model.Data.TargetTemperature);

            return result?.Solution?.MeanTemperature ?? AppSettings.ReferenceTemperature;
        }

        static string PlainSymbol(string symbol)
        {
            return (symbol ?? "")
                .Replace("*", "")
                .Replace("{d}", "d")
                .Replace("{2}", "2")
                .Replace("{p}", "p")
                .Replace("{,2}", "2")
                .Replace("{3}", "3")
                .Replace("{4}", "4")
                .Replace("{,3}", "3")
                .Replace("{,4}", "4");
        }
    }
}
