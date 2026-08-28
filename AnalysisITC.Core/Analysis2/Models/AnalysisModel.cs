using System;
using System.Collections.Generic;

using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis.Models
{
    public class AnalysisModelAttribute : Attribute
    {
        public string Name { get; private set; }
        public string Description { get; private set; }

        public AnalysisModelAttribute(string mdlname, string desc = "")
        {
            Name = mdlname;
            Description = desc;
        }

        public static List<AnalysisModel> GetAll()
        {
            return new List<AnalysisModel>
            {
                AnalysisModel.OneSetOfSites,
                AnalysisModel.TwoSetsOfSites,
                AnalysisModel.SequentialBindingSites,
                AnalysisModel.CompetitiveBinding,
                AnalysisModel.Dissociation,
            };
        }
    }

    public enum AnalysisModel
    {
        [AnalysisModel("One-Set-Of-Sites", "Standard model to fit 1 or more identical binding sites that do not influence each other")]
        OneSetOfSites,
        [AnalysisModel("Two-Sets-Of-Sites", "")]
        TwoSetsOfSites,
        [AnalysisModel("Sequential Binding Sites", "Fit two to four ordered macroscopic binding steps with a fixed integral site count")]
        SequentialBindingSites,
        [AnalysisModel("Dissociation", "Fit dissociation of an injected preformed complex")]
        Dissociation,
        [AnalysisModel("Competitive Binding", "Fit competition experiment where the cell contains a preformed complex and a higher affinity interaction partner is titrated in")]
        CompetitiveBinding
    }
}
