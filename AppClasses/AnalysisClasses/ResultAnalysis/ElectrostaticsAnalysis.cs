using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AppKit;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public class ElectrostaticsAnalysis : ResultAnalysis
    {
        public DissocFitMode Model { get; set; } = DissocFitMode.ExponentialDecay;
        public Energy ElectrostaticStrength { get; private set; } = new(0);

        public ElectrostaticsAnalysis(AnalysisResult result) : base(result)
        {
            DataPoints = new List<Tuple<double, FloatWithError>>();

            foreach (var sol in Data.Solution.Solutions)
            {
                DataPoints.Add(new Tuple<double, FloatWithError>(
                    BufferAttribute.GetIonicStrength(sol.Data),
                    sol.ReportParameters[Analysis2.ParameterType.Affinity1]));
            }
        }

        protected override void Calculate()
        {
            var result = FitModel();

            var results = new List<FitResult>();
            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var r = FitModel(witherror: true, result);

                if (r != null) results.Add(r);

                ResultAnalysisController.ReportCalculationProgress(i + 1);

                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            CompletedIterations = results.Count;

            switch (Model)
            {
                default:
                case DissocFitMode.DebyeHuckel:
                    Fit = new DebyeHuckelFit(
                        new FloatWithError(results.Select(r => r.Kd0), result.Kd0),
                        new FloatWithError(results.Select(r => r.Pars[1]), result.Pars[1]));
                    break;
                case DissocFitMode.ExponentialDecay:
                    Fit = new SingleExponentialDecayFit(
                        new FloatWithError(results.Select(r => r.Kd0), result.Kd0),
                        new FloatWithError(results.Select(r => r.Pars[1]), result.Pars[1]),
                        new FloatWithError(results.Select(r => r.Pars[2]), result.Pars[2]));
                    break;
            }

            FitElectrostaticStrength();
        }

        FitResult FitModel(bool witherror = false, FitResult initial = null)
        {
            var x = DataPoints.Select(dp => dp.Item1).ToArray();
            var y = DataPoints.Select(dp => witherror ? dp.Item2.Sample(Rand) : dp.Item2.Value).ToArray();

            switch (Model)
            {
                case DissocFitMode.DebyeHuckel when initial != null: return FitDebyeHuckel(x, y, initial.Kd0, initial.Pars[1]);
                case DissocFitMode.ExponentialDecay when initial != null: return FitExpDecay(x, y, initial.Kd0, initial.Pars[1], initial.Pars[2], 1000);
                default:
                case DissocFitMode.DebyeHuckel: return FitDebyeHuckel(x, y, y.Average(), 0);
                case DissocFitMode.ExponentialDecay: return FitExpDecay(x, y, y.Min(), y.Max(), 35);
            }
        }

        FitResult FitDebyeHuckel(double[] x, double[] y, double guesskd0, double guessz = 0)
        {
            try
            {
                var fit = MathNet.Numerics.Fit.Curve(x, y,
                            (kd0, z, x) => kd0 * Math.Exp(-0.51 * z * Math.Sqrt(x) / (1 + Math.Sqrt(x))),
                            guesskd0, guessz, tolerance: 1.0E-14, 30000);
                return new FitResult(fit.P0, fit.P1);
            }
            catch { return null; }
        }

        FitResult FitExpDecay(double[] x, double[] y, double guesskd0, double guessplateau, double guessk = 10, int iterations = 100000)
        {
            try
            {
                var exp = MathNet.Numerics.Fit.Curve(x, y,
                            (kd0, Plateau, K, x) => (kd0 - Plateau) * Math.Exp(-K * x) + Plateau,
                            guesskd0, guessplateau, guessk, tolerance: 1.0E-14, iterations);
                return new FitResult(exp.P0, exp.P1, exp.P2);
            }
            catch { return null; }
        }

        public void FitElectrostaticStrength()
        {
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
            }

            ElectrostaticStrength = new Energy(new(results));
        }

        class FitResult
        {
            public double[] Pars { get; private set; } = null;

            public double Kd0 => Pars[0];

            public FitResult(double kd0, double zza)
            {
                Pars = new[] { kd0, zza };
            }

            public FitResult(double kd0, double plateau, double k)
            {
                Pars = new[] { kd0, plateau, k };
            }
        }

        public enum DissocFitMode 
        {
            [Description("Debye-Hückel")]
            DebyeHuckel,
            [Description("Exponential Decay")]
            ExponentialDecay,
        }
    }
}


