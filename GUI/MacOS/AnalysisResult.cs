using System;
using System.Linq;

namespace AnalysisITC
{
    public class AnalysisResult : ITCDataContainer
    {
        public GlobalSolution Solution { get; set; }
        GlobalModel Model => Solution.Model;
        SolverOptions Options => Model.Options;

        public AnalysisResult(GlobalSolution solution)
        {
            Solution = solution;

            FileName = solution.Model.Models[0].ModelName;
            Date = DateTime.Now;
        }

        public string GetResultString()
        {
            string s = "Fit of " + Solution.Solutions.Count.ToString() + " experiments" + Environment.NewLine;
            if (Options.EnthalpyStyle == Analysis.VariableConstraint.None &&
                Options.AffinityStyle == Analysis.VariableConstraint.None &&
                Options.NStyle == Analysis.VariableConstraint.None) s += "All variables unconstrained" + Environment.NewLine;
            else
            {
                if (Options.EnthalpyStyle != Analysis.VariableConstraint.None) s += "Enthalpy: " + Options.EnthalpyStyle.ToString() + Environment.NewLine;
                if (Options.AffinityStyle != Analysis.VariableConstraint.None) s += "Affinity: " + Options.AffinityStyle.ToString() + Environment.NewLine;
                if (Options.NStyle != Analysis.VariableConstraint.None) s += "N-value: " + Options.NStyle.ToString() + Environment.NewLine;
            }

            s += "∆H° = " + Solution.StandardEnthalpy.ToString(EnergyUnit.KiloJoule, permole: true) + Environment.NewLine;
            s += "∆Cₚ = " + Solution.HeatCapacity.ToString(EnergyUnit.Joule, "F0", permole: true, perK: true);

            return s.Trim();
        }

        internal double GetMinimumTemperature() => Solution.Solutions.Min(s => s.T);

        internal double GetMaximumTemperature() => Solution.Solutions.Max(s => s.T);

        internal double GetMaximumParameter()
        {
            var maxentropy = Solution.Solutions.Max(s => s.TdS);
            var maxenthalpy = Solution.Solutions.Max(s => s.Enthalpy);
            var maxgibbs = Solution.Solutions.Max(s => s.GibbsFreeEnergy);

            return (new Energy[] { maxentropy, maxenthalpy, maxgibbs }).Max();
        }

        internal double GetMinimumParameter()
        {
            var minentropy = Solution.Solutions.Min(s => s.TdS);
            var minenthalpy = Solution.Solutions.Min(s => s.Enthalpy);
            var mingibbs = Solution.Solutions.Min(s => s.GibbsFreeEnergy);

            return (new Energy[] { minentropy, minenthalpy, mingibbs }).Min();
        }
    }
}
