using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Core.Analysis
{
    public class FTSRMethod : AdvancedAnalysis
    {
        public static FloatWithError ApCoeff { get; } = new FloatWithError(-0.590884521921104, 0.12);
        public static FloatWithError AnpCoeff { get; } = new FloatWithError(1.3721067124117, 0.06);
        public static FloatWithError RatioID { get; } = new FloatWithError(0.919743, 0.030589);
        public static FloatWithError RatioGlob { get; } = new FloatWithError(0.59, 0);
        public static FloatWithError GlobalZeroEntropy { get; } = new FloatWithError(386, 2.2);
        public static FloatWithError PerResidueEntropyLoss { get; } = new FloatWithError(-23.96662, 0);
        public static FloatWithError RototranslationalEntropy = new FloatWithError(-110, 12);

        public SRFoldedMode FoldedMode { get; set; } = SRFoldedMode.Glob;
        public SRTempMode TempMode { get; set; } = SRTempMode.IsoEntropicPoint;

        FloatWithError Ratio { get; set; } = RatioGlob;

        public SROutput Result { get; private set; }

        internal SRFoldedMode? CompletedFoldedMode { get; private set; }
        internal SRTempMode? CompletedTempMode { get; private set; }

        LinearFitWithError EnthalpyDependence { get; set; }
        LinearFitWithError EntropyDependence { get; set; }
        SummaryDependence ExactEntropyDependence { get; set; }
        FloatWithError LinkedHeatCapacityChange { get; set; }

        FloatWithError HeatCapacityChange => ExactEntropyDependence == null
            ? EnthalpyDependence.Slope
            : LinkedHeatCapacityChange;
        FloatWithError TS { get; set; }
        FloatWithError ReferenceEntropy => ExactEntropyDependence?.Evaluate(
            ExactEntropyDependence.ReferenceTemperature) ?? EntropyDependence.Intercept;
        FloatWithError OffsetReferenceEntropy { get; set; }
        double TemperatureDependenceReferenceTemperature => ExactEntropyDependence?.ReferenceTemperature
            ?? EntropyDependence.ReferenceT;

        public double EvalutationTemperature(bool sample = true)
        {
            return TempMode switch
            {
                SRTempMode.MeanTemperature => TemperatureDependenceReferenceTemperature,
                SRTempMode.ReferenceTemperature => AppSettings.ReferenceTemperature,
                _ when sample => TS.Sample(Rand),
                _ => TS.Value
            };
        }

        public FTSRMethod(AnalysisResult analysisResult) : base(analysisResult)
        {
            RefreshLinkedInputs();
        }

        void RefreshLinkedInputs()
        {
            EnthalpyDependence = Data.Solution.TemperatureDependence[ParameterType.Enthalpy1];
            EntropyDependence = Data.Solution.TemperatureDependence[ParameterType.EntropyContribution1];
            ExactEntropyDependence = Data.Model.Parameters.GetConstraintForParameter(ParameterType.Affinity1)
                == VariableConstraint.ThermodynamicallyLinked
                ? new AnalysisResultAggregateSummaryCalculator(Data).BuildDependence(ParameterType.EntropyContribution1)
                : null;
            LinkedHeatCapacityChange = ExactEntropyDependence == null
                ? default
                : new AnalysisResultAggregateSummaryCalculator(Data).EvaluateHeatCapacity(
                    ThermodynamicParameterSlots.ForStep(1)).Value;
            TS = ExactEntropyDependence == null
                ? EntropyDependence.GetXAxisIntersect()
                : FindIsoentropicTemperature(ExactEntropyDependence);
            if (ExactEntropyDependence != null && Data.Solution.ProfileLikelihoodRun == null
                && Data.Solution.BootstrapSolutions?.Count > ExactEntropyDependence.Replicates.Count)
                TS = SummaryUncertainty.Unavailable(TS.Value);
        }

        static FloatWithError FindIsoentropicTemperature(SummaryDependence dependence)
        {
            var referenceKelvin = dependence.ReferenceTemperature + 273.15;
            var entropyAtReference = dependence.Evaluate(dependence.ReferenceTemperature).Value;
            var heatCapacity = dependence.HeatCapacityTerm;
            if (!IsFinite(referenceKelvin) || referenceKelvin <= 0
                || !IsFinite(entropyAtReference) || !IsFinite(heatCapacity)
                || heatCapacity == 0)
                return SummaryUncertainty.Unavailable(double.NaN);

            var centralKelvin = referenceKelvin * Math.Exp(entropyAtReference / (referenceKelvin * heatCapacity));
            if (!IsFinite(centralKelvin) || centralKelvin <= 0)
                return SummaryUncertainty.Unavailable(double.NaN);

            var centralCelsius = centralKelvin - 273.15;
            if (!IsValidTemperature(centralKelvin, centralCelsius))
                return SummaryUncertainty.Unavailable(centralCelsius);
            if (dependence.Replicates.Count > 0)
            {
                var values = new List<double>();
                foreach (var replicate in dependence.Replicates)
                {
                    var value = RootValue(replicate);
                    if (!IsFinite(value)) return SummaryUncertainty.Unavailable(centralCelsius);
                    values.Add(value);
                }
                return new FloatWithError(values, centralCelsius);
            }

            double variance = 0, lowerVariance = 0, upperVariance = 0;
            foreach (var term in dependence.Contributions)
            {
                var coefficient = (term.Weight * heatCapacity
                    - entropyAtReference * term.WeightHeatCapacityTerm)
                    / (referenceKelvin * heatCapacity * heatCapacity);
                if (coefficient == 0) continue;
                if (!IsFinite(term.Sd) || !IsFinite(term.LowerWidth) || !IsFinite(term.UpperWidth))
                    return SummaryUncertainty.Unavailable(centralCelsius);
                variance += Square(coefficient * term.Sd);
                lowerVariance += Square(coefficient * (coefficient < 0 ? term.UpperWidth : term.LowerWidth));
                upperVariance += Square(coefficient * (coefficient < 0 ? term.LowerWidth : term.UpperWidth));
            }
            var lowerLogWidth = Math.Sqrt(lowerVariance);
            var upperLogWidth = Math.Sqrt(upperVariance);
            var lowerKelvin = centralKelvin * Math.Exp(-lowerLogWidth);
            var upperKelvin = centralKelvin * Math.Exp(upperLogWidth);
            var lowerCelsius = lowerKelvin - 273.15;
            var upperCelsius = upperKelvin - 273.15;
            if (!IsValidTemperature(lowerKelvin, lowerCelsius)
                || !IsValidTemperature(upperKelvin, upperCelsius))
                return SummaryUncertainty.Unavailable(centralCelsius);
            return new FloatWithError(centralCelsius, Math.Abs(centralKelvin) * Math.Sqrt(variance),
                lowerCelsius, upperCelsius);

            double RootValue(SummaryDependence curve)
            {
                var tr = curve.ReferenceTemperature + 273.15;
                var er = curve.Evaluate(curve.ReferenceTemperature).Value;
                var cp = curve.HeatCapacityTerm;
                if (!IsFinite(tr) || tr <= 0 || !IsFinite(er) || !IsFinite(cp) || cp == 0) return double.NaN;
                var kelvin = tr * Math.Exp(er / (tr * cp));
                var celsius = kelvin - 273.15;
                return IsValidTemperature(kelvin, celsius) ? celsius : double.NaN;
            }
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static bool IsValidTemperature(double kelvin, double celsius) =>
            IsFinite(kelvin) && kelvin > 0 && IsFinite(celsius) && celsius + 273.15 > 0;
        static double Square(double value) => value * value;

        protected override void Calculate()
        {
            RefreshLinkedInputs();
            float f = FoldedMode switch
            {
                SRFoldedMode.Glob => 1,
                SRFoldedMode.Intermediate => 0.5f,
                SRFoldedMode.ID => 0,
                _ => 1,
            };

            Ratio = (RatioID * (1 - f) + RatioGlob * f);
            OffsetReferenceEntropy = ExactEntropyDependence?.Evaluate(AppSettings.ReferenceTemperature)
                ?? EntropyDependence.Evaluate(AppSettings.ReferenceTemperature);

            var exact = Evaluate(exact: true);

            var list_ds_he = new List<double>();
            var list_ds_conf = new List<double>();
            var list_ds_r = new List<double>();

            var niter = 5 * ResultAnalysisController.CalculationIterations;

            for (int i = 0; i < niter; i++)
            {
                var result = Evaluate();

                list_ds_he.Add(result.HydrationEntropy);
                list_ds_conf.Add(result.ConformationalEntropy);
                list_ds_r.Add(result.Rvalue);

                ResultAnalysisController.ReportCalculationProgress(i, niter);
                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            CompletedIterations = list_ds_r.Count;

            Result = new SROutput(
                new FloatWithError(list_ds_he, exact.HydrationEntropy),
                new FloatWithError(list_ds_conf, exact.ConformationalEntropy),
                new FloatWithError(list_ds_r, exact.Rvalue),
                TempMode == SRTempMode.IsoEntropicPoint ? TS : new(EvalutationTemperature(sample: false)));
        }

        protected override object CaptureCommittedState() => new CommittedState
        {
            Result = Result,
            FoldedMode = CompletedFoldedMode,
            TempMode = CompletedTempMode,
        };

        protected override void RestoreCommittedState(object state)
        {
            var previous = state as CommittedState;
            Result = previous?.Result;
            CompletedFoldedMode = previous?.FoldedMode;
            CompletedTempMode = previous?.TempMode;
            if (CompletedFoldedMode.HasValue) FoldedMode = CompletedFoldedMode.Value;
            if (CompletedTempMode.HasValue) TempMode = CompletedTempMode.Value;
        }

        protected override void CommitRunState()
        {
            CompletedFoldedMode = FoldedMode;
            CompletedTempMode = TempMode;
            CompletedErrorEstimationMethod = null;
        }

        internal void RestoreResult(
            SRFoldedMode foldedMode,
            SRTempMode tempMode,
            SROutput result,
            int completedIterations,
            DateTime? completedAtUtc)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            FoldedMode = foldedMode;
            TempMode = tempMode;
            Result = result;
            CompletedFoldedMode = foldedMode;
            CompletedTempMode = tempMode;
            RestoreRunMetadata(completedIterations, completedAtUtc, null);
        }

        sealed class CommittedState
        {
            public SROutput Result { get; set; }
            public SRFoldedMode? FoldedMode { get; set; }
            public SRTempMode? TempMode { get; set; }
        }

        SROutput Evaluate(bool exact = false)
        {
            // The exact result is the point estimate.  Uncertainty iterations
            // sample every uncertain input, including the iso-entropic
            // temperature and rototranslational entropy.
            var linked = ExactEntropyDependence != null;
            var temp = linked
                ? 273.15 + EvalutationTemperature(sample: !exact)
                : Math.Abs(273.15 + EvalutationTemperature(sample: !exact));
            if (linked && (!IsFinite(temp) || temp <= 0))
                throw new InvalidOperationException("Spolar–Record requires a positive absolute temperature.");
            ValidateLinkedInputs(exact);

            var _ds = TempMode switch
            {
                SRTempMode.MeanTemperature => (ReferenceEntropy / (-temp)),
                SRTempMode.ReferenceTemperature => (OffsetReferenceEntropy / (-temp)),
                _ => new(0),
            };

            var ds = GetValue(_ds, exact);
            var cp = GetValue(HeatCapacityChange, exact);
            if (linked && !IsFinite(cp))
                throw new InvalidOperationException("Spolar–Record linked heat capacity is unavailable.");
            var ap = GetValue(ApCoeff, exact);
            var anp = GetValue(AnpCoeff, exact);
            var ratio = GetValue(Ratio, exact);
            var gts = GetValue(GlobalZeroEntropy, exact);

            var danp_coeff = 1f / (anp + ratio * ap);
            var dcp_coeff = danp_coeff * anp;

            var ds_he = cp * dcp_coeff * Math.Log(temp / gts);
            var ds_conf = ds - ds_he - GetValue(RototranslationalEntropy, exact);
            var r = ds_conf / PerResidueEntropyLoss.Value;
            if (linked && (!IsFinite(ds_he) || !IsFinite(ds_conf) || !IsFinite(r)))
                throw new InvalidOperationException("Spolar–Record linked calculation produced a non-finite value.");

            return new SROutput(new(ds_he), new(ds_conf), new(r), TempMode == SRTempMode.IsoEntropicPoint ? TS : new(EvalutationTemperature(sample: false)));

            double GetValue(FloatWithError par, bool exact)
            {
                return exact ? par.Value : par.Sample(Rand);
            }
        }

        void ValidateLinkedInputs(bool exact)
        {
            if (ExactEntropyDependence == null) return;
            Require(HeatCapacityChange, "heat capacity");
            if (TempMode == SRTempMode.IsoEntropicPoint) Require(TS, "isoentropic temperature");
            else Require(TempMode == SRTempMode.ReferenceTemperature ? OffsetReferenceEntropy : ReferenceEntropy,
                "reference entropy");
            if (exact) return;
            RequireInterval(HeatCapacityChange, "heat capacity");
            if (TempMode == SRTempMode.IsoEntropicPoint) RequireInterval(TS, "isoentropic temperature");
            else RequireInterval(TempMode == SRTempMode.ReferenceTemperature ? OffsetReferenceEntropy : ReferenceEntropy,
                "reference entropy");

            void Require(FloatWithError value, string name)
            {
                if (!IsFinite(value.Value)) throw new InvalidOperationException($"Spolar–Record linked {name} is unavailable.");
            }
            void RequireInterval(FloatWithError value, string name)
            {
                if (value.DistributionConfidence95?.Length != 2
                    || !IsFinite(value.Lower) || !IsFinite(value.Upper)
                    || value.Lower > value.Upper || !IsFinite(value.SD))
                    throw new InvalidOperationException($"Spolar–Record linked {name} uncertainty is unavailable.");
            }
        }

        public class SROutput : Tuple<FloatWithError, FloatWithError, FloatWithError, FloatWithError>
        {
            public FloatWithError HydrationEntropy => Item1;
            public FloatWithError ConformationalEntropy => Item2;
            public FloatWithError Rvalue => Item3;
            public FloatWithError ReferenceTemperature => Item4;

            public FloatWithError HydrationContribution(double t) => -(273.15 + t) * HydrationEntropy;
            public FloatWithError ConformationalContribution(double t) => -(273.15 + t) * ConformationalEntropy;

            public SROutput(FloatWithError s_he, FloatWithError s_conf, FloatWithError r, FloatWithError refT) : base(s_he, s_conf, r, refT)
            {
            }

            public void PrintString()
            {
                Console.WriteLine(HydrationEntropy.Value + " " + ConformationalEntropy.Value + " " + Rvalue.Value);
            }
        }

        public enum SRFoldedMode
        {
            Glob,
            Intermediate,
            ID
        }

        public enum SRTempMode
        {
            IsoEntropicPoint,
            MeanTemperature,
            ReferenceTemperature
        }
    }
}
