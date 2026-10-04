using System;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class MixingFractionBiasTests
    {
        [Fact]
        public void DefaultIsCentredAtFivePercentWithStrengthOneHalf()
        {
            Assert.Equal(0.05, MixingFractionBias.Default.Center);
            Assert.Equal(0.5, MixingFractionBias.Default.Strength);
            Assert.Equal(1.0, MixingFractionBias.Default.Factor(0.05));
        }

        [Theory]
        // 1 + 0.5 * d^2 for d = 10, 20 and 50 percentage points.
        [InlineData(0.15, 1.005)]
        [InlineData(0.25, 1.02)]
        [InlineData(0.55, 1.125)]
        [InlineData(0.0, 1.00125)]
        public void FactorMatchesHandCalculatedValues(double fraction, double expected)
        {
            Assert.Equal(expected, MixingFractionBias.Default.Factor(fraction), 12);
        }

        [Theory]
        [InlineData(0.01)]
        [InlineData(0.03)]
        [InlineData(0.05)]
        public void FactorIsSymmetricAroundTheCentre(double distance)
        {
            var bias = MixingFractionBias.Default;
            Assert.Equal(bias.Factor(0.05 - distance), bias.Factor(0.05 + distance), 15);
        }

        [Fact]
        public void ApplyScalesTheScore()
        {
            Assert.Equal(2.04, MixingFractionBias.Default.Apply(2.0, 0.25), 12);
            Assert.Equal(0.0, MixingFractionBias.Default.Apply(0.0, 0.8));
        }

        [Fact]
        public void NegativeStrengthIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MixingFractionBias(0.05, -0.1));
        }
    }
}
