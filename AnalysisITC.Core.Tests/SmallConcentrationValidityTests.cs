using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class SmallConcentrationValidityTests
{
    [Theory]
    [InlineData("value", 5e-9, 5.8e-9)]
    [InlineData("value", 5e-12, 5.8e-12)]
    [InlineData("sd", 0.5e-12, 0.8e-12)]
    [InlineData("lower", 4e-12, 4.2e-12)]
    [InlineData("upper", 6e-12, 6.2e-12)]
    public void CompetitorKdChangesInvalidateDependentFit(string component, double before, double after)
    {
        var model = InjectionProcessingMethodTests.FittedModel("competitive");
        var attribute = ExperimentAttribute.CompetitorResultReference("saved-competitor");
        attribute.SourceSolutionId = "same-source-solution";
        attribute.CapturedAffinity = Kd(component, before);
        attribute.CapturedEnthalpy = new FloatWithError(-32000, 1200);
        model.Data.Attributes.Add(attribute);
        model.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        model.SetModelOptions();
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        Assert.Equal(AnalysisResultValidity.Valid, result.ValiditySnapshot.Compare(result.Solution).Status);

        attribute.CapturedAffinity = Kd(component, after);

        var validity = result.ValiditySnapshot.Compare(result.Solution);
        Assert.Equal(AnalysisResultValidity.Invalid, validity.Status);
        Assert.Contains(validity.Reasons, reason => reason.Contains("fit-relevant experiment attributes changed"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallPreboundConcentrationAndSdChangesInvalidateFit(bool changeSd)
    {
        var model = InjectionProcessingMethodTests.FittedModel("competitive");
        model.Data.Attributes.RemoveAll(attribute => attribute.Key == AttributeKey.PreboundLigandConc);
        var attribute = ExperimentAttribute.FromKey(AttributeKey.PreboundLigandConc);
        attribute.ParameterValue = new FloatWithError(5e-9, 0.5e-9);
        model.Data.Attributes.Add(attribute);
        model.ModelOptions[AttributeKey.PreboundLigandConc].BoolValue = true;
        model.SetModelOptions();
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        Assert.Equal(AnalysisResultValidity.Valid, result.ValiditySnapshot.Compare(result.Solution).Status);

        attribute.ParameterValue = changeSd ? new FloatWithError(5e-9, 0.8e-9) : new FloatWithError(5.8e-9, 0.5e-9);

        var validity = result.ValiditySnapshot.Compare(result.Solution);
        Assert.Equal(AnalysisResultValidity.Invalid, validity.Status);
        Assert.Contains(validity.Reasons, reason => reason.Contains("ligand concentration attribute changed"));
    }

    [Theory]
    [InlineData(5e-12, 5.000000001e-12, true)]
    [InlineData(5e-12, 5.00000002e-12, false)]
    [InlineData(0.005, 0.005000000001, true)]
    [InlineData(0.005, 0.00500000002, false)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1e-15, false)]
    [InlineData(0, double.Epsilon, false)]
    [InlineData(-5e-12, -5.8e-12, false)]
    [InlineData(double.NaN, double.NaN, true)]
    [InlineData(double.NaN, 5e-12, false)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity, true)]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity, false)]
    [InlineData(double.PositiveInfinity, 5e-12, false)]
    public void ConcentrationComparisonIsRelativeAndHandlesSpecialValues(double before, double after, bool equivalent)
    {
        var stored = new ExperimentAttributeSnapshot { Key = AttributeKey.PreboundLigandConc, ParameterValue = before };
        var current = new ExperimentAttributeSnapshot { Key = AttributeKey.PreboundLigandConc, ParameterValue = after };
        Assert.Equal(equivalent, stored.EquivalentTo(current));
        Assert.Equal(equivalent, current.EquivalentTo(stored));
    }

    [Fact]
    public void TinyKdRoundoffKeepsSnapshotEquivalentAfterJsonRoundTrip()
    {
        var saved = new ExperimentAttributeSnapshot
        {
            Key = AttributeKey.CompetitorResult,
            CapturedAffinity = 5e-12,
            CapturedAffinitySD = 0.5e-12,
            CapturedAffinityLower = 4e-12,
            CapturedAffinityUpper = 6e-12,
        };
        var snapshot = new AnalysisResultValiditySnapshot();
        snapshot.Experiments.Add(new ExperimentFitInputSnapshot { Attributes = { saved } });
        var restored = AnalysisResultValiditySnapshot.FromJson(snapshot.ToJson()).Experiments.Single().Attributes.Single();
        Assert.True(saved.EquivalentTo(restored));
        restored.CapturedAffinity = 5.000000001e-12;
        restored.CapturedAffinitySD = 0.5000000001e-12;
        restored.CapturedAffinityLower = 4.000000001e-12;
        restored.CapturedAffinityUpper = 6.000000001e-12;
        Assert.True(saved.EquivalentTo(restored));
    }

    static FloatWithError Kd(string component, double value) => component switch
    {
        "value" => new FloatWithError(value),
        "sd" => new FloatWithError(5e-12, value, 4e-12, 6e-12),
        "lower" => new FloatWithError(5e-12, 0.5e-12, value, 6e-12),
        "upper" => new FloatWithError(5e-12, 0.5e-12, 4e-12, value),
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
}
