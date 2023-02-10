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

        public static ModelFactory Factory { get; set; }

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
			throw new NotImplementedException("Generic GetExposedParameters not implemented");
		}

		public virtual void SetCustomParameter(ParameterTypes key, double value, bool locked)
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

        public virtual void SetCustomParameter(ParameterTypes key, double value, bool locked)
        {

        }

        public override void BuildModel()
        {
			Model.Data.Model = Model;

            base.BuildModel();
        }
    }

	public class GlobalModelFactory : ModelFactory
	{
        private static List<Parameter> PrevParameters = new List<Parameter>();

        public GlobalModel Model { get; private set; }
		public GlobalModelParameters GlobalModelParameters { get; private set; }
		private Dictionary<ParameterTypes, List<Analysis.VariableConstraint>> ExposedGlobalFittingOptions { get; set; }

		public List<Parameter> Parameters => GlobalModelParameters.GlobalTable.Values.ToList();

        public GlobalModelFactory(AnalysisModel model) : base(model)
        {
			
        }

		void StorePrevParameters()
		{
			foreach (var par in Parameters)
			{
				PrevParameters.RemoveAll(p => p.Key == par.Key);

				PrevParameters.Add(par);
			}
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

			StorePrevParameters();

            GlobalModelParameters.ClearGlobalTable();

            var _pars = Model.Models.First().Parameters;

            foreach (var par in _pars.Table.Values)
            {
				Parameter? prevvalue = PrevParameters.Exists(p => p.Key == par.Key) ? PrevParameters.Find(p => p.Key == par.Key) : null;

                switch (par.Key)
                {
                    case ParameterTypes.Nvalue1:
                    case ParameterTypes.Nvalue2:
                        switch (GlobalModelParameters.GetConstraintForParameter(par.Key))
                        {
                            case Analysis.VariableConstraint.SameForAll:
                                GlobalModelParameters.AddorUpdateGlobalParameter(
                                    key: par.Key,
                                    value: prevvalue != null ? prevvalue.Value : Model.Models.Average(mdl => mdl.GuessN()),
                                    islocked: prevvalue != null ? prevvalue.IsLocked : false);
                                break;
							default: break;
                        }
                        break;
                    case ParameterTypes.Enthalpy1:
                    case ParameterTypes.Enthalpy2:
                        switch (GlobalModelParameters.GetConstraintForParameter(par.Key))
                        {
                            case Analysis.VariableConstraint.None: break;
                            case Analysis.VariableConstraint.SameForAll:
                                GlobalModelParameters.AddorUpdateGlobalParameter(
                                    key: par.Key,
                                    value: prevvalue != null ? prevvalue.Value : Model.Models.Average(mdl => mdl.GuessEnthalpy()),
                                    islocked: prevvalue != null ? prevvalue.IsLocked : false);
                                break;
                            case Analysis.VariableConstraint.TemperatureDependent:
								var prevdCp = PrevParameters.Find(p => Parameter.Equal(p.Key, ParameterTypes.HeatCapacity1));

                                GlobalModelParameters.AddorUpdateGlobalParameter(
									key: par.Key == ParameterTypes.Enthalpy1 ? ParameterTypes.HeatCapacity1 : ParameterTypes.HeatCapacity2,
									value: prevdCp != null ? prevdCp.Value : 0,
									islocked: prevdCp != null ? prevdCp.IsLocked : false);
                                GlobalModelParameters.AddorUpdateGlobalParameter(
									key: par.Key,
									value: prevvalue != null ? prevvalue.Value : Model.Models.Average(mdl => mdl.GuessEnthalpy()),
									islocked: prevvalue != null ? prevvalue.IsLocked : false);
                                break;
                        }
                        break;
                    case ParameterTypes.Affinity1:
                    case ParameterTypes.Affinity2:
                        switch (GlobalModelParameters.GetConstraintForParameter(par.Key))
                        {
                            case Analysis.VariableConstraint.SameForAll:
                            case Analysis.VariableConstraint.TemperatureDependent:
								GlobalModelParameters.AddorUpdateGlobalParameter(
									key: par.Key == ParameterTypes.Affinity1 ? ParameterTypes.Gibbs1 : ParameterTypes.Gibbs2,
									value: prevvalue != null ? (double)prevvalue.Value : Model.Models.Average(mdl => mdl.GuessAffinityAsGibbs()),
									islocked: prevvalue != null ? prevvalue.IsLocked : false);
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

        public override void SetCustomParameter(ParameterTypes key, double value, bool locked)
        {
			if (!GlobalModelParameters.GlobalTable.ContainsKey(key)) throw new Exception("Parameter not found [File: GlobalFactory.SetCustomParameter]: " + key.ToString());
			GlobalModelParameters.GlobalTable[key].Update(value, locked);
        }

        public override void BuildModel()
        {
			GlobalModelParameters.IndividualModelParameterList.Clear();

			Model.Parameters = GlobalModelParameters;

            foreach (var mdl in Model.Models)
			{
				mdl.Data.Model = mdl;

                GlobalModelParameters.AddIndivdualParameter(mdl.Parameters);
            }

            GlobalModelParameters.SetIndividualFromGlobal();

            base.BuildModel();
        }
    }
}