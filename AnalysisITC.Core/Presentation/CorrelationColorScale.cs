using System;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>
    /// Shared correlation-plot palette: red for negative values, white at zero,
    /// and blue for positive values. Squared intensity emphasizes strong correlations.
    /// </summary>
    public static class CorrelationColorScale
    {
        public static (byte Red, byte Green, byte Blue) ColorFor(double value)
        {
            value = double.IsNaN(value) || double.IsInfinity(value)
                ? 0
                : Math.Max(-1, Math.Min(1, value));
            var channel = (byte)Math.Round(255 - 150 * value * value);
            return value < 0
                ? ((byte)255, channel, channel)
                : (channel, channel, (byte)255);
        }
    }
}
