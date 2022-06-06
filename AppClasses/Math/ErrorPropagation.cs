using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
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
}
