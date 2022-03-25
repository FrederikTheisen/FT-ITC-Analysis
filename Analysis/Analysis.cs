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
        static GlobalModel Model { get; set; }

        public static void Initialize()
        {
            Model = new GlobalModelFreeGibbs();

            foreach (var data in DataManager.Data)
            {
                Model.Models.Add(new OneSetOfSites(data));
            }
        }

        public static void Solve()
        {
            var solution = Model.SolvewithNelderMeadAlgorithm();

            foreach (var s in solution.Solutions)
            {
                Console.WriteLine(s.Data.FileName);
                s.PrintFit();
                Console.WriteLine("#################");
            }

            SetInitialValues();

            var individual_loss = Model.Models.Select(m => m.Solution.Loss);

            solution = Model.SolvewithNelderMeadAlgorithm();

            foreach (var s in solution.Solutions)
            {
                Console.WriteLine(s.Data.FileName);
                s.PrintFit();
                Console.WriteLine("#################");
            }

            var global_loss = solution.Solutions.Select(s => s.Loss);

            GlobalModel.Hstep *= .1;
            GlobalModel.Gstep *= .1;
            GlobalModel.Cstep *= .1;
            GlobalModel.Nstep *= .1;
            GlobalModel.Ostep *= .1;

            solution = Model.SolvewithNelderMeadAlgorithm();

            foreach (var s in solution.Solutions)
            {
                Console.WriteLine(s.Data.FileName);
                s.PrintFit();
                Console.WriteLine("#################");
            }

            var global_loss2 = solution.Solutions.Select(s => s.Loss);
        }

        static void SetInitialValues()
        {
            foreach (var m in Model.Models)
            {
                m.SolveWithNelderMeadAlogrithm();
            }

            Model.InitialOffsets = Model.Models.Select(m => (double)m.Solution.Offset).ToArray();
            Model.InitialNs = Model.Models.Select(m => (double)m.Solution.N).ToArray();
            if (Model is GlobalModelFreeGibbs) (Model as GlobalModelFreeGibbs).InitialGibbs = Model.Models.Select(m => (double)m.Solution.GibbsFreeEnergy).ToArray();
        }
    }

    public class Analyzer
    {
        public ExperimentData Data { get; set; }

        public Solution Solution { get; private set; }

        public float T;

        public Analyzer(ExperimentData data)
        {
            Data = data;
        }

        public void Test()
        {
            Console.WriteLine("####################");

            var model = new OneSetOfSites(Data);

            Solution = model.SolveWithNelderMeadAlogrithm();
        }

        public void SolveSimplex()
        {
            
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

                solutions.Add(model.SolveWithNelderMeadAlogrithm());
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

        public enum AnalysisModel
        {
            OneSetOfSites
        }
    }

    public class Model
    {
        internal ExperimentData Data { get; private set; }
        public int ExcludeSinglePoint { get; set; } = -1;

        public virtual double GuessN => Data.Injections.Last().Ratio / 2;
        public virtual double GuessH => Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset;
        public virtual double GuessK => 1000000;
        public virtual double GuessOffset => Data.Injections.Last(inj => inj.Include).Enthalpy;

        /// <summary>
        /// Solution parameters
        /// </summary>
        public Solution Solution { get; private set; }

        public Model()
        {

        }

        public Model(ExperimentData experiment)
        {
            Data = experiment;
        }

        public virtual double RMSD(double n, double H, double K, double offset)
        {
            return 0;
        }

        public virtual double Evaluate(int i, double n, double H, double K, double offset)
        {
            return 0;
        }

        public virtual Solution SolveWithNelderMeadAlogrithm()
        {
            var f = new NonlinearObjectiveFunction(4, (w) => this.RMSD(w[0], w[1], w[2], w[3]));
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

            solver.Minimize(new double[4] { GuessN, GuessH, GuessK, GuessOffset });

            Solution = Solution.FromAccordNelderMead(solver.Solution, this, solver.Function(solver.Solution));

            return Solution;
        }
    }

    class OneSetOfSites : Model
    {
        public OneSetOfSites()
        {

        }

        public OneSetOfSites(ExperimentData experiment) : base(experiment)
        {
        }

        public override double RMSD(double n, double H, double K, double offset)
        {
            double loss = 0;

            foreach (var inj in Data.Injections.Where(i => i.Include && i.ID != ExcludeSinglePoint))
            {
                var calc = Evaluate(inj.ID, n, H, K, offset);
                var meas = inj.PeakArea;

                loss += (calc - meas) * (calc - meas);
            }

            return Math.Sqrt(1000000 * loss);
        }

        public override double Evaluate(int i, double n, double H, double K, double offset)
        {
            return GetDeltaHeat(i, n, H, K) + offset * Data.Injections[i].InjectionMass;
        }

        public double GetDeltaHeat(int i, double n, double H, double K)
        {
            var inj = Data.Injections[i];

            var Qi = GetHeatContent(inj, n, H, K);
            var Q_i = 0.0;

            if (i != 0) Q_i = GetHeatContent(Data.Injections[i - 1], n, H, K);

            var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2) - Q_i;

            return dQi;
        }

        public double GetHeatContent(InjectionData inj, double n, double H, double K)
        {
            var first = (n * inj.ActualCellConcentration * H * Data.CellVolume) / 2;
            var XnM = inj.ActualTitrantConcentration / (n * inj.ActualCellConcentration);
            var nKM = 1 / (n * K * inj.ActualCellConcentration);
            var square = (1 + XnM + nKM);
            var root = (square * square) - 4 * XnM;

            return first * (1 + XnM + nKM - Math.Sqrt(root));
        }
    }

    public class GlobalModel
    {
        internal static double Hstep = 0.05;
        internal static double Gstep = 5;
        internal static double Cstep = 0.00001;
        internal static double Nstep = 0.01;
        internal static double Ostep = 0.01;

        public double[] InitialOffsets { get; set; }
        public double[] InitialNs { get; set; }

        public List<Model> Models { get; set; } = new List<Model>();

        public virtual int nVars => 3 + Models.Count * 2;

        internal IEnumerable<double> GetInitialNs() => InitialNs ?? Models.Select(m => m.GuessN);
        internal IEnumerable<double> GetInitialOffsets() => InitialOffsets ?? Models.Select(m => m.GuessOffset);
        internal double GuessGibbs() => (double)new Energy(-40000, EnergyUnit.Joule);

        internal double GuessHeatCapacity
        {
            get
            {
                var xy = Models.Select(m => new double[] { m.Data.MeasuredTemperature, m.GuessH }).ToArray();
                var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

                return reg.Item2;
            }
        }

        internal double GuessReferenceEnthalpy
        {
            get
            {
                var xy = Models.Select(m => new double[] { m.Data.MeasuredTemperature, m.GuessH }).ToArray();
                var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

                var H0 = reg.Item1 + 25 * reg.Item2;

                return H0;
            }
        }

        internal virtual double[] StartValues
        {
            get
            {
                var H0 = GuessReferenceEnthalpy;
                var Cp = GuessHeatCapacity;
                var G = GuessGibbs();

                var ns = GetInitialNs();
                var offsets = GetInitialOffsets();

                var w = new List<double>() { H0, Cp, G };
                w.AddRange(offsets);
                w.AddRange(ns);

                return w.ToArray();
            }
        }

        internal virtual double[] StepSizes
        {
            get
            {
                var stepsizes = new List<double>()
                {
                    Hstep,
                    Cstep,
                    Gstep
                };

                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Ostep);
                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Nstep);


                return stepsizes.ToArray();
            }
        }

        internal virtual double[] LowerBounds
        {
            get
            {
                var bounds = new List<double>()
                {
                    new Energy(-100000, EnergyUnit.Joule),
                    new Energy(-10000, EnergyUnit.Joule),
                    new Energy(-50000, EnergyUnit.Joule),
                };

                bounds.AddRange(new double[Models.Count].Add(-20000));
                bounds.AddRange(new double[Models.Count].Add(0.1));

                return bounds.ToArray();
            }
        }

        internal virtual double[] UpperBounds
        {
            get
            {
                var bounds = new List<double>()
                {
                    new Energy(100000, EnergyUnit.Joule),
                    new Energy(10000, EnergyUnit.Joule),
                    new Energy(-5000, EnergyUnit.Joule),
                };

                bounds.AddRange(new double[Models.Count].Add(20000));
                bounds.AddRange(new double[Models.Count].Add(10));

                return bounds.ToArray();
            }
        }

        public GlobalModel()
        {
            
        }

        void SetBounds(NelderMead solver)
        {
            var lb = LowerBounds;
            var ub = UpperBounds;

            for (int i = 0; i < nVars; i++)
            {
                solver.LowerBounds[i] = lb[i];
                solver.UpperBounds[i] = ub[i];
            }
        }

        public GlobalSolution SolvewithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(nVars, (w) => LossFunction(w));
            var solver = new NelderMead(f);

            var stepsizes = StepSizes;

            for (int i = 0; i < solver.StepSize.Length; i++) solver.StepSize[i] = stepsizes[i];

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(nVars)
            {
                MaximumEvaluations = 300000,
                RelativeFunctionTolerance = 0.00000000000000000001,
                RelativeParameterTolerance = 0.00000000000000000001,
            };

            SetBounds(solver);

            solver.Minimize(StartValues);

            return GlobalSolution.FromAccordNelderMead(solver.Solution, this);
        }

        internal virtual double LossFunction(double[] w)
        {
            //extracts parameters
            double H0 = w[0];
            double Cp = w[1];
            double G = w[2];
            double[] offsets = w.Skip(3).Take(Models.Count).ToArray();
            double[] ns = w.Skip(Models.Count + 3).Take(Models.Count).ToArray();

            return GlobalLoss(ns, H0, Cp, G, offsets);
        }

        double GlobalLoss(double[] n, double H0, double Cp, double G, double[] offset)
        {
            double glob_loss = 0;

            Energy dG = new Energy(G);
            Energy dH0 = new Energy(H0);
            Energy dCp = new Energy(Cp);

            for (int i = 0; i < Models.Count; i++)
            {
                //Calculate T specific parameters
                var m = Models[i];
                var T = m.Data.MeasuredTemperature + 273.15;
                var dt = T - 298.15;
                var H = dH0 + dCp * dt;
                var K = Math.Exp(-1 * dG / (Energy.R * T));

                glob_loss += m.RMSD(n[i], H, K, offset[i]);
            }

            return glob_loss;
        }
    }

    public class GlobalModelFreeGibbs : GlobalModel
    {
        public double[] InitialGibbs { get; set; }

        public override int nVars => 2 + Models.Count * 3;

        private double[] GetInitialGibbs() => InitialGibbs ?? new double[Models.Count].Add(GuessGibbs());

        internal override double[] StartValues
        {
            get
            {
                var H0 = GuessReferenceEnthalpy;
                var Cp = GuessHeatCapacity;

                var gs = GetInitialGibbs();
                var ns = GetInitialNs();
                var offsets = GetInitialOffsets();

                var w = new List<double>() { H0, Cp };
                w.AddRange(gs);
                w.AddRange(offsets);
                w.AddRange(ns);

                return w.ToArray();
            }
        }
        internal override double[] StepSizes
        {
            get
            {
                var stepsizes = new List<double>()
            {
                Hstep,
                Cstep,
            };

                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Gstep);
                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Ostep);
                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Nstep);


                return stepsizes.ToArray();
            }
        }

        internal override double[] LowerBounds
        {
            get
            {
                var bounds = new List<double>()
                {
                    new Energy(-100000, EnergyUnit.Joule),
                    new Energy(-10000, EnergyUnit.Joule),
                };

                bounds.AddRange(new double[Models.Count].Add(new Energy(-50000, EnergyUnit.Joule)));
                bounds.AddRange(new double[Models.Count].Add(-20000));
                bounds.AddRange(new double[Models.Count].Add(0.1));

                return bounds.ToArray();
            }
        }

        internal override double[] UpperBounds
        {
            get
            {
                var bounds = new List<double>()
                {
                    new Energy(100000, EnergyUnit.Joule),
                    new Energy(10000, EnergyUnit.Joule),
                };

                bounds.AddRange(new double[Models.Count].Add(new Energy(-5000, EnergyUnit.Joule)));
                bounds.AddRange(new double[Models.Count].Add(20000));
                bounds.AddRange(new double[Models.Count].Add(10));

                return bounds.ToArray();
            }
        }

        internal override double LossFunction(double[] w)
        {
            //extracts parameters
            double H0 = w[0];
            double Cp = w[1];
            double[] Gs = w.Skip(2).Take(Models.Count).ToArray();
            double[] offsets = w.Skip(2 + Models.Count).Take(Models.Count).ToArray();
            double[] ns = w.Skip(Models.Count + Models.Count + 2).Take(Models.Count).ToArray();

            return GlobalLoss(ns, H0, Cp, Gs, offsets);
        }

        double GlobalLoss(double[] n, double H0, double Cp, double[] Gs, double[] offset)
        {
            double glob_loss = 0;

            Energy dH0 = new Energy(H0);
            Energy dCp = new Energy(Cp);

            for (int i = 0; i < Models.Count; i++)
            {
                //Calculate T specific parameters
                var m = Models[i];
                var T = m.Data.MeasuredTemperature + 273.15;
                var dt = T - 298.15;
                var H = dH0 + dCp * dt;

                var dG = new Energy(Gs[i]);
                var K = Math.Exp(-1 * dG / (Energy.R * T));

                glob_loss += m.RMSD(n[i], H, K, offset[i]);
            }

            return glob_loss;
        }
    }

    public class Solution
    {
        static Energy R => Energy.R;

        public Model Model;

        public ExperimentData Data => Model.Data;

        public Energy Enthalpy { get; private set; }
        public FloatWithError K { get; private set; }
        public FloatWithError N { get; private set; }
        public Energy Offset { get; private set; }

        public double T => Data.MeasuredTemperature;
        double TK => T + 273.15;

        public FloatWithError Kd => new FloatWithError(1) / K;
        public Energy GibbsFreeEnergy => -1 * R * TK * Math.Log(K);
        public Energy TdS => GibbsFreeEnergy - Enthalpy;
        public Energy Entropy => TdS / TK;

        public double Loss { get; private set; }

        public Solution()
        {

        }

        public Solution(double n, Energy dH, double k, double offset, Model model, double loss)
        {
            N = new(n);
            Enthalpy = dH;
            K = new(k);
            Offset = new(offset);
            Model = model;
            Loss = loss;
        }

        public static Solution FromAccordNelderMead(double[] parameters, Model model, double loss)
        {
            return new Solution()
            {
                N = new(parameters[0]),
                Enthalpy = new Energy(parameters[1]),
                K = new(parameters[2]),
                Offset = new Energy(parameters[3]),
                Model = model,
                Loss = loss
            };
        }

        public void PrintFit()
        {
            foreach (var inj in Data.Injections)
            {
                Console.WriteLine(inj.Ratio + " " + inj.Enthalpy + " " + Model.Evaluate(inj.ID, N, Enthalpy, K, Offset) / inj.InjectionMass);
            }
        }

        public override string ToString()
        {
            return Loss.ToString();
        }
    }

    public class GlobalSolution
    {
        public double Loss { get; private set; } = 0;

        public Energy HeatCapacity { get; private set; }
        public Energy EnthalpyRef { get; private set; } //Enthalpy at 298.15 °C
        public Energy FreeEnergy { get; private set; }

        /// <summary>
        /// Parameters of the individual experiments derived from the global solution
        /// </summary>
        public List<Solution> Solutions { get; private set; } = new List<Solution>();

        public static GlobalSolution FromAccordNelderMead(double[] solution, GlobalModel model)
        {
            var global = new GlobalSolution();

            double[] ns = null;
            double[] gs = null;
            double[] offsets = null;

            int nmodels = model.Models.Count;

            if (model is GlobalModelFreeGibbs)
            {
                global.EnthalpyRef = new(solution[0]);
                global.HeatCapacity = new(solution[1]);

                gs = solution.Skip(2).Take(nmodels).ToArray();
                offsets = solution.Skip(2 + nmodels).Take(nmodels).ToArray();
                ns = solution.Skip(2 * nmodels + 2).Take(nmodels).ToArray();
            }
            else
            {
                global.EnthalpyRef = new(solution[0]);
                global.HeatCapacity = new(solution[1]);
                global.FreeEnergy = new(solution[2]);

                offsets = solution.Skip(3).Take(nmodels).ToArray();
                ns = solution.Skip(nmodels + 3).Take(model.nVars).ToArray();
                gs = new double[nmodels].Add(global.FreeEnergy);
            }

            for (int i = 0; i < model.Models.Count; i++)
            {
                var m = model.Models[i];
                

                var dt = m.Data.MeasuredTemperature - 25;

                var dH = global.EnthalpyRef + global.HeatCapacity * dt;
                var K = Math.Exp(gs[i] / (-1 * Energy.R * (m.Data.MeasuredTemperature + 273.15)));

                var sol = new Solution(ns[i], dH, K, offsets[i], m, m.RMSD(ns[i], dH, K, offsets[i]));

                global.Solutions.Add(sol);
            }

            global.Loss = model.LossFunction(solution);

            return global;
        }
    }

}
