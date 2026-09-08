using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Xunit;

namespace AnalysisITC.Avalonia.Tests;

public sealed class GraphControlBaseTests
{
    public static IEnumerable<object[]> Ranges()
    {
        yield return new object[] { 0.0, 10.0, 5,
            new[] { 0.0, 2, 4, 6, 8, 10 }, new[] { 1.0, 3, 5, 7, 9 },
            new[] { "0", "2", "4", "6", "8", "10" } };
        yield return new object[] { 0.0, 0.00001, 5,
            new[] { 0.0, 0.000002, 0.000004, 0.000006, 0.000008, 0.00001 },
            new[] { 0.000001, 0.000003, 0.000005, 0.000007, 0.000009 },
            new[] { "0", "0.000002", "0.000004", "0.000006", "0.000008", "0.00001" } };
        yield return new object[] { -9.0, -1.0, 4,
            new[] { -8.0, -6, -4, -2 }, new[] { -9.0, -7, -5, -3, -1 },
            new[] { "-8", "-6", "-4", "-2" } };
        yield return new object[] { 0.0, 0.0, 5,
            new[] { -0.4, -0.2, 0, 0.2, 0.4 }, new[] { -0.3, -0.1, 0.1, 0.3, 0.5 },
            new[] { "-0.4", "-0.2", "0", "0.2", "0.4" } };
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void TickValuesAndLabelsRemainUnchanged(double min, double max, int count,
        double[] expectedMajor, double[] expectedMinor, string[] expectedLabels)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var (major, minor, labels) = TestGraph.Ticks(min, max, count);
            AssertValues(expectedMajor, major);
            AssertValues(expectedMinor, minor);
            Assert.Equal(expectedLabels, labels);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void LabelsUseCurrentCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            Assert.Equal("-1,25", TestGraph.Format(-2, 2, 8, -1.25));
            Assert.Equal("2000", TestGraph.Format(0, 10_000, 5, 2000));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1.5, 2.5)]
    [InlineData(-1.5, -1.5)]
    public void PixelAlignmentKeepsExistingRounding(double value, double expected) =>
        Assert.Equal(expected, TestGraph.Align(value));

    static void AssertValues(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.InRange(Math.Abs(expected[index] - actual[index]), 0, 1e-14);
    }

    sealed class TestGraph : GraphControlBase
    {
        public static (double[], double[], string[]) Ticks(double min, double max, int count)
        {
            var ticks = AxisTicks.Create(min, max, count);
            return (ticks.Major.ToArray(), ticks.Minor.ToArray(), ticks.Major.Select(ticks.Format).ToArray());
        }

        public static string Format(double min, double max, int count, double value) =>
            AxisTicks.Create(min, max, count).Format(value);

        public static double Align(double value) => Crisp(value);
    }
}
