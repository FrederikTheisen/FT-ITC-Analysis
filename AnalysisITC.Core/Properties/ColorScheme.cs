using System.ComponentModel;

namespace AnalysisITC.Core.Presentation
{
    public enum ColorSchemes
    {
        [Description("Default system color scheme")]
        Default,
        Viridis,
        Waves,
        Floral,
    }

    public enum ColorSchemeGradientMode
    {
        [Description("Only pick defined colors")]
        Stepwise,
        [Description("Smooth gradient between each defined color")]
        Smooth
    }
}
