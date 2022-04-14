using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math.Optimization;
using Accord.Math;
using System.Threading.Tasks;
using static Utilities.Increment;
using Foundation;
using AppKit;

namespace AnalysisITC
{
    public static class Analysis
    {
        public static Random Random { get; } = new Random();

        public static event EventHandler AnalysisIterationFinished;
        public static event EventHandler<SolverConvergence> AnalysisFinished;

        public static double Hstep { get; set; } = 1000;
        public static double Kstep { get; set; } = 10000;
        public static double Gstep { get; set; } = 500;
        public static double Cstep { get; set; } = 100;
        public static double Nstep { get; set; } = 0.5;
        public static double Ostep { get; set; } = 1000;

        public static int BootstrapIterations { get; set; } = 100;

        public static List<ExperimentData> GetValidData()
        {
            return DataManager.Data.Where(d => d.Include).ToList();
        }

        public static class GlobalAnalyzer
        {
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
                foreach (var data in Analysis.GetValidData())
                {
                    Model.Models.Add(new OneSetOfSites(data));
                    if (data.Solution != null) data.Solution.IsValid = false;

                    data.UpdateSolution();
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

                var starttime = DateTime.Now;

                await Task.Run(() =>
                    {
                        convergence = Model.SolveWithNelderMeadAlgorithm();

                        NSApplication.SharedApplication.InvokeOnMainThread(() => { AnalysisIterationFinished?.Invoke(null, null); });

                        Model.Bootstrap();
                    });

                Console.WriteLine((DateTime.Now - starttime).TotalSeconds);

                //var refdat = Model.Models[0].Data;
                //var syndat = new List<ExperimentData>();
                //
                //for (var i = 0; i < 100; i++)
                //{
                //    syndat.Add(refdat.GetSynthClone());
                //}
                //
                //double[][] dat = new double[102][];
                //
                //for (int j = 0; j < refdat.InjectionCount; j++)
                //{
                //    string line = "";
                //
                //    for (var i = 0; i < 102; i++)
                //    {
                //        if (i == 0) line = refdat.Injections[j].Ratio.ToString();
                //        else if (i == 1) line += " " + refdat.Solution.Evaluate(j, true).ToString();
                //        else
                //        {
                //            var d = syndat[i - 2];
                //
                //            line += " " + d.Injections[j].Enthalpy.ToString();
                //        }
                //    }
                //
                //    Console.WriteLine(line);
                //}

                AnalysisFinished?.Invoke(null, convergence);
            }

            static void SetInitialValues()
            {
                foreach (var m in Model.Models)
                {
                    m.SolveWithNelderMeadAlgorithm();
                }

                Model.InitialOffsets = Model.Models.Select(m => (double)m.Solution.Offset).ToArray();
                Model.InitialNs = Model.Models.Select(m => (double)m.Solution.N).ToArray();
                Model.InitialGibbs = Model.Models.Select(m => (double)m.Solution.GibbsFreeEnergy).ToArray();
            }
        }

        public static class Analyzer
        {
            static ExperimentData Data { get; set; }

            static Model Model { get; set; }

            public static void InitializeAnalyzer(ExperimentData data)
            {
                Data = data;
            }

            public static void InitializeOneSetOfSites()
            {
                Model = new OneSetOfSites(Data);
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

                var starttime = DateTime.Now;

                await Task.Run(() =>
                {
                    convergence = Model.SolveWithNelderMeadAlgorithm();

                    NSApplication.SharedApplication.InvokeOnMainThread(() => { AnalysisIterationFinished?.Invoke(null, null); });

                    Model.Bootstrap();
                });

                Console.WriteLine((DateTime.Now - starttime).TotalSeconds);

                AnalysisFinished?.Invoke(null, convergence);
            }

            public static void LM() //TODO move to model solving
            {
                var f = new Accord.Statistics.Models.Regression.Fitting.NonlinearLeastSquares();
                f.Algorithm = new Accord.Math.Optimization.LevenbergMarquardt()
                {
                    MaxIterations = 1000,
                    ParallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = 3 }

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
        }

        public enum VariableStyle
        {
            Free,
            TemperatureDependent,
            SameForAll
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
        public virtual double GuessGibbs => -35000;
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

        public virtual SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(4, (w) => this.RMSD(w[0], w[1], w[2], w[3]));
            var solver = new NelderMead(f);
            solver.StepSize[0] = Analysis.Nstep;
            solver.StepSize[1] = Analysis.Hstep;
            solver.StepSize[2] = Analysis.Kstep;
            solver.StepSize[3] = Analysis.Ostep;
            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(4)
            {
                MaximumEvaluations = 300000,
                AbsoluteFunctionTolerance = double.Epsilon,
                StartTime = DateTime.Now,
            };

