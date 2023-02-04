using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SolverFoundation.Services;
using static CoreFoundation.DispatchSource;

namespace AnalysisITC.AppClasses.Analysis2
{
	/// <summary>
	/// ModelFactory manufactures the model and is used to retrieve objects necessary to construct UI
	/// </summary>
	public class ModelFactory
	{
		public static event EventHandler<ModelFactory> UpdateFactory;

		public AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;
		public bool IsGlobalAnalysis => this is GlobalModelFactory;

		public ModelFactory(AnalysisModel model)
		{
			ModelType = model;
		}

		/// <summary>
		/// Initialize model factory to build either individual or global fitting model
		/// </summary>
		/// <param name="isglobal"></param>
		/// <returns></returns>
		public static ModelFactory InitializeFactory(AnalysisModel model, bool isglobal)
		{
			Console.WriteLine("Initializing ModelFactory...");
			ModelFactory factory;

			if (isglobal)
			{
				factory = new GlobalModelFactory(model);

				(factory as GlobalModelFactory).InitializeModel();
			}
			else
			{
				factory = new SingleModelFactory(model);

				(factory as SingleModelFactory).InitializeModel(DataManager.Current);
			}

			UpdateFactory?.Invoke(factory, null);

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

		public virtual void BuildModel()
		{
			Console.WriteLine("Building model: " + (IsGlobalAnalysis ? "GlobalModel " : "IndividualModel ") + ModelType.ToString());
        }
	}

	public class SingleModelFactory : ModelFactory
	{
        public Model Model { get; private set; }

        public SingleModelFactory(AnalysisModel model) : base(model)
        {
        }

        public void InitializeModel(ExperimentData data)
		{
            Console.WriteLine("Initializing SingleModelFactory...");
            switch (ModelType)
			{
				case AnalysisModel.OneSetOfSites: Model = new OneSetOfSites(data); break;
				case AnalysisModel.TwoSetsOfSites: Model = new TwoSetsOfSites(data); break;
				case AnalysisModel.SequentialBindingSites:
				case AnalysisModel.Dissociation:
				default: throw new NotImplementedException();
			}

            Model.InitializeParameters(data);
        }

		public override IEnumerable<Parameter> GetExposedParameters()
		{
			return Model.Parameters.Table.Values;
		}

        public override void BuildModel()
        {
			Model.Data.Model = Model;

            base.BuildModel();
        }
    }

	public class GlobalModelFactory : ModelFactory
	{
        public GlobalModel Model { get; private set; }
		public GlobalModelParameters GlobalModelParameters { get; private set; }
		public Dictionary<ParameterTypes, List<Analysis.VariableConstraint>> ExposedGlobalFittingOptions { get; private set; }

		public List<Parameter> Parameters => GlobalModelParameters.GlobalTable.Values.ToList();

        public GlobalModelFactory(AnalysisModel model) : base(model)
        {
			
        }

        public void InitializeModel()
		{
			Console.WriteLine("Initializing GlobalModelFactory...");
			Model = new GlobalModel();
			GlobalModelParameters = new GlobalModelParameters();

            foreach (var data in DataManager.Data.Where(d => d.Include))
			{
				Console.WriteLine("Adding data: " + data.FileName);
				var factory = new SingleModelFactory(ModelType);

				factory.InitializeModel(data);
				factory.BuildModel();

				Model.AddModel(factory.Model);
			}

			InitializeExposedGlobalFittingOptions();

            InitializeGlobalParameters();
        }

