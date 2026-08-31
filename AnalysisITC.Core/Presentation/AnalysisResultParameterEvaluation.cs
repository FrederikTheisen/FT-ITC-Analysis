using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AnalysisITC.Core.Analysis;
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

            var rows = new List<AnalysisResultParameterEvaluationRow>();
            AddHeatCapacityRows(result, rows, heatCapacityUnit, uncertaintyStyle);
            foreach (var slot in PresentSlots(result))
                AddInteractionRows(result, rows, slot, temperatureCelsius, molarEnergyUnit, uncertaintyStyle);

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
            List<AnalysisResultParameterEvaluationRow> rows,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            if (result?.IsTemperatureDependenceEnabled != true) return;

            var slots = PresentSlots(result).ToList();
            foreach (var slot in slots)
                AddHeatCapacityRow(result, rows, slot, slots.Count > 1, energyUnit, uncertaintyStyle);
        }

        static void AddHeatCapacityRow(
            AnalysisResult result,
            List<AnalysisResultParameterEvaluationRow> rows,
            ThermodynamicParameterSlot slot,
            bool includeIndex,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            if (!result.Solution.TemperatureDependence.TryGetValue(slot.Enthalpy, out var dependence)) return;

            var slope = dependence.Slope;
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
                    energyUnit.GetUnit() + "/mol·K")));
        }

        static void AddInteractionRows(
            AnalysisResult result,
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

            if (TryEvaluateEnergy(result, enthalpyKey, temperatureCelsius, out var enthalpy))
                rows.Add(EnergyRow(ParameterName(enthalpyKey), "∆H", enthalpy, energyUnit, uncertaintyStyle));

            if (TryEvaluateEnergy(result, entropyKey, temperatureCelsius, out var entropy))
                rows.Add(EnergyRow(ParameterName(entropyKey), "-T∆S", entropy, energyUnit, uncertaintyStyle));

            if (TryEvaluateEnergy(result, gibbsKey, temperatureCelsius, out var gibbs))
            {
                rows.Add(EnergyRow(ParameterName(gibbsKey), "∆G", gibbs, energyUnit, uncertaintyStyle));

                var kelvin = temperatureCelsius + 273.15;

                if (kelvin <= 0) return;

                var kdExponent = gibbs / (kelvin * Energy.R);
                var kd = FWEMath.Exp(kdExponent.FloatWithError);
                rows.Add(new AnalysisResultParameterEvaluationRow(
                    ParameterName(affinityKey),
                    kd.AsFormattedConcentration(withunit: true, style: uncertaintyStyle),
                    ConcentrationTooltip("Kd", kd)));
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
                        molarValues.Add(enthalpy.Evaluate(temperatureCelsius, 100000).Value);
                        heatCapacityValues.Add(enthalpy.Slope.Value);
                    }
                    if (dependences.TryGetValue(slot.EntropyContribution, out var entropy))
                        molarValues.Add(entropy.Evaluate(temperatureCelsius, 100000).Value);
                    if (dependences.TryGetValue(slot.Gibbs, out var gibbs))
                        molarValues.Add(gibbs.Evaluate(temperatureCelsius, 100000).Value);
                }
            }

            return (
                EnergyUnitResolver.Resolve(family, energyUnitOverride, molarValues),
                EnergyUnitResolver.Resolve(family, energyUnitOverride, heatCapacityValues));
        }

        static bool TryEvaluateEnergy(AnalysisResult result, ParameterType key, double temperatureCelsius, out Energy value)
        {
            value = new Energy(0);
            if (result?.Solution?.TemperatureDependence == null ||
                !result.Solution.TemperatureDependence.TryGetValue(key, out var dependence))
            {
                return false;
            }

            value = new Energy(dependence.Evaluate(temperatureCelsius, 100000));
            return true;
        }

        static AnalysisResultParameterEvaluationRow EnergyRow(
            string label,
            string tooltipPrefix,
            Energy value,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            return new AnalysisResultParameterEvaluationRow(
                label,
                value.ToFormattedString(energyUnit, permole: true, style: uncertaintyStyle),
                ErrorTooltip(
                    tooltipPrefix,
                    value.FloatWithError * energyUnit.GetMod(),
                    energyUnit.GetUnit() + "/mol"));
        }

        static string ConcentrationTooltip(string prefix, FloatWithError value)
        {
            var unit = ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(value.Value);
            return ErrorTooltip(prefix, value * unit.GetMod(), unit.GetName());
        }

        static string ErrorTooltip(string prefix, FloatWithError value, string unit)
        {
            var suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit;
            var central = IsFinite(value.Value) ? FormatTooltipNumber(value.Value) : "unavailable";
            var sd = IsFinite(value.SD) ? FormatTooltipNumber(value.SD) : "unavailable";
            var lower = IsFinite(value.Lower) ? FormatTooltipNumber(value.Lower) : "unavailable";
            var upper = IsFinite(value.Upper) ? FormatTooltipNumber(value.Upper) : "unavailable";

            return string.Join(
                Environment.NewLine,
                $"{prefix} (value ± SD): {central} ± {sd}{suffix}",
                $"95% confidence interval: {lower} to {upper}{suffix}");
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static string FormatTooltipNumber(double value)
        {
            return value.ToString("G6", CultureInfo.CurrentCulture);
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
