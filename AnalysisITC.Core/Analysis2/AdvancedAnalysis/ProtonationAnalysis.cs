using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Data;
using Buffer = AnalysisITC.Core.Data.Buffer;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
    public class ProtonationAnalysis : AdvancedAnalysis
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

                ResultAnalysisController.ReportCalculationProgress(i + 1, description: "Protonation Change");

                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            var bindingEnthalpy = new FloatWithError(results.Select(r => r.BindingEnthalpy), result.BindingEnthalpy).Energy;
            var protonationChange = new FloatWithError(results.Select(r => r.ProtonationChange), result.ProtonationChange);

            CompletedIterations = results.Count;
            BindingEnthalpy = bindingEnthalpy;
            ProtonationChange = protonationChange;
            base.Fit = new LinearFitWithError(protonationChange, bindingEnthalpy.FloatWithError, 0);
        }

        protected override object CaptureCommittedState() => new CommittedState
        {
            BindingEnthalpy = BindingEnthalpy,
            ProtonationChange = ProtonationChange,
            Fit = Fit,
        };

        protected override void RestoreCommittedState(object state)
        {
            var previous = state as CommittedState;
            BindingEnthalpy = previous?.BindingEnthalpy ?? new Energy(0);
            ProtonationChange = previous?.ProtonationChange ?? FloatWithError.NaN;
            Fit = previous?.Fit;
        }

        internal void RestoreResult(
            FloatWithError bindingEnthalpy,
            FloatWithError protonationChange,
            int completedIterations,
            DateTime? completedAtUtc,
            ErrorEstimationMethod errorMethod)
        {
            BindingEnthalpy = new Energy(bindingEnthalpy);
            ProtonationChange = protonationChange;
            Fit = new LinearFitWithError(protonationChange, bindingEnthalpy, 0);
            RestoreRunMetadata(completedIterations, completedAtUtc, errorMethod);
        }

        sealed class CommittedState
        {
            public Energy BindingEnthalpy { get; set; }
            public FloatWithError ProtonationChange { get; set; }
            public FitWithError Fit { get; set; }
        }

        FitResult Analyze(bool witherror = false)
        {
            var x = DataPoints.Select(dp => dp.Item1).ToArray();
            var y = DataPoints.Select(dp => witherror ? dp.Item2.Sample(Rand) : dp.Item2.Value).ToArray();

            // This model is linear. Direct least squares avoids the numerical
            // scaling/iteration failures of a nonlinear search when buffer
            // ionization enthalpies are tens of thousands of J/mol.
            var fit = MathNet.Numerics.Fit.Line(x, y);
            if (double.IsNaN(fit.Item1) || double.IsInfinity(fit.Item1)
                || double.IsNaN(fit.Item2) || double.IsInfinity(fit.Item2))
                throw new InvalidOperationException("Protonation analysis requires distinct finite buffer ionization enthalpies and finite binding enthalpies.");

            return new FitResult(fit.Item1, fit.Item2);
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
