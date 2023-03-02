using System;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;

namespace AnalysisITC
{
    public class AnalysisResult : ITCDataContainer
    {
        public AnalysisITC.AppClasses.Analysis2.GlobalSolution Solution { get; private set; }
        AnalysisITC.AppClasses.Analysis2.GlobalModel Model => Solution.Model;
        GlobalModelParameters Options => Model.Parameters;

        public AnalysisResult(AnalysisITC.AppClasses.Analysis2.GlobalSolution solution)
        {
            Solution = solution;

            FileName = solution.Model.Solution.SolutionName;
            Date = DateTime.Now;
        }

        public string GetResultString()
        {
            string s = "Fit of " + Solution.Solutions.Count.ToString() + " experiments" + Environment.NewLine;
            if (Options.Constraints.All(con => con.Value == VariableConstraint.None)) s += "All variables unconstrained" + Environment.NewLine;
            else
            {
                foreach (var con in Options.Constraints)
                {
                    if (con.Value != VariableConstraint.None)
                    {
                        switch (con.Key)
                        {
                            case ParameterTypes.Nvalue1: s += "N-value: "; break;
                            case ParameterTypes.Enthalpy1: s += "Enthalpy: "; break;
                            case ParameterTypes.Affinity1:
                            case ParameterTypes.Gibbs1: s += "Affinity: "; break;
                        }

                        s += con.Value.GetEnumDescription() + Environment.NewLine;
                    }
                }
                //if (Options.Constraints[ParameterTypes.Enthalpy1] != VariableConstraint.None) s += "Enthalpy: " + Options.Constraints[ParameterTypes.Enthalpy1].ToString() + Environment.NewLine;
                //if (Options.Constraints[ParameterTypes.Gibbs1] != VariableConstraint.None) s += "Affinity: " + Options.Constraints[ParameterTypes.Gibbs1].ToString() + Environment.NewLine;
                //if (Options.Constraints[ParameterTypes.Nvalue1] != VariableConstraint.None) s += "N-value: " + Options.Constraints[ParameterTypes.Nvalue1].ToString() + Environment.NewLine;
            }

            s += Model.TemperatureDependenceExposed ? "∆H° = " : "∆H = ";
            s += new Energy(Solution.GetStandardParameterValue(ParameterTypes.Enthalpy1)).ToString(EnergyUnit.KiloJoule, permole: true) + Environment.NewLine;
            if (Model.TemperatureDependenceExposed) s += "∆Cₚ = " + new Energy(Solution.TemperatureDependence[ParameterTypes.Enthalpy1].Slope).ToString(EnergyUnit.Joule, "F0", permole: true, perK: true);

            return s.Trim();
        }

        internal double GetMinimumTemperature() => Solution.Solutions.Min(s => s.Temp);

        internal double GetMaximumTemperature() => Solution.Solutions.Max(s => s.Temp);

        internal double GetMaximumParameter()
        {
            //var maxentropy = Solution.Solutions.Max(s => s.TdS);
            //var maxenthalpy = Solution.Solutions.Max(s => s.Enthalpy);
            //var maxgibbs = Solution.Solutions.Max(s => s.GibbsFreeEnergy);
            //return (new Energy[] { maxentropy, maxenthalpy, maxgibbs }).Max();

            double max = double.MinValue;

            foreach (var sol in Solution.Solutions)
            {
                var list = sol.DependenciesToReport;
                foreach (var par in list)
                {
                    var val = par.Item2(sol);

                    if (val > max) max = val;
                }
            }

            return max;
        }

        internal double GetMinimumParameter()
        {
            //var minentropy = Solution.Solutions.Min(s => s.TdS);
            //var minenthalpy = Solution.Solutions.Min(s => s.Enthalpy);
            //var mingibbs = Solution.Solutions.Min(s => s.GibbsFreeEnergy);
            //return (new Energy[] { minentropy, minenthalpy, mingibbs }).Min();

            double min = double.MaxValue;

            foreach (var sol in Solution.Solutions)
            {
                var list = sol.DependenciesToReport;
                foreach (var par in list)
                {
                    var val = par.Item2(sol);

                    if (val < min) min = val;
                }
            }

            return min;
        }
    }
}
