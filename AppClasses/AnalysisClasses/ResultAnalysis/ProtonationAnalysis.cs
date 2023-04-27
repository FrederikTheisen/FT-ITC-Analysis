using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppKit;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public static class ProtonationAnalysisController
    {
        public static TerminationFlag TerminateAnalysisFlag { get; private set; } = new TerminationFlag();

        public static event EventHandler<TerminationFlag> AnalysisStarted;
        public static event EventHandler<Tuple<int, int, float>> IterationFinished;
        public static event EventHandler<Tuple<int, TimeSpan>> AnalysisFinished;

        public static int CalculationIterations { get; set; } = 1000;

        public static void ReportCalculationProgress(int iteration) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            IterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, CalculationIterations, iteration / (float)CalculationIterations));
        });

        public static async void AnalyzeProtonation(AnalysisResult datafit)
        {
            TerminateAnalysisFlag.Lower();
            DateTime start = DateTime.Now;

            AnalysisStarted?.Invoke(null, TerminateAnalysisFlag);

            var analysis = new ProtonationAnalysis(datafit);

            await analysis.Calculate();

            AnalysisFinished?.Invoke(analysis, new Tuple<int, TimeSpan>(analysis.CompletedIterations, DateTime.Now - start));
        }
    }

    public class ProtonationAnalysis : AnalysisITC.AppClasses.AnalysisClasses.ResultAnalysis.ResultAnalysis
    {
        public Energy BindingEnthalpy { get; set; }
        public FloatWithError ProtonationChange { get; set; }

        public ProtonationAnalysis(AnalysisResult result) : base(result)
        {
            DataPoints = new List<Tuple<double, FloatWithError>>();

            foreach (var sol in Result.Solution.Solutions)
            {
                DataPoints.Add(new Tuple<double, FloatWithError>(
                    ((Buffer)sol.Data.ExperimentOptions.Find(att => att.Key == ModelOptionKey.Buffer).IntValue).GetProtonationEnthalpy(sol.Temp),
                    new FloatWithError(sol.TotalEnthalpy)));
            }
        }

        public override async Task Calculate()
        {
            var result = Fit();
            var results = new List<FitResult>();
            for (int i = 0; i < ElectrostaticsAnalysisController.CalculationIterations; i++)
            {
                results.Add(Fit(witherror: true));

                ElectrostaticsAnalysisController.ReportCalculationProgress(i + 1);

                if (ElectrostaticsAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            CompletedIterations = results.Count;

            BindingEnthalpy = new FloatWithError(results.Select(r => r.BindingEnthalpy), result.BindingEnthalpy).Energy;
            ProtonationChange = new FloatWithError(results.Select(r => r.ProtonationChange), result.ProtonationChange);

            LinearFitWithError = new LinearFitWithError(ProtonationChange, BindingEnthalpy.FloatWithError, 0);
        }

        FitResult Fit(bool witherror = false)
        {
            var x = DataPoints.Select(dp => dp.Item1).ToArray();
            var y = DataPoints.Select(dp => witherror ? dp.Item2.Sample(Rand) : dp.Item2.Value).ToArray();

            var fit = MathNet.Numerics.Fit.Curve(x, y, (dHbind, n, x) => dHbind - n * x, y.Average(), 0);

            return new FitResult(fit.P0, fit.P1);
        }

        class FitResult : Tuple<double, double>
        {
            public double BindingEnthalpy => Item1;
            public double ProtonationChange => Item2;

            public FitResult(double v1, double v2) : base(v1, v2)
            {

            }
        }
    }
}

