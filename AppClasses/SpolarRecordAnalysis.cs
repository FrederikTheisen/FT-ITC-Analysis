using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public class SpolarRecordAnalysis
    {
        public SpolarRecordAnalysis()
        {
        }
    }

    public class ErrorPropagation
    {
        public virtual List<double> Results { get; private set; } = new List<double>();

        public bool AssumeNormalDistribution { get; set; } = false;

        public List<FloatWithError> Parameters { get; set; } = new List<FloatWithError>();
        public List<double[]> ParameterDistributions = new List<double[]>();

        public FloatWithError Result { get; set; }

        public void Evaluate()
        {
            SimulateRandomSampleInput();

            CalculateError();
        }

        void CalculateError()
        {
            double mean = 0;
            foreach (var v in Results) mean += +v;

            mean /= Results.Count;

            if (AssumeNormalDistribution) { mean = GetTrueMean(); }

            double ss = 0;
            foreach (var v in Results) ss += ((v - mean) * (v - mean));

            var var = ss / (Results.Count - 1);
            var sd = (float)Math.Sqrt(var);

            Result = new(mean, sd);
        }

        double GetTrueMean()
        {   
            var dist = Distribution.Selected;
            Distribution.SetDefaultDistribution(Distribution.DistributionType.None);

            SimulateRandomSampleInput(0, 1);

            Distribution.SetDefaultDistribution(dist);

            var v = Results.Last();

            Results.RemoveAt(Results.Count - 1);

            return v;
        }

        public virtual void SimulateRandomSampleInput(double x = 0, int iterations = 30000)
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
        internal FloatWithError Ratio;
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

        public SpolarRecordMethodError(string name, double dcp, double errordcp, double ts, double errorts)
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
        public SpolarRecordMethodErrorGlob(string name, double a, double da, double b, double db) : base(name, a, da, b, db)
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
        public SpolarRecordMethodErrorID(string name, double a, double da, double b, double db) : base(name, a, da, b, db)
        {
            ApCoeff = new FloatWithError(-0.590884521921104, 0.12);
            AnpCoeff = new FloatWithError(1.3721067124117, 0.06);
            Ratio = new FloatWithError(0.919743, 0.030589);
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

    public class FTSRMethod : SpolarRecordMethodError
    {
        public static Tuple<double, double> RatioParameterLimits { get; } = new Tuple<double, double>(0.59, 0.919743);

        public FTSRMethod(string name, double a, double da, double b, double db) : base(name, a, da, b, db)
        {
            ApCoeff = new FloatWithError(-0.590884521921104, 0.12);
            AnpCoeff = new FloatWithError(1.3721067124117, 0.06);
            Ratio = new FloatWithError(0.919743, 0.030589);
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
}
