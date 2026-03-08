using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utilities;

namespace AnalysisITC
{
    public class AnalysisResult : ITCDataContainer
    {
        public GlobalSolution Solution { get; private set; }
        public GlobalModel Model => Solution.Model;
        GlobalModelParameters Options => Model.Parameters;

        public bool IsAdvancedAnalysisAvailable => Model.ModelType == AppClasses.Analysis2.Models.AnalysisModel.OneSetOfSites;
        public bool IsTemperatureDependenceEnabled => (GetMaximumTemperature() - GetMinimumTemperature()) > AppSettings.MinimumTemperatureSpanForFitting;
        public bool IsElectrostaticsAnalysisDependenceEnabled { get; private set; } = false;
        public bool IsProtonationAnalysisEnabled { get; private set; } = false;

        public FTSRMethod SpolarRecordAnalysis { get; private set; }
        public ProtonationAnalysis ProtonationAnalysis { get; private set; }
        public ElectrostaticsAnalysis ElectrostaticsAnalysis { get; private set; }

        public AnalysisResult(GlobalSolution solution)
        {
            Solution = solution;

            //FileName = solution.Model.Solution.SolutionName;
            Date = DateTime.Now;

            // Generate a descriptive name based on the experiments included in this result.
            // Falls back to the underlying solution name if no discriminating label can be generated.
            var suggested = AnalysisResultNameParser.GenerateSuggestedName(Solution);
            FileName = EnsureUniqueName(suggested ?? solution.Model.Solution.SolutionName);


            SetupAnalysisOptions();

            InitializeAnalyses();
        }

        void SetupAnalysisOptions()
        {
            //IsTemperatureDependenceEnabled = (GetMaximumTemperature() - GetMinimumTemperature()) > AppSettings.MinimumTemperatureSpanForFitting;

            var averageIonicStrength = Solution.Solutions.Average(sol => BufferAttribute.GetIonicStrength(sol.Data));
            // Check if data has an ionic strength more than half the minimum span from the average
            IsElectrostaticsAnalysisDependenceEnabled = Solution.Solutions
                .Select(sol => BufferAttribute.GetIonicStrength(sol.Data))
                .Any(ionicStrength => Math.Abs(ionicStrength - averageIonicStrength) > AppSettings.MinimumIonSpanForFitting / 2.0);

            //Check if all data has buffer info and figure out if any are different
            if (Solution.Solutions.All(sol => sol.Data.Attributes.Exists(att => att.Key == AttributeKey.Buffer)))
            {
                var firstSolutionBuffer = Solution.Solutions.First().Data.Attributes.Find(att => att.Key == AttributeKey.Buffer).IntValue;

                IsProtonationAnalysisEnabled = Solution.Solutions
                    .Skip(1)
                    .Any(sol => sol.Data.Attributes
                    .Find(att => att.Key == AttributeKey.Buffer).IntValue != firstSolutionBuffer);
            }
        }

        void InitializeAnalyses()
        {
            if (IsTemperatureDependenceEnabled) SpolarRecordAnalysis = new FTSRMethod(this);
            if (IsProtonationAnalysisEnabled) ProtonationAnalysis = new ProtonationAnalysis(this);
            if (IsElectrostaticsAnalysisDependenceEnabled) ElectrostaticsAnalysis = new ElectrostaticsAnalysis(this);
        }

        /// <summary>
        /// Result string for the list view cell
        /// </summary>
        /// <returns></returns>
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

        string EnsureUniqueName(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return name;

                var existing = DataManager.Results
                    .Where(r => r != null && r != this && !string.IsNullOrWhiteSpace(r.FileName))
                    .Select(r => r.FileName)
                    .ToHashSet();

                if (!existing.Contains(name)) return name;

                int i = 2;
                while (existing.Contains($"{name} ({i})")) i++;
                return $"{name} ({i})";
            }
            catch
            {
                return name;
            }
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

