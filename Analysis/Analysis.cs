using System;
using DataReaders;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MathNet.Numerics.Optimization;
using MathNet.Numerics.LinearAlgebra;
using Accord.Statistics.Models.Regression;
using Accord.Math.Optimization;
using Accord.Math;
using System.Threading.Tasks;
using Utilities;
using static Utilities.Increment;

namespace AnalysisITC
{
    public static class GlobalAnalyzer
    {
        public static event EventHandler<SolverConvergence> AnalysisFinished;

        static GlobalModel Model { get; set; }


        public static void InitializeAnalyzer(bool varEnthalpy, bool varAffinity = true)
        {
            Model = new GlobalModel();

            Model.UseVariableAffinity = varAffinity;
            Model.UseUnifiedAffinity = !varAffinity;
            Model.UseVariableEnthalpy = varEnthalpy;
        }

        static void InitializeOneSetOfSites()
        {
            foreach (var data in DataManager.Data)
            {
                Model.Models.Add(new OneSetOfSites(data));
            }
        }

        public static async void Solve(AnalysisModel analysismodel)
        {
            switch (analysismodel)
            {
                case AnalysisModel.OneSetOfSites: InitializeOneSetOfSites(); break;
                case AnalysisModel.SequentialBindingSites:
                case AnalysisModel.TwoSetsOfSites:
                case AnalysisModel.Dissociation:
                default: InitializeOneSetOfSites(); break;
            }

            SolverConvergence convergence = null;

            await Task.Run(() => convergence = Model.SolvewithNelderMeadAlgorithm());

            AnalysisFinished?.Invoke(null, convergence);
        }

        static void SetInitialValues()
        {
            foreach (var m in Model.Models)
            {
                m.SolveWithNelderMeadAlogrithm();
            }

            Model.InitialOffsets = Model.Models.Select(m => (double)m.Solution.Offset).ToArray();
            Model.InitialNs = Model.Models.Select(m => (double)m.Solution.N).ToArray();
            Model.InitialGibbs = Model.Models.Select(m => (double)m.Solution.GibbsFreeEnergy).ToArray();
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

    public enum AnalysisModel
    {
        OneSetOfSites,
        SequentialBindingSites,
        TwoSetsOfSites,
        Dissociation
    }

    public class Model
    {
        internal ExperimentData Data { get; private set; }
        public int ExcludeSinglePoint { get; set; } = -1;

        public virtual double GuessN => Data.Injections.Last().Ratio / 2;
        public virtual double GuessH => Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset;
        public virtual double GuessK => 1000000;
        public virtual double GuessGibbs => new Energy(-35000, EnergyUnit.Joule);
        public virtual double GuessOffset => Data.Injections.Last(inj => inj.Include).Enthalpy;

        /// <summary>
        /// Solution parameters
        /// </summary>
        public Solution Solution => Data.Solution;

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

            Data.Solution = Solution.FromAccordNelderMead(solver.Solution, this, solver.Function(solver.Solution));

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

            return Math.Sqrt(1000000000000 * loss);
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
        internal static double Hstep = 1;
        internal static double Gstep = 1;
        internal static double Cstep = 0.1;
        internal static double Nstep = 0.01;
        internal static double Ostep = 0.1;

        public List<Model> Models { get; set; } = new List<Model>();

        public bool UseVariableEnthalpy { get; set; } = true;
        public bool UseVariableAffinity { get; set; } = true;
        public bool UseUnifiedAffinity { get; set; } = false;

        public virtual int GetVariableCount
        {
            get
            {
                int nvar = 2 * Models.Count;

                if (UseVariableEnthalpy) nvar += 2;
                else nvar += 1;

                if (UseVariableAffinity || !UseUnifiedAffinity) nvar += Models.Count;
                else nvar += 1;

                return nvar;
            }
            //=> 3 + Models.Count * 2;
        }

        public double[] InitialGibbs { get; set; }
        public double[] InitialOffsets { get; set; }
        public double[] InitialNs { get; set; }

        IEnumerable<double> GuessNs() => InitialNs ?? Models.Select(m => m.GuessN);
        IEnumerable<double> GuessOffsets() => InitialOffsets ?? Models.Select(m => m.GuessOffset);
        IEnumerable<double> GuessGibbs() => InitialGibbs ?? Models.Select(m => m.GuessGibbs);

        internal double GuessHeatCapacity
        {
            get
            {
                if (Models.Count < 0) throw new Exception("Not enough experiments to fit globally");

                var xy = Models.Select(m => new double[] { m.Data.MeasuredTemperature, m.GuessH }).ToArray();
                var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

                return reg.Item2;
            }
        }

        internal double GuessReferenceEnthalpy
        {
            get
            {
                if (Models.Count < 0) throw new Exception("Not enough experiments to fit globally");

                var xy = Models.Select(m => new double[] { m.Data.MeasuredTemperature, m.GuessH }).ToArray();
                var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

                var H0 = reg.Item1 + 25 * reg.Item2;

                return H0;
            }
        }

        internal virtual double[] GetStartValues()
        {
            var H0 = GuessReferenceEnthalpy;
            var Cp = GuessHeatCapacity;
            var G = GuessGibbs();

            var ns = GuessNs();
            var offsets = GuessOffsets();

            var w = new List<double>() { H0 };
            if (UseVariableEnthalpy) w.Add(Cp);
            if (UseVariableAffinity || !UseUnifiedAffinity) w.AddRange(G);
            else w.Add(G.First());

            w.AddRange(offsets);
            w.AddRange(ns);

            return w.ToArray();
        }

        internal virtual double[] StepSizes
        {
            get
            {
                var stepsizes = new List<double>()
                {
                    Hstep,
                };

                if (UseVariableEnthalpy) stepsizes.Add(Cstep);

                if (UseVariableAffinity || !UseUnifiedAffinity) { for (int i = 0; i < Models.Count; i++) stepsizes.Add(Gstep); }
                else stepsizes.Add(Gstep);

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
                };

                if (UseVariableEnthalpy) bounds.Add(new Energy(-10000, EnergyUnit.Joule));

                if (UseVariableAffinity || !UseUnifiedAffinity) { for (int i = 0; i < Models.Count; i++) bounds.Add(new Energy(-50000, EnergyUnit.Joule)); }
                else bounds.Add(new Energy(-50000, EnergyUnit.Joule));

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
                };

                if (UseVariableEnthalpy) bounds.Add(new Energy(10000, EnergyUnit.Joule));

                if (UseVariableAffinity || !UseUnifiedAffinity) { for (int i = 0; i < Models.Count; i++) bounds.Add(new Energy(-5000, EnergyUnit.Joule)); }
                else bounds.Add(new Energy(-5000, EnergyUnit.Joule));

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

            for (int i = 0; i < GetVariableCount; i++)
            {
                solver.LowerBounds[i] = lb[i];
                solver.UpperBounds[i] = ub[i];
            }
        }

        public SolverConvergence SolvewithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(GetVariableCount, (w) => LossFunction(w));
            var solver = new NelderMead(f);

            var stepsizes = StepSizes;

            for (int i = 0; i < solver.StepSize.Length; i++) solver.StepSize[i] = stepsizes[i];

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(GetVariableCount)
            {
                MaximumEvaluations = 300000,
                AbsoluteFunctionTolerance = double.Epsilon,
                StartTime = DateTime.Now,
            };

            SetBounds(solver);

            solver.Minimize(GetStartValues());

            GlobalSolution.FromAccordNelderMead(solver.Solution, this);

            return new SolverConvergence(solver);
        }

