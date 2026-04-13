using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace AnalysisITC.AppClasses.AnalysisClasses
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

                        if (sol.Data.Attributes.Exists(att => att.Key == AttributeKey.Salt))
                            ions = 1000 * sol.Data.Attributes.Find(att => att.Key == AttributeKey.Salt).ParameterValue;

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
            CalculateIonTransfer();

            CalculateIonicStrengthDependence();

            Calculated = true;
        }

        void CalculateIonTransfer()
        {
            AppEventHandler.PrintAndLog("Performing Ion Release Analysis...");

            var dps = new List<(double x, FloatWithError y)>();

            foreach (var sol in Data.Solution.Solutions)
            {
                var activity = SaltAttribute.GetIonActivity(sol.Data);
                var affinity = sol.ReportParameters[ParameterType.Affinity1];

                if (!double.IsFinite(activity) || activity <= 0) continue;
                if (!double.IsFinite(affinity.Value) || affinity.Value <= 0) continue;

                var x = Math.Log(activity);
                var y = FWEMath.Log(affinity);

                if (!double.IsFinite(x) || !double.IsFinite(y.Value)) continue;

                dps.Add((x, y));
            }

            if (dps.Count < 3)
            {
                CounterIonRelease = FloatWithError.NaN;
                CounterIonReleaseFit = null;
                return;
            }

            var result = FitLinear(dps.Select(dp => dp.Item1).ToArray(), dps.Select(dp => dp.Item2.Value).ToArray());
            var results = new List<LinearFit>();

            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var _dps = GetErrorData(dps);

                results.Add(FitLinear(_dps.Select(dp => dp.Item1).ToArray(), _dps.Select(dp => dp.Item2).ToArray()));

                ResultAnalysisController.ReportCalculationProgress(i + 1, description: "Ion Transfer");
                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            var slope = new FloatWithError(results.Select(r => r.Slope), result.Slope);
            var intercept = new FloatWithError(results.Select(r => r.Intercept), result.Intercept);

            CounterIonRelease = slope;

            CounterIonReleaseFit = new LinearFitWithError(slope, intercept, 0);
        }

        void CalculateIonicStrengthDependence()
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
                IonicStrengthDependenceFit = null;
                CompletedIterations = 0;
                return;
            }

            var point = IonicStrengthDependence.FitIonicStrengthDependence(
                dps.Select(dp => dp.Item1).ToArray(),
                dps.Select(dp => dp.Item2.Value).ToArray());

            if (point == null)
            {
                IonicStrengthDependenceFit = null;
                CompletedIterations = 0;
                return;
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

            IonicStrengthDependenceFit = new IonicStrengthDependenceFit(kd0, sensitivity, curvature, point.UsesCurvature);

            CompletedIterations = results.Count;

            AppEventHandler.PrintAndLog($"Kd0 = {kd0}", 1);
            AppEventHandler.PrintAndLog($"sensitivity = {sensitivity}", 1);
            AppEventHandler.PrintAndLog($"curvature = {curvature}", 1);
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

