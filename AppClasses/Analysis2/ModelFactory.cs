using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.AppClasses.Analysis2
{
	public static class ModelFactory
	{
		public static Model InitializeModel(ExperimentData data, AnalysisModel type, ModelParameters parameters = null)
		{
            switch (type)
            {
                case AnalysisModel.OneSetOfSites: return OneSetOfSites(data);
                case AnalysisModel.SequentialBindingSites:
                case AnalysisModel.TwoSetsOfSites:
                case AnalysisModel.Dissociation:
                default: throw new NotImplementedException();
            }
        }

		static OneSetOfSites OneSetOfSites(ExperimentData data)
		{
			var model = new OneSetOfSites(data);

			return model;
		}
    }

    public static class GlobalModelFactory
	{
		public static AnalysisModel ModelType { get; set; } = AnalysisModel.OneSetOfSites;
		public static GlobalModelParameters PredefinedParameters { get; set; }

		public static GlobalModel InitializeModel()
		{
			var globalmodel = new GlobalModel();

            foreach (var data in DataManager.Data.Where(d => d.Include))
			{
				var model = ModelFactory.InitializeModel(data, ModelType);

                //calc mdl parameters. perhaps depends on model, how to implement?
                //load parameters into model, overwriting existing default parameters

                globalmodel.Models.Add(model);
			}

			Reset();

            return globalmodel;
		}

		public static void Reset()
		{
            PredefinedParameters.Clear();
			ModelType = AnalysisModel.OneSetOfSites;
        }
	}
}

