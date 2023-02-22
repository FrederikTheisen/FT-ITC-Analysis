using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public class SolverParameters
    {
        SolverOptions options { get; set; }

        public Analysis.VariableConstraint EnthalpyStyle => options.EnthalpyStyle;
        public Analysis.VariableConstraint AffinityStyle => options.AffinityStyle;
        public Analysis.VariableConstraint NStyle => options.NStyle;
        public int ModelCount => options.ModelCount;

        public List<double> Enthalpies { get; set; } = new List<double>();
        public List<double> Gibbs { get; set; } = new List<double>();
        public List<double> Offsets { get; set; } = new List<double>();
        public List<double> Ns { get; set; } = new List<double>();

        public double HeatCapacity { get; set; } = 0;

        public SolverParameters(SolverOptions options)
        {
            this.options = options;
        }

        public static SolverParameters FromArray(double[] w, SolverOptions options)
        {
            var p = new SolverParameters(options);

            int index = 0;

            switch (options.EnthalpyStyle)
            {
                case Analysis.VariableConstraint.None:
                    p.Enthalpies = w.Take(options.ModelCount).ToList();
                    index += options.ModelCount;
                    break;
                case Analysis.VariableConstraint.TemperatureDependent:
                    p.Enthalpies = w.Take(1).ToList();
                    p.HeatCapacity = w.Skip(1).Take(1).First();
                    index += 2;
                    break;
                case Analysis.VariableConstraint.SameForAll:
                    p.Enthalpies = w.Take(1).ToList();
                    index += 1;
                    break;
            }

            switch (options.AffinityStyle)
            {
                case Analysis.VariableConstraint.None:
                    p.Gibbs = w.Skip(index).Take(options.ModelCount).ToList();
                    index += options.ModelCount;
                    break;
                case Analysis.VariableConstraint.TemperatureDependent:
                case Analysis.VariableConstraint.SameForAll:
                    p.Gibbs = w.Skip(index).Take(1).ToList();
                    index += 1;
                    break;
            }

            p.Offsets = w.Skip(index).Take(options.ModelCount).ToList();
            index += options.ModelCount;

            switch (options.NStyle)
            {
                case Analysis.VariableConstraint.SameForAll: p.Ns = w.Skip(index).Take(1).ToList(); break;
                default: p.Ns = w.Skip(index).Take(options.ModelCount).ToList(); break;
            }

            return p;
        }

        public static SolverParameters FromIndividual(List<Solution> solutions, SolverOptions options)
        {
            var p = new SolverParameters(options);

            p.Ns = solutions.Select(s => s.N.Value).ToList();
            p.Enthalpies = solutions.Select(s => s.Enthalpy.Value).ToList();
            p.Gibbs = solutions.Select(s => s.GibbsFreeEnergy.Value).ToList();
            p.Offsets = solutions.Select(s => s.Offset.Value).ToList();

            return p;
        }

        public double[] ToArray()
        {
            var w = new List<double>();

            w.AddRange(Enthalpies);
            if (EnthalpyStyle == Analysis.VariableConstraint.TemperatureDependent) w.Add(HeatCapacity);
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
                dH = EnthalpyStyle == Analysis.VariableConstraint.None ? Enthalpies[modelnumber] : Enthalpies[0],
                dCp = HeatCapacity,
                dG = AffinityStyle == Analysis.VariableConstraint.None ? Gibbs[modelnumber] : Gibbs[0],
                Offset = Offsets[modelnumber],
                N = NStyle == Analysis.VariableConstraint.None ? Ns[modelnumber] : Ns[0]
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
                    case Analysis.VariableConstraint.TemperatureDependent:
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
                    case Analysis.VariableConstraint.SameForAll: return Math.Exp(-1 * dG / (Energy.R.Value * 298.15));
                    case Analysis.VariableConstraint.TemperatureDependent: return Math.Exp(-1 * dG / (Energy.R.Value * T));
                    default:
                    case Analysis.VariableConstraint.None: return Math.Exp(-1 * dG / (Energy.R.Value * T));
                }
            }

            public double[] ToArray(Model model)
            {
                return new double[] { N, GetK(model, Options), dH, Offset};
            }
        }
    }
}
