using System;
using System.Collections.Generic;


namespace AnalysisITC.Core.Numerics
{
    static class Distribution
    {
        private static readonly Random rng = new Random();

        public static DistributionType Selected { get; private set; } = DistributionType.Normal;

        public static void SetDefaultDistribution(DistributionType distributionType)
        {
            Selected = distributionType;
        }

        public static double Default(FloatWithError number, Random rand = null) => Default(number.Value, number.SD, rand);
        public static double Default(double mean, double stdDev, Random rand = null)
        {
            return Selected switch
            {
                DistributionType.Constant => Constant(mean, stdDev, rand),
                DistributionType.Normal => Normal(mean, stdDev, rand),
                _ => mean,
            };
        }

        public static double Default(double mean, double stdDev, List<double> distribution, Random rand = null)
        {
            if (distribution != null && Selected != DistributionType.None)
            {
                rand ??= rng;
                return distribution[rand.Next(distribution.Count)];
            }
            else return Selected switch
            {
                DistributionType.Constant => Constant(mean, stdDev, rand),
                DistributionType.Normal => Normal(mean, stdDev, rand),
                _ => mean,
            };
        }

        public static double Normal(FloatWithError number, Random rand = null) => SampleSplitNormal(number, rand);
        public static double Normal(double mean, double stdDev, Random rand = null)
        {
            rand ??= rng;
            double u1 = 1.0 - rand.NextDouble(); //uniform(0,1] random doubles
            double u2 = 1.0 - rand.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
            double randNormal = mean + stdDev * randStdNormal; //random normal(mean,stdDev^2)

            return randNormal;
        }

        /// <summary>
        /// Samples a positive multiplicative factor with arithmetic mean one and
        /// fractional standard deviation <paramref name="fractionalSd"/>.
        /// </summary>
        /// <remarks>
        /// If <c>CV</c> is the requested fractional standard deviation, a
        /// log-normal variable with
        /// <c>sigmaLog^2 = log(1 + CV^2)</c> and
        /// <c>muLog = -sigmaLog^2 / 2</c> has mean one and standard deviation
        /// <c>CV</c>.  The optional random source makes the sampler deterministic
        /// for tests and callers that already own a bootstrap stream.
        /// </remarks>
        internal static double LognormalFactor(double fractionalSd, Random rand = null)
        {
            if (!FWEMath.IsFinite(fractionalSd) || fractionalSd < 0)
                throw new ArgumentOutOfRangeException(nameof(fractionalSd), fractionalSd,
                    "Fractional standard deviation must be finite and non-negative.");

            if (fractionalSd == 0) return 1.0;

            // Keep the calculation finite even for very large but finite inputs;
            // values that cannot parameterize a finite log-normal distribution are
            // rejected instead of creating invalid synthetic concentrations.
            var sigmaSquared = Math.Log(1.0 + fractionalSd * fractionalSd);
            if (!FWEMath.IsFinite(sigmaSquared))
                throw new ArgumentOutOfRangeException(nameof(fractionalSd), fractionalSd,
                    "Fractional standard deviation is too large for a finite log-normal distribution.");

            var sigma = Math.Sqrt(sigmaSquared);
            var z = Normal(0, 1, rand);
            var factor = Math.Exp(-0.5 * sigmaSquared + sigma * z);

            if (!FWEMath.IsFinite(factor) || factor <= 0)
                throw new InvalidOperationException("Log-normal concentration sampling produced a non-positive or non-finite factor.");

            return factor;
        }

        public static double SampleSplitNormal(FloatWithError fwe, Random rand = null)
        {
            if (!FWEMath.IsFinite(fwe.Value) ||
                !FWEMath.IsFinite(fwe.Lower) ||
                !FWEMath.IsFinite(fwe.Upper) ||
                fwe.Lower >= fwe.Value ||
                fwe.Upper <= fwe.Value)
            {
                return Normal(fwe.Value, fwe.SD, rand);
            }

            rand ??= rng;

            const double ciZ = 1.96;
            var z = Normal(0, 1, rand);
            var distance = Math.Abs(z);
            var sideWidth = z < 0 ? fwe.LowerWidth : fwe.UpperWidth;
            var commonSlope = Math.Min(fwe.LowerWidth, fwe.UpperWidth) / ciZ;
            double magnitude;

            if (distance <= ciZ)
            {
                var normalizedDistance = distance / ciZ;
                var extraWidth = sideWidth - commonSlope * ciZ;

                // This cubic reaches the selected CI bound at 1.96 while using
                // the same derivative on both sides of the mode. Its derivative
                // at the CI also matches the linear tail below.
                magnitude = commonSlope * distance + extraWidth *
                    normalizedDistance * normalizedDistance * (2 - normalizedDistance);
            }
            else
            {
                magnitude = sideWidth + sideWidth / ciZ * (distance - ciZ);
            }

            return fwe.Value + (z < 0 ? -magnitude : magnitude);
        }

        public static double Constant(FloatWithError number, Random rand = null) => Constant(number.Value, number.SD, rand);
        public static double Constant(double mean, double stdDev, Random rand = null)
        {
            rand ??= rng;
            double u = rand.NextDouble(); //uniform[0,1) random double
            double randStdCons = 2 * (0.5 - u); //uniform(-1,1] random double
            double randCons = mean + stdDev * randStdCons; //random constant within mean +/- SD

            return randCons;
        }

        public static double None(FloatWithError number) => None(number.Value, number.SD);
        public static double None(double mean, double stdDev)
        {
            return mean;
        }

        public enum DistributionType
        {
            Constant,
            Normal,
            None
        }
    }
}
