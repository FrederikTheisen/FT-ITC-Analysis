using System;
using System.Linq;
using System.Reflection;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Core.Tests;

[Collection(BufferIonicStrengthCollection.Name)]
public sealed class BufferIterationTests
{
    static readonly MethodInfo IonicStrengthMethod = typeof(BufferAttribute)
        .GetMethod("GetBufferIonicStrength", BindingFlags.Instance | BindingFlags.NonPublic);

    static double IonicStrength(BufferAttribute buffer, double pH, double concentration, double temperature)
        => (double)IonicStrengthMethod.Invoke(buffer, new object[] { pH, concentration, temperature });

    [Theory]
    [InlineData(7.198, -0.0028, -1, 8.198, 5, 25, 8.209477038418115)]
    [InlineData(7.2, -0.0028, -1, 8.198, 5, 25, 8.205322599847004)]
    [InlineData(7.0, 0, 0, 7.0, 0.1, 25, 0.05526610144721029)]
    [InlineData(7.0, 0, 1, 7.0, 0.1, 25, 0.05526610144721029)]
    [InlineData(7.2, 0, -1, 7.4, 0.05, 25, 0.12865613944018682)]
    [InlineData(7.66, -0.014, 0, 7.4, 0.05, 37, 0.02431310916526958)]
    public void BracketedCorrectionMatchesIndependentScalarReferences(
        double pKa, double slope, int charge, double pH, double concentration, double temperature, double expected)
    {
        var buffer = new BufferAttribute("reference", pKa, slope, charge, "", 0);

        // Expected values were independently calculated with 60-digit Python
        // decimal arithmetic by solving the scalar species-balance equation.
        var actual = IonicStrength(buffer, pH, concentration, temperature);
        Assert.InRange(Math.Abs(actual - expected), 0, 1e-12 * Math.Max(concentration, expected));
    }

    [Theory]
    [InlineData(double.MaxValue, 0.3)]
    [InlineData(-double.MaxValue, 0.1)]
    public void ExtremeFinitePHReturnsPhysicalEndpoint(double pH, double expected)
    {
        var buffer = new BufferAttribute("reference", 7.0, 0, -1, "", 0);
        var actual = IonicStrength(buffer, pH, 0.1, 25);

        Assert.True(FWEMath.IsFinite(actual));
        Assert.Equal(expected, actual, 12);
    }

    [Fact]
    public void ZeroConcentrationWithValidInputsReturnsZero()
    {
        var buffer = new BufferAttribute("reference", 7.0, 0, 0, "", 0);
        Assert.Equal(0, IonicStrength(buffer, 7, 0, 25));
    }

    [Theory]
    [InlineData(double.NaN, 0.1, 25)]
    [InlineData(7, double.NaN, 25)]
    [InlineData(7, double.PositiveInfinity, 25)]
    [InlineData(7, -0.1, 25)]
    [InlineData(7, 0.1, double.NaN)]
    public void InvalidRequiredInputReturnsNaN(double pH, double concentration, double temperature)
    {
        var buffer = new BufferAttribute("reference", 7.0, 0, 0, "", 0);
        Assert.True(double.IsNaN(IonicStrength(buffer, pH, concentration, temperature)));
    }

    [Fact]
    public void NonFiniteTemperatureCorrectionReturnsNaNEvenAtZeroConcentration()
    {
        var buffer = new BufferAttribute("reference", 7.0, 2, 0, "", 0);
        Assert.True(double.IsNaN(IonicStrength(buffer, 7, 0, double.MaxValue)));
    }

    [Fact]
    public void OverflowingIonicStrengthBracketReturnsNaN()
    {
        var buffer = new BufferAttribute("reference", 7.0, 0, -2, "", 0);
        Assert.True(double.IsNaN(IonicStrength(buffer, 7, double.MaxValue, 25)));
    }

    [Fact]
    public void NonFiniteActivityCorrectionReturnsNaN()
    {
        var buffer = new BufferAttribute("reference", 7.0, 0, 0, "", 0);
        Assert.True(double.IsNaN(IonicStrength(buffer, 7, 0.1, double.MaxValue)));
    }

