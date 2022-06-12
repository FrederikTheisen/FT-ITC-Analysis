using System;
using System.Linq;

namespace AnalysisITC
{
    public class AnalysisResult : ITCDataContainer
    {
        public GlobalSolution Solution { get; set; }

        public AnalysisResult(GlobalSolution solution)
        {
            Solution = solution;

            FileName = solution.Model.Models[0].ModelName;
            Date = DateTime.Now;
        }

        public string GetResultString()
        {
            string s = "Fit of " + Solution.Solutions.Count.ToString() + " experiments";

            s += Environment.NewLine;
            s += "Enthalpy:" + Solution.Model.Options.EnthalpyStyle.ToString();
            s += Environment.NewLine;
            s += "Affinity:" + Solution.Model.Options.AffinityStyle.ToString();
            s += Environment.NewLine;
            s += "N-value:" + Solution.Model.Options.NStyle.ToString();
            s += Environment.NewLine;
            s += "∆H @ 25 °C = " + Solution.StandardEnthalpy.ToString(EnergyUnit.KiloJoule, permole: true);
            s += Environment.NewLine;
            s += "∆Cp = " + Solution.HeatCapacity.ToString(EnergyUnit.Joule, "F0", permole: true, perK: true);

            return s;
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
