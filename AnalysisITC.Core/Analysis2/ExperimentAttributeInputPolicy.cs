using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>Entry rules for experiment attribute editors, not scientific limits.</summary>
    public static class ExperimentAttributeInputPolicy
    {
        public const string BufferPhValidationMessage = "pH must be a finite number between 0 and 14.";

        public static bool IsValidBufferPh(double pH) =>
            FWEMath.IsFinite(pH) && pH >= 0 && pH <= 14;
    }
}
