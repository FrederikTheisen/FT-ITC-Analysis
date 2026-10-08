using System.Linq;
using System.Text.Json;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class InterpretationMixedAssessmentTests
{
    [Fact]
    public void IndependentCollectionSerializesEffectiveAndAutomaticSummariesSeparately()
    {
        var result = IndependentAssessmentTests.CreateIndependentResult(out var members);
        var report = AnalysisInterpretationCollectionTests.Report(result);

        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.BindingDetected);
        var mixedPackage = AnalysisInterpretationPackageBuilder.Build(report, result);
        var mixedAssessment = mixedPackage.Result.BindingAssessment.Members.Single(item => item.SolutionId == members[0].Guid);
        Assert.Equal("Mixed", mixedPackage.Result.BindingAssessment.CollectionOutcome);
        Assert.Equal("Mixed", mixedPackage.Result.BindingAssessment.EffectiveOutcome);
        Assert.Equal("NotAssessed", mixedPackage.Result.BindingAssessment.AutomaticOutcome);
        Assert.Equal(1, mixedPackage.Result.BindingAssessment.OutcomeCounts["BindingDetected"]);
        Assert.Equal(1, mixedPackage.Result.BindingAssessment.OutcomeCounts["NotAssessed"]);
        Assert.Equal("BindingDetected", mixedAssessment.EffectiveOutcome);
        Assert.Equal("NotAssessed", mixedAssessment.AutomaticOutcome);

        var mixedFingerprint = AnalysisInterpretationPromptBuilder.Build(mixedPackage).EvidenceFingerprint;
        result.SetMemberBindingAssessmentOverride(members[1].Guid, BindingAssessmentOutcome.BindingDetected);
        var uniformPackage = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.Equal("BindingDetected", uniformPackage.Result.BindingAssessment.CollectionOutcome);
        Assert.Equal("BindingDetected", uniformPackage.Result.BindingAssessment.EffectiveOutcome);
        Assert.Equal("NotAssessed", uniformPackage.Result.BindingAssessment.AutomaticOutcome);
        Assert.Equal(2, uniformPackage.Result.BindingAssessment.OutcomeCounts["BindingDetected"]);
        Assert.NotEqual(mixedFingerprint, AnalysisInterpretationPromptBuilder.Build(uniformPackage).EvidenceFingerprint);

        using var json = JsonDocument.Parse(AnalysisInterpretationModelInputWriter.Write(mixedPackage));
        var assessment = json.RootElement.GetProperty("results")[0].GetProperty("bindingAssessment");
        Assert.Equal("Mixed", assessment.GetProperty("collectionOutcome").GetString());
        Assert.Equal("Mixed", assessment.GetProperty("effectiveOutcome").GetString());
        Assert.Equal("NotAssessed", assessment.GetProperty("automaticOutcome").GetString());
    }

    [Fact]
    public void EffectiveOverridesCanBeUniformWhileAutomaticSummaryIsMixed()
    {
        var result = IndependentAssessmentTests.CreateIndependentResult(out var members);
        result.RestoreMemberAssessment(members[0].Guid,
            BindingAssessmentState.Restore(BindingAssessmentOutcome.BindingDetected, BindingAssessmentState.CurrentRuleId, null));
        result.RestoreMemberAssessment(members[1].Guid,
            BindingAssessmentState.Restore(BindingAssessmentOutcome.NoBindingDetected, BindingAssessmentState.CurrentRuleId, null));
        result.SetMemberBindingAssessmentOverride(members[1].Guid, BindingAssessmentOutcome.BindingDetected);

        var package = AnalysisInterpretationPackageBuilder.Build(
            AnalysisInterpretationCollectionTests.Report(result), result);
        var assessment = package.Result.BindingAssessment;

        Assert.Equal("BindingDetected", assessment.CollectionOutcome);
        Assert.Equal("BindingDetected", assessment.EffectiveOutcome);
        Assert.Equal("Mixed", assessment.AutomaticOutcome);
        Assert.Equal(2, assessment.OutcomeCounts["BindingDetected"]);
        Assert.False(assessment.OutcomeCounts.ContainsKey("NoBindingDetected"));
        Assert.Contains(assessment.Members, member => member.AutomaticOutcome == "NoBindingDetected"
            && member.EffectiveOutcome == "BindingDetected" && member.ManualOverride == "BindingDetected");
    }
}
