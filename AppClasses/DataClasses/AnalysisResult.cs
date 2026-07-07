using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Data
{
    public class AnalysisResult : ITCDataContainer
    {
        public GlobalSolution Solution { get; private set; }
        public GlobalModel Model => Solution.Model;
        GlobalModelParameters Options => Model.Parameters;

        public bool IsAdvancedAnalysisAvailable => Model.ModelType == AnalysisITC.Core.Analysis.Models.AnalysisModel.OneSetOfSites;
        public bool IsTemperatureDependenceEnabled { get; private set; } = false;
        public bool IsElectrostaticsAnalysisDependenceEnabled { get; private set; } = false;
        public bool IsProtonationAnalysisEnabled { get; private set; } = false;

        public FTSRMethod SpolarRecordAnalysis { get; private set; }
        public ProtonationAnalysis ProtonationAnalysis { get; private set; }
        public ElectrostaticsAnalysis ElectrostaticsAnalysis { get; private set; }

        public ConcentrationUnit AppropriateAffinityUnit => ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(Solution.Solutions.Average(s => s.ReportParameters[ParameterType.Affinity1]));

        public AnalysisResult(GlobalSolution solution)
            : this(solution, captureValiditySnapshot: true)
        {
        }

        public AnalysisResultValiditySnapshot ValiditySnapshot { get; private set; }
        public AnalysisResultValidityReport ValidityReport => ValiditySnapshot?.Compare(Solution)
            ?? AnalysisResultValidityReport.Unknown("No validity snapshot is stored for this analysis result.");
        public bool IsValidForCurrentData => ValidityReport.Status == AnalysisResultValidity.Valid;

        public AnalysisResult(GlobalSolution solution, bool captureValiditySnapshot)
        {
            Solution = solution;
            if (captureValiditySnapshot) ValiditySnapshot = AnalysisResultValiditySnapshot.Capture(solution);

            //FileName = solution.Model.Solution.SolutionName;
            Date = DateTime.Now;

            SetFileName(Solution.Solutions[0].SolutionName); // Should save (Global.)Model

            // Generate a descriptive name based on the experiments included in this result.
            // Falls back to the underlying solution name if no discriminating label can be generated.
            var suggested = AnalysisResultNameParser.GenerateSuggestedName(Solution);
            Name = EnsureUniqueName(suggested ?? solution.Model.Solution.SolutionName);


            SetupAnalysisOptions();

            InitializeAnalyses();
        }

        public void SetValiditySnapshot(AnalysisResultValiditySnapshot snapshot)
        {
            ValiditySnapshot = snapshot;
        }

        public void UpdateSolution(GlobalSolution solution)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));

            Solution = solution;
            Date = DateTime.Now;
            ValiditySnapshot = AnalysisResultValiditySnapshot.Capture(solution);

            IsTemperatureDependenceEnabled = false;
            IsElectrostaticsAnalysisDependenceEnabled = false;
            IsProtonationAnalysisEnabled = false;
            SpolarRecordAnalysis = null;
            ProtonationAnalysis = null;
            ElectrostaticsAnalysis = null;

            SetupAnalysisOptions();
            InitializeAnalyses();
            MarkModified();
        }

        void SetupAnalysisOptions()
        {
            // Check temperature variation is great enough
            IsTemperatureDependenceEnabled = (GetMaximumTemperature() - GetMinimumTemperature()) > AppSettings.MinimumTemperatureSpanForFitting;

            // Check if data has an ionic strength more than half the minimum span from the average
            var averageIonicStrength = Solution.Solutions.Average(sol => BufferAttribute.GetIonicStrength(sol.Data));
            bool variable_is = Solution.Solutions
                .Select(sol => BufferAttribute.GetIonicStrength(sol.Data))
                .Any(ionicStrength => Math.Abs(ionicStrength - averageIonicStrength) > AppSettings.MinimumIonSpanForFitting / 2.0);

            // Check if all have salt attribute
            bool allsalt = Solution.Solutions.All(sol => sol.Data.Attributes.Exists(att => att.Key == AttributeKey.Salt));

            IsElectrostaticsAnalysisDependenceEnabled = variable_is && allsalt;

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
            var experimentCount = Solution.Solutions.Count;
            string s = "Fit of " + experimentCount.ToString() + " experiment" + (experimentCount == 1 ? "" : "s") + Environment.NewLine;
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

            var energyUnit = AppSettings.EnergyUnit;
            s += Model.TemperatureDependenceExposed ? "∆H° = " : "∆H = ";
            s += new Energy(Solution.GetStandardParameterValue(ParameterType.Enthalpy1)).ToFormattedString(energyUnit, permole: true) + Environment.NewLine;
            if (Model.TemperatureDependenceExposed) s += "∆Cₚ = " + new Energy(Solution.TemperatureDependence[ParameterType.Enthalpy1].Slope).ToFormattedString(energyUnit, permole: true, perK: true);

            return s.Trim();
        }

        public string GetListDescriptionString()
        {
            var experimentCount = Solution.Solutions.Count;
            var experimentLabel = experimentCount == 1 ? "experiment" : "experiments";
            var modelName = Solution.SolutionName;
            var rmsd = Solution.Loss.ToString("G3");

            var line1 = $"Fit of {experimentCount} {experimentLabel}"; 
            var line2 = $"{modelName}; RMSD {rmsd}";
            var line3 = GetConstraintSummary();

            return string.Join(Environment.NewLine, line1, line2, line3);
        }

        string GetConstraintSummary()
        {
            string constraints = "";

            if (Options.Constraints.All(con => con.Value == VariableConstraint.None)) constraints += "All variables unconstrained";
            else
            {
                bool containstwo = Model.ModelType == AnalysisITC.Core.Analysis.Models.AnalysisModel.TwoSetsOfSites; // Not very flexible

                foreach (var con in Options.Constraints)
                {
                    if (con.Value != VariableConstraint.None)
                    {
                        switch (con.Key)
                        {
                            case ParameterType.Nvalue1: constraints += $"N-value{(containstwo ? "1" : "")}: "; break;
                            case ParameterType.Nvalue2: constraints += "N-value2: "; break;
                            case ParameterType.Enthalpy1: constraints += $"Enthalpy{(containstwo ? "1" : "")}: "; break;
                            case ParameterType.Enthalpy2: constraints += "Enthalpy2: "; break;
                            case ParameterType.Affinity1:
                            case ParameterType.Gibbs1: constraints += $"Affinity{(containstwo ? "1" : "")}: "; break;
                            case ParameterType.Affinity2:
                            case ParameterType.Gibbs2: constraints += "Affinity2: "; break;
                            default: constraints += con.Key.GetProperties().Description + ": "; break;
                        }

                        constraints += con.Value.GetEnumDescription() + Environment.NewLine;
                    }
                }
            }

            return constraints.Trim();
        }

        List<string> GetListFitOptionSummary()
        {
            var items = new List<string>
            {
                Solution.UseWeightedFitting ? "weighted inj errors" : "unweighted",
                GetListErrorEstimationSummary(),
                $"{Model.NumberOfParameters} fitted pars",
            };

            return items.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        }

        string GetListErrorEstimationSummary()
        {
            if (Solution.ErrorEstimationMethod == ErrorEstimationMethod.None) return "no error estimation";

            var method = Solution.ErrorEstimationMethod switch
            {
                ErrorEstimationMethod.BootstrapResiduals => "bootstrap",
                ErrorEstimationMethod.LeaveOneOut => "leave-one-out",
                _ => Solution.ErrorEstimationMethod.Description(),
            };

            return $"{method} x {Solution.BootstrapIterations}";
        }

        static string GetListConstraintName(VariableConstraint constraint)
        {
            return constraint switch
            {
                VariableConstraint.SameForAll => "shared",
                VariableConstraint.TemperatureDependent => "temp-dependent",
                _ => constraint.GetEnumDescription(),
            };
        }

        static string GetListParameterName(ParameterType key)
        {
            return key switch
            {
                ParameterType.Nvalue1 => "N",
                ParameterType.Nvalue2 => "N{2}",
                ParameterType.Enthalpy1 => MarkdownStrings.Enthalpy,
                ParameterType.Enthalpy2 => MarkdownStrings.Enthalpy + "{2}",
                ParameterType.Affinity1 => MarkdownStrings.DissociationConstant,
                ParameterType.Affinity2 => MarkdownStrings.DissociationConstant + "{,2}",
                ParameterType.Gibbs1 => "dG",
                ParameterType.Gibbs2 => "dG2",
                ParameterType.EntropyContribution1 => "-TdS",
                ParameterType.EntropyContribution2 => "-TdS2",
                ParameterType.HeatCapacity1 => "dCp",
                ParameterType.HeatCapacity2 => "dCp2",
                _ => key.GetProperties().Name,
            };
        }

        string EnsureUniqueName(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return name;

                var existing = DataManager.Results
                    .Where(r => r != null && r != this && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(r => r.Name);
                var existingNames = new HashSet<string>(existing);

                if (!existingNames.Contains(name)) return name;

                int i = 2;
                while (existingNames.Contains($"{name} ({i})")) i++;
                return $"{name} ({i})";
            }
            catch
            {
                return name;
            }
        }

        public double GetMinimumTemperature() => Solution.Solutions.Min(s => s.Temp);

        public double GetMaximumTemperature() => Solution.Solutions.Max(s => s.Temp);

        public double[] GetMinMaxIonicStrength()
        {
            var list = Solution.Solutions.Select(sol => BufferAttribute.GetIonicStrength(sol.Data));
            return new double[2] { list.Min(), list.Max() };
        }

        public double GetMaximumParameter()
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

        public double GetMinimumParameter()
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
            return AnalysisResultParameterEvaluator.EvaluateDefaultList(this);
        }
    }
}
