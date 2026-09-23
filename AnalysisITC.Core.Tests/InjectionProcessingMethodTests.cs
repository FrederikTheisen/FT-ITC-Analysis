using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class InjectionProcessingMethodTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    public InjectionProcessingMethodTests() => PreferencesState.Defaults().ApplyToSettings();
    public void Dispose() => original.ApplyToSettings();

    [Theory]
    [InlineData(DilutionMethod.Exponential, InjectionHeatMethod.IdealContinuousMixing, "Ideal continuous mixing")]
    [InlineData(DilutionMethod.DiscreteDisplacement, InjectionHeatMethod.DiscreteDisplacement, "Discrete displacement")]
    public void PreferenceIsOnlyANewDataDefault(DilutionMethod method, InjectionHeatMethod heatMethod, string label)
    {
        var data = NewExperiment();
        RawDataReader.ProcessInjections(data);
        Assert.Equal(DilutionMethod.MicroCal, data.AppliedDilutionMethod);
        Assert.Equal(InjectionHeatMethod.Legacy, data.HeatMethod);
        var concentration = data.Injections[0].ActualCellConcentration;
        AppSettings.DilutionCalculationMethod = method;
        RawDataReader.ProcessInjections(data);
        Assert.Equal(concentration, data.Injections[0].ActualCellConcentration);
        Assert.Equal(InjectionHeatMethod.Legacy, data.HeatMethod);
        var newData = NewExperiment();
        RawDataReader.ProcessInjections(newData);
        Assert.Equal(heatMethod, newData.HeatMethod);
        Assert.Equal(label, newData.BookkeepingDescription);
        Assert.Equal(1, (int)DilutionMethod.Exponential);
        Assert.Equal(2, (int)DilutionMethod.DiscreteDisplacement);
    }

    [Fact]
    public void ExplicitUpgradeInvalidatesFitEvenWithIdenticalExponentialConcentrations()
    {
        var model = FittedModel();
        model.Data.HeatMethod = model.HeatMethod = InjectionHeatMethod.Legacy;
        var snapshot = ExperimentFitInputSnapshot.Capture(model);
        var cells = model.Data.Injections.Select(i => i.ActualCellConcentration).ToArray();
        var heats = model.Data.Injections.Select(i => i.RawPeakArea.Value).ToArray();
        RawDataReader.ReprocessInjections(model.Data, DilutionMethod.Exponential);
        Assert.Equal(cells, model.Data.Injections.Select(i => i.ActualCellConcentration));
        Assert.Equal(heats, model.Data.Injections.Select(i => i.RawPeakArea.Value));
        Assert.False(model.Solution.IsValid);
        Assert.Equal(InjectionHeatMethod.Legacy, model.HeatMethod); // The fit owns its convention.
        Assert.Equal(InjectionHeatMethod.IdealContinuousMixing, new OneSetOfSites(model.Data).HeatMethod);
        var reasons = new List<string>();
        Assert.True(snapshot.Compare(model, reasons));
        Assert.Contains(reasons, r => r.Contains("bookkeeping"));
    }

    [Fact]
    public void BatchReprocessingUpdatesOrdinaryExperimentsAndSkipsTandemExperiments()
    {
        var ordinary = FittedModel(method: DilutionMethod.MicroCal);
        var processingRevision = ordinary.Data.ProcessingRevision;
        var tandem = NewExperiment();
        tandem.AddSegment(new TandemExperimentSegment(0, 20e-6, 0));
        var tandemConcentrations = tandem.Injections
            .Select(injection => injection.ActualCellConcentration)
            .ToArray();

        var updated = RawDataReader.ReprocessInjections(
            new[] { ordinary.Data, tandem }, DilutionMethod.DiscreteDisplacement);

        Assert.Equal(1, updated);
        Assert.Equal(DilutionMethod.DiscreteDisplacement, ordinary.Data.AppliedDilutionMethod);
        Assert.Equal(InjectionHeatMethod.DiscreteDisplacement, ordinary.Data.HeatMethod);
        Assert.Equal(processingRevision + 1, ordinary.Data.ProcessingRevision);
        Assert.False(ordinary.Solution.IsValid);
        Assert.Null(tandem.AppliedDilutionMethod);
        Assert.Equal(tandemConcentrations,
            tandem.Injections.Select(injection => injection.ActualCellConcentration));
    }

    [Fact]
    public void BatchReprocessingValidatesEveryExperimentBeforeChangingAny()
    {
        var valid = NewExperiment();
        var invalid = NewExperiment();
        invalid.CellVolume = 0;

        Assert.Throws<ArgumentOutOfRangeException>(() => RawDataReader.ReprocessInjections(
            new[] { valid, invalid }, DilutionMethod.DiscreteDisplacement));

        Assert.Null(valid.AppliedDilutionMethod);
        Assert.Null(invalid.AppliedDilutionMethod);
    }

    [Theory]
    [InlineData(ErrorEstimationMethod.None, DilutionMethod.Exponential)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood, DilutionMethod.Exponential)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, DilutionMethod.Exponential)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, DilutionMethod.Exponential)]
    [InlineData(ErrorEstimationMethod.None, DilutionMethod.DiscreteDisplacement)]
    [InlineData(ErrorEstimationMethod.ProfileLikelihood, DilutionMethod.DiscreteDisplacement)]
    [InlineData(ErrorEstimationMethod.BootstrapResiduals, DilutionMethod.DiscreteDisplacement)]
    [InlineData(ErrorEstimationMethod.LeaveOneOut, DilutionMethod.DiscreteDisplacement)]
    public void SyntheticAndSnapshotClonesRetainMethod(ErrorEstimationMethod method, DilutionMethod bookkeeping)
    {
        var source = FittedModel(method: bookkeeping);
        source.ModelCloneOptions = new ModelCloneOptions
        {
            ErrorEstimationMethod = method, DiscardedDataPoint = 2,
            IncludeConcentrationErrorsInBootstrap = true,
        };
        source.Data.CellConcentration = new FloatWithError(source.Data.CellConcentration, 1e-6);
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        var clone = source.GenerateSyntheticModel(new Random(123));
        clone.ApplyModelOptions();
        Assert.Equal(source.HeatMethod, clone.HeatMethod);
        Assert.Equal(source.HeatMethod, clone.Data.HeatMethod);
        Assert.Equal(source.Data.AppliedDilutionMethod, clone.Data.AppliedDilutionMethod);
        clone.Solution = SolutionInterface.FromModel(clone, null);
        var snapshot = BootstrapModelSnapshot.Capture(clone.Solution, 0);
        var restored = snapshot.Restore(source);
        Assert.Equal(clone.Data.CellConcentration.Value, restored.Data.CellConcentration.Value);
        foreach (var i in clone.Data.Injections)
            Assert.Equal(clone.Evaluate(i.ID), restored.Model.Evaluate(i.ID));
    }

    [Theory]
    [InlineData(DilutionMethod.Exponential)]
    [InlineData(DilutionMethod.DiscreteDisplacement)]
    public void PredictionCacheTracksHeatMethodAndExplicitReprocessing(DilutionMethod method)
    {
        var model = FittedModel(bootstrap: true, method: method);
        var cache = new BootstrappedEvaluationStorage(model);
        cache.SetDataPoint(0, true, new FloatWithError(1));
        Assert.True(cache.IsValid(model, 0, true));
        model.HeatMethod = InjectionHeatMethod.Legacy;
        Assert.False(cache.IsValid(model, 0, true));
        model.HeatMethod = InjectionBookkeeping.HeatMethodFor(method);
        Assert.True(cache.IsValid(model, 0, true));
        RawDataReader.ReprocessInjections(model.Data, method);
        Assert.False(cache.IsValid(model, 0, true));
    }

    [Theory]
    [InlineData(DilutionMethod.Exponential)]
    [InlineData(DilutionMethod.DiscreteDisplacement)]
    public void DuplicateRetainsSavedStateWithoutUsingPreferences(DilutionMethod method)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        try
        {
            var source = FittedModel(method: method).Data;
            AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
            DataManager.DuplicateSelectedData(source);
            var copy = Assert.Single(DataManager.Data);
            Assert.Equal(source.HeatMethod, copy.HeatMethod);
            Assert.Equal(source.AppliedDilutionMethod, copy.AppliedDilutionMethod);
            Assert.Equal(source.Injections.Select(i => i.ActualTitrantConcentration), copy.Injections.Select(i => i.ActualTitrantConcentration));
        }
        finally { DataManager.Clear(DataClearMode.ResetSession); }
    }

    [Fact]
    public void UnknownSavedStateAndTandemRequireExplicitHandling()
    {
        var data = NewExperiment();
        Assert.Equal(InjectionBookkeeping.SavedProcessingLabel, data.BookkeepingDescription);
        Assert.Throws<InvalidOperationException>(() => RawDataReader.RecalculateInjections(data));
        data.AddSegment(new TandemExperimentSegment(0, 20e-6, 0));
        Assert.Throws<InvalidOperationException>(() => RawDataReader.ReprocessInjections(data, DilutionMethod.Exponential));
    }

    [Fact]
    public void UnchangedDetailAttributesDoNotTouchMeasuredHeats()
    {
        var data = FittedModel().Data;
        var attribute = new ExperimentAttribute();
        attribute.UpdateOptionKey(AttributeKey.PreboundLigandConc);
        attribute.ParameterValue = new FloatWithError(3e-6);
        data.Attributes.Add(attribute);
        var heats = data.Injections.Select(i => i.PeakArea.Value).ToArray();
        Assert.False(data.UpdateDetailAttributes(data.Attributes.Select(a => a.Copy())));
        Assert.Equal(heats, data.Injections.Select(i => i.PeakArea.Value));
    }

    internal static Model FittedModel(string id = "one-c100-v0.01", bool bootstrap = false,
        DilutionMethod method = DilutionMethod.Exponential)
    {
        var model = DumasInjectionHeatTests.CreateModel(DumasInjectionHeatTests.Reference(id));
        if (method != DilutionMethod.Exponential) SetMethod(model, method);
        model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None };
        model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        { Algorithm = SolverAlgorithm.LevenbergMarquardt }));
        if (bootstrap)
        {
            for (var i = 0; i < 2; i++)
            {
                var clone = model.GenerateSyntheticModel(new Random(i));
                clone.ApplyModelOptions();
                clone.Parameters.Table[ParameterType.Enthalpy1].Update(clone.Parameters.Table[ParameterType.Enthalpy1].Value * (1 + 0.01 * (i + 1)));
                clone.Solution = SolutionInterface.FromModel(clone, null);
                clone.Solution.BootstrapReplicateIndex = i;
                model.Solution.BootstrapSolutions.Add(clone.Solution);
            }
        }
        return model;
    }

    internal static void SetMethod(Model model, DilutionMethod method)
    {
        var data = model.Data;
        if (!data.IsTandemExperiment) RawDataReader.ProcessInjections(data, method);
        else
        {
            Assert.Equal(DilutionMethod.DiscreteDisplacement, method);
            var initial = new InjectionConcentrationState(data.CellConcentration, 0);
            var retention = 1.0;
            foreach (var injection in data.Injections)
            {
                var segment = data.Segments.FirstOrDefault(s => s.FirstInjectionID == injection.ID);
                if (segment != null)
                {
                    initial = new InjectionConcentrationState(segment.SegmentInitialActiveCellConc, segment.SegmentInitialActiveTitrantConc);
                    retention = 1.0;
                }
                retention *= InjectionDisplacementCalculator.DiscreteDisplacementRetention(data.CellVolume, injection.Volume);
                InjectionDisplacementCalculator.ApplyToInjection(data, injection,
                    InjectionDisplacementCalculator.DiscreteDisplacementState(initial, data.SyringeConcentration, retention));
            }
            data.AppliedDilutionMethod = method;
            data.HeatMethod = InjectionBookkeeping.HeatMethodFor(method);
        }
        model.HeatMethod = data.HeatMethod;
    }

    static ExperimentData NewExperiment()
    {
        var data = new ExperimentData("bookkeeping.itc")
        { CellVolume = 200e-6, CellConcentration = new(20e-6), SyringeConcentration = new(400e-6) };
        for (var i = 0; i < 3; i++) data.Injections.Add(new InjectionData(data, i, 2e-6, 8e-10, true));
        return data;
    }
}
