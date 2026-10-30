using System;
using System.Globalization;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Presentation
{
    public static class RmsdPresentation
    {
        public const string LabelTooltip = "Unweighted deviation between observed and fitted integrated injection heats.";

        /// <summary>Formats the saved heat RMSD, whose internal unit is microjoules.</summary>
        public static string FormatHeat(double? rmsdMicrojoules, EnergyUnitFamily family, IFormatProvider provider = null)
        {
            if (!rmsdMicrojoules.HasValue || double.IsNaN(rmsdMicrojoules.Value) || double.IsInfinity(rmsdMicrojoules.Value))
                return "Unavailable";

            var value = rmsdMicrojoules.Value * (ThermogramUnits.IntegratedHeatScale(family) / 1_000_000d);
            return value.ToString("G4", provider ?? CultureInfo.CurrentCulture) + " " + ThermogramUnits.IntegratedHeatUnit(family);
        }
    }
}
