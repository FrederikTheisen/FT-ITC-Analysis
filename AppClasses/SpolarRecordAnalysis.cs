using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AnalysisITC
{
    public class SpolarRecordAnalysis
    {
        public SpolarRecordAnalysis()
        {
        }
    }

    public class SpolarRecordMethodError : ErrorPropagation
    {
        /// <summary>
        /// Polar surface area coefficient 
        /// </summary>
        internal FloatWithError ApCoeff;
        /// <summary>
        /// Non-polar surface area coefficient
        /// </summary>
        internal FloatWithError AnpCoeff;
        /// <summary>
        /// Ratio of polar to non-polar surface area change for protein-protein interactions
        /// Value depends on protein structural class
        /// </summary>
        public static FloatWithError Ratio;
        /// <summary>
        /// Temperature for which the hydration entropy is zero
        /// </summary>
        internal FloatWithError GlobalZeroEntropy;
        /// <summary>
        /// Average entropy loss for a single residue folding upon binding
        /// </summary>
        internal FloatWithError PerResidueEntropyLoss;

        public FloatWithError RototranslationalEntropy = new FloatWithError(-110, 12);

        public static bool RatioErrorDistributionOverride { get; set; } = false;
        public static bool GTSErrorDistributionOverride { get; set; } = false;
        public static bool ApErrorDistributionOverride { get; set; } = false;
        public static bool AnpErrorDistributionOverride { get; set; } = false;

        public FloatWithError HeatCapacity => Parameters[0];
        public FloatWithError TS => Parameters[1];

        public SpolarRecordMethodError(double dcp, double errordcp, double ts, double errorts)
        {
            AssumeNormalDistribution = true;

            Parameters.Add(new FloatWithError(dcp, errordcp));
            Parameters.Add(new FloatWithError(ts, errorts));
        }

        internal double GetCp() => Distribution.Default(HeatCapacity.Value, HeatCapacity.SD, ParameterDistributions[0].ToList());
        internal double GetTs() => Distribution.Default(TS.Value, TS.SD, ParameterDistributions[1].ToList());
        internal double GetApCoeff() => !ApErrorDistributionOverride ? Distribution.None(ApCoeff) : Distribution.Default(ApCoeff);
        internal double GetGTS() => !GTSErrorDistributionOverride ? Distribution.None(GlobalZeroEntropy) : Distribution.Default(GlobalZeroEntropy);
        internal double GetRatio() => !RatioErrorDistributionOverride ? Distribution.None(Ratio) : Distribution.Default(Ratio);
        internal double GetAnpCoeff() => !AnpErrorDistributionOverride ? Distribution.None(AnpCoeff) : Distribution.Default(AnpCoeff);
    }

    public class SpolarRecordMethodErrorGlob : SpolarRecordMethodError
    {
        public SpolarRecordMethodErrorGlob(double a, double da, double b, double db) : base(a, da, b, db)
        {
            ApCoeff = new FloatWithError(-0.59, 0.12);
            AnpCoeff = new FloatWithError(1.34, 0.06);
            Ratio = new FloatWithError(0.59, 0.00);
            GlobalZeroEntropy = new FloatWithError(386, 2.2);

            PerResidueEntropyLoss = new FloatWithError(-23.4, 0);
        }

        public override void SimulateRandomSampleInput(double x = 0, int iterations = 30000)
        {
            for (int i = 0; i < iterations; i++)
            {
                var cp = GetCp();
                var ts = GetTs();
                var ap = GetApCoeff();
                var anp = GetAnpCoeff();
                var ratio = GetRatio();
                var gts = GetGTS();

                var danp_coeff = 1f / (anp + ratio * ap);
                var dcp_coeff = danp_coeff * anp;

                var ds_he = cp * dcp_coeff * Math.Log(ts / gts);
                var ds_conf = -ds_he - RototranslationalEntropy;
                var r = ds_conf / PerResidueEntropyLoss.Value;

                Results.Add(r);
            }
        }
    }

    public class SpolarRecordMethodErrorID : SpolarRecordMethodError
    {
        public static FloatWithError Ratio = new FloatWithError(0.919743, 0.030589);

        public SpolarRecordMethodErrorID(double a, double da, double b, double db) : base(a, da, b, db)
        {
            ApCoeff = new FloatWithError(-0.590884521921104, 0.12);
            AnpCoeff = new FloatWithError(1.3721067124117, 0.06);
            GlobalZeroEntropy = new FloatWithError(386, 2.2);

            PerResidueEntropyLoss = new FloatWithError(-23.96662, 0);
        }

        public override void SimulateRandomSampleInput(double x = 0, int iterations = 30000)
        {
            for (int i = 0; i < iterations; i++)
            {
                var cp = GetCp();
                var ts = GetTs();
                var ap = GetApCoeff();
                var anp = GetAnpCoeff();
                var ratio = GetRatio();
                var gts = GetGTS();

                var danp_coeff = 1f / (anp + ratio * ap);
                var dcp_coeff = danp_coeff * anp;

                var ds_he = cp * dcp_coeff * Math.Log(ts / gts);
                var ds_conf = -ds_he - RototranslationalEntropy;
                var r = ds_conf / PerResidueEntropyLoss.Value;

                Results.Add(r);
            }
        }
    }

    public class FTSRMethod
    {
        public event EventHandler<SROutput> DidFinishCalculation;

        public static FloatWithError ApCoeff { get; } = new FloatWithError(-0.590884521921104, 0.12);
        public static FloatWithError AnpCoeff { get; } = new FloatWithError(1.3721067124117, 0.06);
        public static FloatWithError RatioID { get; } = new FloatWithError(0.919743, 0.030589);
        public static FloatWithError RatioGlob { get; } = new FloatWithError(0.59, 0);
        public static FloatWithError GlobalZeroEntropy { get; } = new FloatWithError(386, 2.2);
        public static FloatWithError PerResidueEntropyLoss { get; } = new FloatWithError(-23.96662, 0);
        public static FloatWithError RototranslationalEntropy = new FloatWithError(-110, 12);

        public float DegreeFolded { get; set; } = 1;
        FloatWithError Ratio { get; set; } = RatioGlob;

        LinearFitWithError EnthalpyDependence { get; set; }
        LinearFitWithError EntropyDependence { get; set; }

        FloatWithError HeatCapacityChange => EnthalpyDependence.Slope;
        FloatWithError TS { get; }
        FloatWithError ReferenceEntropy => EntropyDependence.Intercept;
        double TemperatureDependenceReferenceTemperature => EntropyDependence.ReferenceT;

        double EvalutationTemperature(SRMode mode, bool sample = true)
        {
            return mode switch
            {
                SRMode.MeanTemperature => TemperatureDependenceReferenceTemperature,
                SRMode.ReferenceTemperature => AppSettings.ReferenceTemperature,
                _ when sample => TS.Sample(),
                _ => TS.Value
            };
        }

        List<SROutput> Results = new List<SROutput>();
        public SROutput AnalysisResult { get; private set; }

        //public FTSRMethod(FloatWithError heatcapacity, FloatWithError ts)
        //{
        //    HeatCapacityChange = heatcapacity;
        //    ReferenceEntropy = new(0);
        //    ReferenceTemperature = ts;

        //    UseAxisInterceptCalculation = true;
        //}

        //public FTSRMethod(FloatWithError heatcapacity, FloatWithError refS, double refT)
        //{
        //    HeatCapacityChange = heatcapacity;
        //    ReferenceEntropy = refS;
        //    ReferenceTemperature = refT;

        //    UseAxisInterceptCalculation = false;
        //}

        public FTSRMethod(LinearFitWithError enthalpy, LinearFitWithError entropy)
        {
            EnthalpyDependence = enthalpy;
            EntropyDependence = entropy;

            TS = EntropyDependence.GetXAxisIntersect();
        }

        public void SetDegreeFolded(float percent)
        {
            DegreeFolded = percent;

            Ratio = (RatioID * (1 - DegreeFolded) + RatioGlob * DegreeFolded) / 2;
        }


        public async Task Calculate(SRMode mode = SRMode.IsoEntropicPoint)
        {
            Evaluate(mode);

            DidFinishCalculation?.Invoke(this, AnalysisResult);
        }

        public void Evaluate(SRMode mode, int iterations = 10000)
        {
            Results = new List<SROutput>();

            var list_ds_he = new double[iterations];
            var list_ds_conf = new double[iterations];
            var list_ds_r = new double[iterations];

            for (int i = 0; i < iterations; i++)
            {
                var temp = Math.Abs(273.15 + EvalutationTemperature(mode, sample: true));

                var ds = mode switch
                {
                    SRMode.MeanTemperature => (ReferenceEntropy / (-temp)).Sample(),
                    SRMode.ReferenceTemperature => (EntropyDependence.Evaluate(AppSettings.ReferenceTemperature) / (-temp)).Sample(),
                    _ => 0,
                };

                var cp = HeatCapacityChange.Sample();
                var ap = ApCoeff.Sample();
                var anp = AnpCoeff.Sample();
                var ratio = Ratio.Sample();
                var gts = GlobalZeroEntropy.Sample();

                var danp_coeff = 1f / (anp + ratio * ap);
                var dcp_coeff = danp_coeff * anp;

                var ds_he = cp * dcp_coeff * Math.Log(temp / gts);
                var ds_conf = ds - ds_he - RototranslationalEntropy;
                var r = ds_conf / PerResidueEntropyLoss.Value;

                list_ds_he[i] = ds_he;
                list_ds_conf[i] = ds_conf;
                list_ds_r[i] = r;

                Results.Add(new SROutput(new(ds_he), new(ds_conf), new(r), temp));
            }

            AnalysisResult = new SROutput(new(list_ds_he), new(list_ds_conf), new(list_ds_r), EvalutationTemperature(mode, sample: false));
        }

        public enum SRMode
        {
            IsoEntropicPoint,
            MeanTemperature,
            ReferenceTemperature
        }

        public class SROutput : Tuple<FloatWithError, FloatWithError, FloatWithError, double>
        {
            public FloatWithError HydrationEntropy => Item1;
            public FloatWithError ConformationalEntropy => Item2;
            public FloatWithError Rvalue => Item3;
            public double ReferenceTemperature => Item4;

            public FloatWithError HydrationContribution(double t) => t * HydrationEntropy;
            public FloatWithError ConformationalContribution(double t) => t * ConformationalEntropy;

            public SROutput(FloatWithError s_he, FloatWithError s_conf, FloatWithError r, double refT) : base(s_he, s_conf, r, refT)
            {
            }

            public void PrintString()
            {
                Console.WriteLine(HydrationEntropy.Value + " " + ConformationalEntropy.Value + " " + Rvalue.Value);
            }
        }
    }
}
