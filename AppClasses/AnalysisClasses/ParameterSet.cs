using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AnalysisITC.AppClasses.Analysis2.Models;
using AnalysisITC.Utils;

namespace AnalysisITC.AppClasses.Analysis2
{
    public class Parameter
    {
        public ParameterType Key { get; private set; }
        public double Value { get; private set; }
        public bool IsLocked { get; private set; }
        public double[] Limits { get; private set; }
        public double StepSize { get; private set; }
        public bool ChangedByUser { get; private set; } = false;

        public Parameter(ParameterType key, double value, bool islocked = false)
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

#if DEBUG
            if (value < Limits[0] || value > Limits[1])
                throw new Exception("Parameter out of range: " + Key.ToString() + " " + value.ToString() + " [" + Limits[0].ToString() + " - " + Limits[1].ToString() + "]");
#endif
        }

        public void Update(double value, bool lockpar)
        {
            Update(value);

            IsLocked = lockpar;
            ChangedByUser = true;
        }

        public void ReinitializeParameter(Model model)
        {
            this.Value = ModelFactory.InitializeFactory(model.ModelType, false).GetExposedParameters().First(p => p.Key == this.Key).Value;
            IsLocked = false;
            ChangedByUser = false;
        }

        public void ReinitializeParameter(double value)
        {
            this.Value = value;
            IsLocked = false;
            ChangedByUser = false;
        }

        public override string ToString()
        {
            return Key.ToString() + ": " + Value.ToString("G3");
        }

        public static bool Equal(ParameterType t1, ParameterType t2)
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
        public Dictionary<ParameterType, Parameter> Table { get; set; } = new Dictionary<ParameterType, Parameter>();
        public double ExperimentTemperature { get; private set; }

        public int FittingParameterCount => Table.Count(p => !p.Value.IsLocked);

        public ModelParameters(ExperimentData data)
        {
            ExperimentTemperature = data.MeasuredTemperatureKelvin;
        }

