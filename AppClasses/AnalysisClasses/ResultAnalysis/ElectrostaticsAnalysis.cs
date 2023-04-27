using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppKit;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public class ElectrostaticsAnalysis : ResultAnalysis
    {
        public ElectrostaticsAnalysis(AnalysisResult result) : base(result)
        {

        }

        protected override void Calculate()
        {
            var result = Fit();

            var results = new List<FitResult>();
            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                results.Add(Fit(false));

                ResultAnalysisController.ReportCalculationProgress(i + 1);

                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            CompletedIterations = results.Count;
        }

        FitResult Fit(bool exact = true)
        {
            var x = Data.Solution.Solutions.Select(sol => BufferAttribute.GetIonicStrength(sol.Data)).ToArray();
            var y = Data.Solution.Solutions.Select(sol => exact ? sol.Parameters[Analysis2.ParameterType.Affinity1].Value : sol.Parameters[Analysis2.ParameterType.Affinity1].Sample(Rand)).ToArray();

            var fit = MathNet.Numerics.Fit.Curve(x, y, (kd0, z, x) => kd0 * Math.Exp(-0.51 * z * Math.Sqrt(x) / (1 + Math.Sqrt(x))), 0.000001, 0);

            return new FitResult(fit.P0, fit.P1);
        }

        class FitResult : Tuple<double,double>
        {
            public FitResult(double v1, double v2): base(v1,v2)
            {

            }
        }
        //log(Kd/Kd0) = -0.51z1z2*(sqrt(I))/(1 + sqrt(I)*a)
    }
}


