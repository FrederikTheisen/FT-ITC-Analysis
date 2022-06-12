using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppKit;

namespace AnalysisITC
{
    public static class SpolarRecordAnalysisController
    {
        public static event EventHandler<Tuple<int, int, float>> IterationFinished;
        public static event EventHandler<Tuple<int, TimeSpan>> AnalysisFinished;

        public static bool StopAnalysis { get; set; } = false;

        public static int CalculationIterations { get; set; } = 10000;
        public static SRTempMode TempMode { get; set; } = SRTempMode.IsoEntropicPoint;
        public static SRFoldedMode FoldedDegree { get; set; } = SRFoldedMode.Glob;

        public static async void Analyze(GlobalSolution solution)
        {
            StopAnalysis = false;
            DateTime start = DateTime.Now;

            var sr = new FTSRMethod(solution.EnthalpyLine, solution.EntropyLine);
            sr.SRTempMode = TempMode;
            sr.SRFoldedMode = FoldedDegree;

            await sr.Calculate();

            AnalysisFinished?.Invoke(sr, new Tuple<int, TimeSpan>(sr.IterationsCompleted, DateTime.Now - start));
        }

        public static void ReportCalculationProgress(int iteration) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            IterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, CalculationIterations, iteration / (float)CalculationIterations));
        });

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

    //public class SpolarRecordMethodError : ErrorPropagation
    //{
    //    /// <summary>
    //    /// Polar surface area coefficient 
    //    /// </summary>
    //    internal FloatWithError ApCoeff;
    //    /// <summary>
    //    /// Non-polar surface area coefficient
    //    /// </summary>
    //    internal FloatWithError AnpCoeff;
    //    /// <summary>
    //    /// Ratio of polar to non-polar surface area change for protein-protein interactions
    //    /// Value depends on protein structural class
    //    /// </summary>
    //    public static FloatWithError Ratio;
    //    /// <summary>
    //    /// Temperature for which the hydration entropy is zero
    //    /// </summary>
    //    internal FloatWithError GlobalZeroEntropy;
    //    /// <summary>
    //    /// Average entropy loss for a single residue folding upon binding
    //    /// </summary>
    //    internal FloatWithError PerResidueEntropyLoss;

    //    public FloatWithError RototranslationalEntropy = new FloatWithError(-110, 12);

    //    public static bool RatioErrorDistributionOverride { get; set; } = false;
    //    public static bool GTSErrorDistributionOverride { get; set; } = false;
    //    public static bool ApErrorDistributionOverride { get; set; } = false;
    //    public static bool AnpErrorDistributionOverride { get; set; } = false;

    //    public FloatWithError HeatCapacity => Parameters[0];
    //    public FloatWithError TS => Parameters[1];

    //    public SpolarRecordMethodError(double dcp, double errordcp, double ts, double errorts)
    //    {
    //        AssumeNormalDistribution = true;

    //        Parameters.Add(new FloatWithError(dcp, errordcp));
    //        Parameters.Add(new FloatWithError(ts, errorts));
    //    }

    //    internal double GetCp() => Distribution.Default(HeatCapacity.Value, HeatCapacity.SD, ParameterDistributions[0].ToList());
    //    internal double GetTs() => Distribution.Default(TS.Value, TS.SD, ParameterDistributions[1].ToList());
    //    internal double GetApCoeff() => !ApErrorDistributionOverride ? Distribution.None(ApCoeff) : Distribution.Default(ApCoeff);
    //    internal double GetGTS() => !GTSErrorDistributionOverride ? Distribution.None(GlobalZeroEntropy) : Distribution.Default(GlobalZeroEntropy);
    //    internal double GetRatio() => !RatioErrorDistributionOverride ? Distribution.None(Ratio) : Distribution.Default(Ratio);
    //    internal double GetAnpCoeff() => !AnpErrorDistributionOverride ? Distribution.None(AnpCoeff) : Distribution.Default(AnpCoeff);
    //}

    //public class SpolarRecordMethodErrorGlob : SpolarRecordMethodError
    //{
    //    public SpolarRecordMethodErrorGlob(double a, double da, double b, double db) : base(a, da, b, db)
    //    {
    //        ApCoeff = new FloatWithError(-0.59, 0.12);
    //        AnpCoeff = new FloatWithError(1.34, 0.06);
    //        Ratio = new FloatWithError(0.59, 0.00);
    //        GlobalZeroEntropy = new FloatWithError(386, 2.2);

    //        PerResidueEntropyLoss = new FloatWithError(-23.4, 0);
    //    }

    //    public override void SimulateRandomSampleInput(double x = 0, int iterations = 30000)
    //    {
    //        for (int i = 0; i < iterations; i++)
    //        {
    //            var cp = GetCp();
    //            var ts = GetTs();
    //            var ap = GetApCoeff();
    //            var anp = GetAnpCoeff();
    //            var ratio = GetRatio();
    //            var gts = GetGTS();

    //            var danp_coeff = 1f / (anp + ratio * ap);
    //            var dcp_coeff = danp_coeff * anp;

    //            var ds_he = cp * dcp_coeff * Math.Log(ts / gts);
    //            var ds_conf = -ds_he - RototranslationalEntropy;
    //            var r = ds_conf / PerResidueEntropyLoss.Value;

    //            Results.Add(r);
    //        }
    //    }
    //}

    //public class SpolarRecordMethodErrorID : SpolarRecordMethodError
    //{
    //    public static FloatWithError Ratio = new FloatWithError(0.919743, 0.030589);

    //    public SpolarRecordMethodErrorID(double a, double da, double b, double db) : base(a, da, b, db)
    //    {
    //        ApCoeff = new FloatWithError(-0.590884521921104, 0.12);
    //        AnpCoeff = new FloatWithError(1.3721067124117, 0.06);
    //        GlobalZeroEntropy = new FloatWithError(386, 2.2);

    //        PerResidueEntropyLoss = new FloatWithError(-23.96662, 0);
    //    }

    //    public override void SimulateRandomSampleInput(double x = 0, int iterations = 30000)
    //    {
    //        for (int i = 0; i < iterations; i++)
    //        {
    //            var cp = GetCp();
    //            var ts = GetTs();
    //            var ap = GetApCoeff();
    //            var anp = GetAnpCoeff();
    //            var ratio = GetRatio();
    //            var gts = GetGTS();

    //            var danp_coeff = 1f / (anp + ratio * ap);
    //            var dcp_coeff = danp_coeff * anp;

    //            var ds_he = cp * dcp_coeff * Math.Log(ts / gts);
    //            var ds_conf = -ds_he - RototranslationalEntropy;
    //            var r = ds_conf / PerResidueEntropyLoss.Value;

    //            Results.Add(r);
    //        }
    //    }
    //}

    public class FTSRMethod
    {
        public static FloatWithError ApCoeff { get; } = new FloatWithError(-0.590884521921104, 0.12);
        public static FloatWithError AnpCoeff { get; } = new FloatWithError(1.3721067124117, 0.06);
        public static FloatWithError RatioID { get; } = new FloatWithError(0.919743, 0.030589);
        public static FloatWithError RatioGlob { get; } = new FloatWithError(0.59, 0);
        public static FloatWithError GlobalZeroEntropy { get; } = new FloatWithError(386, 2.2);
        public static FloatWithError PerResidueEntropyLoss { get; } = new FloatWithError(-23.96662, 0);
        public static FloatWithError RototranslationalEntropy = new FloatWithError(-110, 12);

        Random Rndm { get; } = new Random();

        public SpolarRecordAnalysisController.SRFoldedMode SRFoldedMode { get; set; } = SpolarRecordAnalysisController.SRFoldedMode.Glob;
        public SpolarRecordAnalysisController.SRTempMode SRTempMode { get; set; } = SpolarRecordAnalysisController.SRTempMode.IsoEntropicPoint;

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
            return SRTempMode switch
            {
                SpolarRecordAnalysisController.SRTempMode.MeanTemperature => TemperatureDependenceReferenceTemperature,
                SpolarRecordAnalysisController.SRTempMode.ReferenceTemperature => AppSettings.ReferenceTemperature,
                _ when sample => TS.Sample(Rndm),
                _ => TS.Value
            };
        }

        public int IterationsCompleted { get; set; } = 0;
        List<SROutput> Results = new List<SROutput>();
        public SROutput AnalysisResult { get; private set; }

        public FTSRMethod(LinearFitWithError enthalpy, LinearFitWithError entropy)
        {
            EnthalpyDependence = enthalpy;
            EntropyDependence = entropy;

            TS = EntropyDependence.GetXAxisIntersect();
        }

        public async Task Calculate()
        {
            float f = SRFoldedMode switch
            {
                SpolarRecordAnalysisController.SRFoldedMode.Glob => 1,
                SpolarRecordAnalysisController.SRFoldedMode.Intermediate => 0.5f,
                SpolarRecordAnalysisController.SRFoldedMode.ID => 0,
                _ => 1,
            };

            Ratio = (RatioID * (1 - f) + RatioGlob * f);

            OffsetReferenceEntropy = EntropyDependence.Evaluate(AppSettings.ReferenceTemperature);

            await Task.Run(() => Evaluate(SpolarRecordAnalysisController.CalculationIterations));
        }

        (double, double, double) EvaluateExact()
        {
            var temp = Math.Abs(273.15 + EvalutationTemperature(sample: false));
            var tds = SRTempMode switch
            {
                SpolarRecordAnalysisController.SRTempMode.MeanTemperature => (ReferenceEntropy).Value,
                SpolarRecordAnalysisController.SRTempMode.ReferenceTemperature => (OffsetReferenceEntropy).Value,
                _ => 0,
            };

            var ds = tds / -temp;

            var cp = HeatCapacityChange.Value;
            var ap = ApCoeff.Value;
            var anp = AnpCoeff.Value;
            var ratio = Ratio.Value;
            var gts = GlobalZeroEntropy.Value;

            var danp_coeff = 1f / (anp + ratio * ap);
            var dcp_coeff = danp_coeff * anp;

            var ds_he = cp * dcp_coeff * Math.Log(temp / gts);
            var ds_conf = ds - ds_he - RototranslationalEntropy.Value;
            var r = ds_conf / PerResidueEntropyLoss.Value;

            return (ds_he, ds_conf, r);
        }

        void Evaluate(int iterations)
        {
            var (exact_ds_he, exact_ds_conf, exact_r) = EvaluateExact();

            var list_ds = new List<double>();
            var list_ds_he = new List<double>();
            var list_ds_conf = new List<double>();
            var list_ds_r = new List<double>();

            for (int i = 0; i < iterations; i++)
            {
                var temp = Math.Abs(273.15 + EvalutationTemperature(sample: true));

                var _ds = SRTempMode switch
                {
                    SpolarRecordAnalysisController.SRTempMode.MeanTemperature => (ReferenceEntropy / (-temp)),
                    SpolarRecordAnalysisController.SRTempMode.ReferenceTemperature => (OffsetReferenceEntropy / (-temp)),
                    _ => new(0),
                };

                var ds = _ds.Sample(Rndm);

                var cp = HeatCapacityChange.Sample(Rndm);
                var ap = ApCoeff.Sample(Rndm);
                var anp = AnpCoeff.Sample(Rndm);
                var ratio = Ratio.Sample(Rndm);
                var gts = GlobalZeroEntropy.Sample(Rndm);

                var danp_coeff = 1f / (anp + ratio * ap);
                var dcp_coeff = danp_coeff * anp;

                var ds_he = cp * dcp_coeff * Math.Log(temp / gts);
                var ds_conf = ds - ds_he - RototranslationalEntropy.Sample(Rndm);
                var r = ds_conf / PerResidueEntropyLoss.Value;

                list_ds.Add(ds);
                list_ds_he.Add(ds_he);
                list_ds_conf.Add(ds_conf);
                list_ds_r.Add(r);

                SpolarRecordAnalysisController.ReportCalculationProgress(i+1);
                IterationsCompleted = i;

                if (SpolarRecordAnalysisController.StopAnalysis) break;
            }

            IterationsCompleted++;

            AnalysisResult = new SROutput(new(list_ds_he, exact_ds_he), new(list_ds_conf, exact_ds_conf), new(list_ds_r, exact_r), SRTempMode == SpolarRecordAnalysisController.SRTempMode.IsoEntropicPoint ? TS : new(EvalutationTemperature(sample: false)));
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
    }
}
