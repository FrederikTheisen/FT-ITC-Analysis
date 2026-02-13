using System;
using System.Collections.Generic;
using System.Linq;
using Accord.Math.Distances;
using AnalysisITC.AppClasses.Analysis2.Models;
using AnalysisITC.AppClasses.AnalysisClasses;
//using Microsoft.SolverFoundation.Services;
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

		public ModelFactory(AnalysisModel type)
		{
			ModelType = type;
		}

		/// <summary>
		/// Initialize model factory to build either individual or global fitting model
		/// </summary>
		/// <param name="isglobal"></param>
		/// <returns></returns>
		public static ModelFactory InitializeFactory(AnalysisModel model, bool isglobal)
		{
			try
			{
				if (!DataManager.DataIsLoaded) throw new Exception("No data loaded");

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
			catch (Exception ex)
			{
				AppEventHandler.PrintAndLog(ex.Message);

				return null;
			}
		}

		public static bool IsModelAvailable(AnalysisModel model)
		{
			try
			{
				var SMF = new SingleModelFactory(model);
				SMF.InitializeModel(null);
			}
			catch (NotImplementedException ex) //Exception thrown if mode is not yet implemented
			{
				return false;
			}
			catch (Exception ex) //some other exception may be thrown in this process, but we don't care at this point
			{
				return true;
			}
			return true;
        }

		/// <summary>
		/// Returns list of exposed parameters.
		/// </summary>
		/// <returns></returns>
		/// <exception cref="NotImplementedException"></exception>
		public virtual IEnumerable<Parameter> GetExposedParameters()
		{
			throw new NotImplementedException("ModelFactory.GetExposedParameters()");
		}

		public virtual void ReinitializeParameter(Parameter par)
		{
			throw new NotImplementedException("ModelFactory.ReinitParam");
		}

		public virtual void SetCustomParameter(ParameterType key, double value, bool locked)
		{
            throw new NotImplementedException("ModelFactory.SetCustomParameter()");
        }

        public virtual void SetModelOption(ExperimentAttribute opt)
        {
            throw new NotImplementedException("ModelFactory.SetModelOption()");
        }

        public virtual IDictionary<AttributeKey, ExperimentAttribute> GetExposedModelOptions()
		{
			throw new NotImplementedException("ModelFactory.GetOptions()");
		}

		public virtual void UpdateData()
		{
			throw new NotImplementedException("ModelFactory.UpdateData()");
		}

		public virtual void BuildModel()
		{
			Console.WriteLine("Building model: " + (IsGlobalAnalysis ? "GlobalModel " : "IndividualModel ") + ModelType.ToString());

			switch (this)
			{
				case SingleModelFactory: (this as SingleModelFactory).Model.ModelCloneOptions = ModelCloneOptions.DefaultOptions; break;
				case GlobalModelFactory: (this as GlobalModelFactory).Model.ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions; break;
			}
        }

		public static void Clear()
		{
			var factory = InitializeFactory(Factory.ModelType, Factory.IsGlobalAnalysis);

            foreach (var par in Factory.GetExposedParameters())
            {
				factory.SetCustomParameter(par.Key, par.Value, par.IsLocked);
            }

            foreach (var opt in Factory.GetExposedModelOptions())
            {
				factory.SetModelOption(opt.Value.Copy());
            }

            if (Factory is SingleModelFactory)
			{
				
			}
			else if (Factory is GlobalModelFactory)
			{
				foreach (var con in ((GlobalModelFactory)Factory).GlobalModelParameters.Constraints)
				{
					(factory as GlobalModelFactory).GlobalModelParameters.Constraints.Add(con.Key, con.Value);
				}

                foreach (var par in ((GlobalModelFactory)Factory).GlobalModelParameters.GlobalTable)
                {
					(factory as GlobalModelFactory).GlobalModelParameters.AddorUpdateGlobalParameter(par.Key, par.Value.Value, par.Value.IsLocked, par.Value.Limits);
                }
            }

			Factory = factory;
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

            var parameters = Model?.Parameters.Table.Where(p => p.Value.ChangedByUser);
            var options = Model?.ModelOptions;

            ConstructModel(data);

            Model.InitializeParameters(data);

            if (parameters != null)
            {
                foreach (var (key, par) in parameters)
                {
                    if (Model.Parameters.Table.ContainsKey(key)) SetCustomParameter(par.Key, par.Value, par.IsLocked);
                }
            }
            if (options != null)
            {
                foreach (var (key, opt) in options)
                {
                    if (Model.ModelOptions.ContainsKey(key)) SetModelOption(opt.Copy());
                }
            }
        }

        public void ConstructModel(ExperimentData data)
        {
            switch (ModelType)
            {
                case AnalysisModel.OneSetOfSites: Model = new OneSetOfSites(data); break;
                case AnalysisModel.CompetitiveBinding: Model = new CompetitiveBinding(data); break;
                case AnalysisModel.TwoSetsOfSites: Model = new TwoSetsOfSites(data); break;
                case AnalysisModel.TwoCompetingSites: Model = new TwoCompetingSites(data); break;
                case AnalysisModel.PeptideProlineIsomerization: Model = new OneSiteIsomerization(data); break;
                case AnalysisModel.SequentialBindingSites:
                case AnalysisModel.Dissociation: Model = new Dissociation(data); break;
                default: throw new NotImplementedException("The selected model has not been implemented yet.");
            }
        }

        public override IEnumerable<Parameter> GetExposedParameters()
		{
			return Model.Parameters.Table.Values;
		}

        public override void SetCustomParameter(ParameterType key, double value, bool locked)
        {
			if (!Model.Parameters.Table.ContainsKey(key)) return; // throw new Exception("Parameter not found [File: GlobalFactory.SetCustomParameter]: " + key.ToString());
            Model.Parameters.Table[key].Update(value, locked);
        }

        public override void ReinitializeParameter(Parameter par)
        {
			par.ReinitializeParameter(Model);
        }

        public override void SetModelOption(ExperimentAttribute opt)
        {
			Model.ModelOptions[opt.Key] = opt;
        }

        public override IDictionary<AttributeKey, ExperimentAttribute> GetExposedModelOptions()
        {
			return Model.ModelOptions;
        }

        public override void UpdateData()
        {
			InitializeModel(DataManager.Current);
        }

        public override void BuildModel()
        {
			Model.Data.Model = Model;
            Model.SetModelOptions();
            base.BuildModel();
        }
    }

	public class GlobalModelFactory : ModelFactory
	{
        private static List<Parameter> PrevParameters = new List<Parameter>();

        public GlobalModel Model { get; private set; }
		public GlobalModelParameters GlobalModelParameters { get; private set; }
		private Dictionary<ParameterType, List<VariableConstraint>> ExposedGlobalFittingOptions { get; set; }

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

            var datas = DataManager.Data.Where(d => d.Include).ToList();

            InitializeModel(datas);
        }

        public void InitializeModel(List<ExperimentData> datas)
        {
            Model = new GlobalModel();
            GlobalModelParameters = new GlobalModelParameters();

            foreach (var data in datas)
            {
                Console.WriteLine("Adding data: " + data.FileName);
                var factory = new SingleModelFactory(ModelType);

                factory.InitializeModel(data);

                Model.AddModel(factory.Model);
            }

            InitializeExposedGlobalFittingOptions();

            InitializeGlobalParameters();
        }

        public void InitializeGlobalParameters() //TODO implement global determined
        {
            if (Model.Models == null || Model.Models.Count == 0) throw new Exception("No models in global model");

			StorePrevParameters();

            GlobalModelParameters.ClearGlobalTable();

            var _pars = Model.Models.First().Parameters;

            foreach (var par in _pars.Table.Values)
            {
				Parameter prevvalue = PrevParameters.Exists(p => p.Key == par.Key) ? PrevParameters.Find(p => p.Key == par.Key) : null;

                switch (par.Key)
                {
                    case ParameterType.Nvalue1:
                    case ParameterType.Nvalue2:
                        switch (GlobalModelParameters.GetConstraintForParameter(par.Key))
                        {
                            case VariableConstraint.SameForAll:
                                GlobalModelParameters.AddorUpdateGlobalParameter(
                                    key: par.Key,
                                    value: prevvalue != null ? prevvalue.Value : Model.Models.Average(mdl => mdl.GuessN()),
                                    islocked: prevvalue != null ? prevvalue.IsLocked : false);
                                break;
							default: break;
                        }
                        break;
                    case ParameterType.Enthalpy1:
                    case ParameterType.Enthalpy2:
                        switch (GlobalModelParameters.GetConstraintForParameter(par.Key))
                        {
                            case VariableConstraint.None: break;
                            case VariableConstraint.SameForAll:
                                GlobalModelParameters.AddorUpdateGlobalParameter(
                                    key: par.Key,
                                    value: prevvalue != null ? prevvalue.Value : Model.Models.Average(mdl => mdl.GuessEnthalpy()),
                                    islocked: prevvalue != null ? prevvalue.IsLocked : false);
                                break;
                            case VariableConstraint.TemperatureDependent:
								var prevdCp = PrevParameters.Find(p => Parameter.Equal(p.Key, ParameterType.HeatCapacity1));

                                GlobalModelParameters.AddorUpdateGlobalParameter(
									key: par.Key == ParameterType.Enthalpy1 ? ParameterType.HeatCapacity1 : ParameterType.HeatCapacity2,
									value: prevdCp != null ? prevdCp.Value : 0,
									islocked: prevdCp != null ? prevdCp.IsLocked : false);
                                GlobalModelParameters.AddorUpdateGlobalParameter(
									key: par.Key,
									value: prevvalue != null ? prevvalue.Value : Model.Models.Average(mdl => mdl.GuessEnthalpy()),
									islocked: prevvalue != null ? prevvalue.IsLocked : false);
                                break;
                        }
                        break;
                    case ParameterType.Affinity1:
                    case ParameterType.Affinity2:
                        switch (GlobalModelParameters.GetConstraintForParameter(par.Key))
                        {
                            case VariableConstraint.SameForAll:
                            case VariableConstraint.TemperatureDependent:
								GlobalModelParameters.AddorUpdateGlobalParameter(
									key: par.Key == ParameterType.Affinity1 ? ParameterType.Gibbs1 : ParameterType.Gibbs2,
									value: prevvalue != null ? (double)prevvalue.Value : Model.Models.Average(mdl => mdl.GuessAffinityAsGibbs()),
									islocked: prevvalue != null ? prevvalue.IsLocked : false);
                                break;
							default: break;
                        }
                        break;
                    case ParameterType.IsomerizationEquilibriumConstant:
                        {
                            switch (GlobalModelParameters.GetConstraintForParameter(par.Key))
                            {
                                case VariableConstraint.SameForAll:
                                    GlobalModelParameters.AddorUpdateGlobalParameter(
                                    key: par.Key,
                                    value: prevvalue != null ? (double)prevvalue.Value : 0.42,
                                    islocked: prevvalue != null ? prevvalue.IsLocked : false);
                                    break;
                                default: break;
                            }
                        }
                        break;
                    default: break;
                }
            }
        }

        void InitializeExposedGlobalFittingOptions()
		{
			if (Model.Models == null || Model.Models.Count == 0) return;

            var dict = new Dictionary<ParameterType, List<VariableConstraint>>();

            var _pars = Model.Models.First().Parameters;

            //Loop exposed parameters
            foreach (var par in _pars.Table.Values)
			{
				switch (par.Key)
				{
					//Temperature dependent variables
					case ParameterType.Affinity1:
					case ParameterType.Affinity2:
                        dict[par.Key] = new List<VariableConstraint> { VariableConstraint.None, VariableConstraint.TemperatureDependent };
                        break;
                    case ParameterType.Enthalpy1:
					case ParameterType.Enthalpy2:
						if (Model.TemperatureDependenceExposed) dict[par.Key] = new List<VariableConstraint> { VariableConstraint.None, VariableConstraint.TemperatureDependent, VariableConstraint.SameForAll};
						else dict[par.Key] = new List<VariableConstraint> { VariableConstraint.None, VariableConstraint.SameForAll };
						break;
					//Not temperature dependent variables
                    case ParameterType.Nvalue1:
					case ParameterType.Nvalue2:
						dict[par.Key] = new List<VariableConstraint> { VariableConstraint.None, VariableConstraint.SameForAll };
						break;
                    case ParameterType.IsomerizationEquilibriumConstant:
                        dict[par.Key] = new List<VariableConstraint> { VariableConstraint.None, VariableConstraint.SameForAll };
                        break;
                    default: Console.WriteLine(par.Key.ToString() + " not handled by factory"); break;
                }
			}

            ExposedGlobalFittingOptions = dict;
		}

		public Dictionary<ParameterType, List<VariableConstraint>> GetExposedConstraints()
		{
			return ExposedGlobalFittingOptions;
		}

        public override IEnumerable<Parameter> GetExposedParameters()
        {
			return Parameters;
        }

        public override IDictionary<AttributeKey, ExperimentAttribute> GetExposedModelOptions()
        {
            return Model.Models.First().ModelOptions;
        }

        public override void SetCustomParameter(ParameterType key, double value, bool locked)
        {
			if (!GlobalModelParameters.GlobalTable.ContainsKey(key)) return; // throw new Exception("Parameter not found [File: GlobalFactory.SetCustomParameter]: " + key.ToString());
			GlobalModelParameters.GlobalTable[key].Update(value, locked);
        }

        public override void ReinitializeParameter(Parameter par)
        {
			var ghostfactory = ModelFactory.InitializeFactory(ModelType, true) as GlobalModelFactory;
			var constraints = GetExposedConstraints().Keys.ToList();
			foreach (var con in constraints) ghostfactory.GlobalModelParameters.SetConstraintForParameter(con, GlobalModelParameters.GetConstraintForParameter(con));
			ghostfactory.InitializeGlobalParameters();

            par.ReinitializeParameter(ghostfactory.GetExposedParameters().First(p => p.Key == par.Key).Value);
        }

        public override void SetModelOption(ExperimentAttribute opt)
        {
			Model.Models.First().ModelOptions[opt.Key] = opt;
        }

        public override void UpdateData()
        {
			Model.Models.Clear();

            var datas = DataManager.Data.Where(d => d.Include).ToList();

            foreach (var data in datas)
            {
                Console.WriteLine("Adding data: " + data.FileName);
                var factory = new SingleModelFactory(ModelType);

                factory.InitializeModel(data);

                Model.AddModel(factory.Model);
            }

            InitializeExposedGlobalFittingOptions();
            InitializeGlobalParameters();
        }

        public override void BuildModel()
        {
			GlobalModelParameters.IndividualModelParameterList.Clear();

            Model.Parameters = GlobalModelParameters;

            foreach (var mdl in Model.Models)
			{
				mdl.Data.Model = mdl;
                mdl.ModelCloneOptions = GlobalModelParameters.RequiresGlobalFitting ? ModelCloneOptions.DefaultGlobalOptions : ModelCloneOptions.DefaultOptions;
				mdl.SetModelOptions(Model.Models.First().ModelOptions);
                GlobalModelParameters.AddIndivdualParameter(mdl.Parameters);
            }

            GlobalModelParameters.SetIndividualFromGlobal();

            base.BuildModel();
        }
    }
}