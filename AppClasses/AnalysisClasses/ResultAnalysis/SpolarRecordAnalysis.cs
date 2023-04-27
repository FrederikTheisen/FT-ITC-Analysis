using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.AppClasses.Analysis2;
using AppKit;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public class FTSRMethod : ResultAnalysis
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

        LinearFitWithError EnthalpyDependence { get; set; }
        LinearFitWithError EntropyDependence { get; set; }

        FloatWithError HeatCapacityChange => EnthalpyDependence.Slope;
        FloatWithError TS { get; }
        FloatWithError ReferenceEntropy => EntropyDependence.Intercept;
        FloatWithError OffsetReferenceEntropy { get; set; }
        double TemperatureDependenceReferenceTemperature => EntropyDependence.ReferenceT;

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

        public SROutput Result { get; private set; }

        public FTSRMethod(AnalysisResult analysisResult) : base(analysisResult)
        {
            EnthalpyDependence = analysisResult.Solution.TemperatureDependence[ParameterType.Enthalpy1];
            EntropyDependence = analysisResult.Solution.TemperatureDependence[ParameterType.EntropyContribution1];

            TS = EntropyDependence.GetXAxisIntersect();
        }

        public override void PerformAnalysis()
        {
            base.PerformAnalysis();
        }

        protected override void Calculate()
        {
            float f = FoldedMode switch
            {
                SRFoldedMode.Glob => 1,
                SRFoldedMode.Intermediate => 0.5f,
                SRFoldedMode.ID => 0,
                _ => 1,
            };

            Ratio = (RatioID * (1 - f) + RatioGlob * f);
            OffsetReferenceEntropy = EntropyDependence.Evaluate(AppSettings.ReferenceTemperature);

            var exact = Evaluate(exact: true);

            var list_ds_he = new List<double>();
            var list_ds_conf = new List<double>();
            var list_ds_r = new List<double>();

            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var result = Evaluate();

                list_ds_he.Add(result.HydrationEntropy);
                list_ds_conf.Add(result.ConformationalEntropy);
                list_ds_r.Add(result.Rvalue);
            }

            Result = new SROutput(
                new FloatWithError(list_ds_he, exact.HydrationEntropy),
                new FloatWithError(list_ds_conf, exact.ConformationalEntropy),
                new FloatWithError(list_ds_r, exact.Rvalue),
                TempMode == SRTempMode.IsoEntropicPoint ? TS : new(EvalutationTemperature(sample: false)));
        }

        SROutput Evaluate(bool exact = false)
        {
            var temp = Math.Abs(273.15 + EvalutationTemperature(sample: exact));

            var _ds = TempMode switch
            {
                SRTempMode.MeanTemperature => (ReferenceEntropy / (-temp)),
                SRTempMode.ReferenceTemperature => (OffsetReferenceEntropy / (-temp)),
                _ => new(0),
            };

            var ds = GetValue(_ds, exact);
            var cp = GetValue(HeatCapacityChange, exact);
            var ap = GetValue(ApCoeff, exact);
            var anp = GetValue(AnpCoeff, exact);
            var ratio = GetValue(Ratio, exact);
            var gts = GetValue(GlobalZeroEntropy, exact);

            var danp_coeff = 1f / (anp + ratio * ap);
            var dcp_coeff = danp_coeff * anp;

            var ds_he = cp * dcp_coeff * Math.Log(temp / gts);
            var ds_conf = ds - ds_he - RototranslationalEntropy.Sample(Rand);
            var r = ds_conf / PerResidueEntropyLoss.Value;

            return new SROutput(new(ds_he), new(ds_conf), new(r), TempMode == SRTempMode.IsoEntropicPoint ? TS : new(EvalutationTemperature(sample: false)));

            double GetValue(FloatWithError par, bool exact)
            {
                return exact ? par.Value : par.Sample(Rand);
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
