using System;
using System.Globalization;

using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>
    /// Shared tooltip text for fit diagnostics shown by the desktop result
    /// inspectors.
    /// </summary>
    internal static class FitMetricTooltipPresentation
    {
        const string MolarRmsdText =
            "Calculated from residuals divided by injection mass; display-only diagnostic, not used for optimisation.";

        internal static string Rmsd(
            SolverConvergence convergence,
            IFormatProvider provider = null)
        {
            provider ??= CultureInfo.CurrentCulture;
            var objective = convergence?.Objective;
            if (!objective.HasValue || double.IsNaN(objective.Value) || double.IsInfinity(objective.Value))
                return "Unweighted root mean square deviation (RMSD) in µJ. Optimisation objective unavailable.";

            return "Unweighted root mean square deviation (RMSD) in µJ. Optimisation objective = "
                + objective.Value.ToString("G6", provider)
                + ".";
        }

        internal static string MolarRmsd => MolarRmsdText;
    }
}
