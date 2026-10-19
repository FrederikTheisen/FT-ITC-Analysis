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

[Collection("Solver events")]
public sealed partial class BootstrapPredictionCacheTests : IDisposable
{
    readonly ObjectiveEvaluationTests environment = new();

    public void Dispose() => environment.Dispose();

    public static IEnumerable<object[]> ReferenceCases()
    {
        foreach (var id in new[] { "two-realistic", "sequential-2", "sequential-3", "sequential-4" })
        foreach (var method in new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement })
        foreach (var variableVolumes in new[] { false, true })
        foreach (var correctedFirst in new[] { false, true })
            yield return new object[] { id, method, variableVolumes, correctedFirst };
    }

    [Theory]
    [MemberData(nameof(ReferenceCases))]
    public void BootstrapBandsExactlyMatchDirectUnscopedReference(
        string id, DilutionMethod method, bool variableVolumes, bool correctedFirst)
    {
        var model = CreateModel(id, method, variableVolumes);
        var expected = DirectReference(model);
        foreach (var withOffset in new[] { !correctedFirst, correctedFirst })
        foreach (var i in Enumerable.Range(0, model.Data.InjectionCount).Reverse())
            AssertSameBits(withOffset ? expected[i].WithOffset : expected[i].Corrected,
                model.EvaluateBootstrap(i, withOffset));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnusableReplicatesRetainPrimaryFallback(bool withOffset)
    {
        var model = CreateModel("two-realistic", DilutionMethod.MicroCal);
        model.Solution.RestoreBootstrapSolutions(new List<SolutionInterface> { null, new SolutionInterface() });
        var expected = DirectReference(model);
        for (var i = 0; i < model.Data.InjectionCount; i++)
            AssertSameBits(withOffset ? expected[i].WithOffset : expected[i].Corrected,
                model.EvaluateBootstrap(i, withOffset));
    }

    static Model CreateModel(string id, DilutionMethod method, bool variableVolumes = false)
    {
        Model Create(int replicate)
        {
            var model = ObjectiveEvaluationTests.CreateModel("PytcBookkeeping", id);
            if (variableVolumes)
            {
                var volumes = new[] { .8e-6, 1.8e-6, 2.4e-6, 3.0e-6 };
                for (var i = 0; i < model.Data.InjectionCount; i++)
                {
                    var previous = model.Data.Injections[i];
                    var injection = new InjectionData(model.Data, i, volumes[i % volumes.Length], 0, true);
                    injection.SetPeakArea(previous.PeakArea);
                    model.Data.Injections[i] = injection;
                }
            }
            model.Data.Injections[0].Include = false;
            model.Data.Injections[2].Include = false;
            if (replicate != 0)
            {
                model.Data.CellConcentration *= 1 + .01 * replicate;
                model.Data.SyringeConcentration *= 1 - .003 * replicate;
                model.Parameters.Table[ParameterType.Affinity1].Update(
                    model.Parameters.Table[ParameterType.Affinity1].Value + .013 * replicate);
                model.Parameters.Table[ParameterType.Enthalpy1].Update(
                    model.Parameters.Table[ParameterType.Enthalpy1].Value * (1 + .017 * replicate));
            }
            model.Parameters.Table[ParameterType.Offset].Update(127.25 + 11.125 * replicate);
            ObjectiveEvaluationTests.SetMethod(model, method);
            model.ApplyModelOptions();
            model.Solution = SolutionInterface.FromModel(model, null);
            return model;
        }

        var primary = Create(0);
        primary.Solution.RestoreBootstrapSolutions(new[] { -3, -1, 1, 2, 7 }
            .Select(i => Create(i).Solution).ToList());
        return primary;
    }

    // Independent of EvaluateBootstrap, its cache, and its evaluation scopes.
    static (FloatWithError WithOffset, FloatWithError Corrected)[] DirectReference(Model model)
    {
        return Enumerable.Range(0, model.Data.InjectionCount).Select(i =>
        {
            var values = model.Solution.BootstrapSolutions.Where(s => s?.Model != null)
                .Select(s => s.Model.EvaluateEnthalpy(i, true)).ToList();
            var primary = model.EvaluateEnthalpy(i, true);
            if (values.Count == 0) values.Add(primary);
            return (new FloatWithError(values, primary),
                new FloatWithError(values.Select(v => v - model.Solution.Offset), primary - model.Solution.Offset));
        }).ToArray();
    }

    static void AssertSameBits(FloatWithError expected, FloatWithError actual)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Value), BitConverter.DoubleToInt64Bits(actual.Value));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.SD), BitConverter.DoubleToInt64Bits(actual.SD));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Lower), BitConverter.DoubleToInt64Bits(actual.Lower));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Upper), BitConverter.DoubleToInt64Bits(actual.Upper));
        Assert.Equal(expected.HasConfidenceInterval, actual.HasConfidenceInterval);
        Assert.Equal(expected.IsAsymmetric, actual.IsAsymmetric);
    }
}
