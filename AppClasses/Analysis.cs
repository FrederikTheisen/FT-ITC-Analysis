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
using static alglib;
using System.ComponentModel;

namespace AnalysisITC
{
    public static class Analysis
    {
        public static Random Random { get; } = new Random();
        public static bool StopAnalysisProcess { get; set; } = false;

        public static event EventHandler AnalysisIterationFinished;
        public static event EventHandler<SolverConvergence> AnalysisFinished;
        public static event EventHandler<Tuple<int, int, float>> BootstrapIterationFinished;

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

        public static bool LockModeIsLoose { get; set; } = false;
        public static float LooseFactor { get; set; } = 0.1f;
        public static bool Hlock { get; set; } = false;
        public static bool Klock { get; set; } = false;
        public static bool Glock { get; set; } = false;
        public static bool Clock { get; set; } = false;
        public static bool Olock { get; set; } = false;
        public static bool Nlock { get; set; } = false;

        static double[] Bounds(double init, bool locked, double dmin, double dmax) => locked && !double.IsNaN(init) ? LockedBounds(init) : new double[] { dmin, dmax };
        static double[] LockedBounds(double init) => LockModeIsLoose ? LooseBounds(init) : new double[] { init * 0.9999, init * 1.0001 };
        static double[] LooseBounds(double init) => new double[] { init * (1 - LooseFactor), init * (1 + LooseFactor) };

        public static double[] Hbounds => Bounds(Hinit, Hlock, -300000, 300000);
        public static double[] Kbounds => Bounds(Kinit, Klock, 1000, 10E12);
        public static double[] Gbounds => Bounds(Ginit, Glock, -500000, -5000);
        public static double[] Cbounds => Bounds(Cinit, Clock, -20000, 20000);
        public static double[] Obounds => Bounds(Oinit, Olock, -50000, 50000);
        public static double[] Nbounds => Bounds(Ninit, Nlock, .1, 10);

        public static SolverAlgorithm Algorithm { get; set; } = SolverAlgorithm.NelderMead;
        public static ErrorEstimationMethod ErrorMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
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

            public static void InitializeAnalyzer(VariableConstraint enthalpystyle, VariableConstraint affinitystyle = VariableConstraint.None, VariableConstraint nstyle = VariableConstraint.None)
            {
                Model = new GlobalModel();

                Model.Options = new SolverOptions()
                {
                    Model = Model,
                    EnthalpyStyle = enthalpystyle,
                    AffinityStyle = affinitystyle,
                    NStyle = nstyle,
                };
            }

            static void InitializeOneSetOfSites()
            {
                foreach (var data in GetValidData())
                {
                    Model.Models.Add(new OneSetOfSites(data));
                    if (data.Solution != null) data.Solution.IsValid = false;

                    data.UpdateSolution(null);
                }

                if (!double.IsNaN(Hinit)) Model.InitialHs = new double[Model.Models.Count].Add(Hinit);
                if (!double.IsNaN(Ginit)) Model.InitialGibbs = new double[Model.Models.Count].Add(Ginit);
                if (!double.IsNaN(Oinit)) Model.InitialOffsets = new double[Model.Models.Count].Add(Oinit);
                if (!double.IsNaN(Ninit)) Model.InitialNs = new double[Model.Models.Count].Add(Ninit);
                if (!double.IsNaN(Cinit)) Model.InitialHeatCapacity = new double[Model.Models.Count].Add(Cinit);
            }

