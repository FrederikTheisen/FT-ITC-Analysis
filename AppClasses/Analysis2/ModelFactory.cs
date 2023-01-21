using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2
{
	/// <summary>
	/// ModelFactory manufactures the model and is used to retrieve objects necessary to construct UI
	/// </summary>
	public class ModelFactory
	{
		public static event EventHandler<ModelFactory> UpdateFactory;

		public static AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;
		public bool IsGlobalAnalysis => this is GlobalModelFactory;

		/// <summary>
		/// Initialize model factory to build either individual or global fitting model
		/// </summary>
		/// <param name="isglobal"></param>
		/// <returns></returns>
		public static ModelFactory InitializeFactory(bool isglobal)
		{
			//Initialize ModelFactory
			ModelFactory factory;

			if (isglobal)
			{
				factory = new GlobalModelFactory();

				(factory as GlobalModelFactory).InitializeModel();
			}
			else
			{
				factory = new SingleModelFactory();

				(factory as SingleModelFactory).InitializeModel(DataManager.Current);
			}

			UpdateFactory.Invoke(factory, null);

			return factory;
		}

		/// <summary>
		/// Returns list of exposed parameters.
		/// </summary>
		/// <returns></returns>
		/// <exception cref="NotImplementedException"></exception>
		public virtual IEnumerable<Parameter> GetExposedParameters()
		{
			throw new NotImplementedException();
		}

		public void SetCustomParameter(Parameter parameter)
		{

		}
	}

	public class SingleModelFactory : ModelFactory
	{
		public Model Model { get; private set; }

		public void InitializeModel(ExperimentData data)
		{
			switch (ModelType)
			{
				case AnalysisModel.OneSetOfSites: Model = InitializeOneSetOfSites(data); break;
				case AnalysisModel.TwoSetsOfSites: Model = InitializeTwoSetsOfSites(data); break;
				case AnalysisModel.SequentialBindingSites:
				case AnalysisModel.Dissociation:
				default: throw new NotImplementedException();
			}
		}

		private OneSetOfSites InitializeOneSetOfSites(ExperimentData data)
		{
			var model = new OneSetOfSites(data);

			model.Parameters.AddParameter(ParameterTypes.Nvalue1, model.GuessN(), limits: new double[] { 0.1, 10 });
            model.Parameters.AddParameter(ParameterTypes.Enthalpy1, model.GuessEnthalpy(), limits: new double[] { -500000, 500000 });
            model.Parameters.AddParameter(ParameterTypes.Affinity1, model.GuessAffinity(), limits: new double[] { 10E-12, 0.1 });
            model.Parameters.AddParameter(ParameterTypes.Offset, model.GuessOffset(), limits: new double[] { -500000, 500000 });

            return model;
		}

		private TwoSetsOfSites InitializeTwoSetsOfSites(ExperimentData data)
		{
			var model = new TwoSetsOfSites(data);

            model.Parameters.AddParameter(ParameterTypes.Nvalue1, model.GuessN(), limits: new double[] { 0.1, 10 });
            model.Parameters.AddParameter(ParameterTypes.Enthalpy1, model.GuessEnthalpy() / 2, limits: new double[] { -500000, 500000 });
            model.Parameters.AddParameter(ParameterTypes.Affinity1, model.GuessAffinity(), limits: new double[] { 10E-12, 0.1 });
            model.Parameters.AddParameter(ParameterTypes.Nvalue2, model.GuessN(), limits: new double[] { 0.1, 10 });
            model.Parameters.AddParameter(ParameterTypes.Enthalpy2, model.GuessEnthalpy() / 2, limits: new double[] { -500000, 500000 });
            model.Parameters.AddParameter(ParameterTypes.Affinity2, model.GuessAffinity(), limits: new double[] { 10E-12, 0.1 });
            model.Parameters.AddParameter(ParameterTypes.Offset, model.GuessOffset(), limits: new double[] { -500000, 500000 });

            return model;
		}

		public override IEnumerable<Parameter> GetExposedParameters()
		{
			return Model.Parameters.Table.Values;
		}
	}

	public class GlobalModelFactory : ModelFactory
	{
		public GlobalModel Model { get; private set; }

		public List<Parameter> GlobalParameters { get; private set; }

		public GlobalModelParameters GlobalModelParameters { get; private set; }

        public void InitializeModel()
		{
			Model = new GlobalModel();

			foreach (var data in DataManager.Data.Where(d => d.Include))
			{
				var factory = new SingleModelFactory();

				factory.InitializeModel(data);

				//calc mdl parameters. perhaps depends on model, how to implement?
				//load parameters into model, overwriting existing default parameters

				Model.Models.Add(factory.Model);
			}

			InitializeParameters();
		}

        public void InitializeParameters()
        {
            if (Model.Models is null || Model.Models.Count == 0) throw new Exception("No model in global model");

            var type = Model.Models.First().ModelType;

            var _pars = Model.Models.First().Parameters;

            foreach (var par in _pars.Table.Values)
            {
                switch (par.Key)
                {
                    case ParameterTypes.Nvalue1:
                    case ParameterTypes.Nvalue2:
                        switch (GlobalModelParameters.NStyle)
                        {
                            case Analysis.VariableConstraint.SameForAll:
                                GlobalModelParameters.AddGlobalParameter(
                                    key: par.Key == ParameterTypes.Nvalue1 ? ParameterTypes.Nvalue1 : ParameterTypes.Nvalue2,
                                    value: Model.Models.Average(mdl => mdl.GuessN()),
                                    islocked: false,
                                    limits: new double[] { 0.1, 10 });
                                break;
							default: break;
                        }
                        break;
                    case ParameterTypes.Enthalpy1:
                    case ParameterTypes.Enthalpy2:
                        switch (GlobalModelParameters.EnthalpyStyle)
                        {
                            case Analysis.VariableConstraint.None: break;
                            case Analysis.VariableConstraint.SameForAll:
                                GlobalModelParameters.AddGlobalParameter(
                                    key: par.Key == ParameterTypes.Enthalpy1 ? ParameterTypes.Enthalpy1 : ParameterTypes.Enthalpy2,
                                    value: Model.Models.Average(mdl => mdl.GuessEnthalpy()),
                                    islocked: false,
                                    limits: new double[] { -500000, 500000 });
                                break;
                            case Analysis.VariableConstraint.TemperatureDependent:
								GlobalModelParameters.AddGlobalParameter(
									key: par.Key == ParameterTypes.Enthalpy1 ? ParameterTypes.HeatCapacity1 : ParameterTypes.HeatCapacity2,
									value: 0,
									islocked: false,
									limits: new double[] { -100000, 100000 });
                                GlobalModelParameters.AddGlobalParameter(
									key: par.Key == ParameterTypes.Enthalpy1 ? ParameterTypes.Enthalpy1 : ParameterTypes.Enthalpy2,
									value: Model.Models.Average(mdl => mdl.GuessEnthalpy()),
									islocked: false,
									limits: new double[] { -500000, 500000 });
                                break;
                        }
                        break;
                    case ParameterTypes.Affinity1:
                    case ParameterTypes.Affinity2:
                        switch (GlobalModelParameters.AffinityStyle)
                        {
                            case Analysis.VariableConstraint.SameForAll:
                            case Analysis.VariableConstraint.TemperatureDependent:
                                GlobalModelParameters.AddGlobalParameter(
                                    key: par.Key == ParameterTypes.Affinity1 ? ParameterTypes.Affinity1 : ParameterTypes.Affinity2,
                                    value: Model.Models.Average(mdl => mdl.GuessAffinity()),
                                    islocked: false,
                                    limits: new double[] { -500000, 500000 });
                                break;
							default: break;
                        }
                        break;
                    default: break;
                }
            }
        }

        public override IEnumerable<Parameter> GetExposedParameters()
        {
            return GlobalParameters;
        }

        public Dictionary<ParameterTypes,List<Analysis.VariableConstraint>> GetExposedGlobalFittingOptions()
		{
			var dict = new Dictionary<ParameterTypes, List<Analysis.VariableConstraint>>();

			bool tempdependenceenabled = false;

            if (Model.Models.Count > 1)
            {
                var min = Model.Models.Min(mdl => mdl.Data.MeasuredTemperature);
                var max = Model.Models.Max(mdl => mdl.Data.MeasuredTemperature);

                if (max - min > AppSettings.MinimumTemperatureSpanForFitting) tempdependenceenabled = true;
                
            }

            foreach (var par in GetExposedParameters())
			{
				switch (par.Key)
				{
					
				}
			}


            

			return dict;
		}
	}
}