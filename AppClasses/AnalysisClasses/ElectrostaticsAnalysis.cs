using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppKit;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public static class ElectrostaticsAnalysisController
    {
        public static TerminationFlag TerminateAnalysisFlag { get; private set; } = new TerminationFlag();

        public static event EventHandler<TerminationFlag> AnalysisStarted;
        public static event EventHandler<Tuple<int, int, float>> IterationFinished;
        public static event EventHandler<Tuple<int, TimeSpan>> AnalysisFinished;

        public static int CalculationIterations { get; set; } = 10000;

        public static void ReportCalculationProgress(int iteration) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            IterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, CalculationIterations, iteration / (float)CalculationIterations));
        });
    }

    public class ElectrostaticsAnalysis
    {
        static Random Rand { get; } = new Random();

        AnalysisResult Result { get; set; }

        

        public ElectrostaticsAnalysis(AnalysisResult result)
        {
            Result = result;
        }

        public async Task Analyze()
        {
            var result = Fit();

            var results = new List<FitResult>();
            for (int i = 0; i < ElectrostaticsAnalysisController.CalculationIterations; i++)
            {
                results.Add(Fit(false));

                ElectrostaticsAnalysisController.ReportCalculationProgress(i + 1);

                if (ElectrostaticsAnalysisController.TerminateAnalysisFlag.Up) break;
            }


        }

        FitResult Fit(bool exact = true)
        {
            var x = Result.Solution.Solutions.Select(sol => BufferAttribute.GetIonicStrength(sol.Data)).ToArray();
            var y = Result.Solution.Solutions.Select(sol => exact ? sol.Parameters[Analysis2.ParameterType.Affinity1].Value : sol.Parameters[Analysis2.ParameterType.Affinity1].Sample(Rand)).ToArray();

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


