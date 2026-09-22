using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Export
{
    public enum AnalysisResultExportRowMode
    {
        Summary,
        AllRows
    }

    public enum AnalysisResultExportErrorStyle
    {
        ValueWithError,
        SeparateColumns
    }

    public enum AnalysisResultExportFileFormat
    {
        CSV,
        TSV
    }

    public class AnalysisResultExportOptions
    {
        public AnalysisResultExportRowMode RowMode { get; set; } = AnalysisResultExportRowMode.Summary;
        public AnalysisResultExportErrorStyle ErrorStyle { get; set; } = AnalysisResultExportErrorStyle.ValueWithError;
        public AnalysisResultExportFileFormat FileFormat { get; set; } = AnalysisResultExportFileFormat.CSV;
        public UncertaintyDisplayStyle UncertaintyDisplayStyle { get; set; } = UncertaintyDisplayStyle.StandardDeviation;
        public EnergyUnitFamily EnergyUnitFamily { get; set; } = AppSettings.EnergyUnitFamily;
        public EnergyUnit? EnergyUnitOverride { get; set; }
        public EnergyUnit ResolvedEnergyUnit { get; internal set; } = EnergyUnit.KiloJoule;
        public EnergyUnit ResolvedHeatCapacityUnit { get; internal set; } = EnergyUnit.KiloJoule;

        /// <summary>
        /// Legacy exact-unit property. Assigning it creates a fixed override;
        /// leaving <see cref="EnergyUnitOverride"/> null selects Automatic.
        /// </summary>
        [Obsolete("Use EnergyUnitFamily and EnergyUnitOverride.")]
        public EnergyUnit EnergyUnit
        {
            get => EnergyUnitOverride ?? EnergyUnitResolver.DefaultUnit(EnergyUnitFamily);
            set
            {
                EnergyUnitResolver.ValidateOverride(value);
                EnergyUnitOverride = value;
            }
        }
        public bool UseKelvin { get; set; } = false;

        public char Delimiter => FileFormat == AnalysisResultExportFileFormat.TSV ? '\t' : ',';
        public string FileExtension => FileFormat == AnalysisResultExportFileFormat.TSV ? "tsv" : "csv";
    }

    public static class AnalysisResultTableExporter
    {
        public static string Build(IEnumerable<AnalysisResult> selectedResults, AnalysisResultExportOptions options)
        {
            var results = selectedResults?.Where(r => r != null).ToList() ?? new List<AnalysisResult>();
            if (results.Count == 0) return "";

            options ??= new AnalysisResultExportOptions();

            var parameters = GetParameterColumns(results, options);
            var concentrationUnits = GetConcentrationUnits(results, parameters, options);
            var energyUnits = GetEnergyUnits(results, parameters, options);
            options.ResolvedEnergyUnit = energyUnits.molar;
            options.ResolvedHeatCapacityUnit = energyUnits.heatCapacity;
            var includeIonicStrength = results.Any(r => r.IsElectrostaticsAnalysisDependenceEnabled);
            var includeProtonation = results.Any(r => r.IsProtonationAnalysisEnabled);
            var rows = new List<List<string>>
            {
                BuildHeader(results, parameters, concentrationUnits, energyUnits, includeIonicStrength, includeProtonation, options)
            };

            if (options.RowMode == AnalysisResultExportRowMode.Summary)
            {
                rows.AddRange(results.Select(result => BuildSummaryRow(result, parameters, concentrationUnits, energyUnits, includeIonicStrength, includeProtonation, options)));
            }
            else
            {
                foreach (var result in results)
                {
                    rows.AddRange(result.Solution.Solutions.Select(solution => BuildSolutionRow(result, solution, parameters, concentrationUnits, energyUnits, includeIonicStrength, includeProtonation, options)));
                }
            }

            return string.Join(Environment.NewLine, rows.Select(row => JoinRow(row, options.Delimiter)));
        }

        public static void WriteToFile(string path, IEnumerable<AnalysisResult> selectedResults, AnalysisResultExportOptions options)
        {
            File.WriteAllText(path, Build(selectedResults, options));
        }

        static List<ParameterType> GetParameterColumns(List<AnalysisResult> results, AnalysisResultExportOptions options)
        {
            var columns = new List<ParameterType>();

            foreach (var result in results)
            {
                foreach (var parameter in result.Solution.Solutions.SelectMany(s => s.ReportParameters.Keys))
                {
                    if (!columns.Contains(parameter)) columns.Add(parameter);
                }

                // ∆Cp is an evaluated summary quantity, not necessarily a member
                // report parameter. Keep replicate-only tables unchanged.
                if (options.RowMode == AnalysisResultExportRowMode.Summary
                    && result.IsTemperatureDependenceEnabled)
                {
                    var calculator = new AnalysisResultAggregateSummaryCalculator(result);
                    foreach (var slot in ThermodynamicParameterSlots.Active(result.Model.Models.First()))
                    {
                        var heatCapacity = calculator.EvaluateHeatCapacity(slot)?.Value;
                        if (heatCapacity != null && SummaryUncertainty.IsFinite(heatCapacity.Value)
                            && Math.Abs(heatCapacity.Value) > 0
                            && !columns.Contains(slot.HeatCapacity))
                        {
                            columns.Add(slot.HeatCapacity);
                        }
                    }
                }
            }

            return columns;
        }

        static Dictionary<ParameterType, ConcentrationUnit> GetConcentrationUnits(
            List<AnalysisResult> results,
            List<ParameterType> parameters,
            AnalysisResultExportOptions options)
        {
            var units = new Dictionary<ParameterType, ConcentrationUnit>();

            foreach (var parameter in parameters.Where(IsConcentrationParameter))
            {
                var values = new List<double>();
                if (options.RowMode == AnalysisResultExportRowMode.Summary)
                {
                    foreach (var result in results)
                    {
                        var value = SummaryValue(result, parameter);
                        if (SummaryUncertainty.HasValue(value) && value.Value > 0)
                            values.Add(Math.Abs(value.Value));
                    }
                }
                else
                {
                    values.AddRange(results
                        .SelectMany(r => r.Solution.Solutions)
                        .Where(s => s.ReportParameters.ContainsKey(parameter))
                        .Select(s => Math.Abs(s.ReportParameters[parameter].Value))
                        .Where(value => value > 0));
                }

                units[parameter] = values.Count > 0
                    ? ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(values.Average())
                    : AppSettings.DefaultConcentrationUnit;
            }

            return units;
        }

        static (EnergyUnit molar, EnergyUnit heatCapacity) GetEnergyUnits(
            List<AnalysisResult> results,
            List<ParameterType> parameters,
            AnalysisResultExportOptions options)
        {
            var molarValues = new List<double>();
            var heatCapacityValues = new List<double>();

            if (options.RowMode == AnalysisResultExportRowMode.Summary)
            {
                foreach (var result in results)
                {
                    foreach (var parameter in parameters)
                    {
                        if (!ParameterTypeAttribute.IsEnergyUnitParameter(parameter)) continue;
                        var value = SummaryValue(result, parameter);
                        if (!SummaryUncertainty.HasValue(value)) continue;
                        if (IsHeatCapacityParameter(parameter)) heatCapacityValues.Add(value.Value);
                        else molarValues.Add(value.Value);
                    }
                }

                return (
                    EnergyUnitResolver.Resolve(options.EnergyUnitFamily, options.EnergyUnitOverride, molarValues),
                    EnergyUnitResolver.Resolve(options.EnergyUnitFamily, options.EnergyUnitOverride, heatCapacityValues));
            }

            foreach (var result in results)
            {
                foreach (var solution in result?.Solution?.Solutions ?? new List<SolutionInterface>())
                {
                    foreach (var item in solution?.ReportParameters ?? new Dictionary<ParameterType, FloatWithError>())
                    {
                        if (!ParameterTypeAttribute.IsEnergyUnitParameter(item.Key)) continue;
                        if (IsHeatCapacityParameter(item.Key)) heatCapacityValues.Add(item.Value.Value);
                        else molarValues.Add(item.Value.Value);
                    }

                    if (result.IsProtonationAnalysisEnabled
                        && BufferAttribute.TryGetProtonationEnthalpy(solution?.Data, out var protonation))
                        molarValues.Add(protonation.Value);
                }

                if (result?.Solution?.TemperatureDependence != null)
                    heatCapacityValues.AddRange(result.Solution.TemperatureDependence.Values.Select(dependence => dependence.Slope.Value));
            }

            return (
                EnergyUnitResolver.Resolve(options.EnergyUnitFamily, options.EnergyUnitOverride, molarValues),
                EnergyUnitResolver.Resolve(options.EnergyUnitFamily, options.EnergyUnitOverride, heatCapacityValues));
        }

        static EnergyUnit EnergyUnitFor(ParameterType parameter, (EnergyUnit molar, EnergyUnit heatCapacity) energyUnits)
        {
            return IsHeatCapacityParameter(parameter) ? energyUnits.heatCapacity : energyUnits.molar;
        }

        static bool IsHeatCapacityParameter(ParameterType parameter)
        {
            return parameter.GetProperties().ParentType == ParameterType.HeatCapacity1;
        }

        static List<string> BuildHeader(List<AnalysisResult> results, List<ParameterType> parameters, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, (EnergyUnit molar, EnergyUnit heatCapacity) energyUnits, bool includeIonicStrength, bool includeProtonation, AnalysisResultExportOptions options)
        {
            var hasProfileLikelihood = results.Any(result => result?.Solution?.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood
                || result?.Solution?.Solutions?.Any(solution => solution?.ErrorMethod == ErrorEstimationMethod.ProfileLikelihood) == true);
            var header = new List<string>
            {
                options.RowMode == AnalysisResultExportRowMode.Summary
                    ? "Analysis Result (Combined SD; Approximate propagated interval for local aggregates; model CI95 unchanged"
                        + (hasProfileLikelihood ? "; profile SD = equivalent scale)" : ")")
                    : hasProfileLikelihood ? "Analysis Result (profile SD = equivalent scale)" : "Analysis Result"
            };

            if (options.RowMode == AnalysisResultExportRowMode.Summary)
            {
                header.Add("Replicates");
            }
            else
            {
                header.Add("Experiment");
            }

            header.Add("Model");
            header.Add((options.RowMode == AnalysisResultExportRowMode.Summary ? "Evaluation temperature" : "Temperature")
                + " (" + (options.UseKelvin ? "K" : "°C") + ")");

            if (includeIonicStrength) header.Add("IS (mM)");
            if (includeProtonation) header.Add("∆H,prot (" + energyUnits.molar.GetUnit() + "/mol)");

            foreach (var parameter in parameters)
            {
                var label = GetParameterHeader(results, parameter, concentrationUnits, energyUnits, options);

                if (options.ErrorStyle == AnalysisResultExportErrorStyle.ValueWithError)
                {
                    header.Add(label);
                }
                else
                {
                    foreach (var suffix in GetSeparateColumnSuffixes(options))
                        header.Add(label + suffix);
                }
            }

            header.Add("Loss");

            return header;
        }

        static List<string> BuildSummaryRow(AnalysisResult result, List<ParameterType> parameters, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, (EnergyUnit molar, EnergyUnit heatCapacity) energyUnits, bool includeIonicStrength, bool includeProtonation, AnalysisResultExportOptions options)
        {
            var solutions = result.Solution.Solutions;
            var row = new List<string>
            {
                result.Name,
                solutions.Count.ToString(),
                GetModelName(result),
                (options.UseKelvin
                    ? AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result) + 273.15
                    : AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result)).ToString("F2")
            };

            if (includeIonicStrength) row.Add(result.IsElectrostaticsAnalysisDependenceEnabled ? "-" : "");

            if (includeProtonation) row.Add(result.IsProtonationAnalysisEnabled ? "-" : "");

            foreach (var parameter in parameters)
            {
                AddValue(
                    row,
                    SummaryValue(result, parameter),
                    parameter,
                    concentrationUnits,
                    energyUnits,
                    options);
            }

            row.Add(result.Solution.UnweightedRmsd.ToString("G3"));

            return row;
        }

        internal static FloatWithError SummaryValue(AnalysisResult result, ParameterType parameter)
        {
            var calculator = new AnalysisResultAggregateSummaryCalculator(result);
            if (ThermodynamicParameterSlots.TryResolve(parameter, out _, out var family))
            {
                if (family == ThermodynamicParameterFamily.HeatCapacity
                    && !result.IsTemperatureDependenceEnabled)
                    return FloatWithError.NaN;

                // Do not substitute raw member values if the saved thermodynamic
                // evaluation is absent: that would silently change the meaning.
                return calculator.EvaluateSummaryParameter(
                    parameter, AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result))?.Value
                    ?? FloatWithError.NaN;
            }

            var members = result.Solution.Solutions
                .Where(member => member.ReportParameters.ContainsKey(parameter))
                .Select(member => (data: member.Data, value: member.ReportParameters[parameter]))
                .Where(item => SummaryUncertainty.HasValue(item.value)).ToList();
            var values = members.Select(item => item.value).ToList();
            if (values.Count == 0) return FloatWithError.NaN;
            if (!calculator.IsLocallyAggregated(parameter))
                return values[0];
            var central = values.Average(value => value.Value);
            if (SummaryUncertainty.HasDuplicateExperiments(members.Select(item => item.data)))
                return SummaryUncertainty.Unavailable(central);
            return SummaryUncertainty.Mean(values, central).Evaluate(0);
        }

        // Existing model-estimated summary path; repeated shared parameters are
        // not independent observations and must not use the new mean formula.
        internal static FloatWithError SummaryValue(AnalysisResult result, List<FloatWithError> values)
        {
            if (values == null || values.Count == 0) return FloatWithError.NaN;
            if (result?.Solution?.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood)
            {
                var first = values[0];
                if (values.Count == 1 || values.All(value => value.Value == first.Value
                    && value.SD == first.SD && value.Lower == first.Lower && value.Upper == first.Upper))
                    return first;
            }
            return new FloatWithError(values, values.Average(value => value.Value));
        }

        static List<string> BuildSolutionRow(AnalysisResult result, SolutionInterface solution, List<ParameterType> parameters, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, (EnergyUnit molar, EnergyUnit heatCapacity) energyUnits, bool includeIonicStrength, bool includeProtonation, AnalysisResultExportOptions options)
        {
            var row = new List<string>
            {
                result.Name,
                solution.Data?.Name ?? solution.SolutionName,
                GetModelName(result),
                (options.UseKelvin ? solution.TempKelvin : solution.Temp).ToString("F2")
            };

            if (includeIonicStrength)
                row.Add(result.IsElectrostaticsAnalysisDependenceEnabled ? (1000 * BufferAttribute.GetIonicStrength(solution.Data)).ToString("F2") : "");

            if (includeProtonation)
                row.Add(result.IsProtonationAnalysisEnabled ? FormatProtonationEnthalpy(solution.Data, energyUnits.molar) : "");

            foreach (var parameter in parameters)
            {
                AddValue(
                    row,
                    solution.ReportParameters.ContainsKey(parameter) ? solution.ReportParameters[parameter] : FloatWithError.NaN,
                    parameter,
                    concentrationUnits,
                    energyUnits,
                    options);
            }

            row.Add(solution.UnweightedRmsd.ToString("G3"));

            return row;
        }

        static string FormatProtonationEnthalpy(ExperimentData data, EnergyUnit energyUnit)
        {
            return BufferAttribute.TryGetProtonationEnthalpy(data, out var enthalpy)
                ? enthalpy.ToString(energyUnit, "F1", withunit: false)
                : "";
        }

        static void AddValue(List<string> row, FloatWithError value, ParameterType parameter, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, (EnergyUnit molar, EnergyUnit heatCapacity) energyUnits, AnalysisResultExportOptions options)
        {
            if (FloatWithError.IsNaN(value))
            {
                if (options.ErrorStyle == AnalysisResultExportErrorStyle.ValueWithError)
                {
                    row.Add("");
                }
                else
                {
                    foreach (var _ in GetSeparateColumnSuffixes(options))
                        row.Add("");
                }
                return;
            }

            if (options.ErrorStyle == AnalysisResultExportErrorStyle.ValueWithError)
            {
                row.Add(FormatValue(value, parameter, concentrationUnits, energyUnits, options));
                return;
            }

            row.Add(FormatScalar(value.Value, parameter, concentrationUnits, energyUnits, options));

            switch (NormalizeExportUncertaintyStyle(options.UncertaintyDisplayStyle))
            {
                case UncertaintyDisplayStyle.ConfidenceInterval:
                    row.Add(FormatScalar(value.Lower, parameter, concentrationUnits, energyUnits, options));
                    row.Add(FormatScalar(value.Upper, parameter, concentrationUnits, energyUnits, options));
                    break;
                case UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval:
                    row.Add(FormatScalar(value.SD, parameter, concentrationUnits, energyUnits, options));
                    row.Add(FormatScalar(value.Lower, parameter, concentrationUnits, energyUnits, options));
                    row.Add(FormatScalar(value.Upper, parameter, concentrationUnits, energyUnits, options));
                    break;
                case UncertaintyDisplayStyle.StandardDeviation:
                default:
                    row.Add(FormatScalar(value.SD, parameter, concentrationUnits, energyUnits, options));
                    break;
            }
        }

        static string FormatValue(FloatWithError value, ParameterType parameter, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, (EnergyUnit molar, EnergyUnit heatCapacity) energyUnits, AnalysisResultExportOptions options)
        {
            var style = NormalizeExportUncertaintyStyle(options.UncertaintyDisplayStyle);

            if (IsConcentrationParameter(parameter))
                return value.AsFormattedConcentration(concentrationUnits[parameter], withunit: false, style: style);

            if (ParameterTypeAttribute.IsEnergyUnitParameter(parameter))
                return new Energy(value).ToFormattedString(EnergyUnitFor(parameter, energyUnits), withunit: false, style: style);

            return value.ToString("G3", style);
        }

        static string FormatScalar(double value, ParameterType parameter, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, (EnergyUnit molar, EnergyUnit heatCapacity) energyUnits, AnalysisResultExportOptions options)
        {
            if (!SummaryUncertainty.IsFinite(value)) return "";
            if (IsConcentrationParameter(parameter))
                return (value * concentrationUnits[parameter].GetMod()).ToString("G5");

            if (ParameterTypeAttribute.IsEnergyUnitParameter(parameter))
                return Energy.ConvertFromJoule(value, EnergyUnitFor(parameter, energyUnits)).ToString("G5");

            return value.ToString("G5");
        }

        static UncertaintyDisplayStyle NormalizeExportUncertaintyStyle(UncertaintyDisplayStyle style)
        {
            return style == UncertaintyDisplayStyle.Automatic
                ? UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval
                : style;
        }

        static string[] GetSeparateColumnSuffixes(AnalysisResultExportOptions options)
        {
            var intervalPrefix = options.RowMode == AnalysisResultExportRowMode.Summary ? "_interval" : "_ci";
            switch (NormalizeExportUncertaintyStyle(options.UncertaintyDisplayStyle))
            {
                case UncertaintyDisplayStyle.ConfidenceInterval:
                    return new[] { "_value", intervalPrefix + "_lower", intervalPrefix + "_upper" };
                case UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval:
                    return new[] { "_value", "_sd", intervalPrefix + "_lower", intervalPrefix + "_upper" };
                case UncertaintyDisplayStyle.StandardDeviation:
                default:
                    return new[] { "_value", "_sd" };
            }
        }

        static string GetParameterHeader(List<AnalysisResult> results, ParameterType parameter, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, (EnergyUnit molar, EnergyUnit heatCapacity) energyUnits, AnalysisResultExportOptions options)
        {
            var reportParameters = results
                .SelectMany(result => result.Solution.IndividualModelReportParameters)
                .ToList();
            var containstwo = ThermodynamicParameterSlots.TryResolve(parameter, out _, out _)
                ? ThermodynamicParameterSlots.FamilyMemberCount(reportParameters, parameter) > 1
                : results.SelectMany(r => r.Solution.Solutions)
                    .Any(solution => solution.ParametersConformingToKey(parameter).Count > 1);

            var modelOptions = results
                .Select(r => r.Solution.Solutions.FirstOrDefault()?.ModelOptions)
                .FirstOrDefault(optionsMap => optionsMap != null);

            var useSyringeCorrection = modelOptions != null
                && modelOptions.ContainsKey(AttributeKey.UseSyringeActiveFraction)
                && (modelOptions[AttributeKey.UseSyringeActiveFraction]?.BoolValue ?? false);

            var unit = IsConcentrationParameter(parameter)
                ? concentrationUnits[parameter].GetName()
                : AppSettings.DefaultConcentrationUnit.GetName();

            var title = GetParameterTitle(parameter, containstwo, useSyringeCorrection);

            switch (parameter.GetProperties().ParentType)
            {
                case ParameterType.Affinity1: return title + " (" + unit + ")";
                case ParameterType.Enthalpy1:
                case ParameterType.Gibbs1:
                case ParameterType.EntropyContribution1:
                case ParameterType.Offset:
                    return title + " (" + energyUnits.molar.GetUnit() + "/mol)";
                case ParameterType.HeatCapacity1:
                    return title + " (" + energyUnits.heatCapacity.GetUnit() + "/(mol·K))";
                case ParameterType.Entropy1:
                    return title + " (" + energyUnits.molar.GetUnit() + "/(mol·K))";
                default:
                    return title;
            }
        }

        static string GetParameterTitle(ParameterType parameter, bool containstwo, bool useSyringeCorrection)
        {
            if (ThermodynamicParameterSlots.TryResolve(parameter, out var slot, out var family))
            {
                var suffix = containstwo || slot.Index > 1 ? slot.Index.ToString() : string.Empty;
                return family switch
                {
                    ThermodynamicParameterFamily.Enthalpy => "∆H" + suffix,
                    ThermodynamicParameterFamily.Affinity => "Kd" + suffix,
                    ThermodynamicParameterFamily.EntropyContribution => "-T∆S" + suffix,
                    ThermodynamicParameterFamily.Gibbs => "∆G" + suffix,
                    ThermodynamicParameterFamily.HeatCapacity => "∆Cp" + suffix,
                    ThermodynamicParameterFamily.Entropy => "∆S" + suffix,
                    _ => parameter.GetProperties().Name,
                };
            }

            switch (parameter)
            {
                case ParameterType.Nvalue1 when useSyringeCorrection: return "α";
                case ParameterType.Nvalue1: return "N" + (containstwo ? "1" : "");
                case ParameterType.Nvalue2: return "N2";
                case ParameterType.ApparentAffinity: return "Kd_app";
                case ParameterType.IsomerizationEquilibriumConstant: return "Keq";
                default: return parameter.GetProperties().Name;
            }
        }

        static bool IsConcentrationParameter(ParameterType parameter)
        {
            switch (parameter.GetProperties().ParentType)
            {
                case ParameterType.Affinity1:
                case ParameterType.ApparentAffinity:
                    return true;
                default:
                    return false;
            }
        }

        static string GetModelName(AnalysisResult result)
        {
            try
            {
                return result.Model.ModelType.GetProperties().Name;
            }
            catch
            {
                return result.Model.ModelType.ToString();
            }
        }

        static string JoinRow(IEnumerable<string> values, char delimiter)
        {
            return string.Join(delimiter.ToString(), values.Select(value => Escape(value, delimiter)));
        }

        static string Escape(string value, char delimiter)
        {
            value ??= "";

            if (value.Contains("\"")) value = value.Replace("\"", "\"\"");

            return value.Contains(delimiter) || value.Contains("\"") || value.Contains("\n") || value.Contains("\r")
                ? "\"" + value + "\""
                : value;
        }
    }
}