        public List<Tuple<string, string>> GetParameterEvaluationList()
        {
            var output = new List<Tuple<string, string>>();

            // Evaluation temperature (°C):
            // - If temperature span is large enough to expose temperature dependence -> use reference temp
            // - Otherwise -> use mean dataset temperature
            var evalTempC = Model.TemperatureDependenceExposed ? AppSettings.ReferenceTemperature : Solution.MeanTemperature;

            var energyUnit = AppSettings.EnergyUnit;

            static string Sub(int i) => i switch { 2 => "{2}", 3 => "{3}", 4 => "{4}", _ => "" };

            string ParameterName(ParameterType key) => $"{key.GetProperties().Name} ({key.GetProperties().SymbolName})";

            void AddInteraction(int idx)
            {
                var enthalpyKey = idx == 1 ? ParameterType.Enthalpy1 : ParameterType.Enthalpy2;
                var gibbsKey = idx == 1 ? ParameterType.Gibbs1 : ParameterType.Gibbs2;
                var affinityKey = idx == 1 ? ParameterType.Affinity1 : ParameterType.Affinity2; // NOTE: UI treats this as Kd
                var nKey = idx == 1 ? ParameterType.Nvalue1 : ParameterType.Nvalue2;

                // ΔH
                if (Solution.TemperatureDependence.ContainsKey(enthalpyKey))
                {
                    var dH = new Energy(Solution.TemperatureDependence[enthalpyKey].Evaluate(evalTempC, 100000));
                    output.Add(new(ParameterName(enthalpyKey), dH.ToFormattedString(energyUnit, permole: true)));

                    // ΔCp (only if temperature dependence was actually fitted)
                    if (Model.TemperatureDependenceExposed)
                    {
                        var slope = Solution.TemperatureDependence[enthalpyKey].Slope;
                        if (Math.Abs(slope.Value) > 0)
                        {
                            var dCp = new Energy(slope);
                            output.Add(new(MarkdownStrings.HeatCapacity + Sub(idx), dCp.ToFormattedString(energyUnit, true, true, true)));
                        }
                    }
                }

                // ΔG and Kd (Kd derived from ΔG)
                if (Solution.TemperatureDependence.ContainsKey(gibbsKey))
                {
                    var dG = new Energy(Solution.TemperatureDependence[gibbsKey].Evaluate(evalTempC, 100000));
                    output.Add(new(ParameterName(gibbsKey), dG.ToFormattedString(energyUnit, permole: true)));

                    // Convert °C -> K for van 't Hoff relation
                    var tK = evalTempC + 273.15;
                    var kdExponent = dG / (tK * Energy.R);
                    var kd = FWEMath.Exp(kdExponent.FloatWithError); // Kd = exp(ΔG / (R T))
                    output.Add(new(ParameterName(affinityKey), kd.AsFormattedConcentration(withunit: true)));
                }
                else
                {
                    // Fallback: average per-experiment Kd if ΔG isn't available for the model
                    try
                    {
                        if (Solution.Solutions.Count > 0 && Solution.Solutions[0].ReportParameters.ContainsKey(affinityKey))
                        {
                            var kdVals = Solution.Solutions.Select(sol => sol.ReportParameters[affinityKey].Value).ToList();
                            var kdAvg = new FloatWithError(kdVals, kdVals.Average());
                            output.Add(new(ParameterName(affinityKey), kdAvg.AsFormattedConcentration(withunit: true)));
                        }
                    }
                    catch { }
                }

                // N (low priority)
                try
                {
                    if (Solution.Solutions.Count > 0 && Solution.Solutions[0].ReportParameters.ContainsKey(nKey))
                    {
                        var nVals = Solution.Solutions.Select(sol => sol.ReportParameters[nKey].Value).ToList();
                        var nAvg = new FloatWithError(nVals, nVals.Average());
                        output.Add(new(ParameterName(nKey), nAvg.AsNumber()));
                    }
                }
                catch { }
            }

            AddInteraction(1);

            // Add second binding event if present
            if (Solution.TemperatureDependence.ContainsKey(ParameterType.Enthalpy2) ||
                Solution.TemperatureDependence.ContainsKey(ParameterType.Gibbs2) ||
                (Solution.Solutions.Count > 0 && Solution.Solutions[0].ReportParameters.ContainsKey(ParameterType.Nvalue2)))
            {
                AddInteraction(2);
            }

            return output;
        }
    }
}