            solver.Minimize(new double[4] { GuessN, GuessH, GuessK, GuessOffset });

            Data.Solution = Solution.FromAccordNelderMead(solver.Solution, this, solver.Function(solver.Solution));

            return new SolverConvergence(solver);
        }

        public void Bootstrap()
        {
            var solutions = new List<Solution>();

            for (int i = 0; i < Analysis.BootstrapIterations; i++)
            {
                var model = this.GenerateSyntheticModel();

                model.SolveWithNelderMeadAlgorithm();

                solutions.Add(model.Solution);
            }

            Solution.BootstrapSolutions = solutions;
            Solution.ComputeErrorsFromBootstrapSolutions();
        }

        public virtual Model GenerateSyntheticModel()
        {
            return new Model(Data.GetSynthClone());
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

            foreach (var inj in Data.Injections)//.Where(i => i.Include && i.ID != ExcludeSinglePoint))
            {
                var calc = Evaluate(inj.ID, n, H, K, offset);
                var meas = inj.PeakArea;

                if (inj.Include && inj.ID != ExcludeSinglePoint) loss += (calc - meas) * (calc - meas);
            }

            return loss;
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

            var dQi = Qi + (inj.Volume / Data.CellVolume) * ((Qi + Q_i) / 2.0) - Q_i;

            return dQi;
        }

        public double GetHeatContent(InjectionData inj, double n, double H, double K)
        {
            var ncell = n * inj.ActualCellConcentration;
            var first = (ncell * H * Data.CellVolume) / 2.0;
            var XnM = inj.ActualTitrantConcentration / ncell;
            var nKM = 1.0 / (K * ncell);
            var square = (1.0 + XnM + nKM);
            var root = (square * square) - 4.0 * XnM;

            return first * (1 + XnM + nKM - Math.Sqrt(root));
        }

        public override Model GenerateSyntheticModel()
        {
            return new OneSetOfSites(Data.GetSynthClone());
        }
    }

    public class GlobalModel
    {
        public List<Model> Models { get; set; } = new List<Model>();
        public GlobalSolution Solution { get; private set; }

        public Analysis.VariableStyle Enthalpystyle { get; set; } = Analysis.VariableStyle.TemperatureDependent;
        public bool UseVariableAffinity { get; set; } = true;
        public bool UseUnifiedAffinity { get; set; } = false;

        public virtual int GetVariableCount
        {
            get
            {
                int nvar = 2 * Models.Count;

                switch (Enthalpystyle)
                {
                    case Analysis.VariableStyle.TemperatureDependent: nvar += 2; break;
                    case Analysis.VariableStyle.Free: nvar += Models.Count; break;
                    case Analysis.VariableStyle.SameForAll:
                    default: nvar += 1; break;
                }

                if (UseVariableAffinity || !UseUnifiedAffinity) nvar += Models.Count;
                else nvar += 1;

                return nvar;
            }
            //=> 3 + Models.Count * 2;
        }

        #region Initial Values

        public double[] InitialGibbs { get; set; }
        public double[] InitialOffsets { get; set; }
        public double[] InitialNs { get; set; }

        IEnumerable<double> GuessNs() => InitialNs ?? Models.Select(m => m.GuessN);
        IEnumerable<double> GuessOffsets() => InitialOffsets ?? Models.Select(m => m.GuessOffset);
        IEnumerable<double> GuessGibbs() => InitialGibbs ?? Models.Select(m => m.GuessGibbs);

        double GuessHeatCapacity
        {
            get
            {
                if (Models.Count < 0) throw new Exception("Not enough experiments to fit globally");

                var xy = Models.Select(m => new double[] { m.Data.MeasuredTemperature, m.GuessH }).ToArray();
                var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

                return reg.Item2;
            }
        }

        double GuessReferenceEnthalpy
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

        double[] GetStartValues()
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

        double[] StepSizes
        {
            get
            {
                var stepsizes = new List<double>()
                {
                    Analysis.Hstep,
                };

                if (UseVariableEnthalpy) stepsizes.Add(Analysis.Cstep);

                if (UseVariableAffinity || !UseUnifiedAffinity) { for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Gstep); }
                else stepsizes.Add(Analysis.Gstep);

                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Ostep);
                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Nstep);

