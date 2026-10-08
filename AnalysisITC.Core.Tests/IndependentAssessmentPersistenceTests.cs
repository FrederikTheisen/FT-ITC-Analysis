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
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class IndependentAssessmentPersistenceTests
{
    [Fact]
    public async Task IndependentMemberAssessmentsAndSavedCriteriaRoundTripCleanly()
    {
        var result = CreateResult(out var members);
        SetEvidence(result, members[0], BindingAssessmentOutcome.BindingDetected, 12.5);
        SetEvidence(result, members[1], BindingAssessmentOutcome.NotAssessed, null, "Comparison unavailable.");
        result.SetMemberBindingAssessmentOverride(members[1].Guid, BindingAssessmentOutcome.NoBindingDetected);
        var expected = result.MemberAssessments.ToDictionary(member => member.SolutionId);
        members[0].Data.Injections[0].SetPeakArea(new FloatWithError(
            members[0].Data.Injections[0].PeakArea.Value + 1e-5,
            members[0].Data.Injections[0].PeakArea.SD));

        using var package = await Write(result, members);
        package.Position = 0;
        var restored = Assert.IsType<AnalysisResult>(Assert.Single((await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict)).Containers
            .OfType<AnalysisResult>()));

        Assert.True(restored.IsIndependentAssessmentCollection);
        Assert.Equal(BindingAssessmentSummaryOutcome.Mixed, restored.CollectionAssessmentOutcome);
        Assert.Null(restored.BindingAssessment);
        Assert.False(restored.IsModified);
        foreach (var member in restored.MemberAssessments)
        {
            var before = expected[member.SolutionId];
            Assert.Equal(before.Assessment.AutomaticOutcome, member.Assessment.AutomaticOutcome);
            Assert.Equal(before.Assessment.ManualOverride, member.Assessment.ManualOverride);
            Assert.Equal(before.Assessment.EffectiveOutcome, member.Assessment.EffectiveOutcome);
            Assert.Equal(before.Comparison?.DeltaAicc, member.Comparison?.DeltaAicc);
            Assert.Equal(before.Comparison?.NullInformationCriteria?.Aicc, member.Comparison?.NullInformationCriteria?.Aicc);
            Assert.Equal(before.Comparison?.ComparisonUnavailableReason, member.Comparison?.ComparisonUnavailableReason);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictReadOfAbsentOrValidMemberMetadataStaysClean(bool removeMetadata)
    {
        var result = CreateResult(out var members);
        SetEvidence(result, members[0], BindingAssessmentOutcome.BindingDetected, 12.25);
        SetEvidence(result, members[1], BindingAssessmentOutcome.NotAssessed, null, "No comparison.");
        using var package = await Write(result, members);
        using var edited = removeMetadata
            ? FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
            {
                if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
                var node = JsonNode.Parse(bytes).AsObject();
                node.Remove("memberAssessments");
                return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
            }, schemaMinor: FTXTCFormat.SchemaMinor)
            : package;

        edited.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(edited, FtxtcReadPolicy.Strict)).Containers.OfType<AnalysisResult>());
        Assert.False(restored.IsModified);
        Assert.Collection(restored.MemberAssessments,
            first =>
            {
                Assert.Equal(removeMetadata ? BindingAssessmentOutcome.NotAssessed : BindingAssessmentOutcome.BindingDetected,
                    first.Assessment.EffectiveOutcome);
                if (removeMetadata) Assert.Null(first.Comparison);
            },
            second =>
            {
                Assert.Equal(BindingAssessmentOutcome.NotAssessed, second.Assessment.EffectiveOutcome);
                if (removeMetadata) Assert.Null(second.Comparison);
            });
        Assert.Equal(removeMetadata ? BindingAssessmentSummaryOutcome.NotAssessed
            : BindingAssessmentSummaryOutcome.Mixed, restored.CollectionAssessmentOutcome);
    }

    [Fact]
    public async Task RecoveryDropsMalformedUnknownAndDuplicatedMemberRecordsButKeepsValidSibling()
    {
        var result = CreateResult(out var members, 3);
        NullModelComparisonCalculator.Calculate(result.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        result.RestoreNullComparison(result.Solution.NullComparison);
        SetEvidence(result, members[0], BindingAssessmentOutcome.BindingDetected, 15.0);
        SetEvidence(result, members[1], BindingAssessmentOutcome.NoBindingDetected, -2.0);
        SetEvidence(result, members[2], BindingAssessmentOutcome.BindingDetected, 15.0);
        var healthyId = members[2].Guid;
        var expectedHealthySnapshot = result.PooledNullComparison.Members.Single(item => item.ExperimentId == members[2].Data.UniqueID);
        using var package = await Write(result, members);
        using var malformed = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            var list = (JsonArray)node["memberAssessments"]["members"];
            var first = (JsonObject)list[0];
            var second = (JsonObject)list[1];
            // The malformed first record and duplicated second ID are discarded;
            // the third member remains a healthy sibling.
            first["automaticOutcome"] = "future-outcome";
            var duplicate = second.DeepClone();
            list.Add(duplicate);
            var unknown = second.DeepClone().AsObject();
            unknown["solutionId"] = "unknown-solution-id";
            list.Add(unknown);
            ((JsonArray)node["nullComparison"]["members"]).RemoveAt(0);
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        malformed.Position = 0;
        var recovery = await FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.RecoverUsableContent);
        var restored = Assert.Single(recovery.Containers.OfType<AnalysisResult>());
        Assert.True(recovery.IsPartial);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed,
            restored.GetMemberBindingAssessment(restored.Solution.Solutions[0]).EffectiveOutcome);
        var duplicated = restored.GetMemberNullComparison(restored.Solution.Solutions[1]);
        Assert.True(duplicated == null || !duplicated.DeltaAicc.HasValue);
        var healthy = restored.MemberAssessments.Single(member => member.SolutionId == healthyId);
        Assert.Equal(BindingAssessmentOutcome.BindingDetected, healthy.Assessment.EffectiveOutcome);
        Assert.NotNull(healthy.Comparison.DeltaAicc);
        var recoveredSnapshot = Assert.Single(healthy.Comparison.Members);
        Assert.Equal(expectedHealthySnapshot.ExperimentId, recoveredSnapshot.ExperimentId);
        Assert.Equal(expectedHealthySnapshot.Points.Select(point => point.PredictedHeatJoules),
            recoveredSnapshot.Points.Select(point => point.PredictedHeatJoules));
        Assert.False(restored.PooledNullComparison.NullFitSucceeded);
        Assert.Null(restored.PooledNullComparison.DeltaAicc);
        Assert.Contains(recovery.Issues, issue => issue.Code.Contains("member", StringComparison.OrdinalIgnoreCase));

        malformed.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadWithRecovery(malformed, FtxtcReadPolicy.Strict));
    }

    [Fact]
    public void MemberOverridesAndComparisonsAreOwnedByTheirResultSnapshot()
    {
        var result = CreateResult(out var members);
        var sibling = new AnalysisResult(result.Solution);
        SetEvidence(result, members[0], BindingAssessmentOutcome.BindingDetected, 6.75);
        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.NoBindingDetected);

        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, result.GetMemberBindingAssessment(members[0]).EffectiveOutcome);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, sibling.GetMemberBindingAssessment(members[0]).EffectiveOutcome);
        Assert.Null(sibling.GetMemberNullComparison(members[0]));
        Assert.Equal(6.75, result.GetMemberNullComparison(members[0]).DeltaAicc);
        result.UseAutomaticBindingAssessments();
        Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.GetMemberBindingAssessment(members[0]).EffectiveOutcome);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, sibling.GetMemberBindingAssessment(members[0]).EffectiveOutcome);
    }

    [Fact]
    public async Task ManualMemberOverrideRemainsUsableWithoutComparisonEvidence()
    {
        var result = CreateResult(out var members);
        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
        using var package = await Write(result, members);
        package.Position = 0;

        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict)).Containers.OfType<AnalysisResult>());
        var assessment = restored.GetMemberBindingAssessment(restored.Solution.Solutions[0]);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, assessment.AutomaticOutcome);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, assessment.ManualOverride);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, assessment.EffectiveOutcome);
        Assert.Null(restored.GetMemberNullComparison(restored.Solution.Solutions[0]).DeltaAicc);
    }

    [Fact]
    public async Task RecoveryDowngradesAutomaticAssessmentWhenEvidenceIsRemovedButKeepsManualOverride()
    {
        var result = CreateResult(out var members);
        SetEvidence(result, members[0], BindingAssessmentOutcome.BindingDetected, 12.0);
        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
        using var package = await Write(result, members);
        using var noEvidence = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            var record = (JsonObject)node["memberAssessments"]["members"][0];
            record["bindingFitSucceeded"] = false;
            record["nullFitSucceeded"] = false;
            record["bindingInformationCriteria"] = null;
            record["nullInformationCriteria"] = null;
            record["deltaAicc"] = null;
            record["comparisonUnavailableReason"] = "Evidence was unavailable when restored.";
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);

        noEvidence.Position = 0;
        var recovery = await FTXTCReader.ReadWithRecovery(noEvidence, FtxtcReadPolicy.RecoverUsableContent);
        var restored = Assert.Single(recovery.Containers.OfType<AnalysisResult>());
        var assessment = restored.GetMemberBindingAssessment(restored.Solution.Solutions[0]);
        Assert.Equal(BindingAssessmentOutcome.NotAssessed, assessment.AutomaticOutcome);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, assessment.ManualOverride);
        Assert.Equal(BindingAssessmentOutcome.NoBindingDetected, assessment.EffectiveOutcome);
    }

    [Theory]
    [InlineData(FtxtcReadPolicy.Strict)]
    [InlineData(FtxtcReadPolicy.RecoverUsableContent)]
    public async Task PooledComparisonSnapshotsRoundTripAndMissingSnapshotMakesPooledEvidenceUnavailable(FtxtcReadPolicy readPolicy)
    {
        var result = CreateResult(out var members);
        NullModelComparisonCalculator.Calculate(result.Solution, weighted: false,
            SolverAlgorithm.LevenbergMarquardt, maxIterations: 3000, toleranceModifier: 1);
        result.RestoreNullComparison(result.Solution.NullComparison);
        var expected = Assert.IsType<NullModelComparison>(result.PooledNullComparison);
        Assert.True(expected.NullFitSucceeded, expected.NullFitReason);
        using var package = await Write(result, members);

        package.Position = 0;
        var valid = Assert.Single((await FTXTCReader.ReadWithRecovery(package, readPolicy)).Containers.OfType<AnalysisResult>());
        Assert.True(valid.PooledNullComparison.NullFitSucceeded, valid.PooledNullComparison.NullFitReason);
        Assert.Equal(expected.DeltaAicc, valid.PooledNullComparison.DeltaAicc);
        Assert.Equal(expected.NullSolutions.Count, valid.PooledNullComparison.NullSolutions.Count);
        foreach (var snapshot in valid.PooledNullComparison.Members)
        {
            var before = Assert.Single(expected.Members, member => member.ExperimentId == snapshot.ExperimentId);
            Assert.Equal(before.Points.Select(point => point.PredictedHeatJoules),
                snapshot.Points.Select(point => point.PredictedHeatJoules));
        }

        using var missingSnapshot = FTXTCFormatTests.RewriteAuthenticatedPackage(package, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var node = JsonNode.Parse(bytes).AsObject();
            var snapshots = (JsonArray)node["nullComparison"]["members"];
            snapshots.RemoveAt(0);
            return Encoding.UTF8.GetBytes(node.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: FTXTCFormat.SchemaMinor);
        missingSnapshot.Position = 0;
        var recovered = Assert.Single((await FTXTCReader.ReadWithRecovery(missingSnapshot,
            FtxtcReadPolicy.RecoverUsableContent)).Containers.OfType<AnalysisResult>());
        Assert.False(recovered.PooledNullComparison.NullFitSucceeded);
        Assert.Null(recovered.PooledNullComparison.DeltaAicc);
        Assert.Single(recovered.PooledNullComparison.Members);
        Assert.Single(recovered.PooledNullComparison.NullSolutions);
    }

    static void SetEvidence(AnalysisResult result, SolutionInterface member, BindingAssessmentOutcome outcome,
        double? delta, string unavailableReason = "")
    {
        FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(
            20, 1, 2, GaussianLikelihoodMode.EstimatedCommonVariance,
            aicc - 5, aicc - 2, aicc, true, true, string.Empty, string.Empty, 1, 1, 1, 0);
        var comparison = new NullModelComparison
        {
            NullModelId = "offset",
            BindingFitSucceeded = delta.HasValue,
            NullFitSucceeded = delta.HasValue,
            BindingInformationCriteria = delta.HasValue ? Criteria(100) : null,
            NullInformationCriteria = delta.HasValue ? Criteria(100 + delta.Value) : null,
            DeltaAicc = delta,
            ComparisonUnavailableReason = unavailableReason,
        };
        result.RestoreMemberComparisonAndAssessment(member.Guid, comparison,
            BindingAssessmentState.Restore(outcome, BindingAssessmentState.CurrentRuleId, null));
    }

    static AnalysisResult CreateResult(out SolutionInterface[] solutions, int count = 2)
    {
        var models = Enumerable.Range(0, count)
            .Select(_ => InjectionProcessingMethodTests.FittedModel(bootstrap: false)).ToArray();
        var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        solutions = models.Select(model => model.Solution).ToArray();
        var convergence = SolverConvergence.FromMultiExperimentAnalysis(solutions.Select(solution => solution.Convergence).ToList());
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, solutions.ToList(), convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        return new AnalysisResult(global);
    }

    static async Task<MemoryStream> Write(AnalysisResult result, SolutionInterface[] members)
    {
        var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package, members.Select(member => member.Data), new[] { result });
        return package;
    }
}
