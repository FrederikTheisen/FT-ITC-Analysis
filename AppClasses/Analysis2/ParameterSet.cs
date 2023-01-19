using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.Optimization;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class Solver
    {
        Model Model { get; set; }

        public SolverConvergence Fit(Analysis.SolverAlgorithm algorithm)
        {
            var starttime = DateTime.Now;

            switch (algorithm)
            {
                case Analysis.SolverAlgorithm.NelderMead:
                    break;
                case Analysis.SolverAlgorithm.LevenbergMarquardt:
                    break;
            }
        }

        SolverConvergence SolveUsingNelderMead()
        {

        }
    }

    public struct Parameter
    {
        public double Value { get; set; }
        public bool IsLocked { get; set; }
        public double[] Limits { get; set; }

        public Parameter(double value, bool islocked = false, double[] limits = null)
        {
            Value = value;
            IsLocked = islocked;
            if (limits == null) Limits = new double[] { double.MinValue, double.MaxValue };
            else Limits = limits;
        }
    }

    public class ModelParameters
    {
        public AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;

        public Dictionary<string, Parameter> Table { get; set; } = new Dictionary<string, Parameter>();

        public int Length => Table.Count;

        public ModelParameters()
        {
        }

        public void AddParameter(string key, double value, bool islocked = false, double[] limits = null)
        {
            Table.Add(key, new Parameter(value, islocked, limits));
        }

        public static ModelParameters FromArray(double[] parameters)
        {
            return new ModelParameters();
        }

        public static ModelParameters FromMathNetArray(MathNet.Numerics.LinearAlgebra.Vector<double> parameters)
        {
            return FromArray(parameters.ToArray());
        }

        public double[] ToArray() 
        {
            var w = new List<double>();

            foreach (KeyValuePair<string,Parameter> parameter in Table)
            {
                if (!parameter.Value.IsLocked) w.Add(parameter.Value.Value);
            }

            return w.ToArray();
        }
    }

    public class GlobalModelParameters
    {
        Dictionary<string,Parameter> GlobalParameters { get; set; }
        public List<ModelParameters> ParameterList { get; private set; }
        //could be things like dCp, dH0, N, dG

        public void FromArray(double[] globalparameters)
        {
            
        }

        public double[] ToArray()
        {
            var w = new List<double>();

            foreach (KeyValuePair<string,Parameter> parameter in GlobalParameters)
            {
                if (!parameter.Value.IsLocked) w.Add(parameter.Value.Value);
            }

            foreach (var modelparameterset in ParameterList)
            {
                w.AddRange(modelparameterset.ToArray());
            }

            return w.ToArray();
        }
    }
}

