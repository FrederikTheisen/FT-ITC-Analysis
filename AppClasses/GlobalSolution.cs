using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math;

namespace AnalysisITC
{
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
