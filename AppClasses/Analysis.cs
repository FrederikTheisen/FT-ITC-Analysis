using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math.Optimization;
using Accord.Math;
using System.Threading.Tasks;
using static Utilities.Increment;
using Foundation;
using AppKit;
using System.Threading;

namespace AnalysisITC
{
    public static class Analysis
    {
        public static Random Random { get; } = new Random();

        public static event EventHandler AnalysisIterationFinished;
        public static event EventHandler<SolverConvergence> AnalysisFinished;
        public static event EventHandler<Tuple<int,int,float>> BootstrapIterationFinished;

        public static double Hstep { get; set; } = 1000;
        public static double Kstep { get; set; } = 10000;
        public static double Gstep { get; set; } = 500;
        public static double Cstep { get; set; } = 100;
        public static double Nstep { get; set; } = 0.5;
        public static double Ostep { get; set; } = 1000;

        public static double Hinit { get; set; } = double.NaN;
        public static double Kinit { get; set; } = double.NaN;
        public static double Ginit { get; set; } = double.NaN;
        public static double Cinit { get; set; } = double.NaN;
        public static double Oinit { get; set; } = double.NaN;
        public static double Ninit { get; set; } = double.NaN;

        public static double[] Hbounds { get; set; } = { -300000, 300000 };

        public static int BootstrapIterations { get; set; } = 100;

        public static List<ExperimentData> GetValidData()
        {
            return DataManager.Data.Where(d => d.Include).ToList();
        }

