using System;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class EnergyUnitResolverTests
    {
        [Theory]
        [InlineData(99.999, EnergyUnit.Joule)]
        [InlineData(100.0, EnergyUnit.KiloJoule)]
        [InlineData(-100.0, EnergyUnit.KiloJoule)]
        public void JouleFamilyUsesExactThreshold(double value, EnergyUnit expected)
        {
            Assert.Equal(expected, EnergyUnitResolver.Resolve(EnergyUnitFamily.Joules, value));
        }

        [Theory]
        [InlineData(418.399, EnergyUnit.Cal)]
        [InlineData(418.4, EnergyUnit.KCal)]
        [InlineData(-418.4, EnergyUnit.KCal)]
        public void CalorieFamilyUsesExactThreshold(double value, EnergyUnit expected)
        {
            Assert.Equal(expected, EnergyUnitResolver.Resolve(EnergyUnitFamily.Calories, value));
        }

        [Fact]
        public void CalorieThresholdHonorsAdjacentFloatingPointValues()
        {
            Assert.Equal(
                EnergyUnit.Cal,
                EnergyUnitResolver.Resolve(
                    EnergyUnitFamily.Calories,
                    Math.BitDecrement(EnergyUnitResolver.CalorieThresholdJoules)));
            Assert.Equal(
                EnergyUnit.KCal,
                EnergyUnitResolver.Resolve(
                    EnergyUnitFamily.Calories,
                    Math.BitIncrement(EnergyUnitResolver.CalorieThresholdJoules)));
        }

        [Fact]
        public void ZeroAndNonFiniteValuesUseKiloUnit()
        {
            Assert.Equal(EnergyUnit.KiloJoule, EnergyUnitResolver.Resolve(EnergyUnitFamily.Joules, Array.Empty<double>()));
            Assert.Equal(EnergyUnit.KiloJoule, EnergyUnitResolver.Resolve(EnergyUnitFamily.Joules, 0, double.NaN, double.PositiveInfinity));
            Assert.Equal(EnergyUnit.KCal, EnergyUnitResolver.Resolve(EnergyUnitFamily.Calories, 0, double.NegativeInfinity));
        }

        [Fact]
        public void LargestFiniteMagnitudeControlsMixedGroups()
        {
            Assert.Equal(
                EnergyUnit.KiloJoule,
                EnergyUnitResolver.Resolve(EnergyUnitFamily.Joules, -99, double.NaN, 100, -0.5));
            Assert.Equal(
                EnergyUnit.Cal,
                EnergyUnitResolver.Resolve(EnergyUnitFamily.Calories, -418.399, double.PositiveInfinity, 1));
        }

        [Fact]
        public void UncertaintyDoesNotAffectSelection()
        {
            var value = new FloatWithError(1, 100000);
            Assert.Equal(EnergyUnit.Joule, EnergyUnitResolver.Resolve(EnergyUnitFamily.Joules, new[] { value }));
        }

        [Fact]
        public void MicroCaloriesAreNotValidFigureOrResultOverrides()
        {
            Assert.False(EnergyUnitResolver.IsValidOverride(EnergyUnit.MicroCal));
            Assert.Throws<ArgumentException>(() => EnergyUnitResolver.ValidateOverride(EnergyUnit.MicroCal));
            Assert.False(EnergyUnitResolver.IsValidOverride((EnergyUnit)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => EnergyUnitResolver.ValidateOverride((EnergyUnit)999));
        }

        [Theory]
        [InlineData(EnergyUnit.Joule)]
        [InlineData(EnergyUnit.KiloJoule)]
        [InlineData(EnergyUnit.Cal)]
        [InlineData(EnergyUnit.KCal)]
        public void FixedOverrideWinsOverAutomaticResolution(EnergyUnit energyUnitOverride)
        {
            Assert.Equal(
                energyUnitOverride,
                EnergyUnitResolver.Resolve(
                    EnergyUnitFamily.Joules,
                    energyUnitOverride,
                    new[] { 1_000_000.0 }));
        }

        [Fact]
        public void ThermogramUnitsFollowTheFamily()
        {
            Assert.Equal("µW", ThermogramUnits.DifferentialPowerUnit(EnergyUnitFamily.Joules));
            Assert.Equal("µJ", ThermogramUnits.IntegratedHeatUnit(EnergyUnitFamily.Joules));
            Assert.Equal("µcal/s", ThermogramUnits.DifferentialPowerUnit(EnergyUnitFamily.Calories));
            Assert.Equal("µcal", ThermogramUnits.IntegratedHeatUnit(EnergyUnitFamily.Calories));
        }
    }
}