                return stepsizes.ToArray();
            }
        }

        double[] LowerBounds
        {
            get
            {
                var bounds = new List<double>()
                {
                    -100000,
                };

                if (UseVariableEnthalpy) bounds.Add(-10000);

                if (UseVariableAffinity || !UseUnifiedAffinity) { for (int i = 0; i < Models.Count; i++) bounds.Add(-50000); }
                else bounds.Add(-50000);

                bounds.AddRange(new double[Models.Count].Add(-20000));
                bounds.AddRange(new double[Models.Count].Add(0.1));

                return bounds.ToArray();
            }
        }

        double[] UpperBounds
        {
            get
            {
                var bounds = new List<double>()
                {
                    100000,
                };

                if (UseVariableEnthalpy) bounds.Add(10000);

                if (UseVariableAffinity || !UseUnifiedAffinity) { for (int i = 0; i < Models.Count; i++) bounds.Add(-5000); }
                else bounds.Add(-5000);

                bounds.AddRange(new double[Models.Count].Add(20000));
                bounds.AddRange(new double[Models.Count].Add(10));

                return bounds.ToArray();
            }
        }

        #endregion

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

        public SolverConvergence SolveWithNelderMeadAlgorithm()
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

            Solution = GlobalSolution.FromAccordNelderMead(solver.Solution, this);

            return new SolverConvergence(solver);
        }

        public void Bootstrap()
        {
            var solutions = new List<GlobalSolution>();

            for (int i = 0; i < Analysis.BootstrapIterations; i++)
            {
                var models = new List<Model>();

                foreach (var m in Models) models.Add(m.GenerateSyntheticModel());

                var gm = new GlobalModel();
                gm.UseVariableAffinity = this.UseVariableAffinity;
                gm.UseUnifiedAffinity = this.UseUnifiedAffinity;
                gm.UseVariableEnthalpy = this.UseVariableEnthalpy;

                gm.Models.AddRange(models);

                gm.SolveWithNelderMeadAlgorithm();

                solutions.Add(gm.Solution);
            }

            Solution.EnthalpyRef = new Energy(new FloatWithError(solutions.Select(s => s.EnthalpyRef.Value)));
            Solution.HeatCapacity = new Energy(new FloatWithError(solutions.Select(s => s.HeatCapacity.Value)));

            foreach (var model in Models)
            {
                var sols = solutions.SelectMany(gs => gs.Solutions.Where(s => s.Data.FileName == model.Data.FileName)).ToList();

                model.Solution.BootstrapSolutions = sols;
                model.Solution.ComputeErrorsFromBootstrapSolutions();
            }
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
    }

    public class Solution
    {
        static Energy R => Energy.R;

        public Model Model;
        public ExperimentData Data => Model.Data;
        public bool IsValid { get; set; } = true;

        public Energy Enthalpy { get; private set; }
        public FloatWithError K { get; private set; }
        public FloatWithError N { get; private set; }
        public Energy Offset { get; private set; }

        public double T => Data.MeasuredTemperature;
        double TK => T + 273.15;

        public FloatWithError Kd => new FloatWithError(1) / K;
        public Energy GibbsFreeEnergy => new(-1.0 * R.FloatWithError * TK * FWEMath.Log(K));
        public Energy TdS => GibbsFreeEnergy - Enthalpy;
        public Energy Entropy => TdS / TK;

        public double Loss { get; private set; }

        public List<Solution> BootstrapSolutions;

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

        public void ComputeErrorsFromBootstrapSolutions()
        {
            var enthalpies = BootstrapSolutions.Select(s => s.Enthalpy.FloatWithError.Value);
            Enthalpy = new Energy(new FloatWithError(enthalpies, Enthalpy));

            var k = BootstrapSolutions.Select(s => s.K.Value);
            K = new FloatWithError(k, K);

            var n = BootstrapSolutions.Select(s => s.N.Value);
            N = new FloatWithError(n, N);

            var offsets = BootstrapSolutions.Select(s => (double)s.Offset);
            Offset = Energy.FromDistribution(offsets, Offset);

            Data.UpdateSolution(null);
        }

        public double Evaluate(int i, bool withoffset = true)
        {
            return Model.Evaluate(i, N, Enthalpy, K, withoffset ? Offset : 0) / Data.Injections[i].InjectionMass;
        }

        public FloatWithError EvaluateBootstrap(int i)
        {
            var results = new List<double>();

            foreach (var sol in BootstrapSolutions)
            {
                results.Add(sol.Evaluate(i, true));
            }

            return new FloatWithError(results, Evaluate(i, true)) - Offset.FloatWithError;
        }

        public static Tuple<double, double> EvaluateBootstrap(int i, bool withoffset, List<Solution> solutions)
        {
            double min = double.MaxValue;
            double max = double.MinValue;

            foreach (var sol in solutions)
            {
                var result = sol.Evaluate(i, withoffset);

                if (result > max) max = result;
                if (result < min) min = result;
            }

            return new Tuple<double, double>(min, max);
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

        public Energy HeatCapacity { get; set; }
        public Energy EnthalpyRef { get; set; } //Enthalpy at 298.15 °C
        public Energy FreeEnergy { get; set; }

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