        public static void ReportBootstrapProgress(int iteration) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            BootstrapIterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, BootstrapIterations, iteration / (float)BootstrapIterations));
        });

        public static class GlobalAnalyzer
        {
            static GlobalModel Model { get; set; }

            public static void InitializeAnalyzer(VariableStyle enthalpystyle, VariableStyle affinitystyle)
            {
                Model = new GlobalModel();

                Model.Options = new SolverOptions()
                {
                    Model = Model,
                    EnthalpyStyle = enthalpystyle,
                    AffinityStyle = affinitystyle,
                };
            }

            static void InitializeOneSetOfSites()
            {
                foreach (var data in GetValidData())
                {
                    Model.Models.Add(new OneSetOfSites(data));
                    if (data.Solution != null) data.Solution.IsValid = false;

                    data.UpdateSolution();
                }

                if (!double.IsNaN(Hinit)) Model.InitialHs = new double[Model.Models.Count].Add(Hinit);
                if (!double.IsNaN(Ginit)) Model.InitialGibbs = new double[Model.Models.Count].Add(Ginit);
                if (!double.IsNaN(Oinit)) Model.InitialOffsets = new double[Model.Models.Count].Add(Oinit);
                if (!double.IsNaN(Ninit)) Model.InitialNs = new double[Model.Models.Count].Add(Ninit);
                if (!double.IsNaN(Cinit)) Model.InitialHeatCapacity = new double[Model.Models.Count].Add(Cinit);
            }

            public static async void Solve(AnalysisModel analysismodel)
            {
                try
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

                            NSApplication.SharedApplication.InvokeOnMainThread(() => { AnalysisIterationFinished?.Invoke(null, null); Model.Models.ForEach(m => m.Data.UpdateSolution(null)); });

                            Model.Bootstrap();
                        });

                    Console.WriteLine((DateTime.Now - starttime).TotalSeconds);

                    DataManager.AddData(new AnalysisResult(Model.Solution));

                    AnalysisFinished?.Invoke(null, convergence);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    StatusBarManager.ClearAppStatus();
                    StatusBarManager.SetStatusScrolling("Analysis failed: " + ex.Message);

                    AnalysisFinished?.Invoke(null, null);
                }
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

        public double Temperature => Data.MeasuredTemperature;

        public virtual double GuessN => Data.Injections.Last().Ratio / 2;
        public virtual double GuessH => Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset;
        public virtual double GuessK => 1000000;
        public virtual double GuessGibbs => -35000;
        public virtual double GuessOffset => Data.Injections.Where(inj => inj.Include).TakeLast(2).Average(inj => inj.Enthalpy);

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

        public virtual double RMSD(double n, double H, double K, double offset, bool isloss = true)
        {
            return 0;
        }

        public virtual double Evaluate(int i, double n, double H, double K, double offset)
        {
            return 0;
        }

        public virtual SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(4, (w) => this.RMSD(w[0], w[1], w[2], w[3], true));
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

                Console.WriteLine(model.Solution.ToString());

                Analysis.ReportBootstrapProgress(i);
            }

            var clones = new List<ExperimentData>();
            for (int i = 0; i < 10; i++) clones.Add(Data.GetSynthClone());

            for (int i = 0; i < Data.InjectionCount; i++)
            {
                string s = Data.Injections[i].Ratio.ToString() + " ";

                for (int j = 0; j < clones.Count; j++)
                {
                    s += clones[j].Injections[i].Enthalpy + " ";
                }

                Console.WriteLine(s);
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

        public override double RMSD(double n, double H, double K, double offset, bool isloss = false)
        {
            double loss = 0;

            foreach (var inj in Data.Injections.Where(i => i.Include && i.ID != ExcludeSinglePoint))
            {
                var diff = Evaluate(inj.ID, n, H, K, offset) - inj.PeakArea;
                loss += diff * diff;
            }

            return isloss ? loss : Math.Sqrt(loss / Data.Injections.Where(i => i.Include && i.ID != ExcludeSinglePoint).Count());
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
        public SolverOptions Options { get; set; }

        public double MeanTemperature => Models.Average(m => m.Temperature);
        public Analysis.VariableStyle EnthalpyStyle => Options.EnthalpyStyle;
        public Analysis.VariableStyle AffinityStyle => Options.AffinityStyle;
        public int MaximumEvaluations { get; set; } = 300000;
        public double AbsoluteFunctionTolerance { get; set; } = double.Epsilon;
        public double AbsoluteParameterTolerance { get; set; } = 0.001;

        public virtual int GetVariableCount
        {
            get
            {
                int nvar = 2 * Models.Count;

                switch (EnthalpyStyle)
                {
                    case Analysis.VariableStyle.TemperatureDependent: nvar += 2; break;
                    case Analysis.VariableStyle.Free: nvar += Models.Count; break;
                    case Analysis.VariableStyle.SameForAll:
                    default: nvar += 1; break;
                }

                switch (AffinityStyle)
                {
                    case Analysis.VariableStyle.Free: nvar += Models.Count; break;
                    case Analysis.VariableStyle.TemperatureDependent:
                    case Analysis.VariableStyle.SameForAll:
                    default: nvar += 1; break;
                }

                return nvar;
            }
        }

        #region Initial Values

        public double[] InitialGibbs { get; set; }
        public double[] InitialOffsets { get; set; }
        public double[] InitialNs { get; set; }
        public double[] InitialHs { get; set; }
        public double[] InitialHeatCapacity { get; set; }

        IEnumerable<double> GuessNs() => InitialNs ?? Models.Select(m => m.GuessN);
        IEnumerable<double> GuessOffsets() => InitialOffsets ?? Models.Select(m => m.GuessOffset);
        IEnumerable<double> GuessGibbs() => InitialGibbs ?? Models.Select(m => m.GuessGibbs);

        double[] BootstrapInitialValues { get; set; }

        double GuessHeatCapacity
        {
            get
            {
                if (EnthalpyStyle != Analysis.VariableStyle.TemperatureDependent) return 0;
                if (Models.Count < 2) return 0;
                if (Models.Max(m => m.Data.MeasuredTemperature) - Models.Min(m => m.Data.MeasuredTemperature) < 1) return 0;

                var xy = Models.Select(m => new double[] { m.Data.MeasuredTemperature, m.GuessH }).ToArray();
                var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

                return reg.Item2;
            }
        }

        double GuessReferenceEnthalpy
        {
            get
            {
                if (EnthalpyStyle != Analysis.VariableStyle.TemperatureDependent) return Models.Select(m => m.GuessH).Average();
                if (Models.Count < 2) return Models.First().GuessH;
                if (Models.Max(m => m.Data.MeasuredTemperature) - Models.Min(m => m.Data.MeasuredTemperature) < 1) return Models.Average(m => m.Data.Injections.First(inj => inj.Include).Enthalpy);

                var xy = Models.Select(m => new double[] { m.Data.MeasuredTemperature, m.GuessH }).ToArray();
                var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

                var H0 = reg.A + MeanTemperature * reg.B;

                return Math.Clamp(H0, Analysis.Hbounds[0], Analysis.Hbounds[1]);
            }
        }

        double[] GetStartValues()
        {
            if (BootstrapInitialValues != null) return BootstrapInitialValues;

            var H0 = GuessReferenceEnthalpy;
            var Cp = GuessHeatCapacity;
            var G = GuessGibbs();

            var ns = GuessNs();
            var offsets = GuessOffsets();

            var w = new List<double>();
            if (EnthalpyStyle == Analysis.VariableStyle.Free) w.AddRange(Models.Select(m => m.GuessH));
            else w.Add(H0);

            if (EnthalpyStyle == Analysis.VariableStyle.TemperatureDependent) w.Add(Cp);

            if (AffinityStyle == Analysis.VariableStyle.Free) w.AddRange(G);
            else w.Add(G.First());

            w.AddRange(offsets);
            w.AddRange(ns);

            return w.ToArray();
        }

        double[] StepSizes
        {
            get
            {
                var stepsizes = new List<double>();

                if (EnthalpyStyle == Analysis.VariableStyle.Free) { for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Hstep); }
                else stepsizes.Add(Analysis.Hstep);

                if (EnthalpyStyle == Analysis.VariableStyle.TemperatureDependent) stepsizes.Add(Analysis.Cstep);

                if (AffinityStyle == Analysis.VariableStyle.Free) { for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Gstep); }
                else stepsizes.Add(Analysis.Gstep);

                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Ostep);
                for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Nstep);

                //if (BootstrapInitialValues != null) stepsizes = stepsizes.Select(v => v / 10).ToList();

                return stepsizes.ToArray();
            }
        }

        double[] LowerBounds
        {
            get
            {
                //if (BootstrapInitialValues != null) return BootstrapInitialValues.Select(v => v - 0.1 * Math.Abs(v)).ToArray();
                var bounds = new List<double>();

                if (EnthalpyStyle == Analysis.VariableStyle.Free) { for (int i = 0; i < Models.Count; i++) bounds.Add(Analysis.Hbounds[0]); }
                else bounds.Add(Analysis.Hbounds[0]);

                if (EnthalpyStyle == Analysis.VariableStyle.TemperatureDependent) bounds.Add(-10000);

                if (AffinityStyle == Analysis.VariableStyle.Free) { for (int i = 0; i < Models.Count; i++) bounds.Add(-50000); }
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
                //if (BootstrapInitialValues != null) return BootstrapInitialValues.Select(v => v + 0.1 * Math.Abs(v)).ToArray();
                var bounds = new List<double>();

                if (EnthalpyStyle == Analysis.VariableStyle.Free) { for (int i = 0; i < Models.Count; i++) bounds.Add(Analysis.Hbounds[1]); }
                else bounds.Add(Analysis.Hbounds[1]);

                if (EnthalpyStyle == Analysis.VariableStyle.TemperatureDependent) bounds.Add(10000);

                if (AffinityStyle == Analysis.VariableStyle.Free) { for (int i = 0; i < Models.Count; i++) bounds.Add(-5000); }
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

        public GlobalModel(double[] bootstrapinitialvalues)
        {
            BootstrapInitialValues = bootstrapinitialvalues;
            AbsoluteFunctionTolerance = 0.0000000000000000001;
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
                MaximumEvaluations = MaximumEvaluations,
                AbsoluteFunctionTolerance = AbsoluteFunctionTolerance,
                RelativeParameterTolerance = 0.01,
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

            int counter = 0;

            Analysis.ReportBootstrapProgress(0);

            var start = DateTime.Now;

            var res = Parallel.For(0, Analysis.BootstrapIterations, (i) =>
            {
                var models = new List<Model>();

                foreach (var m in Models) models.Add(m.GenerateSyntheticModel());

                var gm = new GlobalModel(Solution.Raw.Copy())
                {
                    Options = Options
                };

                gm.Models.AddRange(models);

                gm.SolveWithNelderMeadAlgorithm();

                solutions.Add(gm.Solution);

                var currcounter = Interlocked.Increment(ref counter);

                Analysis.ReportBootstrapProgress(currcounter);
            });

            Solution.SetEnthalpiesFromBootstrap(solutions);

            foreach (var model in Models)
            {
                var sols = solutions.SelectMany(gs => gs.Solutions.Where(s => s.Data.FileName == model.Data.FileName)).ToList();

                model.Solution.BootstrapSolutions = sols;
                model.Solution.ComputeErrorsFromBootstrapSolutions();
            }
        }

        internal virtual double LossFunction(double[] w)
        {
            var parameters = SolverParameters.FromArray(w, Options);

            return GlobalLoss(parameters);
        }

        double GlobalLoss(SolverParameters parameters)
        {
            double glob_loss = 0;

            for (int i = 0; i < Models.Count; i++)
            {
                var m = Models[i];
                var pset = parameters.ParameterSetForModel(i);

                glob_loss += m.RMSD(pset.N, pset.GetEnthalpy(m, Options), pset.GetK(m, Options), pset.Offset, true);
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
        public double[] Raw { get; set; }

        public Energy Enthalpy { get; private set; }
        public FloatWithError K { get; private set; }
        public FloatWithError N { get; private set; }
        public Energy Offset { get; private set; }

        public double T => Data.MeasuredTemperature;
        double TempKelvin => T + 273.15;

        public FloatWithError Kd => new FloatWithError(1) / K;
        public Energy GibbsFreeEnergy => new(-1.0 * R.FloatWithError * TempKelvin * FWEMath.Log(K));
        public Energy TdS => GibbsFreeEnergy - Enthalpy;
        public Energy Entropy => TdS / TempKelvin;

        public double Loss { get; private set; }

        public List<Solution> BootstrapSolutions;

        public Solution()
        {

        }

        public Solution(double n, double dH, double k, double offset, Model model, double loss)
        {
            N = new(n);
            Enthalpy = new(dH);
            K = new(k);
            Offset = new(offset);
            Model = model;
            Loss = loss;
        }

        public static Solution FromAccordNelderMead(double[] parameters, Model model, double loss)
        {
            return new Solution()
            {
                Raw = parameters,
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
        public GlobalModel Model { get; set; }
        public double[] Raw { get; private set; }

        public double Loss { get; private set; } = 0;

        public double ReferenceTemperature => Model.MeanTemperature;
        public Energy HeatCapacity { get; private set; } = new(0);
        public Energy StandardEnthalpy { get; private set; } = new(0);//Enthalpy at 298.15 °C
        public Energy ReferenceEnthalpy { get; private set; } = new(); //Fitting reference value

        public LinearFit EnthalpyLine => new LinearFit(HeatCapacity, ReferenceEnthalpy);
        public LinearFit EntropyLine { get; private set; }
        public LinearFit GibbsLine { get; private set; }

        /// <summary>
        /// Parameters of the individual experiments derived from the global solution
        /// </summary>
        public List<Solution> Solutions { get; private set; } = new List<Solution>();

        public void SetEnthalpiesFromBootstrap(List<GlobalSolution> solutions) => this.SetEnthalpiesFromBootstrap(solutions.Select(gs => gs.ReferenceEnthalpy.Value), solutions.Select(gs => gs.StandardEnthalpy.Value), solutions.Select(gs => gs.HeatCapacity.Value));

        public static GlobalSolution FromAccordNelderMead(double[] solution, GlobalModel model)
        {
            var parameters = SolverParameters.FromArray(solution, model.Options);

            var globparams = GetEnthalpies(parameters, model);

            var global = new GlobalSolution
            {
                Raw = solution,
                Model = model,
                HeatCapacity = globparams.HeatCapacity,
                StandardEnthalpy = globparams.StandardEnthalpy,
                ReferenceEnthalpy = globparams.ReferenceEnthalpy
            };

            for (int j = 0; j < model.Models.Count; j++)
            {
                var m = model.Models[j];
                var dt = m.Data.MeasuredTemperature - model.MeanTemperature;
                var pset = parameters.ParameterSetForModel(j);

                var dH = model.EnthalpyStyle == Analysis.VariableStyle.TemperatureDependent ? pset.dH + pset.dCp * dt : pset.dH;
                var K = Math.Exp(pset.dG / (-1 * Energy.R.Value * (m.Data.MeasuredTemperature + 273.15)));
                var sol = new Solution(pset.N, dH, K, pset.Offset, m, m.RMSD(pset.N, dH, K, pset.Offset));

                m.Data.Solution = sol;

                global.Solutions.Add(sol);
            }

            global.Loss = model.LossFunction(solution);

            global.SetEntropyTemperatureDependence(model);
            global.SetGibbsTemperatureDependence(model);

            return global;
        }

        void SetEntropyTemperatureDependence(GlobalModel model)
        {
            var xy = model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature - model.MeanTemperature, m.Solution.TdS }).ToArray();
            var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

            EntropyLine = new LinearFit(reg.B, reg.A);
        }

        void SetGibbsTemperatureDependence(GlobalModel model)
        {
            var xy = model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature - model.MeanTemperature, m.Solution.GibbsFreeEnergy }).ToArray();
            var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

            GibbsLine = new LinearFit(reg.B, reg.A);
        }

        /// <summary>
        /// Obsolete
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        static double[] DetermineEnthalpiesFromParameters(SolverParameters parameters, GlobalModel model)
        {
            if (parameters.EnthalpyStyle == Analysis.VariableStyle.Free)
            {
                var temps = model.Models.Select(m => m.Data.MeasuredTemperature);

                var xy = model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature, parameters.Enthalpies[i] }).ToArray();
                var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

                var H0 = reg.A + 25 * reg.B;

                return new double[] { H0, reg.B };
            }
            else
            {
                var cp = parameters.HeatCapacity;
                var dt = 25 - model.MeanTemperature;

                return new double[] { parameters.Enthalpies.First() + cp * dt, cp };

            }
        }

        /// <summary>
        /// Extract enthalpy related values from fit parameters. Values are best fit with not error component. Errors should be derived from bootstrapping.
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public static EnthalpyTuple GetEnthalpies(SolverParameters parameters, GlobalModel model)
        {
            double Hstandard;
            double H0;
            double cp;

            switch (parameters.EnthalpyStyle)
            {
                default:
                case Analysis.VariableStyle.Free: //if dH is free, then fit both reference and standard enthalpies
                    var xy = model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature, parameters.Enthalpies[i] }).ToArray();
                    var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));
                    cp = reg.B;
                    Hstandard = reg.A + 25 * cp;
                    H0 = reg.A + model.MeanTemperature * cp;
                    break;
                case Analysis.VariableStyle.TemperatureDependent: //if temperature dependent, then the reference is the fit value, propagate to standard enthalpy
                    var dt = 25 - model.MeanTemperature;
                    cp = parameters.HeatCapacity;
                    Hstandard = parameters.HeatCapacity * dt + parameters.Enthalpies.First();
                    H0 = parameters.Enthalpies.First();
                    break;
                case Analysis.VariableStyle.SameForAll: //if same for all, then fitted value is both reference and standard??
                    cp = 0;
                    H0 = parameters.Enthalpies.First();
                    Hstandard = H0;
                    break;
            }

            return new EnthalpyTuple(H0, Hstandard, cp);
        }

        void SetEnthalpiesFromBootstrap(IEnumerable<double> refs, IEnumerable<double> stds, IEnumerable<double> cps)
        {
            this.ReferenceEnthalpy = new Energy(new FloatWithError(refs));
            this.StandardEnthalpy = new Energy(new FloatWithError(stds));
            this.HeatCapacity = new Energy(new FloatWithError(cps));

            Console.WriteLine("Bootstrap enthalpies (refT: " + ReferenceTemperature.ToString("G4") + "): " + ReferenceEnthalpy.ToString("G3") + " | " + StandardEnthalpy.ToString("G3") + " | " + HeatCapacity.ToString("G3"));
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

    public class SolverOptions
    {
        public object Model { get; set; }

        public Analysis.VariableStyle EnthalpyStyle { get; set; } = Analysis.VariableStyle.TemperatureDependent;
        public Analysis.VariableStyle AffinityStyle { get; set; } = Analysis.VariableStyle.Free;

        public int ModelCount => this.Model switch
        {
            GlobalModel => (Model as GlobalModel).Models.Count,
            _ => 1,
        };

        public double MeanTemperature => this.Model switch
        {
            GlobalModel => (Model as GlobalModel).MeanTemperature,
            _ => 25,
        };
    }

    public class SolverParameters
    {
        public Analysis.VariableStyle EnthalpyStyle { get; }
        public Analysis.VariableStyle AffinityStyle { get; }
        public int ModelCount { get; }

        public List<double> Enthalpies = new List<double>();
        public List<double> Gibbs = new List<double>();
        public List<double> Offsets = new List<double>();
        public List<double> Ns = new List<double>();

        public double HeatCapacity = 0;

        public SolverParameters(SolverOptions options)
        {
            EnthalpyStyle = options.EnthalpyStyle;
            AffinityStyle = options.AffinityStyle;
            ModelCount = options.ModelCount;
        }

        public static SolverParameters FromArray(double[] w, SolverOptions options)
        {
            var p = new SolverParameters(options);

            int index = 0;

            switch (options.EnthalpyStyle)
            {
                case Analysis.VariableStyle.Free:
                    p.Enthalpies = w.Take(options.ModelCount).ToList();
                    index += options.ModelCount;
                    break;
                case Analysis.VariableStyle.TemperatureDependent:
                    p.Enthalpies = w.Take(1).ToList();
                    p.HeatCapacity = w.Skip(1).Take(1).First();
                    index += 2;
                    break;
                case Analysis.VariableStyle.SameForAll:
                    p.Enthalpies = w.Take(1).ToList();
                    index += 1;
                    break;
            }

            switch (options.AffinityStyle)
            {
                case Analysis.VariableStyle.Free:
                    p.Gibbs = w.Skip(index).Take(options.ModelCount).ToList();
                    index += options.ModelCount;
                    break;
                case Analysis.VariableStyle.TemperatureDependent:
                case Analysis.VariableStyle.SameForAll:
                    p.Gibbs = w.Skip(index).Take(1).ToList();
                    index += 1;
                    break;
            }

            p.Offsets = w.Skip(index).Take(options.ModelCount).ToList();
            index += options.ModelCount;

            p.Ns = w.Skip(index).Take(options.ModelCount).ToList();

            return p;
        }

        public double[] ToArray()
        {
            var w = new List<double>();

            w.AddRange(Enthalpies);
            if (EnthalpyStyle == Analysis.VariableStyle.TemperatureDependent) w.Add(HeatCapacity);
            w.AddRange(Gibbs);
            w.AddRange(Offsets);
            w.AddRange(Ns);

            return w.ToArray();
        }

        public ParameterSet ParameterSetForModel(int modelnumber, SolverOptions options = null)
        {
            return new ParameterSet
            {
                Options = options,
                dH = EnthalpyStyle == Analysis.VariableStyle.Free ? Enthalpies[modelnumber] : Enthalpies[0],
                dCp = HeatCapacity,
                dG = AffinityStyle == Analysis.VariableStyle.Free ? Gibbs[modelnumber] : Gibbs[0],
                Offset = Offsets[modelnumber],
                N = Ns[modelnumber]
            };
        }

        public struct ParameterSet
        {
            public SolverOptions Options { get; set; }

            public double dH { get; set; }
            public double dCp { get; set; }
            public double dG { get; set; }
            public double Offset { get; set; }
            public double N { get; set; }

            public double GetEnthalpy(Model model, SolverOptions options)
            {
                switch (options.EnthalpyStyle)
                {
                    case Analysis.VariableStyle.TemperatureDependent:
                        var dt = model.Data.MeasuredTemperature - options.MeanTemperature;
                        return dH + dCp * dt;
                    default: return dH;
                }
            }

            public double GetK(Model model, SolverOptions options)
            {
                var T = model.Data.MeasuredTemperature + 273.15;

                switch (options.AffinityStyle)
                {
                    case Analysis.VariableStyle.SameForAll: return Math.Exp(-1 * dG / (Energy.R.Value * 298.15));
                    case Analysis.VariableStyle.TemperatureDependent: return Math.Exp(-1 * dG / (Energy.R.Value * T));
                    default:
                    case Analysis.VariableStyle.Free: return Math.Exp(-1 * dG / (Energy.R.Value * T));
                }
            }
        }
    }
}
