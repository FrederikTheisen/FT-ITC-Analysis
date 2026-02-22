using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AppKit;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public class ElectrostaticsAnalysis : AdvancedAnalysis
    {
        public DissocFitMode Mode { get; private set; } = DissocFitMode.DebyeHuckel;

        public bool Calculated { get; private set; } = false;
        public Energy ElectrostaticStrength { get; private set; } = new(0);
        public FloatWithError CounterIonRelease { get; private set; } = new(0);
        public FloatWithError Kd0 { get; private set; } = new(0);
        public FloatWithError KdInf { get; private set; } = new(0);

        public DebyeHuckelFit DebyeHuckelFit { get; private set; } = null;
        public LinearFitWithError CounterIonReleaseFit { get; private set; } = null;

        public ElectrostaticsAnalysis(AnalysisResult result) : base(result)
        {
            DataPoints = new List<Tuple<double, FloatWithError>>();

            switch (Mode)
            {
                case DissocFitMode.CounterIonRelease:
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        if (SaltAttribute.GetIonActivity(sol.Data) > 0)
                            DataPoints.Add(new Tuple<double, FloatWithError>(
                                Math.Log(SaltAttribute.GetIonActivity(sol.Data)),
                                FWEMath.Log(sol.Parameters[Analysis2.ParameterType.Affinity1])));
                    }
                    break;
                default:
                case DissocFitMode.DebyeHuckel:
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        DataPoints.Add(new Tuple<double, FloatWithError>(
                            BufferAttribute.GetIonicStrength(sol.Data),
                            sol.ReportParameters[Analysis2.ParameterType.Affinity1]));
                    }
                    break;
            }
        }

        public void SetMode(DissocFitMode mode)
        {
            Mode = mode;

            DataPoints.Clear();

            switch (Mode)
            {
                case DissocFitMode.CounterIonRelease:
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        if (SaltAttribute.GetIonActivity(sol.Data) > 0)
                            DataPoints.Add(new Tuple<double, FloatWithError>(
                                Math.Log(SaltAttribute.GetIonActivity(sol.Data)),
                                FWEMath.Log(sol.Parameters[Analysis2.ParameterType.Affinity1])));
                    }
                    break;
                default:
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        DataPoints.Add(new Tuple<double, FloatWithError>(
                            BufferAttribute.GetIonicStrength(sol.Data),
                            sol.ReportParameters[Analysis2.ParameterType.Affinity1]));
                    }
                    break;
            }
        }

        protected override void Calculate()
        {
            CalculateIonTransfer();

            CalculateIonicStrengthDependence();

            Calculated = true;
        }

        void CalculateIonTransfer()
        {
            var dps = new List<Tuple<double, FloatWithError>>();

            foreach (var sol in Data.Solution.Solutions)
            {
                if (SaltAttribute.GetIonActivity(sol.Data) > 0)
                    dps.Add(new Tuple<double, FloatWithError>(
                        Math.Log(SaltAttribute.GetIonActivity(sol.Data)),
                        FWEMath.Log(sol.Parameters[Analysis2.ParameterType.Affinity1])));
            }

            var result = FitLinear(dps.Select(dp => dp.Item1).ToArray(), dps.Select(dp => dp.Item2.Value).ToArray());
            var results = new List<LinearFit>();

            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var _dps = GetErrorData(dps);

                results.Add(FitLinear(_dps.Select(dp => dp.Item1).ToArray(), _dps.Select(dp => dp.Item2).ToArray()));

                ResultAnalysisController.ReportCalculationProgress(i + 1);
                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            var slope = new FloatWithError(results.Select(r => r.Slope), result.Slope);
            var intercept = new FloatWithError(results.Select(r => r.Intercept), result.Intercept);

            CounterIonRelease = slope;

            CounterIonReleaseFit = new LinearFitWithError(slope, intercept, 0);
        }

        void CalculateIonicStrengthDependence()
        {
            var dps = new List<Tuple<double, FloatWithError>>();

            foreach (var sol in Data.Solution.Solutions)
            {
                dps.Add(new Tuple<double, FloatWithError>(
                    BufferAttribute.GetIonicStrength(sol.Data),
                    sol.ReportParameters[Analysis2.ParameterType.Affinity1]));
            }

            var result = FitDebyeHuckel(dps.Select(dp => dp.Item1).ToArray(), dps.Select(dp => dp.Item2.Value).ToArray(), dps.Min(dp => dp.Item2));
            var results = new List<DebyeHuckel>();

            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var _dps = GetErrorData(dps);

                var r = FitDebyeHuckel(_dps.Select(dp => dp.Item1).ToArray(), _dps.Select(dp => dp.Item2).ToArray(), result.Kd0, result.ZZ);

                if (r != null) results.Add(r);

                ResultAnalysisController.ReportCalculationProgress(i + 1);
                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            var zz = new FloatWithError(results.Select(r => r.ZZ), result.ZZ);

            Kd0 = new(results.Select(r => r.Kd0), result.Kd0);
            KdInf = Kd0 * FWEMath.Exp(-0.51 * zz);

            DebyeHuckelFit = new DebyeHuckelFit(Kd0, zz);
            CompletedIterations = results.Count;
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

        DebyeHuckel FitDebyeHuckel(double[] x, double[] y, double guesskd0, double guessz = 0)
        {
            try
            {
                var fit = MathNet.Numerics.Fit.Curve(x, y,
                            (kd0, z, x) => kd0 * Math.Exp(-0.51 * z * Math.Sqrt(x) / (1 + Math.Sqrt(x))),
                            guesskd0, guessz, tolerance: 1.0E-14, 30000);
                return new DebyeHuckel(fit.P0, fit.P1);
            }
            catch { return null; }
        }

        public void FitElectrostaticStrength()
        {
            return;

            var dps = new List<Tuple<double, FloatWithError>>();

            foreach (var sol in Data.Solution.Solutions)
            {
                var kdkd0 = sol.ReportParameters[Analysis2.ParameterType.Affinity1] / (Fit as ElectrostaticsFit).Kd0;

                dps.Add(new Tuple<double, FloatWithError>(
                    Math.Sqrt(BufferAttribute.GetIonicStrength(sol.Data)),
                    new(Math.Log(kdkd0), Math.Log(kdkd0) * kdkd0.FractionSD)));
            }

            var results = new List<double>();
            var x = dps.Select(dp => dp.Item1).ToArray();

            for (int i = 0; i < 1000; i++)
            {
                var y = dps.Select(dp => dp.Item2.Sample(Rand)).ToArray();

                var (inter, slope) = MathNet.Numerics.Fit.Line(x, y);

                results.Add(slope * Energy.R * (273.15 + Data.Solution.MeanTemperature));

                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            ElectrostaticStrength = new Energy(new(results));
        }

        class DebyeHuckel
        {
            public double[] Pars { get; private set; } = null;

            public double Kd0 => Pars[0];
            public double ZZ => Pars[1];

            public DebyeHuckel(double kd0, double zza)
            {
                Pars = new[] { kd0, zza };
            }
        }

        public enum DissocFitMode 
        {
            [Description("Debye-Hückel")]
            DebyeHuckel,
            [Description("Counter Ion Release")]
            CounterIonRelease,
        }
    }
}