        internal virtual double LossFunction(double[] w)
        {
            int i = 0;

            double H0 = w[i++];
            double Cp;

            if (UseVariableEnthalpy) Cp = w[i++];
            else Cp = 0;

            double[] G;

            if (UseVariableAffinity || !UseUnifiedAffinity) G = w.Skip(PostIncrement(i, Models.Count, out i)).Take(Models.Count).ToArray();//Variable affinity of experiments
            else G = new double[Models.Count].Add(w[i++]); //Use same affinity for all experiments

            double[] offsets = w.Skip(PostIncrement(i, Models.Count, out i)).Take(Models.Count).ToArray();
            double[] ns = w.Skip(i).Take(Models.Count).ToArray();

            return GlobalLoss(ns, H0, Cp, G, offsets);
        }

        double GlobalLoss(double[] ns, double H0, double Cp, double[] Gs, double[] offsets)
        {
            double glob_loss = 0;

            Energy dH0 = new Energy(H0);
            Energy dCp = new Energy(Cp); //If non-varaible enthalpy, this value is zero

            for (int i = 0; i < Models.Count; i++)
            {
                //Calculate T specific parameters
                var m = Models[i];
                var T = m.Data.MeasuredTemperature + 273.15;
                var dt = T - 298.15;
                var H = dH0 + dCp * dt;

                var dG = new Energy(Gs[i]); //If non-variable affinity, all G[i]'s will be same value
                var K = Math.Exp(-1 * dG / (Energy.R * T));

                glob_loss += m.RMSD(ns[i], H, K, offsets[i]);
            }

            return glob_loss;
        }

        double GlobalLossObsolete(double[] n, double H0, double Cp, double G, double[] offset)
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

        double GlobalLossObsolete(double[] n, double H0, double Cp, double[] Gs, double[] offset)
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

    //public class GlobalModelFreeGibbs : GlobalModel
    //{
    //    public double[] InitialGibbs { get; set; }

    //    public override int GetVariableCount => 2 + Models.Count * 3;

    //    private double[] GetInitialGibbs() => InitialGibbs ?? new double[Models.Count].Add(GuessGibbs());

    //    internal override double[] GetStartValues()
    //    {
    //        var H0 = GuessReferenceEnthalpy;
    //        var Cp = GuessHeatCapacity;

    //        var gs = GetInitialGibbs();
    //        var ns = GuessNs();
    //        var offsets = GuessOffsets();

    //        var w = new List<double>() { H0, Cp };
    //        w.AddRange(gs);
    //        w.AddRange(offsets);
    //        w.AddRange(ns);

