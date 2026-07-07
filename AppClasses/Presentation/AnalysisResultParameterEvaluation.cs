using System;
using System.Collections.Generic;
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
            temperatureCelsius = Math.Max(AbsoluteZeroCelsius, temperatureCelsius);

            if (result?.Solution?.TemperatureDependence == null)
                return AnalysisResultParameterEvaluation.Unavailable(temperatureCelsius, "Parameter evaluation unavailable.");

            var rows = new List<AnalysisResultParameterEvaluationRow>();
            AddHeatCapacityRows(result, rows, energyUnit, uncertaintyStyle);
            AddInteractionRows(result, rows, 1, temperatureCelsius, energyUnit, uncertaintyStyle);

            if (HasSecondInteraction(result))
                AddInteractionRows(result, rows, 2, temperatureCelsius, energyUnit, uncertaintyStyle);

            return rows.Count == 0
                ? AnalysisResultParameterEvaluation.Unavailable(temperatureCelsius, "Parameter evaluation unavailable for this result.")
                : AnalysisResultParameterEvaluation.Available(temperatureCelsius, rows);
        }

        public static List<Tuple<string, string>> EvaluateDefaultList(AnalysisResult result)
        {
            var temperatureCelsius = DefaultEvaluationTemperatureCelsius(result);
            var evaluation = Evaluate(result, temperatureCelsius, AppSettings.EnergyUnit, AppSettings.UncertaintyDisplayStyle);

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

            AddHeatCapacityRow(result, rows, 1, energyUnit, uncertaintyStyle);
            AddHeatCapacityRow(result, rows, 2, energyUnit, uncertaintyStyle);
        }

        static void AddHeatCapacityRow(
            AnalysisResult result,
            List<AnalysisResultParameterEvaluationRow> rows,
            int index,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            var key = index == 1 ? ParameterType.Enthalpy1 : ParameterType.Enthalpy2;
            if (!result.Solution.TemperatureDependence.TryGetValue(key, out var dependence)) return;

            var slope = dependence.Slope;
            if (Math.Abs(slope.Value) <= 0) return;

            var heatCapacity = new Energy(slope);
            var label = index == 1 ? "Heat capacity change (∆Cp)" : "Heat capacity change 2 (∆Cp2)";

            rows.Add(new AnalysisResultParameterEvaluationRow(
                label,
                heatCapacity.ToFormattedString(energyUnit, withunit: true, permole: true, perK: true, style: uncertaintyStyle),
                "∆Cp = " + heatCapacity.ToFormattedString(energyUnit, withunit: true, permole: true, perK: true, withci: true, style: uncertaintyStyle)));
        }

        static void AddInteractionRows(
            AnalysisResult result,
            List<AnalysisResultParameterEvaluationRow> rows,
            int index,
            double temperatureCelsius,
            EnergyUnit energyUnit,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            var enthalpyKey = index == 1 ? ParameterType.Enthalpy1 : ParameterType.Enthalpy2;
            var entropyKey = index == 1 ? ParameterType.EntropyContribution1 : ParameterType.EntropyContribution2;
            var gibbsKey = index == 1 ? ParameterType.Gibbs1 : ParameterType.Gibbs2;
            var affinityKey = index == 1 ? ParameterType.Affinity1 : ParameterType.Affinity2;

            if (TryEvaluateEnergy(result, enthalpyKey, temperatureCelsius, out var enthalpy))
                rows.Add(EnergyRow(ParameterName(enthalpyKey), "∆H", enthalpy, energyUnit, uncertaintyStyle));

            if (TryEvaluateEnergy(result, entropyKey, temperatureCelsius, out var entropy))
                rows.Add(EnergyRow(ParameterName(entropyKey), "-T∆S", entropy, energyUnit, uncertaintyStyle));

            if (TryEvaluateEnergy(result, gibbsKey, temperatureCelsius, out var gibbs))
            {
                rows.Add(EnergyRow(ParameterName(gibbsKey), "∆G", gibbs, energyUnit, uncertaintyStyle));

                var kelvin = temperatureCelsius + 273.15;
                var kdExponent = gibbs / (kelvin * Energy.R);
                var kd = FWEMath.Exp(kdExponent.FloatWithError);
                rows.Add(new AnalysisResultParameterEvaluationRow(
                    ParameterName(affinityKey),
                    kd.AsFormattedConcentration(withunit: true, style: uncertaintyStyle),
                    "Kd = " + kd.AsFormattedConcentration(withunit: true, withci: true, style: uncertaintyStyle)));
            }
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
                tooltipPrefix + " = " + value.ToFormattedString(energyUnit, permole: true, withci: true, style: uncertaintyStyle));
        }

        static string ParameterName(ParameterType key)
        {
            var properties = key.GetProperties();
            return $"{properties.Name} ({PlainSymbol(properties.SymbolName)})";
        }

        static bool HasSecondInteraction(AnalysisResult result)
        {
            return result?.Solution?.TemperatureDependence != null &&
                (result.Solution.TemperatureDependence.ContainsKey(ParameterType.Enthalpy2) ||
                 result.Solution.TemperatureDependence.ContainsKey(ParameterType.EntropyContribution2) ||
                 result.Solution.TemperatureDependence.ContainsKey(ParameterType.Gibbs2));
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
                .Replace("{,2}", "2");
        }
    }
}
