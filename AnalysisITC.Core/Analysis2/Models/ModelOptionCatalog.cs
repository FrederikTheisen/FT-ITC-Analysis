using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis.Models
{
    /// <summary>Canonical model option defaults, independent of experiment data.</summary>
    public static class ModelOptionCatalog
    {
        public static IDictionary<AttributeKey, ExperimentAttribute> CreateOptions(AnalysisModel model)
        {
            var options = new Dictionary<AttributeKey, ExperimentAttribute>();
            void Add(ExperimentAttribute option) => options.Add(option.Key, option);
            switch (model)
            {
                case AnalysisModel.OneSetOfSites:
                    Add(ExperimentAttribute.Bool(AttributeKey.UseSyringeActiveFraction, AttributeKey.UseSyringeActiveFraction.GetProperties().Name, false));
                    Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites1, AttributeKey.NumberOfSites1.GetProperties().Name, 1));
                    break;
                case AnalysisModel.Dissociation:
                    break;
                case AnalysisModel.TwoSetsOfSites:
                    Add(ExperimentAttribute.Bool(AttributeKey.LockDuplicateParameter, AttributeKey.LockDuplicateParameter.GetProperties().Name, false));
                    Add(ExperimentAttribute.Bool(AttributeKey.UseSyringeActiveFraction, AttributeKey.UseSyringeActiveFraction.GetProperties().Name, false));
                    Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites1, "1^st^ " + AttributeKey.NumberOfSites1.GetProperties().Name, 1));
                    Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites2, "2^nd^ " + AttributeKey.NumberOfSites2.GetProperties().Name, 1));
                    break;
                case AnalysisModel.CompetitiveBinding:
                    Add(ExperimentAttribute.Concentration(AttributeKey.PreboundLigandConc, AttributeKey.PreboundLigandConc.GetProperties().Name, new FloatWithError(10e-6, 0)));
                    Add(ExperimentAttribute.Parameter(AttributeKey.PreboundLigandEnthalpy, AttributeKey.PreboundLigandEnthalpy.GetProperties().Name, new FloatWithError(-40000, 0)));
                    Add(ExperimentAttribute.Affinity(AttributeKey.PreboundLigandAffinity, AttributeKey.PreboundLigandAffinity.GetProperties().Name, new FloatWithError(6.0, 0)));
                    Add(ExperimentAttribute.Bool(AttributeKey.UseSyringeActiveFraction, AttributeKey.UseSyringeActiveFraction.GetProperties().Name, false));
                    Add(ExperimentAttribute.Double(AttributeKey.NumberOfSites1, AttributeKey.NumberOfSites1.GetProperties().Name, 1));
                    break;
                case AnalysisModel.SequentialBindingSites:
                    Add(ExperimentAttribute.Int(AttributeKey.SequentialSiteCount, AttributeKey.SequentialSiteCount.GetProperties().Name, ThermodynamicParameterSlots.MinimumSequentialCount));
                    break;
                default:
                    throw new System.NotImplementedException($"No model option defaults are defined for {model}.");
            }
            return options;
        }

        public static IDictionary<AttributeKey, ExperimentAttribute> CopyOptions(IDictionary<AttributeKey, ExperimentAttribute> options)
            => options?.ToDictionary(item => item.Key, item => item.Value.Copy()) ?? new Dictionary<AttributeKey, ExperimentAttribute>();
    }
}