    //        return w.ToArray();
    //    }
    //    internal override double[] StepSizes
    //    {
    //        get
    //        {
    //            var stepsizes = new List<double>()
    //        {
    //            Hstep,
    //            Cstep,
    //        };

    //            for (int i = 0; i < Models.Count; i++) stepsizes.Add(Gstep);
    //            for (int i = 0; i < Models.Count; i++) stepsizes.Add(Ostep);
    //            for (int i = 0; i < Models.Count; i++) stepsizes.Add(Nstep);


    //            return stepsizes.ToArray();
    //        }
    //    }

    //    internal override double[] LowerBounds
    //    {
    //        get
    //        {
    //            var bounds = new List<double>()
    //            {
    //                new Energy(-100000, EnergyUnit.Joule),
    //                new Energy(-10000, EnergyUnit.Joule),
    //            };

    //            bounds.AddRange(new double[Models.Count].Add(new Energy(-50000, EnergyUnit.Joule)));
    //            bounds.AddRange(new double[Models.Count].Add(-20000));
    //            bounds.AddRange(new double[Models.Count].Add(0.1));

    //            return bounds.ToArray();
    //        }
    //    }

    //    internal override double[] UpperBounds
    //    {
    //        get
    //        {
    //            var bounds = new List<double>()
    //            {
    //                new Energy(100000, EnergyUnit.Joule),
    //                new Energy(10000, EnergyUnit.Joule),
    //            };

    //            bounds.AddRange(new double[Models.Count].Add(new Energy(-5000, EnergyUnit.Joule)));
    //            bounds.AddRange(new double[Models.Count].Add(20000));
    //            bounds.AddRange(new double[Models.Count].Add(10));

    //            return bounds.ToArray();
    //        }
    //    }

    //    internal override double LossFunction(double[] w)
    //    {
    //        //extracts parameters
    //        double H0 = w[0];
    //        double Cp = w[1];
    //        double[] Gs = w.Skip(2).Take(Models.Count).ToArray();
    //        double[] offsets = w.Skip(2 + Models.Count).Take(Models.Count).ToArray();
    //        double[] ns = w.Skip(Models.Count + Models.Count + 2).Take(Models.Count).ToArray();

    //        return GlobalLoss(ns, H0, Cp, Gs, offsets);
    //    }

    //    double GlobalLoss(double[] n, double H0, double Cp, double[] Gs, double[] offset)
    //    {
    //        double glob_loss = 0;

    //        Energy dH0 = new Energy(H0);
    //        Energy dCp = new Energy(Cp);

    //        for (int i = 0; i < Models.Count; i++)
    //        {
    //            //Calculate T specific parameters
    //            var m = Models[i];
    //            var T = m.Data.MeasuredTemperature + 273.15;
    //            var dt = T - 298.15;
    //            var H = dH0 + dCp * dt;

    //            var dG = new Energy(Gs[i]);
    //            var K = Math.Exp(-1 * dG / (Energy.R * T));

    //            glob_loss += m.RMSD(n[i], H, K, offset[i]);
    //        }

    //        return glob_loss;
    //    }
    //}

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

        public double Evaluate(int i, bool withoffset = true)
        {
            return Model.Evaluate(i, N, Enthalpy, K, withoffset ? Offset : 0) / Data.Injections[i].InjectionMass;
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
            int nmodels = model.Models.Count;

            int i = 0;

            global.EnthalpyRef = new(solution[i++]);
            if (model.UseVariableEnthalpy) global.HeatCapacity = new(solution[i++]);

            double[] gs;
            if (model.UseVariableAffinity || !model.UseUnifiedAffinity) gs = solution.Skip(PostIncrement(i, model.Models.Count, out i)).Take(nmodels).ToArray();
            else
            {
                global.FreeEnergy = new(solution[i++]);
                gs = new double[nmodels].Add(global.FreeEnergy);
            }

            var offsets = solution.Skip(PostIncrement(i, model.Models.Count, out i)).Take(nmodels).ToArray();
            var ns = solution.Skip(i).Take(nmodels).ToArray();

            for (int j = 0; j < model.Models.Count; j++)
            {
                var m = model.Models[j];
                

                var dt = m.Data.MeasuredTemperature - 25;

                var dH = global.EnthalpyRef + global.HeatCapacity * dt;
                var K = Math.Exp(gs[j] / (-1 * Energy.R * (m.Data.MeasuredTemperature + 273.15)));

                var sol = new Solution(ns[j], dH, K, offsets[j], m, m.RMSD(ns[j], dH, K, offsets[j]));

                m.Data.Solution = sol;

                global.Solutions.Add(sol);
            }

            global.Loss = model.LossFunction(solution);

            return global;
        }
    }

    public class SolverConvergence
    {
        public int Iterations { get; private set; }
        public string Message { get; private set; }
        public TimeSpan Time { get; private set; }
        public double Loss { get; private set; }

        public SolverConvergence(NelderMead solver)
        {
            Iterations = solver.Convergence.Evaluations;
            Message = solver.Status.ToString();
            Time = DateTime.Now - solver.Convergence.StartTime;
            Loss = solver.Value;
        }
    }

}
