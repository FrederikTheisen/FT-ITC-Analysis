using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisAxisPresentation
    {
        /// <summary>Returns the injection coordinate before display-unit scaling.</summary>
        public static double XValue(AnalysisXAxisType axisType, InjectionData injection)
        {
            return axisType switch
            {
                AnalysisXAxisType.TitrantConcentration => injection.ActualTitrantConcentration,
                AnalysisXAxisType.ID => injection.ID + 1,
                _ => injection.Ratio
            };
        }

        public static int XScaleFactor(AnalysisXAxisType axisType)
        {
            return axisType == AnalysisXAxisType.TitrantConcentration ? 1000000 : 1;
        }

        public static double DisplayXValue(AnalysisXAxisType axisType, InjectionData injection)
        {
            return XValue(axisType, injection) * XScaleFactor(axisType);
        }
    }
}
