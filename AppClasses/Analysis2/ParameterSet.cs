using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class Parameter
    {
        public ParameterTypes Key { get; private set; }
        public double Value { get; private set; }
        public bool IsLocked { get; private set; }
        public double[] Limits { get; private set; }
        public double StepSize { get; private set; }

        public Parameter(ParameterTypes key, double value, bool islocked = false, double[] limits = null, double stepsize = double.NaN)
        {
            Key = key;
            Value = value;
            IsLocked = islocked;
            if (limits == null) Limits = new double[] { double.MinValue, double.MaxValue };
            else Limits = limits;

            if (double.IsNaN(stepsize)) StepSize = Math.Abs(Value / 50) + 100; //guess a reasonable step size
            else StepSize = stepsize;
        }

        public void Update(double value)
        {
            Value = value;

            if (value < Limits[0] || value > Limits[1]) throw new Exception("Parameter out of range");
        }
    }

    public class ModelParameters
    {
        public AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;

        public Dictionary<ParameterTypes, Parameter> Table { get; set; } = new Dictionary<ParameterTypes, Parameter>();

        public int FittingParameterCount => Table.Count(p => !p.Value.IsLocked);

        public void AddParameter(ParameterTypes key, double value, bool islocked = false, double[] limits = null)
        {
            Table.Add(key, new Parameter(key, value, islocked, limits));
        }

        public void UpdateFromArray(double[] globalparameters)
        {
            int index = 0;

            foreach (var parameter in Table)
            {
                if (parameter.Key != parameter.Value.Key) throw new KeyNotFoundException("Parameter key mismatch");
                if (!parameter.Value.IsLocked)
                {
                    parameter.Value.Update(globalparameters[index]);
                    index++;
                }
            }
        }

        /// <summary>
        /// Method to extract initial guess parameter vector
        /// </summary>
        /// <returns></returns>
        public double[] ToArray()
        {
            var w = new List<double>();

            foreach (KeyValuePair<ParameterTypes, Parameter> parameter in Table)
            {
                if (!parameter.Value.IsLocked) w.Add(parameter.Value.Value);
            }

            return w.ToArray();
        }

        public double[] GetStepSizes()
        {
            return Table.Select(p => p.Value).Where(p => !p.IsLocked).Select(p => p.StepSize).ToArray();
        }

        public List<double[]> GetLimits()
        {
            return Table.Select(p => p.Value).Where(p => !p.IsLocked).Select(p => p.Limits).ToList();
        }
    }

    public class GlobalModelParameters
    {
        public Analysis.VariableConstraint EnthalpyStyle { get; set; } = Analysis.VariableConstraint.TemperatureDependent;
        public Analysis.VariableConstraint AffinityStyle { get; set; } = Analysis.VariableConstraint.None;
        public Analysis.VariableConstraint NStyle { get; set; } = Analysis.VariableConstraint.None;

        public Dictionary<ParameterTypes, Parameter> GlobalParameters { get; private set; } = new Dictionary<ParameterTypes, Parameter>();
        public List<ModelParameters> IndividualModelParameterList { get; private set; }

        /// <summary>
        /// Get the number of gloablly fitted parameters for this global model
        /// </summary>
        int GlobalFittingParameterCount => GlobalParameters.Count(p => !p.Value.IsLocked);

        /// <summary>
        /// Check if GlobalModel requires global fitting (any globally fitted parameters?)
        /// </summary>
        public bool RequiresGlobalFitting => GlobalFittingParameterCount == 0;

        public void AddGlobalParameter(ParameterTypes key, double value, bool islocked = false, double[] limits = null)
        {
            GlobalParameters.Add(key, new Parameter(key, value, islocked, limits));
        }

        /// <summary>
        /// Updates ParameterList based on array of doubles.
        /// </summary>
        /// <param name="parameterarray"></param>
        public void UpdateFromArray(double[] parameterarray)
        {
            int index = 0;

            foreach (var parameter in GlobalParameters)
            {
                if (!parameter.Value.IsLocked)
                {
                    parameter.Value.Update(parameterarray[index]);
                    index++;
                }
            }

            foreach (var mdl_params in IndividualModelParameterList)
            {
                int take = mdl_params.FittingParameterCount;

                mdl_params.UpdateFromArray(parameterarray.Skip(index).Take(take).ToArray());

                index += take;
            }
        }

        /// <summary>
        /// Method to extract initial guess parameter vector
        /// </summary>
        /// <returns></returns>
        public double[] ToArray()
        {
            var w = new List<double>();

            foreach (KeyValuePair<ParameterTypes, Parameter> parameter in GlobalParameters)
            {
                if (!parameter.Value.IsLocked) w.Add(parameter.Value.Value);
            }

            foreach (var modelparameterset in IndividualModelParameterList)
            {
                w.AddRange(modelparameterset.ToArray());
            }

            return w.ToArray();
        }

        public ModelParameters GetParametersForModel(GlobalModel parent, Model model)
        {
            int index = parent.Models.IndexOf(model);

            return IndividualModelParameterList[index];
        }
    }

    public enum ParameterTypes
    {
        Nvalue1,
        Nvalue2,
        Enthalpy1,
        Enthalpy2,
        Affinity1,
        Affinity2,
        Offset,
        HeatCapacity1,
        HeatCapacity2
    }
}

