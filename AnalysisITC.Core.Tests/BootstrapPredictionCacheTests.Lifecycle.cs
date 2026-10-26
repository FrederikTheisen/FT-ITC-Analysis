using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class BootstrapPredictionCacheTests
{
    [Theory]
    [InlineData("two-realistic", DilutionMethod.MicroCal, 4)]
    [InlineData("two-realistic", DilutionMethod.DiscreteDisplacement, 4)]
    [InlineData("two-realistic", DilutionMethod.Exponential, 7)]
    [InlineData("sequential-2", DilutionMethod.MicroCal, 4)]
    [InlineData("sequential-2", DilutionMethod.DiscreteDisplacement, 4)]
    [InlineData("sequential-2", DilutionMethod.Exponential, 7)]
    [InlineData("sequential-3", DilutionMethod.MicroCal, 4)]
    [InlineData("sequential-4", DilutionMethod.MicroCal, 4)]
    public void BothBandsUseExactlyOneScopedPassPerReplicate(string id, DilutionMethod method, int solvesPerReplicate)
    {
        var model = CreateModel(id, method);
        var replicas = model.Solution.BootstrapSolutions.Select(s => s.Model).ToArray();
        foreach (var member in replicas.Prepend(model))
            member.Data.Injections.RemoveRange(3, member.Data.InjectionCount - 3);
        long Count() => replicas.Sum(m => m is TwoSetsOfSites two ? two.HeatContentSolveCount
            : ((SequentialBindingSites)m).HeatContentSolveCount);

        var before = Count();
        model.EvaluateBootstrap(2, false);
        // Three shots share four endpoints; continuous mixing adds three midpoints.
        Assert.Equal(solvesPerReplicate * replicas.Length, Count() - before);
        before = Count();
        for (var repeat = 0; repeat < 2; repeat++)
        foreach (var withOffset in new[] { true, false })
        for (var i = 0; i < model.Data.InjectionCount; i++) model.EvaluateBootstrap(i, withOffset);
        Assert.Equal(0, Count() - before);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("solution")]
    [InlineData("revision")]
    [InlineData("method")]
    [InlineData("count")]
    [InlineData("injections")]
    [InlineData("explicit")]
    public void ChangedCacheIdentityRecomputesBothBands(string change)
    {
        var model = CreateModel("two-realistic", DilutionMethod.MicroCal);
        var replicas = model.Solution.BootstrapSolutions;
        model.EvaluateBootstrap(0);
        switch (change)
        {
            case "list":
                model.Solution.RestoreBootstrapSolutions(replicas.ToList());
                break;
            case "solution":
                model.Solution = SolutionInterface.FromModel(model, null);
                model.Solution.RestoreBootstrapSolutions(replicas);
                break;
            case "revision": model.Data.UpdateProcessing(invalidate: false); break;
            case "method": model.HeatMethod = InjectionHeatMethod.IdealContinuousMixing; break;
            case "count": replicas.RemoveAt(replicas.Count - 1); break;
            case "injections": model.Data.Injections.RemoveAt(model.Data.InjectionCount - 1); break;
            case "explicit": model.InvalidatePredictionCache(); break;
        }
        // Capture the retained replicas after a count change, before the independent reference evaluates them.
        var before = replicas.Sum(s => ((TwoSetsOfSites)s.Model).HeatContentSolveCount);
        var actual = model.EvaluateBootstrap(0, false);
        var after = replicas.Sum(s => ((TwoSetsOfSites)s.Model).HeatContentSolveCount);
        Assert.Equal((model.Data.InjectionCount + 1) * replicas.Count, after - before);
        AssertSameBits(DirectReference(model)[0].Corrected, actual);
    }

    [Fact]
    public void ReplicateScopesEvaluateEveryIndexInOrderAndLeavePrimaryUnscoped()
    {
        var primary = ProbeModel(4);
        var first = new CurveProbe(primary.Data, 6);
        var second = new CurveProbe(primary.Data, 8);
        primary.Solution.RestoreBootstrapSolutions(new List<SolutionInterface> { first.Solution, second.Solution });
        primary.EvaluateBootstrap(2, false);
        var expected = new[] { "begin" }.Concat(Enumerable.Range(0, primary.Data.InjectionCount)
            .Select(i => "scoped:" + i)).Append("end").ToArray();
        Assert.Equal(expected, first.Events);
        Assert.Equal(expected, second.Events);
        Assert.All(primary.Events, entry => Assert.StartsWith("direct:", entry));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedWholeCurveFallsBackPerInjectionWithoutRetryUntilInvalidation(bool throwOnBegin)
    {
        var primary = ProbeModel(4);
        var replicate = new CurveProbe(primary.Data, 6) { ThrowAt = 2, ThrowOnBegin = throwOnBegin };
        primary.Solution.RestoreBootstrapSolutions(new List<SolutionInterface> { replicate.Solution });
        var expected = new FloatWithError(new[] { replicate.EvaluateEnthalpy(0, true) }, primary.EvaluateEnthalpy(0, true));
        AssertSameBits(expected, primary.EvaluateBootstrap(0));
        Assert.False(replicate.Active);
        Assert.Equal(1, replicate.Events.Count(e => e == "begin"));
        Assert.Equal(1, replicate.Events.Count(e => e == "end"));
        primary.EvaluateBootstrap(1);
        primary.EvaluateBootstrap(0, false);
        Assert.Throws<InvalidOperationException>(() => primary.EvaluateBootstrap(2));
        Assert.Equal(1, replicate.Events.Count(e => e == "begin"));

        replicate.ThrowAt = null;
        replicate.ThrowOnBegin = false;
        primary.InvalidatePredictionCache();
        primary.EvaluateBootstrap(2);
        Assert.Equal(2, replicate.Events.Count(e => e == "begin"));
        Assert.Equal(2, replicate.Events.Count(e => e == "end"));
        Assert.False(replicate.Active);
    }

    [Fact]
    public void ZeroPrimaryPredictionWithUncertaintyIsCachedForBothVariants()
    {
        var primary = ProbeModel(0);
        var replicate = new CurveProbe(primary.Data, 1);
        var secondReplicate = new CurveProbe(primary.Data, 2);
        primary.Solution.RestoreBootstrapSolutions(new List<SolutionInterface> { replicate.Solution, secondReplicate.Solution });
        primary.EvaluateBootstrap(0);
        primary.Events.Clear();
        replicate.Events.Clear();
        secondReplicate.Events.Clear();
        foreach (var withOffset in new[] { false, true })
        {
            var result = primary.EvaluateBootstrap(0, withOffset);
            Assert.Equal(0, result.Value);
            Assert.Equal(1, result.Lower);
            Assert.Equal(2, result.Upper);
        }
        Assert.Empty(primary.Events);
        Assert.Empty(replicate.Events);
        Assert.Empty(secondReplicate.Events);
    }

    [Fact]
    public void NoBootstrapStillUsesRequestedDirectPredictionWithoutScope()
    {
        var primary = ProbeModel(4);
        foreach (var withOffset in new[] { false, true })
            AssertSameBits(new FloatWithError(primary.EvaluateEnthalpy(1, withOffset)),
                primary.EvaluateBootstrap(1, withOffset));
        Assert.All(primary.Events, entry => Assert.StartsWith("direct:", entry));
    }

    static CurveProbe ProbeModel(double value) => new(
        ObjectiveEvaluationTests.CreateModel("PytcBookkeeping", "one-exothermic").Data, value);

    sealed class CurveProbe : Model
    {
        readonly double value;
        public readonly List<string> Events = new();
        public bool Active, ThrowOnBegin;
        public int? ThrowAt;
        public CurveProbe(ExperimentData data, double value) : base(data)
        {
            this.value = value;
            Solution = new ProbeSolution(this);
        }
        protected override void BeginObjectiveEvaluation()
        {
            Events.Add("begin");
            Active = true;
            if (ThrowOnBegin) throw new InvalidOperationException("begin");
        }
        protected override void EndObjectiveEvaluation() { Events.Add("end"); Active = false; }
        public override double Evaluate(int injectionindex, bool withoffset = true)
        {
            Events.Add((Active ? "scoped:" : "direct:") + injectionindex);
            if (injectionindex == ThrowAt) throw new InvalidOperationException("injection");
            return value * Data.Injections[injectionindex].InjectionMass;
        }
    }

    sealed class ProbeSolution : SolutionInterface
    {
        public ProbeSolution(Model model) { Model = model; Parameters.Add(ParameterType.Offset, new FloatWithError(0)); }
    }
}
