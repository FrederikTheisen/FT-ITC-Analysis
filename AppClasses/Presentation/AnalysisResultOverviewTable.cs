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
    public enum AnalysisResultColumnAlignment
    {
        Left,
        Center,
        Right
    }

    public sealed class AnalysisResultOverviewColumn
    {
        public AnalysisResultOverviewColumn(string id, string title, AnalysisResultColumnAlignment alignment, double preferredWidth, ParameterType? parameter = null)
        {
            Id = id;
            Title = title;
            Alignment = alignment;
            PreferredWidth = preferredWidth;
            Parameter = parameter;
        }

        public string Id { get; }
        public string Title { get; }
        public AnalysisResultColumnAlignment Alignment { get; }
        public double PreferredWidth { get; }
        public ParameterType? Parameter { get; }
    }

    public sealed class AnalysisResultOverviewRow
    {
        readonly Dictionary<string, string> values;

        public AnalysisResultOverviewRow(SolutionInterface solution, Dictionary<string, string> values)
        {
            Solution = solution;
            this.values = values;
        }

        public SolutionInterface Solution { get; }
        public string this[string columnId] => values.TryGetValue(columnId, out var value) ? value : "";
    }

    public sealed class AnalysisResultOverviewTable
    {
        AnalysisResultOverviewTable(List<AnalysisResultOverviewColumn> columns, List<AnalysisResultOverviewRow> rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public IReadOnlyList<AnalysisResultOverviewColumn> Columns { get; }
        public IReadOnlyList<AnalysisResultOverviewRow> Rows { get; }

        public static AnalysisResultOverviewTable Build(AnalysisResult result, EnergyUnit energyUnit, bool useKelvin)
        {
            var columns = new List<AnalysisResultOverviewColumn>
            {
                new AnalysisResultOverviewColumn("Experiment", "Experiment", AnalysisResultColumnAlignment.Left, 170)
            };

            if (result?.IsTemperatureDependenceEnabled == true)
                columns.Add(new AnalysisResultOverviewColumn("Temp", "Temperature (" + (useKelvin ? "K" : "°C") + ")", AnalysisResultColumnAlignment.Right, 116));

            if (result?.IsElectrostaticsAnalysisDependenceEnabled == true)
                columns.Add(new AnalysisResultOverviewColumn("IS", "[Ions] (mM)", AnalysisResultColumnAlignment.Right, 96));

            if (result?.IsProtonationAnalysisEnabled == true)
                columns.Add(new AnalysisResultOverviewColumn("HPROT", "∆H,prot (" + energyUnit.GetUnit() + "/mol)", AnalysisResultColumnAlignment.Right, 126));

            var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            var options = solutions.FirstOrDefault()?.ModelOptions ?? new Dictionary<AttributeKey, ExperimentAttribute>();
            var parameters = result?.Solution?.IndividualModelReportParameters ?? new List<ParameterType>();
            var affinityUnit = ResolveAffinityUnit(result);

            foreach (var parameter in parameters)
            {
                var containsTwo = solutions.FirstOrDefault()?.ParametersConformingToKey(parameter).Count > 1;
                var title = ParameterTypeAttribute.TableHeader(options, parameter, containsTwo == true, energyUnit, affinityUnit.GetName());
                columns.Add(new AnalysisResultOverviewColumn(ParameterColumnId(parameter), title, AnalysisResultColumnAlignment.Right, 108, parameter));
            }

            columns.Add(new AnalysisResultOverviewColumn("Loss", "Loss", AnalysisResultColumnAlignment.Right, 76));

            var rows = solutions
                .Select(solution => new AnalysisResultOverviewRow(solution, BuildRow(result, solution, columns, energyUnit, affinityUnit, useKelvin)))
                .ToList();

            return new AnalysisResultOverviewTable(columns, rows);
        }

        static Dictionary<string, string> BuildRow(
            AnalysisResult result,
            SolutionInterface solution,
            List<AnalysisResultOverviewColumn> columns,
            EnergyUnit energyUnit,
            ConcentrationUnit affinityUnit,
            bool useKelvin)
        {
            var values = new Dictionary<string, string>
            {
                ["Experiment"] = solution?.Data?.Name ?? "",
                ["Temp"] = solution == null ? "" : (solution.Temp + (useKelvin ? 273.15 : 0)).ToString("F2", CultureInfo.CurrentCulture),
                ["IS"] = solution?.Data == null ? "" : (1000 * BufferAttribute.GetIonicStrength(solution.Data)).ToString("F1", CultureInfo.CurrentCulture),
                ["HPROT"] = solution?.Data == null ? "" : BufferAttribute.GetProtonationEnthalpy(solution.Data).ToString(energyUnit, "F1", withunit: false),
                ["Loss"] = solution?.Loss.ToString("G3", CultureInfo.CurrentCulture) ?? ""
            };

            foreach (var column in columns.Where(column => column.Parameter.HasValue))
            {
                var parameter = column.Parameter.Value;
                values[column.Id] = solution?.ReportParameters != null && solution.ReportParameters.TryGetValue(parameter, out var value)
                    ? FormatParameter(parameter, value, energyUnit, affinityUnit)
                    : "";
            }

            return values;
        }

        static string FormatParameter(ParameterType parameter, FloatWithError value, EnergyUnit energyUnit, ConcentrationUnit affinityUnit)
        {
            return parameter.GetProperties().ParentType switch
            {
                ParameterType.Affinity1 => value.AsFormattedConcentration(affinityUnit, withunit: false),
                ParameterType.Enthalpy1 => value.Energy.ToFormattedString(energyUnit, withunit: false),
                ParameterType.Gibbs1 => value.Energy.ToFormattedString(energyUnit, withunit: false),
                ParameterType.EntropyContribution1 => value.Energy.ToFormattedString(energyUnit, withunit: false),
                _ => value.AsNumber()
            };
        }

        static ConcentrationUnit ResolveAffinityUnit(AnalysisResult result)
        {
            try
            {
                return result == null ? ConcentrationUnit.µM : result.AppropriateAffinityUnit;
            }
            catch
            {
                return AppSettings.DefaultConcentrationUnit;
            }
        }

        public static string ParameterColumnId(ParameterType parameter)
        {
            return "Parameter:" + ((int)parameter).ToString(CultureInfo.InvariantCulture);
        }
    }
}
