using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppKit;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public class ProtonationAnalysis : AnalysisITC.AppClasses.AnalysisClasses.AdvancedAnalysis
    {
        public Energy BindingEnthalpy { get; set; }
        public FloatWithError ProtonationChange { get; set; }

        public ProtonationAnalysis(AnalysisResult result) : base(result)
        {
            DataPoints = new List<Tuple<double, FloatWithError>>();

            foreach (var sol in Data.Solution.Solutions)
            {
                DataPoints.Add(new Tuple<double, FloatWithError>(
                    ((Buffer)sol.Data.Attributes.Find(att => att.Key == AttributeKey.Buffer).IntValue).GetProtonationEnthalpy(sol.Temp),
                    sol.TotalEnthalpy));
            }
        }

        protected override void Calculate()
        {
            var result = Analyze();
            var results = new List<FitResult>();
            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                results.Add(Analyze(witherror: true));

                ResultAnalysisController.ReportCalculationProgress(i + 1);

                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            CompletedIterations = results.Count;

            BindingEnthalpy = new FloatWithError(results.Select(r => r.BindingEnthalpy), result.BindingEnthalpy).Energy;
            ProtonationChange = new FloatWithError(results.Select(r => r.ProtonationChange), result.ProtonationChange);

            base.Fit = new LinearFitWithError(ProtonationChange, BindingEnthalpy.FloatWithError, 0);
        }

        FitResult Analyze(bool witherror = false)
        {
            var x = DataPoints.Select(dp => dp.Item1).ToArray();
            var y = DataPoints.Select(dp => witherror ? dp.Item2.Sample(Rand) : dp.Item2.Value).ToArray();

            var fit = MathNet.Numerics.Fit.Curve(x, y, (dHbind, n, x) => dHbind + n * x, y.Average(), 0);

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

