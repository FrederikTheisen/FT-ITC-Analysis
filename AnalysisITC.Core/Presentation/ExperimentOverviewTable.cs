using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    public enum ExperimentOverviewColumnAlignment
    {
        Left,
        Center,
        Right
    }

    public sealed class ExperimentOverviewColumn
    {
        public ExperimentOverviewColumn(string id, string title, ExperimentOverviewColumnAlignment alignment, double preferredWidth, bool isVisible = true)
        {
            Id = id;
            Title = title;
            Alignment = alignment;
            PreferredWidth = preferredWidth;
            IsVisible = isVisible;
        }

        public string Id { get; }
        public string Title { get; }
        public ExperimentOverviewColumnAlignment Alignment { get; }
        public double PreferredWidth { get; }
        public bool IsVisible { get; }
    }

    public sealed class ExperimentOverviewRow
    {
        readonly Dictionary<string, string> values;

        public ExperimentOverviewRow(InjectionData injection, Dictionary<string, string> values)
        {
            Injection = injection;
            this.values = values;
        }

        public InjectionData Injection { get; }
        public bool IsIncluded => Injection.Include;

        public string this[string columnId] => values.TryGetValue(columnId, out var value) ? value : "";
    }

    public sealed class ExperimentOverviewTable
    {
        ExperimentOverviewTable(List<ExperimentOverviewColumn> columns, List<ExperimentOverviewRow> rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public IReadOnlyList<ExperimentOverviewColumn> Columns { get; }
        public IReadOnlyList<ExperimentOverviewRow> Rows { get; }

        public static ExperimentOverviewTable Build(ExperimentData experiment)
        {
            var hasFit = experiment?.Solution != null && experiment.Model != null;
            var energyUnit = AppSettings.EnergyUnit;

            var columns = new List<ExperimentOverviewColumn>
            {
                new ExperimentOverviewColumn("ID", "#", ExperimentOverviewColumnAlignment.Center, 42),
                new ExperimentOverviewColumn("Included", "Use", ExperimentOverviewColumnAlignment.Center, 46),
                new ExperimentOverviewColumn("Volume", "Vol. (µL)", ExperimentOverviewColumnAlignment.Right, 76),
                new ExperimentOverviewColumn("Cell", "[M] (µM)", ExperimentOverviewColumnAlignment.Right, 88),
                new ExperimentOverviewColumn("Titrant", "[L] (µM)", ExperimentOverviewColumnAlignment.Right, 88),
                new ExperimentOverviewColumn("XValue", XColumnTitle(experiment), ExperimentOverviewColumnAlignment.Right, 82),
                new ExperimentOverviewColumn("Heat", $"Heat ({energyUnit.GetUnit()}/mol)", ExperimentOverviewColumnAlignment.Right, 112),
                new ExperimentOverviewColumn("HeatError", $"Heat SD ({energyUnit.GetUnit()}/mol)", ExperimentOverviewColumnAlignment.Right, 116),
                new ExperimentOverviewColumn("FittedHeat", $"Fit ({energyUnit.GetUnit()}/mol)", ExperimentOverviewColumnAlignment.Right, 106, hasFit),
                new ExperimentOverviewColumn("Residual", $"Residual ({energyUnit.GetUnit()}/mol)", ExperimentOverviewColumnAlignment.Right, 118, hasFit),
            };

            var rows = new List<ExperimentOverviewRow>();
            if (experiment?.Injections == null) return new ExperimentOverviewTable(columns, rows);

            foreach (var injection in experiment.Injections)
                rows.Add(new ExperimentOverviewRow(injection, BuildRowValues(experiment, injection, hasFit)));

            return new ExperimentOverviewTable(columns, rows);
        }

        static Dictionary<string, string> BuildRowValues(ExperimentData experiment, InjectionData injection, bool hasFit)
        {
            var values = new Dictionary<string, string>
            {
                ["ID"] = (injection.ID + 1).ToString(CultureInfo.CurrentCulture),
                ["Included"] = injection.Include ? "Yes" : "No",
                ["Volume"] = (1_000_000 * injection.Volume).ToString("G5", CultureInfo.CurrentCulture),
                ["Cell"] = FormatConcentration(injection.ActualCellConcentration),
                ["Titrant"] = FormatConcentration(injection.ActualTitrantConcentration),
                ["XValue"] = FormatXValue(experiment, injection),
                ["Heat"] = "",
                ["HeatError"] = "",
                ["FittedHeat"] = "",
                ["Residual"] = "",
            };

            if (!injection.IsIntegrated) return values;

            values["Heat"] = FormatEnergyPerMole(injection.Enthalpy);
            values["HeatError"] = FormatEnergyPerMole(injection.SD);

            if (hasFit)
            {
                values["FittedHeat"] = FormatEnergyPerMole(experiment.Model.EvaluateEnthalpy(injection.ID, true));
                values["Residual"] = FormatEnergyPerMole(injection.ResidualEnthalpy);
            }

            return values;
        }

        static string XColumnTitle(ExperimentData experiment)
        {
            return experiment?.AxisType switch
            {
                AnalysisXAxisType.TitrantConcentration => "[L] axis (µM)",
                AnalysisXAxisType.ID => "Injection",
                _ => "Ratio",
            };
        }

        static string FormatXValue(ExperimentData experiment, InjectionData injection)
        {
            return experiment?.AxisType switch
            {
                AnalysisXAxisType.TitrantConcentration => FormatConcentration(injection.ActualTitrantConcentration),
                AnalysisXAxisType.ID => (injection.ID + 1).ToString(CultureInfo.CurrentCulture),
                _ => injection.Ratio.ToString("G5", CultureInfo.CurrentCulture),
            };
        }

        static string FormatConcentration(double value)
        {
            return new FloatWithError(value).AsConcentration(ConcentrationUnit.µM, withunit: false);
        }

        static string FormatEnergyPerMole(double value)
        {
            if (!IsFinite(value)) return "";

            return new Energy(value).ToString(AppSettings.EnergyUnit, "G5", withunit: false, permole: true);
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
