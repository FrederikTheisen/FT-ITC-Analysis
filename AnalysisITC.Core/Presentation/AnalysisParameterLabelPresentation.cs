using System.Collections.Generic;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Parameter labels whose meaning depends on the model options.</summary>
    public sealed class AnalysisParameterLabelPresentation
    {
        AnalysisParameterLabelPresentation(string title, string symbol, string toolTip)
        {
            Title = title;
            Symbol = symbol;
            ToolTip = toolTip;
        }

        public string Title { get; }
        /// <summary>Symbol in the shared scientific Markdown notation.</summary>
        public string Symbol { get; }
        /// <summary>Scientific meaning, shared by editors and saved parameter rows.</summary>
        public string ToolTip { get; }

        public static AnalysisParameterLabelPresentation Create(
            ParameterType key,
            IDictionary<AttributeKey, ExperimentAttribute> options = null,
            bool includeSiteIndex = false)
        {
            if (key == ParameterType.Nvalue1)
            {
                var syringeCorrection = options != null
                    && options.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var option)
                    && option?.BoolValue == true;
                return syringeCorrection
                    ? new AnalysisParameterLabelPresentation("Correction factor", "α",
                        "Scales delivered titrant concentration during model evaluation.")
                    : new AnalysisParameterLabelPresentation("N-value", includeSiteIndex ? "N{1}" : "N",
                        "Apparent binding sites per macromolecule in the cell.");
            }

            if (key == ParameterType.Nvalue2)
                return new AnalysisParameterLabelPresentation("N-value 2", "N{2}",
                    "Apparent cell stoichiometry for the second site class.");

            var properties = key.GetProperties();
            return new AnalysisParameterLabelPresentation(properties.Description, properties.SymbolName, null);
        }
    }
}
