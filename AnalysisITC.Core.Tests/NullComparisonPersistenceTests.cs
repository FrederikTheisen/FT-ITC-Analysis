using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class NullComparisonPersistenceTests
{
    [Fact]
    public async Task SuccessfulComparisonRoundTripsAsCapturedEvidenceAfterSourceDataChanges()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        var expected = result.NullComparison;
        var expectedObservedHeats = expected.Members.Single().Points.Select(point => point.ObservedHeatJoules).ToArray();

        // The saved comparison describes the data used when it was calculated.
        // A later edit must not cause loading the project to solve again.
        model.Data.Injections[0].SetPeakArea(new FloatWithError(model.Data.Injections[0].PeakArea.Value + 5e-6,
            model.Data.Injections[0].PeakArea.SD));
        using var package = await Write(model.Data, result);
        package.Position = 0;

        var viewer = await new ViewerDocumentReader().ReadAsync(package, "null-comparison.ftxtc", ViewerFileFormat.Ftxtc);
        var viewerFits = Assert.Single(viewer.Experiments).Fits
            .Where(fit => fit.ModelName == model.ModelType.GetProperties().Name)
            .ToList();
        Assert.NotEmpty(viewerFits);
        var viewerFit = viewerFits.First();
        Assert.Equal(model.Data.Injections.Count, viewerFit.FittedKilojoulesPerMole.Length);

        package.Position = 0;

        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
        var actual = Assert.IsType<NullModelComparison>(restored.NullComparison);
        Assert.True(actual.NullFitSucceeded, actual.NullFitReason);
        Assert.Equal(result.BindingAssessment.AutomaticOutcome, restored.BindingAssessment.AutomaticOutcome);
        Assert.Equal(result.BindingAssessment.AutomaticRuleId, restored.BindingAssessment.AutomaticRuleId);
        Assert.Null(restored.BindingAssessment.ManualOverride);
        Assert.Equal(expected.NullModelId, actual.NullModelId);
        Assert.Equal(expected.BindingFitSucceeded, actual.BindingFitSucceeded);
        Assert.Equal(expected.BindingInformationCriteria?.Aicc, actual.BindingInformationCriteria?.Aicc);
        Assert.Equal(expected.NullInformationCriteria?.Aicc, actual.NullInformationCriteria?.Aicc);
        Assert.Equal(expected.DeltaAicc, actual.DeltaAicc);

        var expectedMember = Assert.Single(expected.Members);
        var actualMember = Assert.Single(actual.Members);
        Assert.Equal(expectedMember.ExperimentId, actualMember.ExperimentId);
        Assert.Equal(expectedMember.Offset, actualMember.Offset, 12);
        Assert.Equal(expectedMember.Scope, actualMember.Scope);
        Assert.Equal("local", actualMember.Scope);
        Assert.Equal(expectedMember.Convergence.Algorithm, actualMember.Convergence.Algorithm);
        Assert.Equal(expectedObservedHeats, actualMember.Points.Select(point => point.ObservedHeatJoules));
        Assert.Equal(expectedMember.Points.Select(point => point.PredictedHeatJoules),
            actualMember.Points.Select(point => point.PredictedHeatJoules));
        Assert.Equal(expectedMember.Points.Select(point => point.Ratio), actualMember.Points.Select(point => point.Ratio));
        Assert.NotEqual(actualMember.Points[0].ObservedHeatJoules, restored.Solution.Solutions[0].Data.Injections[0].PeakArea.Value);
        var detachedNullSolution = Assert.Single(actual.NullSolutions);
        Assert.Equal(AnalysisModel.Offset, detachedNullSolution.Model.ModelType);
        Assert.NotSame(restored.Solution.Solutions[0].Model.Data, detachedNullSolution.Model.Data);
        foreach (var point in actualMember.Points)
            Assert.Equal(point.PredictedHeatJoules, detachedNullSolution.Model.Evaluate(point.InjectionId), 12);
    }

    [Theory]
    [InlineData(BindingAssessmentOutcome.BindingDetected)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected)]
    public async Task ManualAssessmentRoundTripsWithoutChangingSavedComparison(BindingAssessmentOutcome outcome)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        var comparison = result.NullComparison;
        var date = result.Date;
        result.SetBindingAssessmentOverride(outcome);
        using var package = await Write(model.Data, result);
        package.Position = 0;

        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
        Assert.Equal(outcome, restored.BindingAssessment.ManualOverride);
        Assert.Equal(outcome, restored.BindingAssessment.EffectiveOutcome);
        Assert.True(restored.BindingAssessment.IsManual);
        Assert.Equal(date, restored.Date);
        Assert.Equal(comparison.DeltaAicc, restored.NullComparison.DeltaAicc);
        Assert.False(restored.IsModified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedGlobalComparisonRoundTripsPooledCriteriaAndPerMemberPredictions(bool locked)
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var members = new[] { first, second };
        var globalModel = new GlobalModel(members.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var member in members) globalModel.Parameters.AddIndivdualParameter(member.Parameters);
        globalModel.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
        globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0, locked);
        globalModel.Parameters.SetIndividualFromGlobal();

        var convergence = SolverConvergence.FromFixedFit(0, 0);
        var memberSolutions = members.Select(member => SolutionInterface.FromModel(member, convergence)).ToList();
        for (var index = 0; index < members.Length; index++) members[index].Solution = memberSolutions[index];
        var globalSolution = new GlobalSolution(new GlobalSolver { Model = globalModel }, memberSolutions,
            convergence, reconstructBootstrap: false);
        globalModel.Solution = globalSolution;
        NullModelComparisonCalculator.Calculate(globalSolution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = new AnalysisResult(globalSolution);
        var expected = Assert.IsType<NullModelComparison>(result.NullComparison);
        Assert.True(expected.NullFitSucceeded, expected.NullFitReason);

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, members.Select(member => member.Data), new[] { result });
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
        var actual = Assert.IsType<NullModelComparison>(restored.NullComparison);

        Assert.True(actual.NullFitSucceeded, actual.NullFitReason);
        Assert.Equal(2, actual.Members.Count);
        Assert.All(actual.Members, member => Assert.Equal("local", member.Scope));
        Assert.Equal(2, actual.NullInformationCriteria.FittedParameterCount);
        Assert.Equal(expected.NullInformationCriteria.ObservationCount, actual.NullInformationCriteria.ObservationCount);
        Assert.Equal(expected.NullInformationCriteria.FittedParameterCount, actual.NullInformationCriteria.FittedParameterCount);
        Assert.Equal(expected.NullInformationCriteria.Aicc, actual.NullInformationCriteria.Aicc);
        Assert.Equal(expected.DeltaAicc, actual.DeltaAicc);
        Assert.Equal(members.Select(member => member.Data.UniqueID).OrderBy(id => id),
            actual.Members.Select(member => member.ExperimentId).OrderBy(id => id));

        Assert.Equal(2, actual.NullSolutions.Count);
        foreach (var comparisonMember in actual.Members)
        {
            var nullSolution = Assert.Single(actual.NullSolutions,
                solution => solution.Model.Data.UniqueID == comparisonMember.ExperimentId);
            Assert.Equal(AnalysisModel.Offset, nullSolution.Model.ModelType);
            Assert.False(nullSolution.Model.Parameters.Table[ParameterType.Offset].IsLocked);
            var restoredBinding = Assert.Single(restored.Solution.Solutions,
                solution => solution.Data.UniqueID == comparisonMember.ExperimentId);
            Assert.NotSame(restoredBinding.Model.Data, nullSolution.Model.Data);
            foreach (var point in comparisonMember.Points)
                Assert.Equal(point.PredictedHeatJoules, nullSolution.Model.Evaluate(point.InjectionId), 12);
        }
    }

    [Fact]
    public async Task FailedNullComparisonSnapshotRoundTripsAlongsidePrimaryFit()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var failed = Assert.IsType<NullModelComparison>(model.Solution.NullComparison);
        failed.NullFitSucceeded = false;
        failed.NullFitReason = "The automatic null fit did not converge.";
        failed.NullInformationCriteria = null;
        failed.DeltaAicc = null;
        failed.Members.Clear();
        var result = CreateResult(model);

        using var package = await Write(model.Data, result);
        package.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());

        Assert.True(restored.Solution.IsValid);
        Assert.Equal(model.Solution.ReportParameters[ParameterType.Enthalpy1].Value,
            restored.Solution.Solutions[0].ReportParameters[ParameterType.Enthalpy1].Value);
        Assert.False(restored.NullComparison.NullFitSucceeded);
        Assert.Equal("The automatic null fit did not converge.", restored.NullComparison.NullFitReason);
        Assert.Null(restored.NullComparison.NullInformationCriteria);
        Assert.Null(restored.NullComparison.DeltaAicc);
    }

    [Fact]
    public async Task HistoricalResultWithoutNullComparisonRemainsReadable()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        using var package = await Write(model.Data, result);
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node.Remove("nullComparison");
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        historical.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(historical)).OfType<AnalysisResult>());
        Assert.True(restored.Solution.IsValid);
        Assert.Null(restored.NullComparison);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, restored.BindingAssessment.AutomaticOutcome);
        Assert.False(restored.IsModified);
    }

    [Fact]
    public async Task LegacyComparisonWithoutAssessmentInitializesFromSavedEvidenceAndStaysClean()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        var expected = result.BindingAssessment.AutomaticOutcome;
        using var package = await Write(model.Data, result);
        using var historical = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node.Remove("bindingAssessment");
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        historical.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(historical)).OfType<AnalysisResult>());
        Assert.NotNull(restored.NullComparison);
        Assert.Equal(expected, restored.BindingAssessment.AutomaticOutcome);
        Assert.False(restored.IsModified);
    }

    [Fact]
    public async Task SavedAutomaticAssessmentAndRuleAreRestoredWithoutRecalculation()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        using var package = await Write(model.Data, result);
        using var snapshot = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node["bindingAssessment"]["automaticOutcome"] = "inconclusive";
            node["bindingAssessment"]["ruleId"] = "future-rule-9";
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        snapshot.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadStream(snapshot)).OfType<AnalysisResult>());
        Assert.Equal(BindingAssessmentOutcome.Inconclusive, restored.BindingAssessment.AutomaticOutcome);
        Assert.Equal("future-rule-9", restored.BindingAssessment.AutomaticRuleId);
        Assert.Null(restored.BindingAssessment.ManualOverride);
        Assert.False(restored.IsModified);
    }

    [Fact]
    public async Task MalformedAssessmentIsDiscardedInRecoveryAndRejectedInStrictMode()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        var expected = result.BindingAssessment.AutomaticOutcome;
        using var package = await Write(model.Data, result);
        using var malformed = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node["bindingAssessment"]["schemaVersion"] = 999;
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        malformed.Position = 0;
        var recovery = await FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.RecoverUsableContent);
        var restored = Assert.Single(recovery.Containers.OfType<AnalysisResult>());
        Assert.Equal(expected, restored.BindingAssessment.AutomaticOutcome);
        Assert.Null(restored.BindingAssessment.ManualOverride);
        Assert.Contains(recovery.Issues, issue => issue.Code == "binding-assessment-skipped");
        malformed.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.Strict));
    }

    [Fact]
    public async Task MalformedOptionalComparisonIsDroppedDuringRecoveryButRejectedInStrictMode()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        NullModelComparisonCalculator.Calculate(model.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        var result = CreateResult(model);
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        using var package = await Write(model.Data, result);
        using var malformed = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            node["nullComparison"] = new JsonObject { ["schemaVersion"] = 999 };
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        malformed.Position = 0;
        var recovery = await FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.RecoverUsableContent);
        var recovered = Assert.Single(recovery.Containers.OfType<AnalysisResult>());
        Assert.True(recovered.Solution.IsValid);
        Assert.Null(recovered.NullComparison);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, recovered.BindingAssessment.AutomaticOutcome);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, recovered.BindingAssessment.ManualOverride);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, recovered.BindingAssessment.EffectiveOutcome);
        Assert.Contains(recovery.Issues, issue => issue.Code == "null-comparison-skipped");

        malformed.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.Strict));
    }

    static AnalysisResult CreateResult(AnalysisITC.Core.Analysis.Models.Model model)
    {
        var solver = new Solver { Model = model };
        var global = GlobalSolution.FromSingleExperimentSolver(solver);
        return new AnalysisResult(global);
    }

    static async Task<MemoryStream> Write(ExperimentData data, AnalysisResult result)
    {
        var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, new[] { data }, new[] { result });
        return package;
    }
}
