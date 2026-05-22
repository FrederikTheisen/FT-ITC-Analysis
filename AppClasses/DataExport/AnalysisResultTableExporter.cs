using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.AppClasses.AnalysisClasses.Models;

namespace AnalysisITC
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
        public EnergyUnit EnergyUnit { get; set; } = AppSettings.EnergyUnit;
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

            var parameters = GetParameterColumns(results);
            var concentrationUnits = GetConcentrationUnits(results, parameters);
            var includeIonicStrength = results.Any(r => r.IsElectrostaticsAnalysisDependenceEnabled);
            var includeProtonation = results.Any(r => r.IsProtonationAnalysisEnabled);
            var rows = new List<List<string>>
            {
                BuildHeader(results, parameters, concentrationUnits, includeIonicStrength, includeProtonation, options)
            };

            if (options.RowMode == AnalysisResultExportRowMode.Summary)
            {
                rows.AddRange(results.Select(result => BuildSummaryRow(result, parameters, concentrationUnits, includeIonicStrength, includeProtonation, options)));
            }
            else
            {
                foreach (var result in results)
                {
                    rows.AddRange(result.Solution.Solutions.Select(solution => BuildSolutionRow(result, solution, parameters, concentrationUnits, includeIonicStrength, includeProtonation, options)));
                }
            }

            return string.Join(Environment.NewLine, rows.Select(row => JoinRow(row, options.Delimiter)));
        }

        public static void WriteToFile(string path, IEnumerable<AnalysisResult> selectedResults, AnalysisResultExportOptions options)
        {
            File.WriteAllText(path, Build(selectedResults, options));
        }

        static List<ParameterType> GetParameterColumns(List<AnalysisResult> results)
        {
            var columns = new List<ParameterType>();

            foreach (var result in results)
            {
                foreach (var parameter in result.Solution.Solutions.SelectMany(s => s.ReportParameters.Keys))
                {
                    if (!columns.Contains(parameter)) columns.Add(parameter);
                }
            }

            return columns;
        }

        static Dictionary<ParameterType, ConcentrationUnit> GetConcentrationUnits(List<AnalysisResult> results, List<ParameterType> parameters)
        {
            var units = new Dictionary<ParameterType, ConcentrationUnit>();

            foreach (var parameter in parameters.Where(IsConcentrationParameter))
            {
                var values = results
                    .SelectMany(r => r.Solution.Solutions)
                    .Where(s => s.ReportParameters.ContainsKey(parameter))
                    .Select(s => Math.Abs(s.ReportParameters[parameter].Value))
                    .Where(v => v > 0)
                    .ToList();

                units[parameter] = values.Count > 0
                    ? ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(values.Average())
                    : AppSettings.DefaultConcentrationUnit;
            }

            return units;
        }

        static List<string> BuildHeader(List<AnalysisResult> results, List<ParameterType> parameters, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, bool includeIonicStrength, bool includeProtonation, AnalysisResultExportOptions options)
        {
            var header = new List<string> { "Analysis Result" };

            if (options.RowMode == AnalysisResultExportRowMode.Summary)
            {
                header.Add("Replicates");
            }
            else
            {
                header.Add("Experiment");
            }

            header.Add("Model");
            header.Add("Temperature (" + (options.UseKelvin ? "K" : "°C") + ")");

            if (includeIonicStrength) header.Add("IS (mM)");
            if (includeProtonation) header.Add("∆H,prot (" + options.EnergyUnit.GetUnit() + "/mol)");

            foreach (var parameter in parameters)
            {
                var label = GetParameterHeader(results, parameter, concentrationUnits, options);

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

        static List<string> BuildSummaryRow(AnalysisResult result, List<ParameterType> parameters, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, bool includeIonicStrength, bool includeProtonation, AnalysisResultExportOptions options)
        {
            var solutions = result.Solution.Solutions;
            var row = new List<string>
            {
                result.Name,
                solutions.Count.ToString(),
                GetModelName(result),
                solutions.Average(s => options.UseKelvin ? s.TempKelvin : s.Temp).ToString("F2")
            };

            if (includeIonicStrength) row.Add(result.IsElectrostaticsAnalysisDependenceEnabled ? "-" : "");

            if (includeProtonation) row.Add(result.IsProtonationAnalysisEnabled ? "-" : "");

            foreach (var parameter in parameters)
            {
                var values = solutions
                    .Where(solution => solution.ReportParameters.ContainsKey(parameter))
                    .Select(solution => solution.ReportParameters[parameter])
                    .ToList();

                AddValue(row, values.Count > 0 ? new FloatWithError(values) : FloatWithError.NaN, parameter, concentrationUnits, options);
            }

            row.Add(result.Solution.Loss.ToString("G3"));

            return row;
        }

        static List<string> BuildSolutionRow(AnalysisResult result, SolutionInterface solution, List<ParameterType> parameters, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, bool includeIonicStrength, bool includeProtonation, AnalysisResultExportOptions options)
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
                row.Add(result.IsProtonationAnalysisEnabled ? BufferAttribute.GetProtonationEnthalpy(solution.Data).ToString(options.EnergyUnit, "F1", withunit: false) : "");

            foreach (var parameter in parameters)
            {
                AddValue(
                    row,
                    solution.ReportParameters.ContainsKey(parameter) ? solution.ReportParameters[parameter] : FloatWithError.NaN,
                    parameter,
                    concentrationUnits,
                    options);
            }

            row.Add(solution.Loss.ToString("G3"));

            return row;
        }

        static void AddValue(List<string> row, FloatWithError value, ParameterType parameter, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, AnalysisResultExportOptions options)
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
                row.Add(FormatValue(value, parameter, concentrationUnits, options));
                return;
            }

            row.Add(FormatScalar(value.Value, parameter, concentrationUnits, options));

            switch (NormalizeExportUncertaintyStyle(options.UncertaintyDisplayStyle))
            {
                case UncertaintyDisplayStyle.ConfidenceInterval:
                    row.Add(FormatScalar(value.Lower, parameter, concentrationUnits, options));
                    row.Add(FormatScalar(value.Upper, parameter, concentrationUnits, options));
                    break;
                case UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval:
                    row.Add(FormatScalar(value.SD, parameter, concentrationUnits, options));
                    row.Add(FormatScalar(value.Lower, parameter, concentrationUnits, options));
                    row.Add(FormatScalar(value.Upper, parameter, concentrationUnits, options));
                    break;
                case UncertaintyDisplayStyle.StandardDeviation:
                default:
                    row.Add(FormatScalar(value.SD, parameter, concentrationUnits, options));
                    break;
            }
        }

        static string FormatValue(FloatWithError value, ParameterType parameter, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, AnalysisResultExportOptions options)
        {
            var style = NormalizeExportUncertaintyStyle(options.UncertaintyDisplayStyle);

            if (IsConcentrationParameter(parameter))
                return value.AsFormattedConcentration(concentrationUnits[parameter], withunit: false, style: style);

            if (ParameterTypeAttribute.IsEnergyUnitParameter(parameter))
                return new Energy(value).ToFormattedString(options.EnergyUnit, withunit: false, style: style);

            return value.ToString("G3", style);
        }

        static string FormatScalar(double value, ParameterType parameter, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, AnalysisResultExportOptions options)
        {
            if (IsConcentrationParameter(parameter))
                return (value * concentrationUnits[parameter].GetMod()).ToString("G5");

            if (ParameterTypeAttribute.IsEnergyUnitParameter(parameter))
                return Energy.ConvertFromJoule(value, options.EnergyUnit).ToString("G5");

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
            switch (NormalizeExportUncertaintyStyle(options.UncertaintyDisplayStyle))
            {
                case UncertaintyDisplayStyle.ConfidenceInterval:
                    return new[] { "_value", "_ci_lower", "_ci_upper" };
                case UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval:
                    return new[] { "_value", "_sd", "_ci_lower", "_ci_upper" };
                case UncertaintyDisplayStyle.StandardDeviation:
                default:
                    return new[] { "_value", "_sd" };
            }
        }

        static string GetParameterHeader(List<AnalysisResult> results, ParameterType parameter, Dictionary<ParameterType, ConcentrationUnit> concentrationUnits, AnalysisResultExportOptions options)
        {
            var containstwo = results
                .SelectMany(r => r.Solution.Solutions)
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
                case ParameterType.HeatCapacity1:
                case ParameterType.Offset:
                    return title + " (" + options.EnergyUnit.GetUnit() + "/mol)";
                default:
                    return title;
            }
        }

        static string GetParameterTitle(ParameterType parameter, bool containstwo, bool useSyringeCorrection)
        {
            switch (parameter)
            {
                case ParameterType.Nvalue1 when useSyringeCorrection: return "α";
                case ParameterType.Nvalue1: return "N" + (containstwo ? "1" : "");
                case ParameterType.Nvalue2: return "N2";
                case ParameterType.Enthalpy1: return "∆H" + (containstwo ? "1" : "");
                case ParameterType.Enthalpy2: return "∆H2";
                case ParameterType.Affinity1: return "Kd" + (containstwo ? "1" : "");
                case ParameterType.Affinity2: return "Kd2";
                case ParameterType.EntropyContribution1: return "-T∆S" + (containstwo ? "1" : "");
                case ParameterType.EntropyContribution2: return "-T∆S2";
                case ParameterType.Gibbs1: return "∆G" + (containstwo ? "1" : "");
                case ParameterType.Gibbs2: return "∆G2";
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
