namespace AnalysisITC.Core.Presentation
{
    /// <summary>Convention for regression against buffer ionization enthalpy.</summary>
    public static class ProtonationPresentation
    {
        public const string UptakeLabel = "Net proton uptake";
        public const string UptakeToolTip = "Positive: uptake; negative: release.";
        public const string EnthalpyLabel = "Buffer-independent enthalpy";
        public const string EnthalpyToolTip = "Model-estimated intercept at experimental pH; retains binding-partner protonation heat.";
        public const string RunToolTip = "Estimate net proton uptake and buffer-independent enthalpy.";
        public const string NotRunMessage = "Run the analysis to calculate net proton uptake and buffer-independent enthalpy.";
    }
}
