using System.ComponentModel;

using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Data
{
    public enum AnalysisXAxisType
    {
        [Description("Molar Ratio")]
        MolarRatio,
        [Description("[Titrant] (µM)")]
        TitrantConcentration,
        [Description("Injection Number")]
        ID
    }
}
