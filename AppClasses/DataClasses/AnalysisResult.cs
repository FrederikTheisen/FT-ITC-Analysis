using System;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utilities;

namespace AnalysisITC
{
    public class AnalysisResult : ITCDataContainer
    {
        public AnalysisITC.AppClasses.Analysis2.GlobalSolution Solution { get; private set; }
        public AnalysisITC.AppClasses.Analysis2.GlobalModel Model => Solution.Model;
        GlobalModelParameters Options => Model.Parameters;

        public bool IsTemperatureDependenceEnabled => (GetMaximumTemperature() - GetMinimumTemperature()) > AppSettings.MinimumTemperatureSpanForFitting;
        public bool IsElectrostaticsAnalysisDependenceEnabled { get; private set; } = false;
        public bool IsProtonationAnalysisEnabled { get; private set; } = false;

        public FTSRMethod SpolarRecordAnalysis { get; private set; }
        public ProtonationAnalysis ProtonationAnalysis { get; private set; }
        public ElectrostaticsAnalysis ElectrostaticsAnalysis { get; private set; }

        public AnalysisResult(AnalysisITC.AppClasses.Analysis2.GlobalSolution solution)
        {
            Solution = solution;

            FileName = solution.Model.Solution.SolutionName;
            Date = DateTime.Now;

            SetupAnalysisOptions();

            InitializeAnalyses();
        }

        void SetupAnalysisOptions()
        {
            //IsTemperatureDependenceEnabled = (GetMaximumTemperature() - GetMinimumTemperature()) > AppSettings.MinimumTemperatureSpanForFitting;

            var firstSolutionIonicStrength = BufferAttribute.GetIonicStrength(Solution.Solutions.First().Data);
            // Check if any other solution has a different ionic strength from the first one
            IsElectrostaticsAnalysisDependenceEnabled = Solution.Solutions
                .Skip(1)
                .Select(sol => BufferAttribute.GetIonicStrength(sol.Data))
                .Any(ionicStrength => ionicStrength != firstSolutionIonicStrength);

            //Check if all data has buffer info and figure out if any are different
            if (Solution.Solutions.All(sol => sol.Data.Attributes.Exists(att => att.Key == ModelOptionKey.Buffer)))
            {
                var firstSolutionBuffer = Solution.Solutions.First().Data.Attributes.Find(att => att.Key == ModelOptionKey.Buffer).IntValue;

                IsProtonationAnalysisEnabled = Solution.Solutions
                    .Skip(1)
                    .Any(sol => sol.Data.Attributes
                    .Find(att => att.Key == ModelOptionKey.Buffer).IntValue != firstSolutionBuffer);
            }
        }

        void InitializeAnalyses()
        {
            if (IsTemperatureDependenceEnabled) SpolarRecordAnalysis = new FTSRMethod(this);
            if (IsProtonationAnalysisEnabled) ProtonationAnalysis = new ProtonationAnalysis(this);
            if (IsElectrostaticsAnalysisDependenceEnabled) ElectrostaticsAnalysis = new ElectrostaticsAnalysis(this);
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
                            case ParameterType.Nvalue1: s += "N-value: "; break;
                            case ParameterType.Enthalpy1: s += "Enthalpy: "; break;
                            case ParameterType.Affinity1:
                            case ParameterType.Gibbs1: s += "Affinity: "; break;
                            default: s += con.Key.GetProperties().Description + ": "; break;
                        }

                        s += con.Value.GetEnumDescription() + Environment.NewLine;
                    }
                }
            }

            s += Model.TemperatureDependenceExposed ? "∆H° = " : "∆H = ";
            s += new Energy(Solution.GetStandardParameterValue(ParameterType.Enthalpy1)).ToFormattedString(EnergyUnit.KiloJoule, permole: true) + Environment.NewLine;
            if (Model.TemperatureDependenceExposed) s += "∆Cₚ = " + new Energy(Solution.TemperatureDependence[ParameterType.Enthalpy1].Slope).ToString(EnergyUnit.Joule, "F0", permole: true, perK: true);

            return s.Trim();
        }

        internal double GetMinimumTemperature() => Solution.Solutions.Min(s => s.Temp);

        internal double GetMaximumTemperature() => Solution.Solutions.Max(s => s.Temp);

        public double[] GetMinMaxIonicStrength()
        {
            var list = Solution.Solutions.Select(sol => BufferAttribute.GetIonicStrength(sol.Data));
            return new double[2] { list.Min(), list.Max() };
        }

        internal double GetMaximumParameter()
        {
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
