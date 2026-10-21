using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(SequentialPersistenceTestCollectionDefinition.Name)]
public sealed class SummaryRepeatabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleMemberGlobalSummaryPreservesCompleteInterval(bool profileLikelihood)
    {
        var (_, result) = CreateSummary(1, profileLikelihood);
        var member = Assert.Single(result.Solution.Solutions);
        Assert.False(result.Model.TemperatureDependenceExposed);
        Assert.Empty(member.BootstrapSolutions);
        Assert.Equal(profileLikelihood ? ErrorEstimationMethod.ProfileLikelihood : ErrorEstimationMethod.None,
            member.ErrorMethod);
        Assert.True(member.Parameters[ParameterType.Enthalpy1].IsAsymmetric);
        AssertCompleteEqual(member.Parameters[ParameterType.Enthalpy1],
            result.Solution.TemperatureDependence[ParameterType.Enthalpy1].Intercept);
    }

    [Fact]
    public void MultiMemberGlobalSummaryRecomputationPreservesCenterAndCompleteInterval()
    {
        var (_, first) = CreateSummary(2);
        var expected = first.Solution.TemperatureDependence[ParameterType.Enthalpy1].Intercept;
        Assert.Equal(20, expected.Value); // Original-data centers are 10 and 30.
        Assert.True(expected.SD > 0);
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var (_, recomputed) = CreateSummary(2);
            AssertCompleteEqual(expected,
                recomputed.Solution.TemperatureDependence[ParameterType.Enthalpy1].Intercept);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FtxtcReloadReconstructsRepeatableSummariesWithoutBootstrapReplacement(int memberCount)
    {
        var (experiments, source) = CreateSummary(memberCount, profileLikelihood: memberCount == 1);
        var expected = source.Solution.TemperatureDependence[ParameterType.Enthalpy1].Intercept;
        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, experiments, new[] { source });
        var bytes = package.ToArray();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            using var input = new MemoryStream(bytes);
            var restored = Assert.Single((await FTXTCReader.ReadStream(input)).OfType<AnalysisResult>());
            Assert.Equal(memberCount, restored.Solution.Solutions.Count);
            Assert.False(restored.Model.TemperatureDependenceExposed);
            Assert.Empty(restored.Solution.BootstrapSolutions);
            Assert.All(restored.Solution.Solutions, member => Assert.Empty(member.BootstrapSolutions));
            Assert.Equal(memberCount == 1 ? 10 : 20,
                restored.Solution.TemperatureDependence[ParameterType.Enthalpy1].Intercept.Value);
            AssertCompleteEqual(expected,
                restored.Solution.TemperatureDependence[ParameterType.Enthalpy1].Intercept);
            if (memberCount == 1)
            {
                var member = Assert.Single(restored.Solution.Solutions);
                Assert.Equal(ErrorEstimationMethod.ProfileLikelihood, member.ErrorMethod);
                AssertCompleteEqual(member.Parameters[ParameterType.Enthalpy1],
                    restored.Solution.TemperatureDependence[ParameterType.Enthalpy1].Intercept);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconstructedSpolarRecordLinearIsoentropicInputsAreRepeatable(bool uncertain)
    {
        var (_, result) = SequentialPersistenceTests.CreateGlobalResult(2);
        var tsProperty = typeof(FTSRMethod).GetProperty("TS", BindingFlags.Instance | BindingFlags.NonPublic);
        FloatWithError? first = null;
        for (var iteration = 0; iteration < 4; iteration++)
        {
            result.Solution.TemperatureDependence[ParameterType.EntropyContribution1] =
                new LinearFitWithError(new FloatWithError(-2, uncertain ? 0.1 : 0),
                    new FloatWithError(40, uncertain ? 2 : 0), 25);
            var analysis = new FTSRMethod(result);
            var temperature = (FloatWithError)tsProperty.GetValue(analysis);
            Assert.Equal(45, temperature.Value); // 40 - 2 * (T - 25) = 0 at 45 °C.
            Assert.Equal(45, analysis.EvalutationTemperature(sample: false));
            if (uncertain)
                Assert.True(temperature.SD > 0);
            else
                AssertCompleteEqual(new FloatWithError(45), temperature);
            if (first.HasValue) AssertCompleteEqual(first.Value, temperature);
            first = temperature;
        }
    }

    static (List<ExperimentData> experiments, AnalysisResult result) CreateSummary(
        int memberCount, bool profileLikelihood = false) => SequentialPersistenceTests.CreateGlobalResult(
        2,
        members =>
        {
            if (memberCount == 1) members.RemoveAt(1);
            for (var index = 0; index < members.Count; index++)
            {
                members[index].ErrorMethod = profileLikelihood
                    ? ErrorEstimationMethod.ProfileLikelihood : ErrorEstimationMethod.None;
                members[index].Parameters[ParameterType.Enthalpy1] = index == 0
                    ? new FloatWithError(10, 1, 8, 18)
                    : new FloatWithError(30, 2, 25, 33);
            }
        },
        exposeTemperatureDependence: false);

    static void AssertCompleteEqual(FloatWithError expected, FloatWithError actual)
    {
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.SD, actual.SD);
        Assert.Equal(expected.Lower, actual.Lower);
        Assert.Equal(expected.Upper, actual.Upper);
        Assert.Equal(expected.IsAsymmetric, actual.IsAsymmetric);
    }
}
