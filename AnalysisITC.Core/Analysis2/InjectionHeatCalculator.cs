using System;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Processing;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// Pure injection bookkeeping. Dumas uses one Simpson panel for ideal continuous
    /// displacement (doi:10.1007/s00249-021-01588-4). Discrete displacement uses the
    /// replacement convention of Freire et al. (2009; doi:10.1016/S0076-6879(08)04205-5).
    /// Neither is a kinetic or imperfect-mixing model.
    /// Heat content is in J, volumes in L, and incoming heat density in J/L.
    /// </summary>
    internal static class InjectionHeatCalculator
    {
        internal static double DiscreteDisplacement(
            double cellVolume, double injectionVolume,
            InjectionConcentrationState before, InjectionConcentrationState after,
            Func<double, double, double> heatContent, double incomingHeatDensity = 0.0)
        {
            var retention = InjectionDisplacementCalculator.DiscreteDisplacementRetention(cellVolume, injectionVolume);
            if (injectionVolume == 0.0) return 0.0;
            var startHeat = heatContent(before.CellConcentration, before.TitrantConcentration);
            var endHeat = heatContent(after.CellConcentration, after.TitrantConcentration);
            return endHeat - retention * startHeat - injectionVolume * incomingHeatDensity;
        }

        internal static double Dumas(
            double cellVolume, double injectionVolume, double syringeConcentration,
            InjectionConcentrationState before, InjectionConcentrationState after,
            Func<double, double, double> heatContent, double incomingHeatDensity = 0.0)
        {
            var midpoint = InjectionDisplacementCalculator.AdvanceState(
                DilutionMethod.Exponential, cellVolume, syringeConcentration,
                before, 0.0, injectionVolume / 2.0);
            var startHeat = heatContent(before.CellConcentration, before.TitrantConcentration);
            var middleHeat = heatContent(midpoint.CellConcentration, midpoint.TitrantConcentration);
            var endHeat = heatContent(after.CellConcentration, after.TitrantConcentration);
            return endHeat - startHeat
                + (injectionVolume / cellVolume) / 6.0 * (startHeat + 4.0 * middleHeat + endHeat)
                - injectionVolume * incomingHeatDensity;
        }
    }
}