            public static async void Solve(AnalysisModel analysismodel)
            {
                StopAnalysisProcess = false;

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
                            convergence = Model.Solve();

                            NSApplication.SharedApplication.InvokeOnMainThread(() => { AnalysisIterationFinished?.Invoke(null, null); Model.Models.ForEach(m => m.Data.UpdateSolution(null)); });

                            if (ErrorMethod == ErrorEstimationMethod.BootstrapResiduals) Model.Bootstrap();
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

            static void SetInitialValuesFromFitting()
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
                StopAnalysisProcess = false;

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
                    convergence = Model.Solve();

                    NSApplication.SharedApplication.InvokeOnMainThread(() => { AnalysisIterationFinished?.Invoke(null, null); });

                    if (ErrorMethod == ErrorEstimationMethod.BootstrapResiduals) Model.Bootstrap();
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

        public enum VariableConstraint
        {
            None,
            TemperatureDependent,
            SameForAll
        }

        public enum ErrorEstimationMethod
        {
            None,
            BootstrapResiduals
        }

        public enum SolverAlgorithm
        {
            [Description("Nelder-Mead [SIMPLEX]")]
            NelderMead,
            [Description("Levenberg-Marquardt")]
            LevenbergMarquardt
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
        public bool FittedGlobally { get; set; } = false;
        public int ExcludeSinglePoint { get; set; } = -1;

        public double Temperature => Data.MeasuredTemperature;

        public virtual double GuessN => Data.Injections.Last().Ratio / 2;
        public virtual double GuessH => Data.Injections.First(inj => inj.Include).Enthalpy - GuessOffset;
        public virtual double GuessK => 1000000;
        public virtual double GuessGibbs => -35000;
        public virtual double GuessOffset => Data.Injections.Where(inj => inj.Include).TakeLast(2).Average(inj => inj.Enthalpy);
        public virtual double[] InitialGuessVector { get; set; }

        public virtual string ModelName => FittedGlobally ? "Global." : "";

        double LevenbergMarquardtDifferentiationStepSize { get; set; } = 0.00001;
        double LevenbergMarquardtEpsilon { get; set; } = 1E-12;

        public virtual double[] LowerBounds { get; } 
        public virtual double[] UpperBounds { get; }

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

        public void SetBootstrapStart(double[] initialvalues)
        {
            InitialGuessVector = initialvalues;
            LevenbergMarquardtDifferentiationStepSize *= 100;
            LevenbergMarquardtEpsilon *= 1000;
        }

        public virtual double RMSD(double n, double H, double K, double offset, bool isloss = true)
        {
            return 0;
        }

        public virtual double Evaluate(int i, double n, double H, double K, double offset)
        {
            return 0;
        }

        public  virtual void SetBounds(object solver)
        {
            switch (solver)
            {
                case NelderMead simplex:
                    for (int i = 0; i < simplex.NumberOfVariables; i++)
                    {
                        simplex.LowerBounds[i] = LowerBounds[i];
                        simplex.UpperBounds[i] = UpperBounds[i];
                    }
                    break;
                case minlmstate state:
                    alglib.minlmsetbc(state, LowerBounds, UpperBounds);
                    break;
            }
        }

        public SolverConvergence Solve()
        {
            switch (Analysis.Algorithm)
            {
                case Analysis.SolverAlgorithm.NelderMead: return SolveWithNelderMeadAlgorithm();
                case Analysis.SolverAlgorithm.LevenbergMarquardt: return SolveWithLevenbergMarquardt();
                default: return null;
            }
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
            SetBounds(solver);
            solver.Minimize(InitialGuessVector is null ? new double[4] { GuessN, GuessH, GuessK, GuessOffset } : InitialGuessVector);

            Data.Solution = Solution.FromAccordNelderMead(solver.Solution, this, RMSD(solver.Solution[0], solver.Solution[1], solver.Solution[2], solver.Solution[3], false)); // solver.Function(solver.Solution));
            Data.Solution.Convergence = new SolverConvergence(solver);

            return Data.Solution.Convergence;
        }

        public virtual SolverConvergence SolveWithLevenbergMarquardt()
        {
            DateTime start = DateTime.Now;
            int maxits = 0;
            var guess = InitialGuessVector is null ? new double[4] { GuessN, GuessH, GuessK, GuessOffset } : InitialGuessVector;

            alglib.minlmcreatev(4, guess, LevenbergMarquardtDifferentiationStepSize, out minlmstate state);
            alglib.minlmsetcond(state, LevenbergMarquardtEpsilon, maxits);
            alglib.minlmsetscale(state, guess);
            alglib.minlmoptimize(state, (double[] x, double[] fi, object obj) => { fi[0] = this.RMSD(x[0], x[1], x[2], x[3]); }, null, null);
            SetBounds(state);
            alglib.minlmresults(state, out guess, out minlmreport rep);

            Data.Solution = Solution.FromAlgLibLevenbergMarquardt(guess, this, RMSD(guess[0], guess[1], guess[2], guess[3], false));
            Data.Solution.Convergence = new SolverConvergence(state, rep, DateTime.Now - start);

            return Data.Solution.Convergence;
        }

        public void Bootstrap()
        {
            Analysis.ReportBootstrapProgress(0);

            int counter = 0;
            var start = DateTime.Now;
            var solutions = new List<Solution>();

            var res = Parallel.For(0, Analysis.BootstrapIterations, (i) =>
            {
                if (!Analysis.StopAnalysisProcess)
                {
                    var model = this.GenerateSyntheticModel();
                    model.SetBootstrapStart(Solution.Raw);
                    model.Solve();
                    solutions.Add(model.Solution);
                }

                var currcounter = Interlocked.Increment(ref counter);

                Analysis.ReportBootstrapProgress(currcounter);
            });

            Solution.BootstrapSolutions = solutions.Where(sol => !sol.Convergence.Failed).ToList();
            Solution.ComputeErrorsFromBootstrapSolutions();
        }

        public virtual Model GenerateSyntheticModel()
        {
            return new Model(Data.GetSynthClone());
        }
    }

    class OneSetOfSites : Model
    {
        public override string ModelName => base.ModelName + "OneSetOfSites";

        public override double[] LowerBounds { get; } = new double[] { Analysis.Nbounds[0], Analysis.Hbounds[0], Analysis.Kbounds[0], Analysis.Obounds[0] };
        public override double[] UpperBounds { get; } = new double[] { Analysis.Nbounds[1], Analysis.Hbounds[1], Analysis.Kbounds[1], Analysis.Obounds[1] };

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

            return isloss ? loss : Math.Sqrt(loss / Data.Injections.Count(i => i.Include && i.ID != ExcludeSinglePoint));
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

        public double MeanTemperature { get; private set; }
        public Analysis.VariableConstraint EnthalpyStyle => Options.EnthalpyStyle;
        public Analysis.VariableConstraint AffinityStyle => Options.AffinityStyle;
        public Analysis.VariableConstraint NStyle => Options.NStyle;
        int MaximumEvaluations { get; set; } = 300000;
        double AbsoluteFunctionTolerance { get; set; } = double.Epsilon;
        double AbsoluteParameterTolerance { get; set; } = 0.001;
        double RelativeSolutionTolerance { get; set; } = 2E-10;
        double LevenbergMarquardtDifferentiationStepSize { get; set; } = 0.00001;
        double LevenbergMarquardtEpsilon { get; set; } = 1E-11;

        public virtual int GetVariableCount
        {
            get
            {
                int nvar = Models.Count;

                switch (EnthalpyStyle)
                {
                    case Analysis.VariableConstraint.TemperatureDependent: nvar += 2; break;
                    case Analysis.VariableConstraint.None: nvar += Models.Count; break;
                    case Analysis.VariableConstraint.SameForAll:
                    default: nvar += 1; break;
                }

                switch (AffinityStyle)
                {
                    case Analysis.VariableConstraint.None: nvar += Models.Count; break;
                    case Analysis.VariableConstraint.TemperatureDependent:
                    case Analysis.VariableConstraint.SameForAll:
                    default: nvar += 1; break;
                }

                switch (NStyle)
                {
                    case Analysis.VariableConstraint.SameForAll: nvar += 1; break;
                    default: nvar += Models.Count; break;
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
                if (EnthalpyStyle != Analysis.VariableConstraint.TemperatureDependent) return 0;
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
                if (EnthalpyStyle != Analysis.VariableConstraint.TemperatureDependent) return Models.Select(m => m.GuessH).Average();
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
            if (EnthalpyStyle == Analysis.VariableConstraint.None) w.AddRange(Models.Select(m => m.GuessH));
            else w.Add(H0);

            if (EnthalpyStyle == Analysis.VariableConstraint.TemperatureDependent) w.Add(Cp);

            if (AffinityStyle == Analysis.VariableConstraint.None) w.AddRange(G);
            else w.Add(G.First());

            w.AddRange(offsets);

            if (NStyle == Analysis.VariableConstraint.SameForAll) w.Add(ns.First());
            else w.AddRange(ns);

            return w.ToArray();
        }

        double[] StepSizes
        {
            get
            {
                var stepsizes = new List<double>();

                if (EnthalpyStyle == Analysis.VariableConstraint.None) { for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Hstep); }
                else stepsizes.Add(Analysis.Hstep);

                if (EnthalpyStyle == Analysis.VariableConstraint.TemperatureDependent) stepsizes.Add(Analysis.Cstep);

                if (AffinityStyle == Analysis.VariableConstraint.None) { for (int i = 0; i < Models.Count; i++) stepsizes.Add(Analysis.Gstep); }
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

                if (EnthalpyStyle == Analysis.VariableConstraint.None) { for (int i = 0; i < Models.Count; i++) bounds.Add(Analysis.Hbounds[0]); }
                else bounds.Add(Analysis.Hbounds[0]);

                if (EnthalpyStyle == Analysis.VariableConstraint.TemperatureDependent) bounds.Add(Analysis.Cbounds[0]);

                if (AffinityStyle == Analysis.VariableConstraint.None) { for (int i = 0; i < Models.Count; i++) bounds.Add(Analysis.Gbounds[0]); }
                else bounds.Add(Analysis.Gbounds[0]);

                bounds.AddRange(new double[Models.Count].Add(Analysis.Obounds[0]));
                bounds.AddRange(new double[Models.Count].Add(Analysis.Nbounds[0]));

                return bounds.ToArray();
            }
        }

        double[] UpperBounds
        {
            get
            {
                //if (BootstrapInitialValues != null) return BootstrapInitialValues.Select(v => v + 0.1 * Math.Abs(v)).ToArray();
                var bounds = new List<double>();

                if (EnthalpyStyle == Analysis.VariableConstraint.None) { for (int i = 0; i < Models.Count; i++) bounds.Add(Analysis.Hbounds[1]); }
                else bounds.Add(Analysis.Hbounds[1]);

                if (EnthalpyStyle == Analysis.VariableConstraint.TemperatureDependent) bounds.Add(Analysis.Cbounds[1]);

                if (AffinityStyle == Analysis.VariableConstraint.None) { for (int i = 0; i < Models.Count; i++) bounds.Add(Analysis.Gbounds[1]); }
                else bounds.Add(Analysis.Gbounds[1]);

                bounds.AddRange(new double[Models.Count].Add(Analysis.Obounds[1]));
                bounds.AddRange(new double[Models.Count].Add(Analysis.Nbounds[1]));

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
            RelativeSolutionTolerance = 1.5E-3;
            LevenbergMarquardtDifferentiationStepSize *= 2;
            LevenbergMarquardtEpsilon *= 10;
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

        public SolverConvergence Solve()
        {
            Models.ForEach(m => m.FittedGlobally = true);
            MeanTemperature = Models.Average(m => m.Temperature);

            //If no variables are globally constrained then perform individual fitting of each experiment
            if (EnthalpyStyle == Analysis.VariableConstraint.None && 
                AffinityStyle == Analysis.VariableConstraint.None &&
                NStyle == Analysis.VariableConstraint.None)
            {
                var convergence = new List<SolverConvergence>();

                foreach (var model in Models)
                {
                    convergence.Add(model.Solve());
                }

                var output = SolverParameters.FromIndividual(Models.Select(m => m.Solution).ToList(), Options);

                Solution = GlobalSolution.FromAccordNelderMead(output.ToArray(), this);
                Solution.SetConvergence(new SolverConvergence(convergence));

                return Solution.Convergence;
            }
            else switch (Analysis.Algorithm)
                {
                    case Analysis.SolverAlgorithm.NelderMead: return SolveWithNelderMeadAlgorithm();
                    case Analysis.SolverAlgorithm.LevenbergMarquardt: return SolveWithLevenbergMarquardt();
                    default: return null;
                }
        }

        SolverConvergence SolveWithNelderMeadAlgorithm()
        {
            var f = new NonlinearObjectiveFunction(GetVariableCount, (w) => LossFunction(w));
            var solver = new NelderMead(f);

            var stepsizes = StepSizes;

            for (int i = 0; i < solver.StepSize.Length; i++) solver.StepSize[i] = stepsizes[i];

            solver.Convergence = new Accord.Math.Convergence.GeneralConvergence(GetVariableCount)
            {
                MaximumEvaluations = MaximumEvaluations,
                AbsoluteFunctionTolerance = AbsoluteFunctionTolerance,
                RelativeParameterTolerance = RelativeSolutionTolerance,
                StartTime = DateTime.Now,
            };

            SetBounds(solver);

            solver.Minimize(GetStartValues());

            Solution = GlobalSolution.FromAccordNelderMead(solver.Solution, this);
            Solution.SetConvergence(new SolverConvergence(solver));

            return Solution.Convergence;
        }

        SolverConvergence SolveWithLevenbergMarquardt()
        {
            DateTime start = DateTime.Now;
            var varcount = GetVariableCount;
            var guess = GetStartValues();

            alglib.minlmcreatev(varcount, guess, LevenbergMarquardtDifferentiationStepSize, out minlmstate state);
            alglib.minlmsetcond(state, LevenbergMarquardtEpsilon, MaximumEvaluations/3);
            alglib.minlmsetscale(state, guess);
            alglib.minlmsetbc(state, LowerBounds, UpperBounds);
            alglib.minlmoptimize(state, (double[] parameters, double[] fi, object obj) => { fi[0] = LossFunction(parameters); }, null, null);
            alglib.minlmresults(state, out double[] result, out minlmreport rep);

            Solution = GlobalSolution.FromAlgLibLevenbergMarquardt(result, this);
            Solution.SetConvergence(new SolverConvergence(state, rep, DateTime.Now - start));

            return Solution.Convergence;
        }

        public void Bootstrap()
        {
            var solutions = new List<GlobalSolution>();

            int counter = 0;

            Analysis.ReportBootstrapProgress(0);

            var start = DateTime.Now;

            var opt = new ParallelOptions();
            opt.MaxDegreeOfParallelism = 10;

            var res = Parallel.For(0, Analysis.BootstrapIterations, opt, (i) =>
            {
                if (!Analysis.StopAnalysisProcess)
                {
                    var models = new List<Model>();

                    foreach (var m in Models)
                    {
                        var _m = m.GenerateSyntheticModel();
                        _m.SetBootstrapStart(m.Solution.Raw);
                        models.Add(_m);
                    }

                    var gm = new GlobalModel(Solution.Raw.Copy())
                    {
                        Options = Options
                    };

                    gm.Models.AddRange(models);

                    gm.Solve();

                    solutions.Add(gm.Solution);
                }

                var currcounter = Interlocked.Increment(ref counter);

                Analysis.ReportBootstrapProgress(currcounter);
            });

            Solution.SetEnthalpiesFromBootstrap(solutions);

            foreach (var model in Models)
            {
                var sols = solutions.SelectMany(gs => gs.Solutions.Where(s => s.Data.UniqueID == model.Data.UniqueID)).ToList();

                model.Solution.BootstrapSolutions = sols.Where(sol => !sol.Convergence.Failed).ToList();
                model.Solution.ComputeErrorsFromBootstrapSolutions();
            }

            Solution.BootstrapTime = DateTime.Now - start;
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

        public double RMSD(double[] w)
        {
            var parameters = SolverParameters.FromArray(w, Options);

            double glob_loss = 0;

            for (int i = 0; i < Models.Count; i++)
            {
                var m = Models[i];
                var pset = parameters.ParameterSetForModel(i);

                glob_loss += m.RMSD(pset.N, pset.GetEnthalpy(m, Options), pset.GetK(m, Options), pset.Offset, false);
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
        public string Guid { get; private set; } = new Guid().ToString();
        public SolverConvergence Convergence { get; set; }

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

        public List<Solution> BootstrapSolutions { get; set; } = new List<Solution>();

        public Solution()
        {

        }

        public Solution(string guid)
        {
            Guid = guid;
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

        public static Solution FromAlgLibLevenbergMarquardt(double[] parameters, Model model, double loss) => FromAccordNelderMead(parameters, model, loss);
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
                Loss = loss,
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
        public SolverConvergence Convergence { get; private set; }

        public double Loss { get; private set; } = 0;
        public int BootstrapIterations => Solutions[0].BootstrapSolutions.Count;

        public double ReferenceTemperature => Model.MeanTemperature;
        public Energy HeatCapacity { get; private set; } = new(0);
        public Energy StandardEnthalpy { get; private set; } = new(0);//Enthalpy at 298.15 °C
        public Energy ReferenceEnthalpy { get; private set; } = new(); //Fitting reference value

        public LinearFitWithError EnthalpyLine => new LinearFitWithError(HeatCapacity.FloatWithError, ReferenceEnthalpy.FloatWithError, ReferenceTemperature);
        public LinearFitWithError EntropyLine { get; private set; }
        public LinearFitWithError GibbsLine { get; private set; }

        /// <summary>
        /// Parameters of the individual experiments derived from the global solution
        /// </summary>
        public List<Solution> Solutions { get; private set; } = new List<Solution>();
        public TimeSpan BootstrapTime { get; internal set; }

        public void SetEnthalpiesFromBootstrap(List<GlobalSolution> solutions)
        {
            this.SetEnthalpiesFromBootstrap(solutions.Select(gs => gs.ReferenceEnthalpy.Value), solutions.Select(gs => gs.StandardEnthalpy.Value), solutions.Select(gs => gs.HeatCapacity.Value));

            this.SetTemperatureDependeceFromBootstrap(solutions);

            Console.WriteLine("BOOTSTRAP THERMODYNAMICS: " + Model.Models[0].ModelName);
            Console.WriteLine("ENTHALPY: " + EnthalpyLine.Evaluate(25).ToString());
            Console.WriteLine("ENTROPY:  " + EntropyLine.Evaluate(25).ToString());
            Console.WriteLine("GIBBS:    " + GibbsLine.Evaluate(25).ToString());
        }


        public static GlobalSolution FromAlgLibLevenbergMarquardt(double[] solution, GlobalModel model) => FromAccordNelderMead(solution, model);
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

                var dH = model.EnthalpyStyle == Analysis.VariableConstraint.TemperatureDependent ? pset.dH + pset.dCp * dt : pset.dH;
                var K = Math.Exp(pset.dG / (-1 * Energy.R.Value * (m.Data.MeasuredTemperature + 273.15)));

                //var sol = new Solution(pset.N, dH, K, pset.Offset, m, m.RMSD(pset.N, dH, K, pset.Offset, false));
                var sol = Solution.FromAccordNelderMead(new double[] { pset.N, dH, K, pset.Offset }, m, m.RMSD(pset.N, dH, K, pset.Offset, false));

                m.Data.Solution = sol;

                global.Solutions.Add(sol);
            }

            global.Loss = model.RMSD(solution);

            global.SetEntropyTemperatureDependence(model);
            global.SetGibbsTemperatureDependence(model);

            return global;
        }


        public void SetConvergence(SolverConvergence convergence)
        {
            Convergence = convergence;

            Solutions.ForEach(sol => sol.Convergence = convergence);
        }

        void SetEntropyTemperatureDependence(GlobalModel model)
        {
            var xy = model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature - model.MeanTemperature, m.Solution.TdS }).ToArray();
            var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

            //var fit = await LinearFitWithError.FitData(model.Models.Select(m => m.Data.MeasuredTemperature).ToArray(), model.Models.Select(m => m.Solution.TdS.Value).ToArray(), model.MeanTemperature);

            EntropyLine = new LinearFitWithError(reg.B, reg.A, ReferenceTemperature);
        }

        void SetGibbsTemperatureDependence(GlobalModel model)
        {
            var xy = model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature - model.MeanTemperature, m.Solution.GibbsFreeEnergy }).ToArray();
            var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));

            GibbsLine = new LinearFitWithError(reg.B, reg.A, ReferenceTemperature);
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
                case Analysis.VariableConstraint.None: //if dH is free, then fit both reference and standard enthalpies
                    var xy = model.Models.Select((m, i) => new double[] { m.Data.MeasuredTemperature, parameters.Enthalpies[i] }).ToArray();
                    var reg = MathNet.Numerics.LinearRegression.SimpleRegression.Fit(xy.GetColumn(0), xy.GetColumn(1));
                    cp = reg.B;
                    Hstandard = reg.A + 25 * cp;
                    H0 = reg.A + model.MeanTemperature * cp;
                    break;
                case Analysis.VariableConstraint.TemperatureDependent: //if temperature dependent, then the reference is the fit value, propagate to standard enthalpy
                    var dt = 25 - model.MeanTemperature;
                    cp = parameters.HeatCapacity;
                    Hstandard = parameters.HeatCapacity * dt + parameters.Enthalpies.First();
                    H0 = parameters.Enthalpies.First();
                    break;
                case Analysis.VariableConstraint.SameForAll: //if same for all, then fitted value is both reference and standard??
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
        }

        void SetTemperatureDependeceFromBootstrap(List<GlobalSolution> solutions)
        {
            var sfit_slope_dist = solutions.Select(gsol => gsol.EntropyLine.Slope.Value).ToList();
            var sfit_intercept_dist = solutions.Select(gsol => gsol.EntropyLine.Intercept.Value).ToList();
            var gfit_slope_dist = solutions.Select(gsol => gsol.GibbsLine.Slope.Value).ToList();
            var gfit_intercept_dist = solutions.Select(gsol => gsol.GibbsLine.Intercept.Value).ToList();

            EntropyLine = new LinearFitWithError(new FloatWithError(sfit_slope_dist), new FloatWithError(sfit_intercept_dist), Model.MeanTemperature);
            GibbsLine = new LinearFitWithError(new FloatWithError(gfit_slope_dist), new FloatWithError(gfit_intercept_dist), Model.MeanTemperature);
        }
    }
}
