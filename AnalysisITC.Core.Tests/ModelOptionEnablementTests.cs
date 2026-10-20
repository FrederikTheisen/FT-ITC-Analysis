using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ModelOptionEnablementTests
{
    [Theory]
    [InlineData(AttributeKey.NumberOfSites1, false, false)]
    [InlineData(AttributeKey.NumberOfSites2, false, false)]
    [InlineData(AttributeKey.LockDuplicateParameter, false, true)]
    [InlineData(AttributeKey.NumberOfSites1, true, true)]
    [InlineData(AttributeKey.NumberOfSites2, true, true)]
    [InlineData(AttributeKey.LockDuplicateParameter, true, false)]
    public void FixedCountsAndSharingHaveDistinctApplicableModes(AttributeKey key, bool syringe, bool enabled)
    {
        Assert.Equal(enabled, ModelOptionCatalog.IsOptionEnabled(key, syringe));
    }

    [Theory]
    [InlineData(AttributeKey.UseSyringeActiveFraction)]
    [InlineData(AttributeKey.PreboundLigandAffinity)]
    [InlineData(AttributeKey.SequentialSiteCount)]
    public void OtherOptionsRemainEditable(AttributeKey key)
    {
        Assert.True(ModelOptionCatalog.IsOptionEnabled(key, false));
        Assert.True(ModelOptionCatalog.IsOptionEnabled(key, true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FittedStoichiometrySharingDoesNotShareFixedCounts(bool syringe, bool shared)
    {
        var data = InjectionProcessingMethodTests.FittedModel().Data;
        var model = new TwoSetsOfSites(data);
        model.InitializeParameters(data);
        model.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = syringe;
        model.ModelOptions[AttributeKey.LockDuplicateParameter].BoolValue = shared;
        model.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = 2;
        model.ModelOptions[AttributeKey.NumberOfSites2].DoubleValue = 3;
        model.ApplyModelOptions();

        Assert.True(model.Parameters.Table[ParameterType.Nvalue1].IsFitted);
        Assert.Equal(!syringe && !shared, model.Parameters.Table[ParameterType.Nvalue2].IsFitted);
        if (syringe || shared)
            Assert.Equal(model.Parameters.Table[ParameterType.Nvalue1].Value, model.Parameters.Table[ParameterType.Nvalue2].Value);
        Assert.Equal(2, model.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue);
        Assert.Equal(3, model.ModelOptions[AttributeKey.NumberOfSites2].DoubleValue);
    }
}
