using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Avalonia.Units;

/// <summary>Desktop-facing helpers for choosing one unit per visible value group.</summary>
internal static class EnergyDisplay
{
    public static EnergyUnit CurrentDefault => EnergyUnitResolver.DefaultUnit(AppSettings.EnergyUnitFamily);

    public static EnergyUnit Resolve(EnergyUnitFamily family, double centralValue)
        => Resolve(family, new[] { centralValue });

    public static EnergyUnit Resolve(EnergyUnitFamily family, IEnumerable<double>? centralValues)
        => EnergyUnitResolver.Resolve(family, centralValues ?? Array.Empty<double>());

    public static EnergyUnit ResultMolarUnit(AnalysisResult? result)
    {
        if (result?.Solution?.Solutions == null)
            return CurrentDefault;

        var values = result.Solution.Solutions
            .SelectMany(solution => solution.ReportParameters ?? new Dictionary<ParameterType, AnalysisITC.Core.Numerics.FloatWithError>())
            .Where(pair => IsMolarEnergy(pair.Key))
            .Select(pair => pair.Value.Value)
            .ToList();

        // Protonation enthalpy is part of the same visible molar-energy group in
        // result tables. Include its central value when the source buffer exposes
        // it, while leaving uncertainty/error magnitudes out of unit selection.
        values.AddRange(result.Solution.Solutions
            .Where(solution => BufferAttribute.TryGetProtonationEnthalpy(solution.Data, out _))
            .Select(solution => BufferAttribute.GetProtonationEnthalpy(solution.Data).Value));

        return Resolve(AppSettings.EnergyUnitFamily, values);
    }

    public static EnergyUnit ResultHeatCapacityUnit(AnalysisResult? result)
    {
        var values = result?.Solution?.Solutions?
            .SelectMany(solution => solution.ReportParameters ?? new Dictionary<ParameterType, AnalysisITC.Core.Numerics.FloatWithError>())
            .Where(pair => IsHeatCapacity(pair.Key))
            .Select(pair => pair.Value.Value)
            .ToList();
        return Resolve(AppSettings.EnergyUnitFamily, values);
    }

    public static EnergyUnit ParameterUnit(AnalysisResult? result, ParameterType parameter)
        => IsHeatCapacity(parameter) ? ResultHeatCapacityUnit(result) : ResultMolarUnit(result);

    public static string DifferentialPowerLabel
        => ThermogramUnits.DifferentialPowerUnit(AppSettings.EnergyUnitFamily);

    public static string IntegratedHeatLabel
        => ThermogramUnits.IntegratedHeatUnit(AppSettings.EnergyUnitFamily);

    public static bool IsHeatCapacity(ParameterType parameter)
    {
        if (parameter == ParameterType.HeatCapacity1 || parameter == ParameterType.HeatCapacity2)
            return true;

        return parameter.GetProperties().ParentType == ParameterType.HeatCapacity1;
    }

    public static bool IsMolarEnergy(ParameterType parameter)
    {
        if (!ParameterTypeAttribute.IsEnergyUnitParameter(parameter))
            return false;

        return !IsHeatCapacity(parameter);
    }
}
