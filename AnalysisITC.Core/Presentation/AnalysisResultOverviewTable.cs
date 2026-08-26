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
        AnalysisResultOverviewTable(List<AnalysisResultOverviewColumn> columns, List<AnalysisResultOverviewRow> rows, EnergyUnit energyUnit, EnergyUnit heatCapacityUnit)
        {
            Columns = columns;
            Rows = rows;
            ResolvedEnergyUnit = energyUnit;
            ResolvedHeatCapacityUnit = heatCapacityUnit;
        }

        public IReadOnlyList<AnalysisResultOverviewColumn> Columns { get; }
        public IReadOnlyList<AnalysisResultOverviewRow> Rows { get; }
        public EnergyUnit ResolvedEnergyUnit { get; }
        public EnergyUnit ResolvedHeatCapacityUnit { get; }

        public static AnalysisResultOverviewTable Build(AnalysisResult result, EnergyUnit energyUnit, bool useKelvin)
        {
            EnergyUnitResolver.ValidateOverride(energyUnit);
            return BuildInternal(result, energyUnit, energyUnit, useKelvin);
        }

        public static AnalysisResultOverviewTable Build(AnalysisResult result, EnergyUnitFamily family, bool useKelvin)
        {
            return Build(result, family, null, useKelvin);
        }

        public static AnalysisResultOverviewTable Build(AnalysisResult result, EnergyUnitFamily family, EnergyUnit? energyUnitOverride, bool useKelvin)
        {
            var units = ResolveEnergyUnits(result, family, energyUnitOverride);
            return BuildInternal(result, units.molar, units.heatCapacity, useKelvin);
        }

        static AnalysisResultOverviewTable BuildInternal(AnalysisResult result, EnergyUnit molarEnergyUnit, EnergyUnit heatCapacityUnit, bool useKelvin)
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
                columns.Add(new AnalysisResultOverviewColumn("HPROT", "∆H,prot (" + molarEnergyUnit.GetUnit() + "/mol)", AnalysisResultColumnAlignment.Right, 126));

            var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            var options = solutions.FirstOrDefault()?.ModelOptions ?? new Dictionary<AttributeKey, ExperimentAttribute>();
            var parameters = result?.Solution?.IndividualModelReportParameters ?? new List<ParameterType>();
            var affinityUnits = parameters
                .Where(IsAffinityParameter)
                .Distinct()
                .ToDictionary(parameter => parameter, parameter => ResolveAffinityUnit(result, parameter));

            foreach (var parameter in parameters)
            {
                var containsTwo = ThermodynamicParameterSlots.TryResolve(parameter, out _, out _)
                    ? ThermodynamicParameterSlots.FamilyMemberCount(parameters, parameter) > 1
                    : solutions.FirstOrDefault()?.ParametersConformingToKey(parameter).Count > 1;
                var affinityUnit = affinityUnits.TryGetValue(parameter, out var unit)
                    ? unit
                    : AppSettings.DefaultConcentrationUnit;
                var parameterUnit = IsHeatCapacityParameter(parameter) ? heatCapacityUnit : molarEnergyUnit;
                var title = ParameterTypeAttribute.TableHeader(options, parameter, containsTwo == true, parameterUnit, affinityUnit.GetName());
                columns.Add(new AnalysisResultOverviewColumn(ParameterColumnId(parameter), title, AnalysisResultColumnAlignment.Right, 108, parameter));
            }

            columns.Add(new AnalysisResultOverviewColumn("Loss", "Loss", AnalysisResultColumnAlignment.Right, 76));

            var rows = solutions
                .Select(solution => new AnalysisResultOverviewRow(solution, BuildRow(result, solution, columns, molarEnergyUnit, heatCapacityUnit, affinityUnits, useKelvin)))
                .ToList();

            return new AnalysisResultOverviewTable(columns, rows, molarEnergyUnit, heatCapacityUnit);
        }

        static Dictionary<string, string> BuildRow(
            AnalysisResult result,
            SolutionInterface solution,
            List<AnalysisResultOverviewColumn> columns,
            EnergyUnit molarEnergyUnit,
            EnergyUnit heatCapacityUnit,
            IReadOnlyDictionary<ParameterType, ConcentrationUnit> affinityUnits,
            bool useKelvin)
        {
            var values = new Dictionary<string, string>
            {
                ["Experiment"] = solution?.Data?.Name ?? "",
                ["Temp"] = solution == null ? "" : (solution.Temp + (useKelvin ? 273.15 : 0)).ToString("F2", CultureInfo.CurrentCulture),
                ["IS"] = solution?.Data == null ? "" : (1000 * BufferAttribute.GetIonicStrength(solution.Data)).ToString("F1", CultureInfo.CurrentCulture),
                ["HPROT"] = FormatProtonationEnthalpy(solution?.Data, molarEnergyUnit),
                ["Loss"] = solution?.Loss.ToString("G3", CultureInfo.CurrentCulture) ?? ""
            };

            foreach (var column in columns.Where(column => column.Parameter.HasValue))
            {
                var parameter = column.Parameter.Value;
                values[column.Id] = solution?.ReportParameters != null && solution.ReportParameters.TryGetValue(parameter, out var value)
                    ? FormatParameter(
                        parameter,
                        value,
                        IsHeatCapacityParameter(parameter) ? heatCapacityUnit : molarEnergyUnit,
                        affinityUnits.TryGetValue(parameter, out var unit)
                            ? unit
                            : AppSettings.DefaultConcentrationUnit)
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
                ParameterType.HeatCapacity1 => value.Energy.ToFormattedString(energyUnit, withunit: false, perK: true),
                ParameterType.Offset => value.Energy.ToFormattedString(energyUnit, withunit: false),
                ParameterType.Entropy1 => value.Energy.ToFormattedString(energyUnit, withunit: false),
                _ => value.AsNumber()
            };
        }

        static string FormatProtonationEnthalpy(ExperimentData data, EnergyUnit energyUnit)
        {
            return BufferAttribute.TryGetProtonationEnthalpy(data, out var enthalpy)
                ? enthalpy.ToString(energyUnit, "F1", withunit: false)
                : "";
        }

        static ConcentrationUnit ResolveAffinityUnit(AnalysisResult result, ParameterType parameter)
        {
            try
            {
                return result == null
                    ? AppSettings.DefaultConcentrationUnit
                    : result.GetAppropriateAffinityUnit(parameter);
            }
            catch
            {
                return AppSettings.DefaultConcentrationUnit;
            }
        }

        static bool IsAffinityParameter(ParameterType parameter)
        {
            return parameter == ParameterType.ApparentAffinity
                || parameter.GetProperties().ParentType == ParameterType.Affinity1;
        }

        static bool IsHeatCapacityParameter(ParameterType parameter)
        {
            return parameter.GetProperties().ParentType == ParameterType.HeatCapacity1;
        }

        static (EnergyUnit molar, EnergyUnit heatCapacity) ResolveEnergyUnits(AnalysisResult result, EnergyUnitFamily family, EnergyUnit? energyUnitOverride)
        {
            var solutions = result?.Solution?.Solutions ?? new List<SolutionInterface>();
            var molarValues = new List<double>();
            var heatCapacityValues = new List<double>();

            foreach (var solution in solutions)
            {
                if (solution?.ReportParameters != null)
                {
                    foreach (var item in solution.ReportParameters)
                    {
                        if (!ParameterTypeAttribute.IsEnergyUnitParameter(item.Key)) continue;
                        if (IsHeatCapacityParameter(item.Key)) heatCapacityValues.Add(item.Value.Value);
                        else molarValues.Add(item.Value.Value);
                    }
                }

                if (result?.IsProtonationAnalysisEnabled == true
                    && BufferAttribute.TryGetProtonationEnthalpy(solution?.Data, out var protonation))
                    molarValues.Add(protonation.Value);
            }

            if (result?.Solution?.TemperatureDependence != null)
                heatCapacityValues.AddRange(result.Solution.TemperatureDependence.Values.Select(dependence => dependence.Slope.Value));

            var molar = EnergyUnitResolver.Resolve(family, energyUnitOverride, molarValues);
            var heatCapacity = EnergyUnitResolver.Resolve(family, energyUnitOverride, heatCapacityValues);
            return (molar, heatCapacity);
        }

        public static string ParameterColumnId(ParameterType parameter)
        {
            return "Parameter:" + ((int)parameter).ToString(CultureInfo.InvariantCulture);
        }
    }
}
