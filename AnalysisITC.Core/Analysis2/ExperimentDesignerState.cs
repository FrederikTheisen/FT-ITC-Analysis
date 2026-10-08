using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// Generation inputs for one experiment designer window. Parameter values and model
    /// options entered by the user are kept per key across setup and model changes, including
    /// keys the current model does not use. Values are recorded only from user edits, never
    /// read back from a model, so fitted values cannot become generation inputs.
    /// </summary>
    internal sealed class ExperimentDesignerState
    {
        internal const double DefaultEnthalpy = -30000;
        internal const double DefaultNValue = 1;

        readonly Dictionary<ParameterType, double> parameters = new Dictionary<ParameterType, double>();
        readonly Dictionary<AttributeKey, ExperimentAttribute> options = new Dictionary<AttributeKey, ExperimentAttribute>();

        // Defaults of the factory the designer currently generates with.
        Dictionary<ParameterType, double> defaults = new Dictionary<ParameterType, double>();
        SingleModelFactory defaultsFactory;

        public void RememberParameter(ParameterType key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            parameters[key] = value;
        }

        public void ForgetParameter(ParameterType key) => parameters.Remove(key);

        public bool TryGetParameter(ParameterType key, out double value) => parameters.TryGetValue(key, out value);

        public void RememberOption(ExperimentAttribute option)
        {
            if (option == null) return;
            options[option.Key] = option.Copy();
        }

        public bool TryGetOption(AttributeKey key, out ExperimentAttribute option)
        {
            option = options.TryGetValue(key, out var stored) ? stored.Copy() : null;
            return option != null;
        }

        /// <summary>
        /// Creates a generation factory for the data and applies the remembered options and
        /// parameter values. Parameters without a remembered value use the designer defaults.
        /// The state is unchanged if initialization fails.
        /// </summary>
        public SingleModelFactory CreateFactory(AnalysisModel model, ExperimentData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var factory = new SingleModelFactory(model);
            factory.InitializeModel(data);

            foreach (var key in factory.Model.ModelOptions.Keys.ToList())
            {
                if (!options.TryGetValue(key, out var option)) continue;

                var copy = option.Copy();
                copy.OptionName = factory.Model.ModelOptions[key].OptionName;
                factory.Model.ModelOptions[key] = copy;
            }

            // Options define the parameter table shape, so restore values afterwards.
            factory.Model.ApplyModelOptions();

            var factoryDefaults = new Dictionary<ParameterType, double>();
            ApplyParameters(factory, factoryDefaults);

            defaults = factoryDefaults;
            defaultsFactory = factory;
            return factory;
        }

        /// <summary>
        /// Sets every parameter of the factory to its generation value. Use after an option
        /// changes the parameter table, or to undo values written to the model by a fit.
        /// </summary>
        public void ApplyParameters(SingleModelFactory factory)
        {
            if (factory == null) return;
            if (!ReferenceEquals(factory, defaultsFactory))
            {
                defaults = new Dictionary<ParameterType, double>();
                defaultsFactory = factory;
            }

            ApplyParameters(factory, defaults);
        }

        void ApplyParameters(SingleModelFactory factory, Dictionary<ParameterType, double> factoryDefaults)
        {
            foreach (var parameter in factory.GetExposedParameters())
            {
                if (!factoryDefaults.ContainsKey(parameter.Key))
                    factoryDefaults[parameter.Key] = DesignerDefault(parameter);

                parameter.Update(parameters.TryGetValue(parameter.Key, out var value)
                    ? Clamp(value, parameter.Limits)
                    : factoryDefaults[parameter.Key]);
            }
        }

        static double DesignerDefault(Parameter parameter)
        {
            var parent = parameter.Key.GetProperties().ParentType;
            if (parent == ParameterType.Enthalpy1) return DefaultEnthalpy;
            if (parent == ParameterType.Nvalue1) return DefaultNValue;
            return parameter.Value;
        }

        static double Clamp(double value, double[] limits)
        {
            if (limits == null || limits.Length < 2) return value;
            return Math.Min(Math.Max(value, limits[0]), limits[1]);
        }
    }
}
