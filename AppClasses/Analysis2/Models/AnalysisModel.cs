using System;
using System.Collections.Generic;

namespace AnalysisITC
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
                AnalysisModel.CompetitiveBinding,
                AnalysisModel.Dissociation,
                AnalysisModel.SequentialBindingSites,
                AnalysisModel.PeptideProlineIsomerization,
            };
        }
    }

    public enum AnalysisModel
    {
        [AnalysisModel("One Set of Sites")]
        OneSetOfSites,
        [AnalysisModel("Two Sets of Sites")]
        TwoSetsOfSites,
        [AnalysisModel("Sequential Binding Sites")]
        SequentialBindingSites,
        [AnalysisModel("Dissociation")]
        Dissociation,
        [AnalysisModel("Competitive Binding")]
        CompetitiveBinding,
        [AnalysisModel("Proline Isomer Binding")]
        PeptideProlineIsomerization
    }
}
