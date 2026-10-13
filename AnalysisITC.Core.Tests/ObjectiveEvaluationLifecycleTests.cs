using System;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class ObjectiveEvaluationTests
{
    [Theory]
    [InlineData("sequential-2")]
    [InlineData("two-realistic")]
    public void ParametersAndOptionsAreFreshBetweenObjectiveEvaluations(string id)
    {
        foreach (var method in new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement })
        {
            var model = CreateModel("PytcBookkeeping", id);
            SetMethod(model, method);
            AssertObjectiveParity(model, false);
            AssertObjectiveParity(model, true);
            model.Parameters.Table[ParameterType.Affinity1].Update(model.Parameters.Table[ParameterType.Affinity1].Value + .03);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(model.Parameters.Table[ParameterType.Enthalpy1].Value * .95);
            model.Parameters.Table[ParameterType.Offset].Update(123);
            AssertObjectiveParity(model, true);
            if (model is SequentialBindingSites)
                model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = 4;
            else
            {
                model.ModelOptions[AttributeKey.UseSyringeActiveFraction].BoolValue = true;
                model.ModelOptions[AttributeKey.NumberOfSites1].DoubleValue = .8;
                model.ModelOptions[AttributeKey.NumberOfSites2].DoubleValue = 1.8;
                model.Parameters.Table[ParameterType.Nvalue1].Update(.7);
            }
            AssertObjectiveParity(model, false);
            AssertObjectiveParity(model, true);
        }
    }

    [Theory]
    [InlineData("sequential-2")]
    [InlineData("two-realistic")]
    public void ZeroVolumeAndExcludedShotsMatchUncachedFixturePredictions(string id)
    {
        foreach (var method in new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement })
        {
            var model = CreateModel("PytcBookkeeping", id);
            var previous = model.Data.Injections[2];
            var zero = new InjectionData(model.Data, previous.ID, 0, 0, true);
            zero.SetPeakArea(previous.PeakArea);
            model.Data.Injections[2] = zero;
            model.Data.Injections[0].Include = false;
            model.Data.Injections[4].Include = false;
            SetMethod(model, method);
            AssertObjectiveParity(model, false);
            AssertObjectiveParity(model, true);
        }
    }

    [Fact]
    public void DiagnosticFractionsRemainOwnedByTheirReturnedState()
    {
        var model = (SequentialBindingSites)CreateModel("PytcBookkeeping", "sequential-4");
        var state = model.CalculateState(10e-6, 1e-6);
        var expected = Bits(state.Fractions);
        var other = model.CalculateState(10e-6, 200e-6);
        Assert.NotSame(state.Fractions, other.Fractions);
        AssertObjectiveParity(model, true);
        Assert.Equal(expected, Bits(state.Fractions));
        model.CalculateState(10e-6, 0);
        Assert.Equal(expected, Bits(state.Fractions));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SequentialExceptionClearsPreparedStateBeforeDirectEvaluation(int objective)
    {
        var model = CreateModel("PytcBookkeeping", "sequential-2");
        var injection = model.Data.Injections[3];
        var cell = injection.ActualCellConcentration;
        injection.ActualCellConcentration = double.NaN;
        Assert.Throws<ArgumentOutOfRangeException>(() => CallObjective(model, objective));
        injection.ActualCellConcentration = cell;
        AssertFreshDirectEvaluation(model, "sequential-2");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void InvalidTwoSiteTrialClearsPreparedParametersBeforeDirectEvaluation(int objective)
    {
        var model = CreateModel("PytcBookkeeping", "two-realistic");
        var affinity = model.Parameters.Table[ParameterType.Affinity1].Value;
        model.Parameters.Table[ParameterType.Affinity1].Update(400);
        var parameters = model.Parameters.GetFittedParameterArray();
        if (objective == 1)
        {
            Assert.False(model.TryLossFunction(parameters, true, out var loss));
            Assert.True(double.IsNaN(loss));
        }
        else
        {
            Assert.False(model.TryLossFunctionResiduals(parameters, true, out var residuals));
            Assert.Null(residuals);
        }
        model.Parameters.Table[ParameterType.Affinity1].Update(affinity);
        AssertFreshDirectEvaluation(model, "two-realistic");
    }

    void AssertFreshDirectEvaluation(Model model, string id)
    {
        var fresh = CreateModel("PytcBookkeeping", id);
        var enthalpy = model.Parameters.Table[ParameterType.Enthalpy1].Value * .9;
        model.Parameters.Table[ParameterType.Enthalpy1].Update(enthalpy);
        fresh.Parameters.Table[ParameterType.Enthalpy1].Update(enthalpy);
        Assert.Equal(Bits(fresh.Data.Injections.Select(i => fresh.Evaluate(i.ID))),
            Bits(model.Data.Injections.Select(i => model.Evaluate(i.ID))));
        AssertObjectiveParity(model, true);
    }

    [Fact]
    public void EmptyAndZeroCellObjectivesKeepSequentialAffinityValidationLazy()
    {
        var model = CreateModel("PytcBookkeeping", "sequential-2");
        model.Parameters.Table[ParameterType.Affinity1].Update(double.PositiveInfinity);
        foreach (var injection in model.Data.Injections) injection.Include = false;
        AssertObjectiveParity(model, true);
        model.Data.CellConcentration = new(0);
        foreach (var injection in model.Data.Injections)
        {
            injection.Include = true;
            injection.ActualCellConcentration = 0;
        }
        AssertObjectiveParity(model, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ObjectiveHooksCloseOnEveryExitAndOpenAfterParameterUpdates(int objective)
    {
        var source = CreateModel("PytcBookkeeping", "one-exothermic");
        var model = new ScopeProbe(source.Data);
        var parameters = model.Parameters.GetFittedParameterArray();
        model.Parameters.Table[ParameterType.Offset].Update(18);
        CallObjective(model, objective, parameters);
        Assert.Equal(1, model.Ends);
        Assert.False(model.Active);
        model.InvalidPrediction = true;
        CallObjective(model, objective);
        Assert.Equal(2, model.Ends);
        Assert.False(model.Active);
        model.InvalidPrediction = false;
        model.ThrowDuringEvaluation = true;
        Assert.Throws<InvalidOperationException>(() => CallObjective(model, objective));
        Assert.Equal(3, model.Ends);
        Assert.False(model.Active);
        model.ThrowDuringEvaluation = false;
        model.ThrowDuringBegin = true;
        Assert.Throws<InvalidOperationException>(() => CallObjective(model, objective));
        Assert.Equal(4, model.Ends);
        Assert.False(model.Active);
        model.ThrowDuringBegin = false;
        model.StopDuringEvaluation = true;
        Assert.Throws<OptimizerStopException>(() => CallObjective(model, objective));
        Assert.Equal(5, model.Ends);
        Assert.False(model.Active);
        model.StopDuringEvaluation = false;
        SolverInterface.TerminateAnalysisFlag.Raise();
        try
        {
            Assert.Throws<OptimizerStopException>(() => CallObjective(model, objective));
            Assert.Equal(5, model.Begins);
            Assert.Equal(5, model.Ends);
        }
        finally { SolverInterface.TerminateAnalysisFlag.Lower(); }
        CallObjective(model, objective);
        Assert.Equal(6, model.Ends);
    }

    static void CallObjective(Model model, int objective, double[] parameters = null)
    {
        parameters ??= model.Parameters.GetFittedParameterArray();
        switch (objective)
        {
            case 0: model.LossFunction(parameters, true); break;
            case 1: model.TryLossFunction(parameters, true, out _); break;
            case 2: model.LossFunctionResiduals(parameters, true); break;
            case 3: model.TryLossFunctionResiduals(parameters, true, out _); break;
            default: throw new ArgumentOutOfRangeException(nameof(objective));
        }
    }

    [Theory]
    [InlineData(DilutionMethod.MicroCal)]
    [InlineData(DilutionMethod.Exponential)]
    [InlineData(DilutionMethod.DiscreteDisplacement)]
    public void ThreeSlotMemoReusesEndpointsInProductionHeatAccessOrder(DilutionMethod method)
    {
        var source = CreateModel("PytcBookkeeping", "one-exothermic");
        SetMethod(source, method);
        var cached = new ContentProbe(source.Data, true);
        var uncached = new ContentProbe(source.Data, false);
        foreach (var injection in source.Data.Injections)
            Assert.Equal(uncached.Evaluate(injection.ID), cached.Evaluate(injection.ID));
        var count = source.Data.Injections.Count;
        Assert.Equal(method == DilutionMethod.Exponential ? 2 * count + 1 : count + 1, cached.Calls);
        Assert.Equal(method == DilutionMethod.Exponential ? 3 * count : 2 * count, uncached.Calls);
    }

    [Fact]
    public void MemoUsesExactConcentrationKeysAndRejectsNonfiniteResults()
    {
        var memo = new HeatContentMemo();
        memo.Store(0, 1, 10);
        Assert.True(memo.TryGet(0, 1, out var heat));
        Assert.Equal(10, heat);
        Assert.False(memo.TryGet(BitConverter.Int64BitsToDouble(long.MinValue), 1, out _));
        Assert.False(memo.TryGet(0, BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1) + 1), out _));
        memo.Store(2, 1, double.NaN);
        memo.Store(3, 1, double.PositiveInfinity);
        Assert.False(memo.TryGet(2, 1, out _));
        Assert.False(memo.TryGet(3, 1, out _));
        memo.Store(1, 1, 11);
        memo.Store(2, 1, 12);
        Assert.True(memo.TryGet(0, 1, out _));
        memo.Store(3, 1, 13);
        Assert.False(memo.TryGet(1, 1, out _));
        Assert.True(memo.TryGet(0, 1, out _));
    }

    sealed class ScopeProbe : Model
    {
        internal ScopeProbe(ExperimentData data) : base(data) => Parameters.AddOrUpdateParameter(ParameterType.Offset, 17);
        internal bool Active, InvalidPrediction, ThrowDuringBegin, ThrowDuringEvaluation, StopDuringEvaluation;
        internal int Begins, Ends;
        protected override void BeginObjectiveEvaluation()
        {
            Begins++;
            Active = true;
            Assert.Equal(17, Parameters.Table[ParameterType.Offset].Value);
            if (ThrowDuringBegin) throw new InvalidOperationException();
        }
        protected override void EndObjectiveEvaluation() { Ends++; Active = false; }
        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            Assert.True(Active);
            if (StopDuringEvaluation) throw new OptimizerStopException();
            if (ThrowDuringEvaluation) throw new InvalidOperationException();
            return InvalidPrediction ? double.NaN : 0;
        }
    }

    sealed class ContentProbe : Model
    {
        readonly bool cached;
        HeatContentMemo memo;
        internal int Calls;
        internal ContentProbe(ExperimentData data, bool cached) : base(data) => this.cached = cached;
        public override double Evaluate(int injectionindex, bool withoffset = true)
            => DeltaHeatFromHeatContent(injectionindex, Content);
        double Content(double cell, double ligand)
        {
            if (cached && memo.TryGet(cell, ligand, out var heat)) return heat;
            Calls++;
            var result = cell * ligand;
            if (cached) memo.Store(cell, ligand, result);
            return result;
        }
    }
}
