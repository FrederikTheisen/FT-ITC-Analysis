using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;
using static AnalysisITC.Core.Tests.LinkedThermodynamicUncertaintyTests;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public class SpolarRecordLinkedWorkflowTests
{
    static FloatWithError Root(SummaryDependence curve) => (FloatWithError)typeof(FTSRMethod)
        .GetMethod("FindIsoentropicTemperature", BindingFlags.NonPublic | BindingFlags.Static)
        .Invoke(null, new object[] { curve });

    static SummaryDependence Curve(double entropy = 15000, double cp = 300, double tr = 300) => new()
    {
        ReferenceTemperature = tr - 273.15, Intercept = entropy, HeatCapacityTerm = cp,
        Slope = entropy / tr - cp,
    };

    [Fact]
    public void RootPropagatesSignedAsymmetricWidthsInLogSpace()
    {
        var curve = Curve();
        // G has positive influence; H and Cp have negative influence on log(Ts).
        curve.Contributions.Add(new(1, 0, 100, 200, 400));
        curve.Contributions.Add(new(-1, 0, 200, 300, 600));
        curve.Contributions.Add(new(0, 0, 10, 20, 30, 1));
        var result = Root(curve);
        var kelvin = 300 * Math.Exp(1.0 / 6);
        var gWeight = 1.0 / 90000;
        var cpWeight = -1.0 / 1800;
        var lower = Math.Sqrt(Math.Pow(gWeight * 200, 2) + Math.Pow(gWeight * 600, 2) + Math.Pow(cpWeight * 30, 2));
        var upper = Math.Sqrt(Math.Pow(gWeight * 400, 2) + Math.Pow(gWeight * 300, 2) + Math.Pow(cpWeight * 20, 2));
        Assert.Equal(kelvin - 273.15, result.Value, 10);
        Assert.Equal(kelvin * Math.Exp(-lower) - 273.15, result.Lower, 10);
        Assert.Equal(kelvin * Math.Exp(upper) - 273.15, result.Upper, 10);
        Assert.Equal(kelvin * Math.Sqrt(Math.Pow(gWeight * 100, 2) + Math.Pow(gWeight * 200, 2) + Math.Pow(cpWeight * 10, 2)), result.SD, 10);
    }

    [Theory]
    [InlineData(0, 0)] // Every temperature is a root.
    [InlineData(100, 0)] // No root.
    [InlineData(1e9, 1)] // Overflow.
    [InlineData(-1e9, 1)] // Underflow.
    public void AbsentNonuniqueAndNonfiniteRootsAreUnavailable(double entropy, double cp)
    {
        var result = Root(Curve(entropy, cp));
        Assert.True(double.IsNaN(result.Value));
        Assert.True(double.IsNaN(result.SD));
    }

    [Fact]
    public void MissingIntervalOrInvalidReplicateKeepsCenterButMakesErrorUnavailable()
    {
        var curve = Curve();
        var primary = Root(curve).Value;
        curve.Contributions.Add(new(1, 0, double.NaN, double.NaN, double.NaN));
        Assert.Equal(primary, Root(curve).Value);
        Assert.True(double.IsNaN(Root(curve).SD));
        curve.Contributions.Clear();
        curve.Replicates.Add(Curve());
        curve.Replicates.Add(Curve(cp: 0));
        Assert.Equal(primary, Root(curve).Value);
        Assert.True(double.IsNaN(Root(curve).SD));
    }

    [Fact]
    public void ReplicateRootsUseTheirOwnReferenceAndPrimaryCenteredDistribution()
    {
        var curve = Curve();
        curve.Replicates.Add(Curve(3000 * Math.Log(2), 10, 300));
        curve.Replicates.Add(Curve(3200 * Math.Log(3), 10, 320));
        var result = Root(curve);
        var primary = 300 * Math.Exp(1.0 / 6) - 273.15;
        Assert.Equal(primary, result.Value, 10);
        Assert.Equal(Math.Sqrt((Math.Pow(600 - 273.15 - primary, 2) + Math.Pow(960 - 273.15 - primary, 2)) / 2), result.SD, 10);
        Assert.Equal(600 - 273.15, result.Lower, 10);
        Assert.Equal(960 - 273.15, result.Upper, 10);
    }

    [Fact]
    public async Task RefreshUsesCurrentProfilesAndKeepsBestFitRootThroughNativeRoundTrip()
    {
        var result = new AnalysisResult(Create(VariableConstraint.TemperatureDependent));
        var analysis = result.SpolarRecordAnalysis;
        var center = analysis.EvalutationTemperature(false);
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.Equal(0, analysis.Result.ReferenceTemperature.SD);
        result.Solution.ProfileLikelihoodRun = Run(Coordinate(ParameterType.Gibbs1, -25000, 500, 800),
            Coordinate(ParameterType.Enthalpy1, -40000, 1000, 1500),
            Coordinate(ParameterType.HeatCapacity1, 300, 10, 15));
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.Equal(center, analysis.Result.ReferenceTemperature.Value);
        Assert.True(analysis.Result.ReferenceTemperature.SD > 0);
        Assert.True(Enumerable.Range(0, 12).Select(_ => analysis.EvalutationTemperature()).Distinct().Count() > 1);

        var saved = analysis.Result;
        using var stream = new MemoryStream();
        await FTXTCWriter.WriteStream(stream, result.Model.Models.Select(model => model.Data), new[] { result });
        stream.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>()).SpolarRecordAnalysis;
        Assert.Equal(saved.ReferenceTemperature.Value, restored.Result.ReferenceTemperature.Value);
        Assert.Equal(saved.ReferenceTemperature.Lower, restored.Result.ReferenceTemperature.Lower);
        Assert.Equal(saved.ConformationalEntropy.Value, restored.Result.ConformationalEntropy.Value);
        Assert.Equal(saved.ConformationalEntropy.SD, restored.Result.ConformationalEntropy.SD);
        Assert.Equal(analysis.CompletedAtUtc, restored.CompletedAtUtc);
    }

    [Fact]
    public async Task FailedAndCancelledLinkedRerunsRetainSuccessfulResult()
    {
        var result = new AnalysisResult(Create(VariableConstraint.TemperatureDependent));
        var analysis = result.SpolarRecordAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        var saved = analysis.Result;
        var completed = analysis.CompletedAtUtc;
        // Cp and H are required but have no profile interval.
        result.Solution.ProfileLikelihoodRun = Run(Coordinate(ParameterType.Gibbs1, -25000, 500, 800));
        Assert.False(await analysis.PerformAnalysisAsync());
        Assert.Same(saved, analysis.Result);
        Assert.Equal(completed, analysis.CompletedAtUtc);
        result.Solution.ProfileLikelihoodRun = null;
        EventHandler<Tuple<int, int, float, string>> cancel = (_, _) => ResultAnalysisController.TerminateAnalysisFlag.Raise();
        ResultAnalysisController.IterationFinished += cancel;
        try { Assert.False(await analysis.PerformAnalysisAsync()); }
        finally
        {
            ResultAnalysisController.IterationFinished -= cancel;
            ResultAnalysisController.TerminateAnalysisFlag.Lower();
        }
        Assert.Same(saved, analysis.Result);
        Assert.Equal(completed, analysis.CompletedAtUtc);
    }

    [Fact]
    public async Task FixedTemperatureModesWorkWithoutUniqueRoot()
    {
        var analysis = new AnalysisResult(Create(VariableConstraint.SameForAll)).SpolarRecordAnalysis;
        Assert.True(double.IsNaN(analysis.EvalutationTemperature(false)));
        Assert.False(await analysis.PerformAnalysisAsync());
        foreach (var mode in new[] { FTSRMethod.SRTempMode.MeanTemperature, FTSRMethod.SRTempMode.ReferenceTemperature })
        {
            analysis.TempMode = mode;
            Assert.True(await analysis.PerformAnalysisAsync());
            Assert.Equal(0, analysis.Result.HydrationEntropy.Value);
        }
    }

    [Fact]
    public void IndependentEnthalpyRegressionDeterminesRootWithoutChangingMemberHeat()
    {
        var solution = Create(VariableConstraint.None);
        var analysis = new AnalysisResult(solution).SpolarRecordAnalysis;
        Assert.Equal(300 * Math.Exp(3000.0 / (300 * 650)) - 273.15, analysis.EvalutationTemperature(false), 10);
        Assert.Equal(-30000, solution.Solutions[1].Parameters[ParameterType.Enthalpy1].Value);
    }

    [Fact]
    public void ReducedReplicateMembershipUsesItsOwnEnthalpyRegression()
    {
        var solution = Create(VariableConstraint.None);
        solution.RestoreGlobalBootstrapSolutions(new List<GlobalSolution>
        {
            Create(VariableConstraint.None, omitted: 0),
            Create(VariableConstraint.None, omitted: 1),
        });
        var analysis = new AnalysisResult(solution).SpolarRecordAnalysis;
        var curve = new AnalysisResultAggregateSummaryCalculator(new AnalysisResult(solution))
            .BuildDependence(ParameterType.EntropyContribution1);
        var value = Root(curve);
        var primary = 300 * Math.Exp(3000.0 / (300 * 650)) - 273.15;
        // Omitting 280 K gives Hr=-30000,Cp=800; omitting 300 K gives Hr=-27000,Cp=650.
        var first = 300 * Math.Exp(5000.0 / (300 * 800)) - 273.15;
        var second = 300 * Math.Exp(2000.0 / (300 * 650)) - 273.15;
        Assert.Equal(primary, analysis.EvalutationTemperature(false), 10);
        Assert.Equal(Math.Sqrt((Math.Pow(first - primary, 2) + Math.Pow(second - primary, 2)) / 2), value.SD, 10);
    }

    [Fact]
    public async Task LockedCoordinatesDoNotNeedProfileIntervals()
    {
        var solution = Create(VariableConstraint.TemperatureDependent);
        solution.Model.Parameters.GlobalTable[ParameterType.Enthalpy1].Update(-40000, true);
        solution.Model.Parameters.GlobalTable[ParameterType.HeatCapacity1].Update(300, true);
        solution.ProfileLikelihoodRun = Run(Coordinate(ParameterType.Gibbs1, -25000, 500, 800));
        var analysis = new AnalysisResult(solution).SpolarRecordAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.True(analysis.Result.ReferenceTemperature.SD > 0);
        Assert.Equal(300 * Math.Exp(1.0 / 6) - 273.15, analysis.Result.ReferenceTemperature.Value, 10);
    }
}
