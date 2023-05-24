using System;
using System.Collections.Generic;

namespace AnalysisITC.AppClasses.Analysis2.Models
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
                AnalysisModel.TwoCompetingSites,
            };
        }
    }

    public enum AnalysisModel
    {
        [AnalysisModel("One Set of Sites", "Standard model to fit 1 or more identical binding sites that do not influence each other")]
        OneSetOfSites,
        [AnalysisModel("Two Sets of Sites", "")]
        TwoSetsOfSites,
        [AnalysisModel("Sequential Binding Sites")]
        SequentialBindingSites,
        [AnalysisModel("Dissociation", "Fit dissociation of an injected preformed complex")]
        Dissociation,
        [AnalysisModel("Competitive Binding", "Fit competition experiment where the cell contains a preformed complex and a higher affinity interaction partner is titrated in")]
        CompetitiveBinding,
        [AnalysisModel("Proline Isomer Binding", "Fit interactions affected by proline cis/trans isomerization")]
        PeptideProlineIsomerization,
        [AnalysisModel("Two Competing Sites", "Protein contains two sites which compete for the ligand. Only one of the two sites can be occupied at a time")]
        TwoCompetingSites
    }
}