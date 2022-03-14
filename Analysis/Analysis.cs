using System;
using DataReaders;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.Optimization;
using MathNet.Numerics.LinearAlgebra;
using Accord.Statistics.Models.Regression;
using Accord.Math.Optimization;
using Accord.Math;

namespace AnalysisITC
{
    public static class GlobalAnalyzer
    {

    }

    public class Analyzer
    {
        public ExperimentData Data { get; set; }

        public float T;

        public Analyzer(ExperimentData data)
        {
            Data = data;

            
        }

        public void Test()
        {
            //foreach (var inj in Data.Injections)
            //{
            //    Console.WriteLine(inj.Ratio + " " + inj.Enthalpy);
            //}

            Console.WriteLine("####################");

            var model = new OneSetOfSites(Data);

            var f = new NonlinearObjectiveFunction(4, (w) => model.Loss(w[0], w[1], w[2], w[3]));
            var solver = new NelderMead(f);
            solver.StepSize[0] = 0.00001;
            solver.StepSize[1] = .1;
            solver.StepSize[2] = .1;
            solver.StepSize[3] = .1;
            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(4)
            {
                MaximumEvaluations = 30000,
                RelativeFunctionTolerance = 0.000000000001
            };

            solver.Minimize(new double[4] { Data.Injections.Last().Ratio, Data.Injections.First(inj => inj.Include).Enthalpy, 1000000, -Data.Injections.Last(inj => inj.Include).Enthalpy });

            foreach (var inj in Data.Injections)
            {
                Console.WriteLine(inj.Enthalpy + " " + model.Evaluate(inj.ID, solver.Solution[0], solver.Solution[1], solver.Solution[2], solver.Solution[3])/inj.InjectionMass);
            }
        }

        public void NelderMeadSolve()
        {
            
        }

        Solution NelderMeadAlogrithm(Model model)
        {
            var f = new NonlinearObjectiveFunction(4, (w) => model.Loss(w[0], w[1], w[2], w[3]));
            var solver = new NelderMead(f);
            solver.StepSize[0] = 0.00001;
            solver.StepSize[1] = .1;
            solver.StepSize[2] = .1;
            solver.StepSize[3] = .1;
            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(4)
            {
                MaximumEvaluations = 30000,
                RelativeFunctionTolerance = 0.000000000001
            };

            solver.Minimize(new double[4] { Data.Injections.Last().Ratio, Data.Injections.First(inj => inj.Include).Enthalpy, 1000000, -Data.Injections.Last(inj => inj.Include).Enthalpy });

            return Solution.FromAccordNelderMead(solver.Solution, Data);
        }

        public void NelderMeadError()
        {
            List<Solution> solutions = new List<Solution>();

            foreach (int i in Data.Injections.Where(inj => inj.Include).Select(inj => inj.ID))
            {
                var model = new OneSetOfSites(Data)
                {
                    ExcludeSinglePoint = i,
                };

                solutions.Add(NelderMeadAlogrithm(model));
            }


        }


        public void LM()
        {
            var f = new Accord.Statistics.Models.Regression.Fitting.NonlinearLeastSquares();
            f.Algorithm = new Accord.Math.Optimization.LevenbergMarquardt()
            {
                MaxIterations = 1000
                
            };

            var model = new OneSetOfSites(Data);

            f.Function = (p, i) => model.Evaluate((int)i[0], (float)p[0], (float)p[1], (float)p[2], (float)p[3]);
            f.StartValues = new double[4] { 1, -50000, 1000000, 0 };
            f.ComputeStandardErrors = false;
            f.Gradient = null;

            f.NumberOfParameters = 4;

            var input = Data.Injections.Where(inj => inj.Include).Select(inj => (double)inj.ID).ToArray().ToJagged();
            var results = Data.Injections.Where(inj => inj.Include).Select(inj => (double)inj.PeakArea).ToArray();


            var output = f.Learn(input, results);

            Console.WriteLine(output.Coefficients);

        }

        class Model
        {
            internal ExperimentData Experiment { get; private set; }
            public int ExcludeSinglePoint { get; set; } = -1;

            public Model(ExperimentData experiment)
            {
                Experiment = experiment;
            }

            public virtual double Loss(double n, double H, double K, double offset)
            {
                return 0;
            }

            public virtual double Evaluate(int i, double n, double H, double K, double offset)
            {
                return 0;
            }
        }

        class OneSetOfSites : Model
        {
            public OneSetOfSites(ExperimentData experiment) : base(experiment)
            {
            }

            public override double Loss(double n, double H, double K, double offset)
            {
                double loss = 0;

                foreach (var inj in Experiment.Injections.Where(i => i.Include && i.ID != ExcludeSinglePoint))
                {
                    var calc = Evaluate(inj.ID, n, H, K, offset);
                    var meas = inj.PeakArea;

                    loss += (calc - meas) * (calc - meas);
                }

                return loss;
            }

            public override double Evaluate(int i, double n, double H, double K, double offset)
            {
                return GetDeltaHeat(i, n, H, K) + offset * Experiment.Injections[i].InjectionMass;
            }

            public double GetDeltaHeat(int i, double n, double H, double K)
            {
                var inj = Experiment.Injections[i];

                var Qi = GetHeatContent(inj, n, H, K);
                var Q_i = 0.0;

                if (i != 0) Q_i = GetHeatContent(Experiment.Injections[i - 1], n, H, K);

                var dQi = Qi + (inj.Volume / Experiment.CellVolume) * ((Qi + Q_i) / 2) - Q_i;

                return dQi;
            }

            public double GetHeatContent(InjectionData inj, double n, double H, double K)
            {
                var first = (n * inj.ActualCellConcentration * H * Experiment.CellVolume) / 2;
                var XnM = inj.ActualTitrantConcentration / (n * inj.ActualCellConcentration);
                var nKM = 1 / (n * K * inj.ActualCellConcentration);
                var square = (1 + XnM + nKM);
                var root = (square*square) - 4 * XnM;

                return first * (1 + XnM + nKM - Math.Sqrt(root));
            }
        }


    }

    public class Solution
    {
        static readonly Energy R = new Energy(8.134f, EnergyUnit.Joule);

        ExperimentData Data;

        public Energy Enthalpy { get; private set; }
        public FloatWithError K { get; private set; }
        public FloatWithError N { get; private set; }
        public Energy Offset { get; private set; }

        float T => Data.MeasuredTemperature;

        public FloatWithError Kd => 1 / K;
        public Energy GibbsFreeEnergy => R * T * Math.Log((double)Kd);
        public Energy TdS => GibbsFreeEnergy - Enthalpy;
        public Energy Entropy => TdS / T;

        public static Solution FromAccordNelderMead(double[] parameters, ExperimentData data)
        {
            return new Solution()
            {
                N = parameters[0],
                Enthalpy = new Energy(parameters[1]),
                K = parameters[2],
                Offset = new Energy(parameters[3]),
                Data = data
            };
        }

        public static Solution SD(List<Solution> solutions)
        {

        }
    }

    public class GlobalSolution
    {

    }

}