        public void AddOrUpdateParameter(ParameterType key, double value, bool islocked = false)
        {
            if (Table.Keys.Contains(key)) Table[key] = new Parameter(key, value, islocked);
            else Table.Add(key, new Parameter(key, value, islocked));
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

            foreach (KeyValuePair<ParameterType, Parameter> parameter in Table)
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
        public Dictionary<ParameterType, VariableConstraint> Constraints { get; private set; } = new Dictionary<ParameterType, VariableConstraint>();

        //Properties
        public Dictionary<ParameterType, Parameter> GlobalTable { get; private set; } = new Dictionary<ParameterType, Parameter>();
        public List<ModelParameters> IndividualModelParameterList { get; private set; } = new List<ModelParameters>();

        //Property derived values
        int GlobalFittingParameterCount => GlobalTable.Count(p => !p.Value.IsLocked);
        int IndividualModelParameterCount => IndividualModelParameterList.Sum(pars => pars.FittingParameterCount);
        public int TotalFittingParameters => GlobalFittingParameterCount + IndividualModelParameterCount;
        double ReferenceTemperature => IndividualModelParameterList.Average(pars => pars.ExperimentTemperature);
        public bool RequiresGlobalFitting => GlobalFittingParameterCount > 0;

        public void ClearGlobalTable() => GlobalTable.Clear();

        public void AddorUpdateGlobalParameter(ParameterType key, double value, bool islocked = false, double[] limits = null)
        {
            if (GlobalTable.Keys.Contains(key)) GlobalTable[key] = new Parameter(key, value, islocked);           
            else GlobalTable.Add(key, new Parameter(key, value, islocked));
        }

        public void AddIndivdualParameter(ModelParameters parameters)
        {
            IndividualModelParameterList.Add(parameters);
        }

        public VariableConstraint GetConstraintForParameter(ParameterType key)
        {
            if (Constraints.ContainsKey(key)) return Constraints[key];
            else return VariableConstraint.None;
        }

        public void SetConstraintForParameter(ParameterType key, VariableConstraint constraint)
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
                        case ParameterType.Nvalue1:
                        case ParameterType.Nvalue2:
                            if (GetConstraintForParameter(par.Key) == VariableConstraint.SameForAll)
                                par.Value.SetGlobal(GlobalTable[par.Key].Value);
                            break;
                        case ParameterType.Enthalpy1:
                        case ParameterType.Enthalpy2:
                            if (GetConstraintForParameter(par.Key) == VariableConstraint.TemperatureDependent)
                            {
                                var refT = ReferenceTemperature;
                                var mdlT = paramset.ExperimentTemperature;
                                var dT = mdlT - refT;
                                var dH = par.Key switch
                                {
                                    ParameterType.Enthalpy2 => GlobalTable[ParameterType.Enthalpy2].Value + dT * GlobalTable[ParameterType.HeatCapacity2].Value,
                                    _ => GlobalTable[ParameterType.Enthalpy1].Value + dT * GlobalTable[ParameterType.HeatCapacity1].Value,
                                };
                                par.Value.SetGlobal(dH);
                            }
                            else if (GetConstraintForParameter(par.Key) == VariableConstraint.SameForAll)
                                par.Value.SetGlobal(GlobalTable[par.Key].Value);
                            else if (GlobalTable.ContainsKey(par.Key) && !double.IsNaN(GlobalTable[par.Key].Value))
                                par.Value.Update(GlobalTable[par.Key].Value, GlobalTable[par.Key].IsLocked);
                            break;
                        case ParameterType.Affinity1:
                        case ParameterType.Affinity2:
                            if (GetConstraintForParameter(par.Key) == VariableConstraint.TemperatureDependent)
                            {
                                var _par = par.Key switch
                                {
                                    ParameterType.Affinity1 => ParameterType.Gibbs1,
                                    ParameterType.Affinity2 => ParameterType.Gibbs2
                                };

                                par.Value.SetGlobal(Math.Exp(-GlobalTable[_par].Value / (Energy.R * paramset.ExperimentTemperature)));
                            }
                            break;
                        case ParameterType.IsomerizationEquilibriumConstant:
                            if (GetConstraintForParameter(par.Key) == VariableConstraint.SameForAll)
                            {
                                par.Value.SetGlobal(GlobalTable[par.Key].Value);
                            }
                            break;
                        default:
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

            foreach (KeyValuePair<ParameterType, Parameter> parameter in GlobalTable)
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

            foreach (KeyValuePair<ParameterType, Parameter> parameter in GlobalTable)
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

    public class ParameterTypeAttribute : DescriptionAttribute
    {
        public string AttributedNameString { get; private set; }
        public double DefaultStepSize { get; private set; }
        public double[] DefaultLimits { get; private set; }
        public ParameterType ParentType { get; private set; }
        public int NumberSubscript { get; private set; } = 1;

        public ParameterTypeAttribute(string name, ParameterType parent) : base(name)
        {
            var parentatt = parent.GetProperties();

            AttributedNameString = parentatt.AttributedNameString;
            DefaultLimits = parentatt.DefaultLimits;
            DefaultStepSize = parentatt.DefaultStepSize;

            ParentType = parent;

            var m = Regex.Match(name, @"\d+");

            if (m.Success)
            {
                NumberSubscript = int.Parse(m.Value);
            }
        }

        public ParameterTypeAttribute(string name, string attstr, double stepsize, double[] limits, ParameterType parent) : base(name)
        {
            AttributedNameString = attstr;
            DefaultStepSize = stepsize;
            DefaultLimits = limits;

            ParentType = parent;
        }

        public static bool ContainsTwo(IEnumerable<ParameterType> list, ParameterType query)
        {
            return list.Where(p => p.GetProperties().ParentType == query).Count() > 1;
        }

        public static string TableHeaderTitle(ParameterType key, bool containstwo)
        {
            switch (key)
            {
                case ParameterType.Nvalue1: return "N" + (containstwo ? "1" : "");
                case ParameterType.Nvalue2: return "N2";
                case ParameterType.Enthalpy1: return "∆H" + (containstwo ? "1" : "");
                case ParameterType.Enthalpy2: return "∆H2";
                case ParameterType.Affinity1: return "Kd" + (containstwo ? "1" : "");
                case ParameterType.Affinity2: return "Kd2";
                case ParameterType.EntropyContribution1: return "-T∆S" + (containstwo ? "1" : "");
                case ParameterType.EntropyContribution2: return "-T∆S2";
                case ParameterType.Gibbs1: return "∆G" + (containstwo ? "1" : "");
                case ParameterType.Gibbs2: return "∆G2";
                case ParameterType.IsomerizationEquilibriumConstant: return "Keq";
                case ParameterType.IsomerizationRate:
                default: AppEventHandler.DisplayHandledException(new NotImplementedException("[ParameterSet.cs] TableHeaderNotImplementedException: " + key.ToString())); return "err";
            }
        }

        public static string TableHeader(ParameterType key, bool containstwo, EnergyUnit energyunit, string kdunit)
        {
            string s = TableHeaderTitle(key, containstwo);

            switch (key.GetProperties().ParentType)
            {
                default:
                case ParameterType.Nvalue1: return s;
                case ParameterType.Affinity1:  return s + " (" + kdunit + ")";
                case ParameterType.Enthalpy1: return s + " (" + energyunit.GetUnit() + "/mol)";
                case ParameterType.Gibbs1: return s + " (" + energyunit.GetUnit() + "/mol)";
                case ParameterType.EntropyContribution1: return s + " (" + energyunit.GetUnit() + "/mol)";
            }
        }

        public static bool IsEnergyUnitParameter(ParameterType parameter)
        {
            switch (parameter.GetProperties().ParentType)
            {
                case ParameterType.Enthalpy1:
                case ParameterType.Offset:
                case ParameterType.HeatCapacity1:
                case ParameterType.Gibbs1:
                case ParameterType.EntropyContribution1: return true;
                default: return false;
            }
        }
    }

    public enum ParameterType
    {
        [ParameterTypeAttribute("N-value", "N", 0.05, new double[] { 0.1, 10 }, ParameterType.Nvalue1)]
        Nvalue1,
        [ParameterTypeAttribute("N-value 2", ParameterType.Nvalue1)]
        Nvalue2,
        [ParameterTypeAttribute("Enthalpy", "∆*H*", 1000, new double[] { -300000, 300000 }, ParameterType.Enthalpy1)]
        Enthalpy1,
        [ParameterTypeAttribute("Enthalpy 2", ParameterType.Enthalpy1)]
        Enthalpy2,
        [ParameterTypeAttribute("Affinity", "*K*{d}", 100000, new double[] { 10, 100000000000 }, ParameterType.Affinity1)]
        Affinity1,
        [ParameterTypeAttribute("Affinity 2", ParameterType.Affinity1)]
        Affinity2,
        [ParameterTypeAttribute("Offset", "Offset", 500, new double[] { -30000, 30000 }, ParameterType.Offset)]
        Offset,
        [ParameterTypeAttribute("Heat capacity", "∆*C*{p}", 500, new double[] { -20000, 20000 }, ParameterType.HeatCapacity1)]
        HeatCapacity1,
        [ParameterTypeAttribute("Heat capacity 2", ParameterType.HeatCapacity1)]
        HeatCapacity2,
        [ParameterTypeAttribute("Gibbs free energy", "∆*G*", 500, new double[] { -100000, -10000 }, ParameterType.Gibbs1)]
        Gibbs1,
        [ParameterTypeAttribute("Gibbs free energy 2", ParameterType.Gibbs1)]
        Gibbs2,
        [ParameterTypeAttribute("Entropy", "∆*S*", 5, null, ParameterType.Entropy1)]
        Entropy1,
        [ParameterTypeAttribute("Entropy 2", ParameterType.Entropy1)]
        Entropy2,
        [ParameterTypeAttribute("Entropy contribution", "-*T*∆*S*", 1000, null, EntropyContribution1)]
        EntropyContribution1,
        [ParameterTypeAttribute("Entropy contribution 2", EntropyContribution1)]
        EntropyContribution2,
        [ParameterTypeAttribute("Isomerization rate constant", "*k*{iso}", 0.01, new double[] { 0.00001, 1 }, IsomerizationRate)]
        IsomerizationRate,
        [ParameterTypeAttribute("Equilibrium constant", "*K*{eq}", 0.01, new double[] { 0.001, 1000 }, IsomerizationEquilibriumConstant)]
        IsomerizationEquilibriumConstant,
    }
}