        public void InitializeGlobalParameters()
        {
            if (Model.Models is null || Model.Models.Count == 0) throw new Exception("No models in global model");

			var prevparams = new List<Parameter>(Parameters);

            var _pars = Model.Models.First().Parameters;

            foreach (var par in _pars.Table.Values)
            {
				double? prevvalue = prevparams.Exists(p => p.Key == par.Key) ? prevparams.Find(p => p.Key == par.Key).Value : null;

                switch (par.Key)
                {
                    case ParameterTypes.Nvalue1:
                    case ParameterTypes.Nvalue2:
                        switch (GlobalModelParameters.NStyle)
                        {
                            case Analysis.VariableConstraint.SameForAll:
                                GlobalModelParameters.AddorUpdateGlobalParameter(
                                    key: par.Key,
                                    value: prevvalue != null ? (double)prevvalue : Model.Models.Average(mdl => mdl.GuessN()),
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
                                GlobalModelParameters.AddorUpdateGlobalParameter(
                                    key: par.Key,
                                    value: prevvalue != null ? (double)prevvalue : Model.Models.Average(mdl => mdl.GuessEnthalpy()),
                                    islocked: false,
                                    limits: new double[] { -500000, 500000 });
                                break;
                            case Analysis.VariableConstraint.TemperatureDependent:
								GlobalModelParameters.AddorUpdateGlobalParameter(
									key: par.Key == ParameterTypes.Enthalpy1 ? ParameterTypes.HeatCapacity1 : ParameterTypes.HeatCapacity2,
									value: prevvalue != null ? (double)prevvalue : 0,
									islocked: false,
									limits: new double[] { -100000, 100000 });
                                GlobalModelParameters.AddorUpdateGlobalParameter(
									key: par.Key,
									value: prevvalue != null ? (double)prevvalue : Model.Models.Average(mdl => mdl.GuessEnthalpy()),
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
                                GlobalModelParameters.AddorUpdateGlobalParameter(
                                    key: par.Key == ParameterTypes.Affinity1 ? ParameterTypes.Gibbs1 : ParameterTypes.Gibbs2,
                                    value: prevvalue != null ? (double)prevvalue : Model.Models.Average(mdl => mdl.GuessAffinity()),
                                    islocked: false,
                                    limits: new double[] { -2000, -70000 });
                                break;
							default: break;
                        }
                        break;
                    default: break;
                }
            }
        }

        void InitializeExposedGlobalFittingOptions()
		{
			var dict = new Dictionary<ParameterTypes, List<Analysis.VariableConstraint>>();

			bool tempdependenceenabled = false;

            if (Model.Models.Count > 1)
            {
                var min = Model.Models.Min(mdl => mdl.Data.MeasuredTemperature);
                var max = Model.Models.Max(mdl => mdl.Data.MeasuredTemperature);

                if (max - min > AppSettings.MinimumTemperatureSpanForFitting) tempdependenceenabled = true;
            }

            var _pars = Model.Models.First().Parameters;

            //Loop exposed parameters
            foreach (var par in _pars.Table.Values)
			{
				switch (par.Key)
				{
					//Temperature dependent variables
					case ParameterTypes.Affinity1:
					case ParameterTypes.Affinity2:
                        dict[par.Key] = new List<Analysis.VariableConstraint> { Analysis.VariableConstraint.None, Analysis.VariableConstraint.TemperatureDependent };
                        break;
                    case ParameterTypes.Enthalpy1:
					case ParameterTypes.Enthalpy2:
						if (tempdependenceenabled) dict[par.Key] = new List<Analysis.VariableConstraint> { Analysis.VariableConstraint.None, Analysis.VariableConstraint.TemperatureDependent, Analysis.VariableConstraint.SameForAll};
						else dict[par.Key] = new List<Analysis.VariableConstraint> { Analysis.VariableConstraint.None, Analysis.VariableConstraint.SameForAll };
						break;
					//Not temperature dependent variables
                    case ParameterTypes.Nvalue1:
					case ParameterTypes.Nvalue2:
						dict[par.Key] = new List<Analysis.VariableConstraint> { Analysis.VariableConstraint.None, Analysis.VariableConstraint.SameForAll };
						break;
				}
			}

            ExposedGlobalFittingOptions = dict;
		}

		public Dictionary<ParameterTypes, List<Analysis.VariableConstraint>> GetExposedOptions()
		{
			return ExposedGlobalFittingOptions;
		}

        public override IEnumerable<Parameter> GetExposedParameters()
        {
			return Parameters;
        }

        public override void BuildModel()
        {
			Model.Parameters = GlobalModelParameters;

            foreach (var mdl in Model.Models)
			{
				mdl.Data.Model = mdl;

				//foreach (var par in mdl.Parameters.Table) //Update and lock parameters based on global parameters
				//{
    //                switch (par.Key)
    //                {
    //                    case ParameterTypes.Nvalue1:
    //                    case ParameterTypes.Nvalue2: if (GlobalModelParameters.NStyle == Analysis.VariableConstraint.SameForAll) par.Value.SetGlobal(GlobalModelParameters.GlobalTable[par.Key].Value); break;
    //                    case ParameterTypes.Enthalpy1:
    //                    case ParameterTypes.Enthalpy2:
    //                        if (GlobalModelParameters.EnthalpyStyle == Analysis.VariableConstraint.TemperatureDependent)
    //                        {
    //                            var refT = Model.ReferenceTemperature;
    //                            var mdlT = mdl.Data.MeasuredTemperature;
    //                            var dT = mdlT - refT;
    //                            var dH = par.Key switch
    //                            {
    //                                ParameterTypes.Enthalpy2 => GlobalModelParameters.GlobalTable[ParameterTypes.Enthalpy2].Value + dT * GlobalModelParameters.GlobalTable[ParameterTypes.HeatCapacity2].Value,
    //                                _ => GlobalModelParameters.GlobalTable[ParameterTypes.Enthalpy1].Value + dT * GlobalModelParameters.GlobalTable[ParameterTypes.HeatCapacity1].Value,
    //                            };
    //                            par.Value.SetGlobal(dH);
    //                        }
    //                        else if (GlobalModelParameters.EnthalpyStyle == Analysis.VariableConstraint.SameForAll) par.Value.SetGlobal(GlobalModelParameters.GlobalTable[par.Key].Value);
    //                        else if (!double.IsNaN(GlobalModelParameters.GlobalTable[par.Key].Value)) par.Value.Update(GlobalModelParameters.GlobalTable[par.Key].Value, GlobalModelParameters.GlobalTable[par.Key].IsLocked);
    //                        break;
    //                    case ParameterTypes.Affinity1:
    //                    case ParameterTypes.Affinity2: if (GlobalModelParameters.AffinityStyle == Analysis.VariableConstraint.TemperatureDependent) par.Value.SetGlobal(Math.Exp(-GlobalModelParameters.GlobalTable[ParameterTypes.Gibbs1].Value / (Energy.R * mdl.Data.MeasuredTemperature))); break;
    //                }
    //            }

                GlobalModelParameters.AddIndivdualParameter(mdl.Parameters);
            }

            GlobalModelParameters.SetIndividualFromGlobal();

            base.BuildModel();
        }
    }
}