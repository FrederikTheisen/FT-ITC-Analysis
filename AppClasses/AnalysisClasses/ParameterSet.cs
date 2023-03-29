using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class Parameter
    {
        public ParameterTypes Key { get; private set; }
        public double Value { get; private set; }
        public bool IsLocked { get; private set; }
        public double[] Limits { get; private set; }
        public double StepSize { get; private set; }
        public bool Changed { get; set; } = true;

        public Parameter(ParameterTypes key, double value, bool islocked = false)
        {
            Key = key;
            Value = value;
            IsLocked = islocked;

            Limits = key.GetProperties().DefaultLimits;
            if (AppSettings.EnableExtendedParameterLimits)
            {
                if (Limits[0] > 0) //Lower value cannot be negative
                {
                    Limits[0] *= 0.1;
                    Limits[1] *= 10;
                }
                else //Lower value can be negative or is zero
                {
                    Limits[0] *= 10;
                    Limits[1] *= 10;
                }
            }

            StepSize = key.GetProperties().DefaultStepSize;
        }

        //public Parameter(ParameterTypes key, double value, bool islocked = false, double[] limits = null, double stepsize = double.NaN)
        //{
        //    Key = key;
        //    Value = value;
        //    IsLocked = islocked;
        //    if (limits == null) Limits = key.GetProperties().DefaultLimits;
        //    else Limits = limits;

        //    if (double.IsNaN(stepsize)) StepSize = key.GetProperties().DefaultStepSize;
        //    else StepSize = stepsize;

        //    if (StepSize == 0) throw new Exception("Parameter initialized with stepsize = 0");
        //}

        /// <summary>
        /// Set the parameter value from the global parameters and tells the model that the parameter should not be fitted
        /// </summary>
        /// <param name="value"></param>
        public void SetGlobal(double value)
        {
            Update(value);
            IsLocked = true;
        }

        public void Update(double value)
        {
            Value = value;

            if (value < Limits[0] || value > Limits[1])
                throw new Exception("Parameter out of range: " + Key.ToString() + " " + value.ToString() + " [" + Limits[0].ToString() + " - " + Limits[1].ToString() + "]");
        }

        public void Update(double value, bool lockpar)
        {
            Update(value);

            IsLocked = lockpar;
        }

        public override string ToString()
        {
            return Key.ToString() + ": " + Value.ToString("G3");
        }

        public static bool Equal(ParameterTypes t1, ParameterTypes t2)
        {
            if (t1 == t2) return true;
            else if (t1.GetProperties().ParentType == t2) return true;
            else if (t2.GetProperties().ParentType == t1) return true;
            else return false;
        }
    }

    public class ModelParameters
    {
        public AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;
        public Dictionary<ParameterTypes, Parameter> Table { get; set; } = new Dictionary<ParameterTypes, Parameter>();
        public double ExperimentTemperature { get; private set; }

        public int FittingParameterCount => Table.Count(p => !p.Value.IsLocked);

        public ModelParameters(ExperimentData data)
        {
            ExperimentTemperature = data.MeasuredTemperatureKelvin;
        }

        public void AddParameter(ParameterTypes key, double value, bool islocked = false)
        {
            Table.Add(key, new Parameter(key, value, islocked));
        }

        public void UpdateFromArray(double[] parameters)
        {
            int index = 0;

            foreach (var parameter in Table)
            {
                if (parameter.Key != parameter.Value.Key) throw new KeyNotFoundException("Parameter key mismatch");
                if (!parameter.Value.IsLocked)
                {
                    parameter.Value.Update(parameters[index]);
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
        //Settings
        public Dictionary<ParameterTypes, VariableConstraint> Constraints { get; private set; } = new Dictionary<ParameterTypes, VariableConstraint>();

        //Properties
        public Dictionary<ParameterTypes, Parameter> GlobalTable { get; private set; } = new Dictionary<ParameterTypes, Parameter>();
        public List<ModelParameters> IndividualModelParameterList { get; private set; } = new List<ModelParameters>();

        //Property derived values
        int GlobalFittingParameterCount => GlobalTable.Count(p => !p.Value.IsLocked);
        int IndividualModelParameterCount => IndividualModelParameterList.Sum(pars => pars.FittingParameterCount);
        public int TotalFittingParameters => GlobalFittingParameterCount + IndividualModelParameterCount;
        double ReferenceTemperature => IndividualModelParameterList.Average(pars => pars.ExperimentTemperature);
        public bool RequiresGlobalFitting => GlobalFittingParameterCount > 0;

        public void ClearGlobalTable() => GlobalTable.Clear();

        public void AddorUpdateGlobalParameter(ParameterTypes key, double value, bool islocked = false, double[] limits = null)
        {
            if (GlobalTable.Keys.Contains(key)) GlobalTable[key] = new Parameter(key, value, islocked);           
            else GlobalTable.Add(key, new Parameter(key, value, islocked));
        }

        public void AddIndivdualParameter(ModelParameters parameters)
        {
            IndividualModelParameterList.Add(parameters);
        }

        public VariableConstraint GetConstraintForParameter(ParameterTypes key)
        {
            if (Constraints.ContainsKey(key)) return Constraints[key];
            else return VariableConstraint.None;
        }

        public void SetConstraintForParameter(ParameterTypes key, VariableConstraint constraint)
        {
            Constraints[key] = constraint;
        }

        /// <summary>
        /// Updates ParameterList based on array of doubles.
        /// </summary>
        /// <param name="parameterarray"></param>
        public void UpdateFromArray(double[] parameterarray)
        {
            int index = 0;

            foreach (var parameter in GlobalTable)
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

            SetIndividualFromGlobal();
        }

        public void SetIndividualFromGlobal()
        {
            foreach (var paramset in IndividualModelParameterList)
            {
                foreach (var par in paramset.Table) //Update and lock parameters based on global parameters
                {
                    switch (par.Key)
                    {
                        case ParameterTypes.Nvalue1:
                        case ParameterTypes.Nvalue2:
                            if (GetConstraintForParameter(par.Key) == VariableConstraint.SameForAll)
                                par.Value.SetGlobal(GlobalTable[par.Key].Value);
                            break;
                        case ParameterTypes.Enthalpy1:
                        case ParameterTypes.Enthalpy2:
                            if (GetConstraintForParameter(par.Key) == VariableConstraint.TemperatureDependent)
                            {
                                var refT = ReferenceTemperature;
                                var mdlT = paramset.ExperimentTemperature;
                                var dT = mdlT - refT;
                                var dH = par.Key switch
                                {
                                    ParameterTypes.Enthalpy2 => GlobalTable[ParameterTypes.Enthalpy2].Value + dT * GlobalTable[ParameterTypes.HeatCapacity2].Value,
                                    _ => GlobalTable[ParameterTypes.Enthalpy1].Value + dT * GlobalTable[ParameterTypes.HeatCapacity1].Value,
                                };
                                par.Value.SetGlobal(dH);
                            }
                            else if (GetConstraintForParameter(par.Key) == VariableConstraint.SameForAll)
                                par.Value.SetGlobal(GlobalTable[par.Key].Value);
                            else if (GlobalTable.ContainsKey(par.Key) && !double.IsNaN(GlobalTable[par.Key].Value))
                                par.Value.Update(GlobalTable[par.Key].Value, GlobalTable[par.Key].IsLocked);
                            break;
                        case ParameterTypes.Affinity1:
                        case ParameterTypes.Affinity2:
                            if (GetConstraintForParameter(par.Key) == VariableConstraint.TemperatureDependent)
                                par.Value.SetGlobal(Math.Exp(-GlobalTable[ParameterTypes.Gibbs1].Value / (Energy.R * paramset.ExperimentTemperature)));
                            break;
                    }
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

            foreach (KeyValuePair<ParameterTypes, Parameter> parameter in GlobalTable)
            {
                if (!parameter.Value.IsLocked) w.Add(parameter.Value.Value);
            }

            foreach (var modelparameterset in IndividualModelParameterList)
            {
                w.AddRange(modelparameterset.ToArray());
            }

            return w.ToArray();
        }

        public List<Parameter> GetParameters()
        {
            var w = new List<Parameter>();

            foreach (KeyValuePair<ParameterTypes, Parameter> parameter in GlobalTable)
            {
                if (!parameter.Value.IsLocked) w.Add(parameter.Value);
            }

            foreach (var modelparameterset in IndividualModelParameterList)
            {
                w.AddRange(modelparameterset.Table.Values.Where(p => !p.IsLocked));
            }

            return w;
        }

        public ModelParameters GetParametersForModel(GlobalModel parent, Model model)
        {
            int index = parent.Models.IndexOf(model);

            return IndividualModelParameterList[index];
        }

        public double[] GetStepSizes()
        {
            var stepsize = new List<double>();
            stepsize.AddRange(GlobalTable.Select(p => p.Value).Where(p => !p.IsLocked).Select(p => p.StepSize));
            foreach (var par in IndividualModelParameterList)
            {
                stepsize.AddRange(par.GetStepSizes());
            }

            return stepsize.ToArray();
        }

        public List<double[]> GetLimits()
        {
            var limits = new List<double[]>();
            limits.AddRange(GlobalTable.Select(p => p.Value).Where(p => !p.IsLocked).Select(p => p.Limits));
            foreach (var par in IndividualModelParameterList)
            {
                limits.AddRange(par.GetLimits());
            }

            return limits;
        }
    }

    public class ParameterTypesAttribute : DescriptionAttribute
    {
        public double DefaultStepSize { get; private set; }
        public double[] DefaultLimits { get; private set; }
        public ParameterTypes ParentType { get; private set; }

        public ParameterTypesAttribute(string name, ParameterTypes parent) : base(name)
        {
            var att = parent.GetProperties();

            DefaultLimits = att.DefaultLimits;
            DefaultStepSize = att.DefaultStepSize;

            ParentType = parent;
        }

        public ParameterTypesAttribute(string name, double stepsize, double[] limits, ParameterTypes parent) : base(name)
        {
            DefaultStepSize = stepsize;
            DefaultLimits = limits;

            ParentType = parent;
        }

        public static string TableHeaderTitle(ParameterTypes key, bool containstwo)
        {
            switch (key)
            {
                case ParameterTypes.Nvalue1: return "N" + (containstwo ? "1" : "");
                case ParameterTypes.Nvalue2: return "N2";
                case ParameterTypes.Enthalpy1: return "∆H" + (containstwo ? "1" : "");
                case ParameterTypes.Enthalpy2: return "∆H2";
                case ParameterTypes.Affinity1: return "Kd" + (containstwo ? "1" : "");
                case ParameterTypes.Affinity2: return "Kd2";
                case ParameterTypes.EntropyContribution1: return "-T∆S" + (containstwo ? "1" : "");
                case ParameterTypes.EntropyContribution2: return "-T∆S2";
                case ParameterTypes.Gibbs1: return "∆G" + (containstwo ? "1" : "");
                case ParameterTypes.Gibbs2: return "∆G2";
                default: throw new NotImplementedException("TableHeaderNotImplementedException: " + key.ToString());
            }
        }

        public static string TableHeader(ParameterTypes key, bool containstwo, EnergyUnit energyunit, string kdunit)
        {
            string s = TableHeaderTitle(key, containstwo);

            switch (key.GetProperties().ParentType)
            {
                default:
                case ParameterTypes.Nvalue1: return s;
                case ParameterTypes.Affinity1:  return s + " (" + kdunit + ")";
                case ParameterTypes.Enthalpy1: return s + " (" + energyunit.GetUnit() + "/mol)";
                case ParameterTypes.Gibbs1: return s + " (" + energyunit.GetUnit() + "/mol)";
                case ParameterTypes.EntropyContribution1: return s + " (" + energyunit.GetUnit() + "/mol)";
            }
        }
    }

    public enum ParameterTypes
    {
        [ParameterTypesAttribute("N-value", 0.05, new double[] { 0.1, 10 }, ParameterTypes.Nvalue1)]
        Nvalue1,
        [ParameterTypesAttribute("N-value 2", ParameterTypes.Nvalue1)]
        Nvalue2,
        [ParameterTypesAttribute("Enthalpy", 1000, new double[] { -300000, 300000 }, ParameterTypes.Enthalpy1)]
        Enthalpy1,
        [ParameterTypesAttribute("Enthalpy 2", ParameterTypes.Enthalpy1)]
        Enthalpy2,
        [ParameterTypesAttribute("Affinity", 100000, new double[] { 10, 100000000000 }, ParameterTypes.Affinity1)]
        Affinity1,
        [ParameterTypesAttribute("Affinity 2", ParameterTypes.Affinity1)]
        Affinity2,
        [ParameterTypesAttribute("Offset", 500, new double[] { -30000, 30000 }, ParameterTypes.Offset)]
        Offset,
        [ParameterTypesAttribute("Heat capacity", 500, new double[] { -20000, 20000 }, ParameterTypes.HeatCapacity1)]
        HeatCapacity1,
        [ParameterTypesAttribute("Heat capacity 2", ParameterTypes.HeatCapacity1)]
        HeatCapacity2,
        [ParameterTypesAttribute("Gibbs free energy", 500, new double[] { -100000, -10000 }, ParameterTypes.Gibbs1)]
        Gibbs1,
        [ParameterTypesAttribute("Gibbs free energy 2", ParameterTypes.Gibbs1)]
        Gibbs2,
        [ParameterTypesAttribute("Entropy", 5, null, ParameterTypes.Entropy1)]
        Entropy1,
        [ParameterTypesAttribute("Entropy 2", ParameterTypes.Entropy1)]
        Entropy2,
        [ParameterTypesAttribute("Entropy contribution", 1000, null, EntropyContribution1)]
        EntropyContribution1,
        [ParameterTypesAttribute("Entropy contribution 2", EntropyContribution1)]
        EntropyContribution2,
        [ParameterTypesAttribute("Isomerization rate constant", 0.00001, new double[] { 0.00001, 1 }, IsomerizationRate)]
        IsomerizationRate,
        [ParameterTypesAttribute("Isomerization equilibrium constant", 0.001, new double[] { 0.001, 1000 }, IsomerizationEquilibriumConstant)]
        IsomerizationEquilibriumConstant,
    }
}

