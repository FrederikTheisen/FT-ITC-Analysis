using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Shared labels, tooltips, and saved-temperature formatting for structuring (Spolar–Record).</summary>
    public static class StructuringPresentation
    {
        public const string Title = "Structuring";
        public const string TitleToolTip = "Spolar–Record method.";

        public const string InteractionLabel = "Interaction";
        public const string InteractionToolTip = "Two folded proteins, or one folded and one disordered.";
        public static readonly string[] InteractionOptions = { "Folded–folded", "Folded–disordered" };

        public const string EvaluatedAtLabel = "Evaluated at";
        public const string EvaluatedAtToolTip = "Temperature used to split hydration and conformational entropy.";
        // ITC-068: desktop runs use the published convention pending scientific review.
        // Core retains the other modes for research and saved-output compatibility.
        public const FTSRMethod.SRTempMode RunTemperatureMode = FTSRMethod.SRTempMode.IsoEntropicPoint;
        public const string RunTemperatureModeName = "Isoentropic point";
        public const string RunTemperatureModeToolTip = "Temperature where model-estimated binding entropy is zero.";

        public const string RunToolTip = "Estimate hydration entropy, conformational entropy, and residues folding upon binding.";
        public const string NotRunMessage = "Run the analysis to calculate structuring values.";

        public const string HydrationLabel = "−TΔS_HE";
        public const string HydrationToolTip = "Hydration entropy contribution.";
        public const string ConformationalLabel = "−TΔS_conf";
        public const string ConformationalToolTip = "Conformational entropy contribution.";
        public const string ResiduesLabel = "Residues";
        public const string ResiduesToolTip = "Residues folding upon binding.";

        public static string FormatEvaluationTemperature(
            FloatWithError celsius,
            bool useKelvin,
            UncertaintyDisplayStyle uncertaintyStyle)
        {
            if (FloatWithError.IsNaN(celsius) || double.IsNaN(celsius.Value) || double.IsInfinity(celsius.Value))
                return "Unavailable";

            var displayed = useKelvin ? celsius + 273.15 : celsius;
            return displayed.AsNumber(uncertaintyStyle) + (useKelvin ? " K" : " °C");
        }

        public static string InteractionName(FTSRMethod.SRFoldedMode mode) => mode switch
        {
            FTSRMethod.SRFoldedMode.ID => "Folded–disordered",
            FTSRMethod.SRFoldedMode.Intermediate => "Intermediate",
            _ => "Folded–folded",
        };
    }
}
