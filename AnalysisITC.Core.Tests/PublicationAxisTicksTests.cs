using System.Linq;

using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class PublicationAxisTicksTests
    {
        [Fact]
        public void NiceTicksUseQuarterStepsWhenTheyAreTheBestFit()
        {
            var axis = new PublicationAxis("", PublicationAxisPlacement.Bottom, 0, 10, 3);

            Assert.Equal(2.5, axis.TickSpacing, 10);
            Assert.Equal(new[] { 0d, 2.5, 5, 7.5, 10 }, axis.MajorTicks);
            Assert.Equal(1, axis.DecimalPlaces);
            Assert.Equal(2.5.ToString("0.0"), axis.FormatTick(2.5));

            var spacings = axis.MajorTicks
                .Zip(axis.MajorTicks.Skip(1), (left, right) => right - left)
                .ToList();
            Assert.All(spacings, spacing => Assert.Equal(axis.TickSpacing, spacing, 10));
        }

        [Fact]
        public void PublicationFigureDefaultsKeepTheNormalTickTarget()
        {
            var options = new PublicationFigureOptions();

            Assert.Equal(7, options.DataXTickCount);
            Assert.Equal(7, options.DataYTickCount);
            Assert.Equal(7, options.FitXTickCount);
            Assert.Equal(7, options.FitYTickCount);
        }

        [Fact]
        public void NormalDensityPrefersAUsableCoarserGridWhenTheInitialGridIsTooDense()
        {
            var axis = new PublicationAxis("", PublicationAxisPlacement.Bottom, 0, 8, 7);

            Assert.Equal(2, axis.TickSpacing, 10);
            Assert.Equal(new[] { 0d, 2, 4, 6, 8 }, axis.MajorTicks);
        }
    }
}
