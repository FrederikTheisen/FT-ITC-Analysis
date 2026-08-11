using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
    public class ElectrostaticsAnalysis : AdvancedAnalysis
    {
        public bool Calculated { get; private set; } = false;
        public Energy ElectrostaticStrength { get; private set; } = new(0);
        public FloatWithError CounterIonRelease { get; private set; } = FloatWithError.NaN;
        public FloatWithError Kd0 => IonicStrengthDependenceFit?.Kd0 ?? FloatWithError.NaN;
        public FloatWithError KdInf { get; private set; } = FloatWithError.NaN;

        public IonicStrengthDependenceFit IonicStrengthDependenceFit { get; private set; } = null;
        public LinearFitWithError CounterIonReleaseFit { get; private set; } = null;
        public int CounterIonReleaseIterations { get; private set; }

        public ElectrostaticsAnalysis(AnalysisResult result) : base(result)
        {
        }

        public List<Tuple<double, FloatWithError>> GetDataPoints(DissocFitMode mode)
        {
            var dataPoints = new List<Tuple<double, FloatWithError>>();

            switch (mode)
            {
                case DissocFitMode.AffinityVsSalt: // Displace a bit around to show all points
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        double ions = 0.0;

                        ions = 1000 * sol.Data.Attributes.Find(att => att.Key == AttributeKey.Salt)?.ParameterValue ?? 0.0;

                        var kd = sol.ReportParameters[ParameterType.Affinity1];
                        dataPoints.Add(new Tuple<double, FloatWithError>(ions, kd));
                    }
                    break;
                case DissocFitMode.CounterIonRelease: // Join data points
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        if (SaltAttribute.GetIonActivity(sol.Data) > 0)
                            dataPoints.Add(new Tuple<double, FloatWithError>(
                                Math.Log(SaltAttribute.GetIonActivity(sol.Data)),
                                FWEMath.Log(sol.ReportParameters[ParameterType.Affinity1])));
                    }
                    break;
                case DissocFitMode.DebyeHuckel: // Join data points
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        dataPoints.Add(new Tuple<double, FloatWithError>(
                            BufferAttribute.GetIonicStrength(sol.Data),
                            sol.ReportParameters[ParameterType.Affinity1]));
                    }
                    break;
            }

            return dataPoints;
        }

        protected override void Calculate()
        {
            var ionTransfer = CalculateIonTransfer();
            if (ResultAnalysisController.TerminateAnalysisFlag.Up) return;

            var ionicStrength = CalculateIonicStrengthDependence();
            if (ResultAnalysisController.TerminateAnalysisFlag.Up) return;

            CounterIonRelease = ionTransfer.CounterIonRelease;
            CounterIonReleaseFit = ionTransfer.Fit;
            CounterIonReleaseIterations = ionTransfer.Iterations;
            IonicStrengthDependenceFit = ionicStrength.Fit;
            CompletedIterations = ionicStrength.Iterations;

            Calculated = true;
        }

        IonTransferResult CalculateIonTransfer()
        {
            AppEventHandler.PrintAndLog("Performing Ion Release Analysis...");

            var dps = new List<(double x, FloatWithError y)>();

            foreach (var sol in Data.Solution.Solutions)
            {
                var activity = SaltAttribute.GetIonActivity(sol.Data);
                var affinity = sol.ReportParameters[ParameterType.Affinity1];

                if (!FWEMath.IsFinite(activity) || activity <= 0) continue;
                if (!FWEMath.IsFinite(affinity.Value) || affinity.Value <= 0) continue;

                var x = Math.Log(activity);
                var y = FWEMath.Log(affinity);

                if (!FWEMath.IsFinite(x) || !FWEMath.IsFinite(y.Value)) continue;

                dps.Add((x, y));
            }

            if (dps.Count < 3)
            {
                return new IonTransferResult(FloatWithError.NaN, null, 0);
            }

            var result = FitLinear(dps.Select(dp => dp.Item1).ToArray(), dps.Select(dp => dp.Item2.Value).ToArray());
            var results = new List<LinearFit>();

            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var _dps = GetErrorData(dps);

                var fitted = FitLinear(_dps.Select(dp => dp.Item1).ToArray(), _dps.Select(dp => dp.Item2).ToArray());
                if (fitted != null) results.Add(fitted);

                ResultAnalysisController.ReportCalculationProgress(i + 1, description: "Ion Transfer");
                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            var slope = new FloatWithError(results.Select(r => r.Slope), result.Slope);
            var intercept = new FloatWithError(results.Select(r => r.Intercept), result.Intercept);

            return new IonTransferResult(slope, new LinearFitWithError(slope, intercept, 0), results.Count);
        }

        IonicStrengthResult CalculateIonicStrengthDependence()
        {
            AppEventHandler.PrintAndLog("Performing Electrostatics Analysis...");

            var dps = new List<(double, FloatWithError)>();

            foreach (var sol in Data.Solution.Solutions)
            {
                double ionicStrength = BufferAttribute.GetIonicStrength(sol.Data);
                var affinity = sol.ReportParameters[ParameterType.Affinity1];

                dps.Add((ionicStrength, affinity));
            }

            if (dps.Count < 3)
            {
                return new IonicStrengthResult(null, 0);
            }

            var point = IonicStrengthDependence.FitIonicStrengthDependence(
                dps.Select(dp => dp.Item1).ToArray(),
                dps.Select(dp => dp.Item2.Value).ToArray());

            if (point == null)
            {
                return new IonicStrengthResult(null, 0);
            }

            var results = new List<IonicStrengthDependence>();

            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var _dps = GetErrorData(dps);

                var r = IonicStrengthDependence.FitIonicStrengthDependence(
                    _dps.Select(dp => dp.Item1).ToArray(),
                    _dps.Select(dp => dp.Item2).ToArray());

                if (r != null) results.Add(r);

                ResultAnalysisController.ReportCalculationProgress(i + 1, description: "Salt Dependence");
                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            // Fall back to point estimate if bootstrap failed completely
            var kd0 = results.Count > 0
                ? new FloatWithError(results.Select(r => r.Kd0), point.Kd0)
                : new FloatWithError(point.Kd0);

            var sensitivity = results.Count > 0
                ? new FloatWithError(results.Select(r => r.SaltSensitivity), point.SaltSensitivity)
                : new FloatWithError(point.SaltSensitivity);

            var curvature = results.Count > 0
                ? new FloatWithError(results.Select(r => r.Curvature), point.Curvature)
                : new FloatWithError(point.Curvature);

            var fit = new IonicStrengthDependenceFit(kd0, sensitivity, curvature, point.UsesCurvature);

            AppEventHandler.PrintAndLog($"Kd0 = {kd0}", 1);
            AppEventHandler.PrintAndLog($"sensitivity = {sensitivity}", 1);
            AppEventHandler.PrintAndLog($"curvature = {curvature}", 1);
            return new IonicStrengthResult(fit, results.Count);
        }

        protected override object CaptureCommittedState() => new CommittedState
        {
            Calculated = Calculated,
            CounterIonRelease = CounterIonRelease,
            CounterIonReleaseFit = CounterIonReleaseFit,
            CounterIonReleaseIterations = CounterIonReleaseIterations,
            IonicStrengthDependenceFit = IonicStrengthDependenceFit,
        };

        protected override void RestoreCommittedState(object state)
        {
            var previous = state as CommittedState;
            Calculated = previous?.Calculated == true;
            CounterIonRelease = previous?.CounterIonRelease ?? FloatWithError.NaN;
            CounterIonReleaseFit = previous?.CounterIonReleaseFit;
            CounterIonReleaseIterations = previous?.CounterIonReleaseIterations ?? 0;
            IonicStrengthDependenceFit = previous?.IonicStrengthDependenceFit;
        }

        internal void RestoreResult(
            IonicStrengthDependenceFit ionicStrengthFit,
            LinearFitWithError counterIonReleaseFit,
            int ionicStrengthIterations,
            int counterIonReleaseIterations,
            DateTime? completedAtUtc,
            ErrorEstimationMethod errorMethod)
        {
            IonicStrengthDependenceFit = ionicStrengthFit;
            CounterIonReleaseFit = counterIonReleaseFit;
            CounterIonRelease = counterIonReleaseFit?.Slope ?? FloatWithError.NaN;
            CounterIonReleaseIterations = counterIonReleaseIterations;
            Calculated = true;
            RestoreRunMetadata(ionicStrengthIterations, completedAtUtc, errorMethod);
        }

        sealed class CommittedState
        {
            public bool Calculated { get; set; }
            public FloatWithError CounterIonRelease { get; set; }
            public LinearFitWithError CounterIonReleaseFit { get; set; }
            public int CounterIonReleaseIterations { get; set; }
            public IonicStrengthDependenceFit IonicStrengthDependenceFit { get; set; }
        }

        readonly struct IonTransferResult
        {
            public IonTransferResult(FloatWithError counterIonRelease, LinearFitWithError fit, int iterations)
            { CounterIonRelease = counterIonRelease; Fit = fit; Iterations = iterations; }
            public FloatWithError CounterIonRelease { get; }
            public LinearFitWithError Fit { get; }
            public int Iterations { get; }
        }

        readonly struct IonicStrengthResult
        {
            public IonicStrengthResult(IonicStrengthDependenceFit fit, int iterations)
            { Fit = fit; Iterations = iterations; }
            public IonicStrengthDependenceFit Fit { get; }
            public int Iterations { get; }
        }

        LinearFit FitLinear(double[] x, double[] y)
        {
            try
            {
                var fit = MathNet.Numerics.Fit.Line(x, y);

                return new LinearFit(fit.B, fit.A, 0);
            }
            catch
            {
                return null;
            }
        }

        public enum DissocFitMode 
        {
            [Description("Affinity vs Salt")]
            AffinityVsSalt,
            [Description("Debye-Hückel")]
            DebyeHuckel,
            [Description("Counter Ion Release")]
            CounterIonRelease,
        }
    }
}
