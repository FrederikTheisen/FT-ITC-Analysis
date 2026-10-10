using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC
{
    /// <summary>
    /// macOS-only glue for controls and macOS renderer classes that take
    /// an exact EnergyUnit. Core presentation owns the canonical resolver; this
    /// small adapter keeps the Xamarin.Mac graph layer independent of the
    /// publication renderer's option object while the migration is in flight.
    /// </summary>
    internal static class MacEnergyUnitPresentation
    {
        public static EnergyUnit Resolve(EnergyUnitFamily family, IEnumerable<double> centralJoules)
            => EnergyUnitResolver.Resolve(family, centralJoules);

        public static string PowerUnitLabel(EnergyUnitFamily family)
            => ThermogramUnits.DifferentialPowerUnit(family);

        public static string IntegratedHeatUnitLabel(EnergyUnitFamily family)
            => ThermogramUnits.IntegratedHeatUnit(family);

        /// <param name="solution">The drawn fit, which may differ from the attached one.</param>
        public static IReadOnlyList<double> MolarEnergyValues(
            AnalysisITC.Core.Data.ExperimentData data,
            SolutionInterface solution,
            bool drawFitOffsetCorrected,
            bool showResiduals,
            FinalFigureDisplayParameters displayedParameters)
        {
            var values = new List<double>();
            if (data?.Injections != null)
            {
                values.AddRange(data.Injections.Select(injection =>
                    drawFitOffsetCorrected && solution != null
                        ? injection.Enthalpy - solution.Offset
                        : injection.Enthalpy));
            }

            if (solution?.Model != null && data?.Injections != null)
            {
                foreach (var injection in data.Injections)
                {
                    values.Add(solution.Model.EvaluateEnthalpy(
                        injection.ID,
                        withoffset: !drawFitOffsetCorrected));
                    if (showResiduals && injection.InjectionMass != 0)
                    {
                        values.Add(solution.Model.Residual(injection) / injection.InjectionMass);
                    }
                }
            }

            var report = solution?.ReportParameters;
            if (report != null)
                values.AddRange(report
                    .Where(item => IsDisplayedMolarEnergyParameter(item.Key, displayedParameters))
                    .Select(item => item.Value.Value));

            return values;
        }

        static bool IsDisplayedMolarEnergyParameter(
            ParameterType parameter,
            FinalFigureDisplayParameters display)
        {
            return parameter.GetProperties().ParentType switch
            {
                ParameterType.Enthalpy1 => display.HasFlag(FinalFigureDisplayParameters.Enthalpy),
                ParameterType.EntropyContribution1 => display.HasFlag(FinalFigureDisplayParameters.Entropy),
                ParameterType.Gibbs1 => display.HasFlag(FinalFigureDisplayParameters.Gibbs),
                ParameterType.Entropy1 => display.HasFlag(FinalFigureDisplayParameters.Entropy),
                ParameterType.Offset => display.HasFlag(FinalFigureDisplayParameters.Offset),
                _ => false,
            };
        }
    }
}