    [Fact]
    public void EveryApplicationBufferConvergesAcrossRegistryGrid()
    {
        foreach (Buffer buffer in Enum.GetValues(typeof(Buffer)))
        {
            var properties = buffer.GetProperties();
            var charges = (int[])typeof(BufferAttribute)
                .GetProperty("AcidCharges", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(properties);
            foreach (var pH in Enumerable.Range(0, 15).Select(value => (double)value))
            foreach (var temperature in new[] { 5.0, 25.0, 45.0 })
            foreach (var concentration in new[] { 0.001, 0.01, 0.1, 1.0 })
            {
                var state = Enumerable.Range(0, properties.pKaValues.Length)
                    .OrderBy(index => Math.Abs(properties.pKaValues[index] - pH)).First();
                var actual = IonicStrength(properties, pH, concentration, temperature);

                Assert.True(FWEMath.IsFinite(actual), $"{buffer} at pH {pH}, {temperature} °C, {concentration} M");
                var z = charges[state];
                var acidBound = 0.5 * concentration * ((double)z * z + Math.Abs(z));
                var baseCharge = (double)z - 1;
                var baseBound = 0.5 * concentration * (baseCharge * baseCharge + Math.Abs(baseCharge));
                Assert.InRange(actual, Math.Min(acidBound, baseBound) - 1e-12, Math.Max(acidBound, baseBound) + 1e-12);

                var pKa = properties.pKaValues[state] + properties.dPKadT[state] * (temperature - 25);
                var expectedBalance = IndependentBalance(pH, pKa, z, actual, concentration, temperature);
                Assert.InRange(Math.Abs(actual - expectedBalance), 0, 1e-11 * Math.Max(concentration, actual));
            }
        }
    }

    [Fact]
    public void PublicCalculationAddsBuffersAndSaltAndHonorsOverridesAndPreference()
    {
        var previous = AppSettings.IncludeBufferInIonicStrengthCalc;
        try
        {
            var data = new ExperimentData("buffer ionic strength") { TargetTemperature = 25 };
            data.Attributes.Add(BufferAttributeEntry(Buffer.Hepes, pH: 7.4, concentration: 0.1));
            data.Attributes.Add(BufferAttributeEntry(Buffer.Acetate, pH: 4.8, concentration: 0.05));
            data.Attributes.Add(SaltEntry(Salt.NaCl, concentration: 0.1));

            AppSettings.IncludeBufferInIonicStrengthCalc = true;
            var withBuffers = BufferAttribute.GetIonicStrength(data);
            var first = IonicStrength(Buffer.Hepes.GetProperties(), 7.4, 0.1, 25);
            var second = IonicStrength(Buffer.Acetate.GetProperties(), 4.8, 0.05, 25);
            Assert.Equal(0.1 + first + second, withBuffers, 10);

            AppSettings.IncludeBufferInIonicStrengthCalc = false;
            Assert.Equal(0.1, BufferAttribute.GetIonicStrength(data), 12);

            var overrideValue = ExperimentAttribute.FromKey(AttributeKey.IonicStrength);
            overrideValue.ParameterValue = new FloatWithError(0.42);
            data.Attributes.Add(overrideValue);
            Assert.Equal(0.42, BufferAttribute.GetIonicStrength(data), 12);
        }
        finally
        {
            AppSettings.IncludeBufferInIonicStrengthCalc = previous;
        }
    }

    [Fact]
    public void FailedBufferCalculationPropagatesNaNThroughPublicTotal()
    {
        var previous = AppSettings.IncludeBufferInIonicStrengthCalc;
        try
        {
            AppSettings.IncludeBufferInIonicStrengthCalc = true;
            var data = new ExperimentData("invalid buffer") { TargetTemperature = 25 };
            data.Attributes.Add(BufferAttributeEntry(Buffer.Hepes, pH: 7.4, concentration: -0.1));
            data.Attributes.Add(SaltEntry(Salt.NaCl, concentration: 0.1));
            Assert.True(double.IsNaN(BufferAttribute.GetIonicStrength(data)));

            AppSettings.IncludeBufferInIonicStrengthCalc = false;
            Assert.Equal(0.1, BufferAttribute.GetIonicStrength(data), 12);

            AppSettings.IncludeBufferInIonicStrengthCalc = true;
            var overrideValue = ExperimentAttribute.FromKey(AttributeKey.IonicStrength);
            overrideValue.ParameterValue = new FloatWithError(0.42);
            data.Attributes.Add(overrideValue);
            Assert.Equal(0.42, BufferAttribute.GetIonicStrength(data), 12);
        }
        finally
        {
            AppSettings.IncludeBufferInIonicStrengthCalc = previous;
        }
    }

    static double IndependentBalance(double pH, double pKa, int charge, double ionicStrength, double concentration, double temperature)
    {
        var correctedPka = pKa + (2 * charge - 1) *
            ((DebyeHuckel(temperature) * Math.Sqrt(ionicStrength) / (1 + Math.Sqrt(ionicStrength))) - 0.1 * ionicStrength);
        var delta = pH - correctedPka;
        var ratio = Math.Pow(10, delta);
        var baseCharge = charge - 1;
        var acidWeight = 0.5 * (charge * charge + Math.Abs(charge));
        var baseWeight = 0.5 * (baseCharge * baseCharge + Math.Abs(baseCharge));
        return concentration * (acidWeight + baseWeight * ratio) / (1 + ratio);
    }

    static double DebyeHuckel(double temperature) => 0.4918 + 0.0006614 * temperature + 0.000004975 * temperature * temperature;

    static ExperimentAttribute BufferAttributeEntry(Buffer buffer, double pH, double concentration)
    {
        var attribute = ExperimentAttribute.FromKey(AttributeKey.Buffer);
        attribute.IntValue = (int)buffer;
        attribute.DoubleValue = pH;
        attribute.ParameterValue = new FloatWithError(concentration);
        return attribute;
    }

    static ExperimentAttribute SaltEntry(Salt salt, double concentration)
    {
        var attribute = ExperimentAttribute.FromKey(AttributeKey.Salt);
        attribute.IntValue = (int)salt;
        attribute.ParameterValue = new FloatWithError(concentration);
        return attribute;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BufferIonicStrengthCollection
{
    public const string Name = "Buffer ionic strength";
}
