using System;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class PhysicalConstantsAndLog10Tests
{
    // Reference values were evaluated independently with Python decimal at
    // 80-digit precision from NIST's exact SI values and stated equations:
    // https://physics.nist.gov/cuu/Constants/Table/allascii.txt
    // G = -R*T*ln(10)*log10Ka; log10(x) = ln(x)/ln(10);
    // sigmaLog10 = sigmaX/(x*ln(10)), with endpoints transformed separately.
    [Fact]
    public void GasConstantUsesExactSiBoltzmannAndAvogadroConstantsWithZeroSd()
    {
        Assert.Equal(8.31446261815324, Energy.R.Value);
        Assert.Equal(0, Energy.R.SD);
    }

    [Theory]
    [InlineData(7.25, 273.15, -37913.081563732747)]
    [InlineData(-2.5, 298.15, 14270.023756338147)]
    [InlineData(0, 400, 0)]
    public void GibbsFromLog10AffinityMatchesIndependentReference(double log10Ka, double temperatureK, double expectedGibbs)
    {
        AssertClose(expectedGibbs, GlobalConstraintSemantics.GibbsFromLog10Affinity(log10Ka, temperatureK));
        AssertClose(log10Ka, GlobalConstraintSemantics.Log10AffinityFromGibbs(expectedGibbs, temperatureK));
    }

    [Theory]
    [InlineData(0.25, 0.05, 0.2, 0.4, -0.6020599913279624, 0.08685889638065036, -0.6989700043360187, -0.3979400086720376)]
    [InlineData(1, 0.1, 0.8, 1.3, 0, 0.04342944819032518, -0.09691001300805639, 0.11394335230683678)]
    [InlineData(4, 0.3, 2, 6, 0.6020599913279624, 0.03257208614274389, 0.3010299956639812, 0.7781512503836436)]
    [InlineData(10, 0, 10, 10, 1, 0, 1, 1)]
    public void Log10PropagatesSdAndExplicitEndpoints(double value, double sd, double lower, double upper,
        double expectedValue, double expectedSd, double expectedLower, double expectedUpper)
    {
        var actual = FWEMath.Log10(new FloatWithError(value, sd, lower, upper));

        AssertClose(expectedValue, actual.Value);
        AssertClose(expectedSd, actual.SD);
        AssertClose(expectedLower, actual.Lower);
        AssertClose(expectedUpper, actual.Upper);
    }

    static void AssertClose(double expected, double actual)
    {
        if (expected == 0)
        {
            Assert.Equal(0, actual);
            return;
        }

        Assert.InRange(Math.Abs(actual - expected) / Math.Abs(expected), 0, 2e-15);
    }
}
