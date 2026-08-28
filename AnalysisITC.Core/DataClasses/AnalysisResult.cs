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
        public bool IsSpolarRecordAnalysisEnabled => IsAdvancedAnalysisAvailable && IsTemperatureDependenceEnabled;
        public bool IsElectrostaticsAnalysisDependenceEnabled { get; private set; } = false;
        public bool IsProtonationAnalysisEnabled { get; private set; } = false;
        public string AdvancedAnalysisUnavailableReason => IsAdvancedAnalysisAvailable
            ? string.Empty
            : "Structuring, protonation, and electrostatics analyses are available only for the one-set-of-sites model.";
        public string SpolarRecordAnalysisUnavailableReason => !IsAdvancedAnalysisAvailable
            ? AdvancedAnalysisUnavailableReason
            : IsTemperatureDependenceEnabled ? string.Empty : "Structuring analysis requires a sufficient temperature span.";
        public string ElectrostaticsAnalysisUnavailableReason => !IsAdvancedAnalysisAvailable
            ? AdvancedAnalysisUnavailableReason
            : IsElectrostaticsAnalysisDependenceEnabled ? string.Empty : "Electrostatics analysis requires varying ionic strength and salt metadata for every experiment.";
        public string ProtonationAnalysisUnavailableReason => !IsAdvancedAnalysisAvailable
            ? AdvancedAnalysisUnavailableReason
            : IsProtonationAnalysisEnabled ? string.Empty : "Protonation analysis requires multiple buffers with protonation metadata.";

        public FTSRMethod SpolarRecordAnalysis { get; private set; }
        public ProtonationAnalysis ProtonationAnalysis { get; private set; }
        public ElectrostaticsAnalysis ElectrostaticsAnalysis { get; private set; }

        public ConcentrationUnit AppropriateAffinityUnit => GetAppropriateAffinityUnit(ParameterType.Affinity1);

        /// <summary>
        /// Selects a readable concentration unit independently for one reported
        /// affinity column. Sequential affinities can differ by several orders of
        /// magnitude, so using the first step's unit for every step is misleading.
        /// </summary>
        public ConcentrationUnit GetAppropriateAffinityUnit(ParameterType key)
        {
            if (key != ParameterType.ApparentAffinity
                && key.GetProperties().ParentType != ParameterType.Affinity1)
                return AppSettings.DefaultConcentrationUnit;

            var values = Solution?.Solutions?
                .Where(solution => solution?.ReportParameters != null
                    && solution.ReportParameters.ContainsKey(key))
                .Select(solution => Math.Abs(solution.ReportParameters[key].Value))
                .Where(value => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0)
                .ToList() ?? new List<double>();

            return values.Count == 0
                ? AppSettings.DefaultConcentrationUnit
                : ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(values.Average());
        }

        public AnalysisResult(GlobalSolution solution)
            : this(solution, captureValiditySnapshot: true)
        {
        }

        public AnalysisResultValiditySnapshot ValiditySnapshot { get; private set; }
        public AnalysisResultValidityReport ValidityReport => ValiditySnapshot?.Compare(Solution)
            ?? AnalysisResultValidityReport.Unknown("No validity snapshot is stored for this analysis result.");
        public bool IsValidForCurrentData => ValidityReport.Status == AnalysisResultValidity.Valid;
        public AnalysisResultHealth Health
        {
            get
            {
                var validity = ValidityReport.Status;
                if (validity == AnalysisResultValidity.Invalid) return AnalysisResultHealth.Invalid;
                if (validity == AnalysisResultValidity.PartialInvalid) return AnalysisResultHealth.PartialInvalid;
                if (validity == AnalysisResultValidity.Unknown) return AnalysisResultHealth.Unknown;

                var hasBoundaryWarning = Solution?.Solutions?.Any(solution =>
                    solution?.ParameterBoundaryHit == true
                    || solution?.BootstrapParameterBoundaryHit == true) == true;
                return hasBoundaryWarning ? AnalysisResultHealth.Warning : AnalysisResultHealth.Valid;
            }
        }

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

            IsElectrostaticsAnalysisDependenceEnabled = IsAdvancedAnalysisAvailable && variable_is && allsalt;

            //Check if all data has buffer info and figure out if any are different
            if (Solution.Solutions.All(sol => sol.Data.Attributes.Exists(att => att.Key == AttributeKey.Buffer)))
            {
                var firstSolutionBuffer = Solution.Solutions.First().Data.Attributes.Find(att => att.Key == AttributeKey.Buffer).IntValue;

                IsProtonationAnalysisEnabled = IsAdvancedAnalysisAvailable && Solution.Solutions
                    .Skip(1)
                    .Any(sol => sol.Data.Attributes
                    .Find(att => att.Key == AttributeKey.Buffer).IntValue != firstSolutionBuffer);
            }
        }

        void InitializeAnalyses()
        {
            if (IsSpolarRecordAnalysisEnabled) SpolarRecordAnalysis = new FTSRMethod(this);
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
                foreach (var con in DisplayConstraints())
                {
                    if (con.Value != VariableConstraint.None)
                    {
                        s += ConstraintDisplayName(con.Key, includeSlot: false) + ": ";

                        s += con.Value.GetEnumDescription() + Environment.NewLine;
                    }
                }
            }

            var enthalpySlots = ThermodynamicParameterSlots.All
                .Where(slot => Solution.TemperatureDependence.ContainsKey(slot.Enthalpy)
                    || Solution.IndividualModelReportParameters.Contains(slot.Enthalpy))
                .ToList();
            var enthalpyValues = enthalpySlots
                .Select(slot => Solution.GetStandardParameterValue(slot.Enthalpy));
            var enthalpyUnit = EnergyUnitResolver.Resolve(AppSettings.EnergyUnitFamily, enthalpyValues);
            var heatCapacityUnit = EnergyUnitResolver.Resolve(
                AppSettings.EnergyUnitFamily,
                enthalpySlots
                    .Where(slot => Solution.TemperatureDependence.ContainsKey(slot.Enthalpy))
                    .Select(slot => Solution.TemperatureDependence[slot.Enthalpy].Slope.Value));
            foreach (var slot in enthalpySlots)
            {
                var suffix = enthalpySlots.Count > 1 ? slot.Index.ToString() : string.Empty;
                s += (Model.TemperatureDependenceExposed ? $"∆H{suffix}° = " : $"∆H{suffix} = ");
                s += new Energy(Solution.GetStandardParameterValue(slot.Enthalpy)).ToFormattedString(enthalpyUnit, permole: true) + Environment.NewLine;
                if (Model.TemperatureDependenceExposed && Solution.TemperatureDependence.TryGetValue(slot.Enthalpy, out var dependence))
                    s += $"∆Cₚ{suffix} = " + new Energy(dependence.Slope).ToFormattedString(heatCapacityUnit, permole: true, perK: true) + Environment.NewLine;
            }

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
                var constraintsToDisplay = DisplayConstraints().ToList();
                var keys = constraintsToDisplay.Select(item => item.Key).ToList();
                foreach (var con in constraintsToDisplay)
                {
                    if (con.Value != VariableConstraint.None)
                    {
                        var includeSlot = Model.ModelType != AnalysisModel.SequentialBindingSites
                            && ThermodynamicParameterSlots.FamilyMemberCount(keys, con.Key) > 1;
                        constraints += ConstraintDisplayName(con.Key, includeSlot) + ": ";

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
            if (ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family))
            {
                var suffix = slot.Index == 1 ? string.Empty : slot.Index.ToString();
                return family switch
                {
                    ThermodynamicParameterFamily.Enthalpy => MarkdownStrings.Enthalpy + suffix,
                    ThermodynamicParameterFamily.Affinity => MarkdownStrings.DissociationConstant + (slot.Index == 1 ? string.Empty : "{," + slot.Index + "}"),
                    ThermodynamicParameterFamily.Gibbs => "dG" + suffix,
                    ThermodynamicParameterFamily.EntropyContribution => "-TdS" + suffix,
                    ThermodynamicParameterFamily.HeatCapacity => "dCp" + suffix,
                    _ => key.GetProperties().Name,
                };
            }

            return key switch
            {
                ParameterType.Nvalue1 => "N",
                ParameterType.Nvalue2 => "N{2}",
                _ => key.GetProperties().Name,
            };
        }

        IEnumerable<KeyValuePair<ParameterType, VariableConstraint>> DisplayConstraints()
        {
            var constraints = Options.Constraints.Where(item => item.Value != VariableConstraint.None);
            if (Model.ModelType != AnalysisModel.SequentialBindingSites) return constraints;

            return constraints
                .GroupBy(item => ThermodynamicParameterSlots.TryResolve(item.Key, out _, out var family)
                    ? "thermodynamic:" + family
                    : "parameter:" + item.Key)
                .Select(group => group.First());
        }

        static string ConstraintDisplayName(ParameterType key, bool includeSlot)
        {
            if (!ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family))
            {
                if (key == ParameterType.Nvalue1) return "N-value";
                if (key == ParameterType.Nvalue2) return "N-value2";
                return key.GetProperties().Description;
            }

            var name = family == ThermodynamicParameterFamily.Enthalpy ? "Enthalpy"
                : family == ThermodynamicParameterFamily.Affinity || family == ThermodynamicParameterFamily.Gibbs ? "Affinity"
                : key.GetProperties().Description;
            return includeSlot ? name + slot.Index : name;
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
