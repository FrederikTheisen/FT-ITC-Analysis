using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Labels and tooltips for the structuring (Spolar–Record) analysis, shared by both result views.</summary>
    public static class StructuringPresentation
    {
        public const string Title = "Structuring";
        public const string TitleToolTip = "Spolar–Record method.";

        public const string InteractionLabel = "Interaction";
        public const string InteractionToolTip = "Two folded proteins, or one folded and one disordered.";
        public static readonly string[] InteractionOptions = { "Folded–folded", "Folded–disordered" };

        public const string EvaluatedAtLabel = "Evaluated at";
        public const string EvaluatedAtToolTip = "Temperature used to split hydration and conformational entropy.";
        public static readonly string[] EvaluatedAtOptions = { "Isoentropic point", "Mean temperature", "Reference temperature" };

        public const string RunToolTip = "Estimate hydration entropy, conformational entropy, and residues folding upon binding.";
        public const string NotRunMessage = "Run the analysis to calculate structuring values.";

        public const string HydrationLabel = "−TΔS_HE";
        public const string HydrationToolTip = "Hydration entropy contribution.";
        public const string ConformationalLabel = "−TΔS_conf";
        public const string ConformationalToolTip = "Conformational entropy contribution.";
        public const string ResiduesLabel = "Residues";
        public const string ResiduesToolTip = "Residues folding upon binding.";

        public static string InteractionName(FTSRMethod.SRFoldedMode mode) => mode switch
        {
            FTSRMethod.SRFoldedMode.ID => "Folded–disordered",
            FTSRMethod.SRFoldedMode.Intermediate => "Intermediate",
            _ => "Folded–folded",
        };
    }
}
