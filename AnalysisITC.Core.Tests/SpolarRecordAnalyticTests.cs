using System;
using System.Reflection;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class SpolarRecordAnalyticTests
{
    [Fact]
    public void ScalarDependenceEvaluationMatchesFullCentralValue()
    {
        var dependence = new SummaryDependence
        {
            ReferenceTemperature = 26.85,
            Intercept = -1200,
            Slope = 25,
            HeatCapacityTerm = 80,
        };

        var full = dependence.Evaluate(41.0);
        Assert.Equal(full.Value, dependence.EvaluateScalar(41.0), 12);
    }

    [Fact]
    public void LinkedIsoentropicRootUsesKelvinRelationshipWithoutSearchBounds()
    {
        const double referenceKelvin = 300;
        const double heatCapacity = 100;
        var dependence = new SummaryDependence
        {
            ReferenceTemperature = referenceKelvin - 273.15,
            Intercept = referenceKelvin * heatCapacity * Math.Log(6),
            HeatCapacityTerm = heatCapacity,
        };

        var method = typeof(FTSRMethod).GetMethod("FindIsoentropicTemperature",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (AnalysisITC.Core.Numerics.FloatWithError)method.Invoke(null, new object[] { dependence });

        Assert.Equal(referenceKelvin * 6 - 273.15, result.Value, 10);
    }

    [Fact]
    public void LinkedIsoentropicRootSkipsUnavailableCoordinateWhenCombinedWeightCancels()
    {
        const double referenceKelvin = 300;
        const double heatCapacity = 100;
        var dependence = new SummaryDependence
        {
            ReferenceTemperature = referenceKelvin - 273.15,
            Intercept = 200,
            HeatCapacityTerm = heatCapacity,
        };
        // Weight*Cp - Er*CpTerm is exactly zero, so this unavailable interval
        // must not make the derived interval unavailable.
        dependence.Contributions.Add(new SummaryErrorContribution(
            2, 0, double.NaN, double.NaN, double.NaN, 1));

        var method = typeof(FTSRMethod).GetMethod("FindIsoentropicTemperature",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (AnalysisITC.Core.Numerics.FloatWithError)method.Invoke(null, new object[] { dependence });

        Assert.Equal(referenceKelvin * Math.Exp(200 / (referenceKelvin * heatCapacity)) - 273.15, result.Value, 10);
        Assert.False(double.IsNaN(result.Lower));
    }

    [Fact]
    public void LinkedIsoentropicRootRejectsZeroHeatCapacityAndEvaluatesReplicateReferences()
    {
        var method = typeof(FTSRMethod).GetMethod("FindIsoentropicTemperature",
            BindingFlags.Static | BindingFlags.NonPublic);
        var unavailable = new SummaryDependence
        {
            ReferenceTemperature = 26.85,
            Intercept = 10,
            HeatCapacityTerm = 0,
        };
        var missing = (AnalysisITC.Core.Numerics.FloatWithError)method.Invoke(null, new object[] { unavailable });
        Assert.True(double.IsNaN(missing.Value));

        var central = new SummaryDependence
        {
            ReferenceTemperature = 26.85,
            Intercept = 3000 * Math.Log(2),
            HeatCapacityTerm = 10,
        };
        central.Replicates.Add(new SummaryDependence
        {
            ReferenceTemperature = 26.85,
            Intercept = 3000 * Math.Log(2),
            HeatCapacityTerm = 10,
        });
        central.Replicates.Add(new SummaryDependence
        {
            ReferenceTemperature = 46.85,
            Intercept = 3200 * Math.Log(3),
            HeatCapacityTerm = 10,
        });
        var replicated = (AnalysisITC.Core.Numerics.FloatWithError)method.Invoke(null, new object[] { central });
        Assert.Equal(600 - 273.15, replicated.Value, 10);
        Assert.True(replicated.SD > 0);
    }

    [Theory]
    [InlineData(VariableConstraint.None, "Independent")]
    [InlineData(VariableConstraint.TemperatureDependent, "Shared ΔG")]
    [InlineData(VariableConstraint.SameForAll, "Shared Kd")]
    [InlineData(VariableConstraint.ThermodynamicallyLinked, "Thermodynamically linked")]
    public void AffinityConstraintLabelsAreParameterAware(VariableConstraint constraint, string expected)
    {
        Assert.Equal(expected, ConstraintPresentation.Description(ParameterType.Affinity1, constraint));
        Assert.Equal("Same for all", ConstraintPresentation.Description(ParameterType.Enthalpy1, VariableConstraint.SameForAll));
    }
}
