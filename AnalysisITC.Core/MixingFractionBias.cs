using System;

namespace AnalysisITC.Core.Processing
{
    /// <summary>
    /// Weak multiplicative preference for a typical tandem mixing fraction. A score is multiplied by
    /// 1 + Strength (f - Center)^2, so the modulation is independent of the score's scale and only
    /// decides the fraction where the score profile is nearly flat. With the defaults the score rises
    /// by 0.5% at 10 percentage points from the centre, 2% at 20 points and 12.5% at 50 points.
    /// </summary>
    public sealed class MixingFractionBias
    {
        public static MixingFractionBias Default { get; } = new MixingFractionBias(0.05, 0.5);

        public double Center { get; }
        public double Strength { get; }

        public MixingFractionBias(double center, double strength)
        {
            if (strength < 0) throw new ArgumentOutOfRangeException(nameof(strength), "The bias strength cannot be negative.");

            Center = center;
            Strength = strength;
        }

        public double Factor(double fraction)
        {
            var distance = fraction - Center;
            return 1 + Strength * distance * distance;
        }

        public double Apply(double score, double fraction) => score * Factor(fraction);
    }
}
