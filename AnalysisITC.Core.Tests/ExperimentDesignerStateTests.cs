using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;

using Xunit;

namespace AnalysisITC.Core.Tests;

// SingleModelFactory reads the static ModelFactory.PreviousAttributes, so these tests
// run outside the parallel collections.
[Collection(AnalysisBuilderConstraintCollectionDefinition.Name)]
public sealed class ExperimentDesignerStateTests
{
    [Fact]
    public void SetupReconstructionKeepsUserValues()
    {
        var state = new ExperimentDesignerState();
        state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));
        state.RememberParameter(ParameterType.Nvalue1, 1.7);
        state.RememberParameter(ParameterType.Enthalpy1, -43210);
        state.RememberParameter(ParameterType.Affinity1, 7.25);
        state.RememberParameter(ParameterType.Offset, 1234);

        var rebuilt = state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(21, cellMicroMolar: 25));

        Assert.Equal(21, rebuilt.Model.Data.Injections.Count);
        Assert.Equal(1.7, Value(rebuilt, ParameterType.Nvalue1));
        Assert.Equal(-43210, Value(rebuilt, ParameterType.Enthalpy1));
        Assert.Equal(7.25, Value(rebuilt, ParameterType.Affinity1));
        Assert.Equal(1234, Value(rebuilt, ParameterType.Offset));
    }

    [Fact]
    public void ParametersWithoutUserValuesUseTheDesignerDefaults()
    {
        var state = new ExperimentDesignerState();

        var factory = state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));

        Assert.Equal(1, Value(factory, ParameterType.Nvalue1));
        Assert.Equal(-30000, Value(factory, ParameterType.Enthalpy1));
    }

    [Fact]
    public void ModelSwitchSharesKeysAndRestoresInactiveParameters()
    {
        var state = new ExperimentDesignerState();
        state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));
        state.RememberParameter(ParameterType.Nvalue1, 1.7);
        state.RememberParameter(ParameterType.Affinity1, 7.25);
        state.RememberParameter(ParameterType.Enthalpy1, -43210);

        var dissociation = state.CreateFactory(AnalysisModel.Dissociation, DesignerExperiment(20));
        Assert.DoesNotContain(dissociation.GetExposedParameters(), parameter => parameter.Key == ParameterType.Nvalue1);
        Assert.Equal(7.25, Value(dissociation, ParameterType.Affinity1));
        Assert.Equal(-43210, Value(dissociation, ParameterType.Enthalpy1));
        state.RememberParameter(ParameterType.Enthalpy1, -5000);

        var oneSite = state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));

        Assert.Equal(1.7, Value(oneSite, ParameterType.Nvalue1));
        Assert.Equal(7.25, Value(oneSite, ParameterType.Affinity1));
        Assert.Equal(-5000, Value(oneSite, ParameterType.Enthalpy1));
    }

    [Fact]
    public void RememberedOptionsOverrideOptionsStoredByOtherFactories()
    {
        var state = new ExperimentDesignerState();
        var first = state.CreateFactory(AnalysisModel.TwoSetsOfSites, DesignerExperiment(20));
        var option = first.GetExposedModelOptions()[AttributeKey.LockDuplicateParameter].Copy();
        option.BoolValue = true;
        state.RememberOption(option);

        var global = option.Copy();
        global.BoolValue = false;
        ModelFactory.StorePreviousAttribute(global);
        try
        {
            var rebuilt = state.CreateFactory(AnalysisModel.TwoSetsOfSites, DesignerExperiment(21));

            Assert.True(rebuilt.GetExposedModelOptions()[AttributeKey.LockDuplicateParameter].BoolValue);
            Assert.NotSame(option, rebuilt.GetExposedModelOptions()[AttributeKey.LockDuplicateParameter]);
        }
        finally
        {
            ModelFactory.PreviousAttributes.RemoveAll(attribute => attribute.Key == AttributeKey.LockDuplicateParameter);
        }
    }

    [Fact]
    public void OptionsThatAddParametersKeepUserValuesAndDefaultTheNewOnes()
    {
        // Start from two sites regardless of a site count other tests stored globally.
        var state = new ExperimentDesignerState();
        var siteCount = state.CreateFactory(AnalysisModel.SequentialBindingSites, DesignerExperiment(20))
            .GetExposedModelOptions()[AttributeKey.SequentialSiteCount].Copy();
        siteCount.IntValue = 2;
        state.RememberOption(siteCount);
        var first = state.CreateFactory(AnalysisModel.SequentialBindingSites, DesignerExperiment(20));
        Assert.DoesNotContain(first.GetExposedParameters(), parameter => parameter.Key == ParameterType.Affinity3);
        state.RememberParameter(ParameterType.Affinity1, 6.5);
        state.RememberParameter(ParameterType.Enthalpy2, -12000);
        siteCount.IntValue = 3;
        state.RememberOption(siteCount);

        var rebuilt = state.CreateFactory(AnalysisModel.SequentialBindingSites, DesignerExperiment(20));

        Assert.Equal(7, rebuilt.GetExposedParameters().Count());
        Assert.Equal(6.5, Value(rebuilt, ParameterType.Affinity1));
        Assert.Equal(-12000, Value(rebuilt, ParameterType.Enthalpy2));
        Assert.Equal(-30000, Value(rebuilt, ParameterType.Enthalpy3));
    }

    [Fact]
    public void ApplyParametersReplacesValuesWrittenToTheModel()
    {
        var state = new ExperimentDesignerState();
        var factory = state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));
        var defaultAffinity = Value(factory, ParameterType.Affinity1);
        state.RememberParameter(ParameterType.Nvalue1, 1.7);

        // A fit writes its estimates to the model parameters.
        factory.UpdateParameter(ParameterType.Nvalue1, 3.3, false);
        factory.UpdateParameter(ParameterType.Affinity1, defaultAffinity + 1.5, false);
        factory.UpdateParameter(ParameterType.Enthalpy1, -80000, false);
        state.ApplyParameters(factory);

        Assert.Equal(1.7, Value(factory, ParameterType.Nvalue1));
        Assert.Equal(defaultAffinity, Value(factory, ParameterType.Affinity1));
        Assert.Equal(-30000, Value(factory, ParameterType.Enthalpy1));
        Assert.False(state.TryGetParameter(ParameterType.Affinity1, out _));
    }

    [Fact]
    public void ForgottenParametersReturnToTheirDefaults()
    {
        var state = new ExperimentDesignerState();
        var factory = state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));
        state.RememberParameter(ParameterType.Enthalpy1, -43210);
        state.ApplyParameters(factory);

        state.ForgetParameter(ParameterType.Enthalpy1);
        state.ApplyParameters(factory);

        Assert.Equal(-30000, Value(factory, ParameterType.Enthalpy1));
    }

    [Fact]
    public void RestoredValuesAreClampedToTheParameterLimits()
    {
        var previous = AppSettings.ParameterLimitSetting;
        try
        {
            AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
            var state = new ExperimentDesignerState();
            state.RememberParameter(ParameterType.Nvalue1, 1e9);
            state.RememberParameter(ParameterType.Affinity1, -1e9);

            var factory = state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));
            var n = Parameter(factory, ParameterType.Nvalue1);
            var affinity = Parameter(factory, ParameterType.Affinity1);

            Assert.Equal(n.Limits[1], n.Value);
            Assert.Equal(affinity.Limits[0], affinity.Value);
            Assert.True(state.TryGetParameter(ParameterType.Nvalue1, out var remembered));
            Assert.Equal(1e9, remembered);
        }
        finally
        {
            AppSettings.ParameterLimitSetting = previous;
        }
    }

    [Fact]
    public void FailedReconstructionKeepsThePreviousDefaults()
    {
        var state = new ExperimentDesignerState();
        var factory = state.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));
        var defaultAffinity = Value(factory, ParameterType.Affinity1);
        var empty = DesignerExperiment(20);
        foreach (var injection in empty.Injections) injection.Include = false;

        Assert.ThrowsAny<Exception>(() => state.CreateFactory(AnalysisModel.OneSetOfSites, empty));

        factory.UpdateParameter(ParameterType.Affinity1, defaultAffinity + 2, false);
        state.ApplyParameters(factory);
        Assert.Equal(defaultAffinity, Value(factory, ParameterType.Affinity1));
    }

    [Fact]
    public void DesignerWindowsKeepIndependentValues()
    {
        var first = new ExperimentDesignerState();
        var second = new ExperimentDesignerState();
        first.RememberParameter(ParameterType.Nvalue1, 1.7);
        var option = new SingleModelFactory(AnalysisModel.TwoSetsOfSites);
        option.InitializeModel(DesignerExperiment(20));
        var lockOption = option.GetExposedModelOptions()[AttributeKey.LockDuplicateParameter].Copy();
        lockOption.BoolValue = !lockOption.BoolValue;
        first.RememberOption(lockOption);

        var factory = second.CreateFactory(AnalysisModel.OneSetOfSites, DesignerExperiment(20));

        Assert.Equal(1, Value(factory, ParameterType.Nvalue1));
        Assert.False(second.TryGetParameter(ParameterType.Nvalue1, out _));
        Assert.False(second.TryGetOption(AttributeKey.LockDuplicateParameter, out _));
    }

    static double Value(SingleModelFactory factory, ParameterType key) => Parameter(factory, key).Value;

    static Parameter Parameter(SingleModelFactory factory, ParameterType key) =>
        factory.GetExposedParameters().Single(parameter => parameter.Key == key);

    static ExperimentData DesignerExperiment(int injectionCount, double cellMicroMolar = 10, double syringeMicroMolar = 100)
    {
        var data = new ExperimentData("ExperimentDesignerStateTests")
        {
            Instrument = ITCInstrument.MicroCalITC200,
            CellVolume = 202.8e-6,
            CellConcentration = new FloatWithError(cellMicroMolar * 1e-6, 0),
            SyringeConcentration = new FloatWithError(syringeMicroMolar * 1e-6, 0),
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        for (var i = 0; i < injectionCount; i++)
            data.Injections.Add(new InjectionData(data, i, i == 0 ? 0.5e-6 : 2e-6, 0, i != 0));
        RawDataReader.ProcessInjections(data);
        return data;
    }
}
