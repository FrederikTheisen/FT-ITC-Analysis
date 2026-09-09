using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class ScientificGuidanceTests
{
    [Fact]
    public void VersionedGuidanceRetainsCoreScientificClauses()
    {
        var text = ScientificGuidance.Text;

        Assert.Contains("modest departures from the expected N", text, StringComparison.Ordinal);
        Assert.Contains("not universally required", text, StringComparison.Ordinal);
        Assert.Contains("Smooth baseline drift is common and ordinarily manageable", text, StringComparison.Ordinal);
        Assert.Contains("K = p + 1", text, StringComparison.Ordinal);
        Assert.Contains("bound-limited", text, StringComparison.Ordinal);
        Assert.Contains("log-affinity", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectsOnlyApplicableModelGuidance()
    {
        var prompt = PromptWithPackage("one-set-of-sites");

        Assert.Contains("equivalent independent sites", prompt.SystemInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain("two site classes are supported", prompt.SystemInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain("competitor concentration", prompt.SystemInstructions, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData("17", false)]
    public void OptionalKnowledgeSelectorToleratesBooleanNullAndScalarValues(string value, bool allowsGeneralKnowledge)
    {
        var package = "{\"requestedInterpretation\":{\"allowGeneralModelKnowledge\":" + value + "},\"results\":[]}";
        var prompt = ScientificGuidance.BuildPrompt("future-format", "Output instructions", package);

        Assert.Equal(allowsGeneralKnowledge,
            prompt.SystemInstructions.Contains("General ITC knowledge may support", StringComparison.Ordinal));
    }

    [Fact]
    public void PromptDiagnosticsContainStructureAndFingerprintsOnly()
    {
        var requestId = "request-log-test-" + Guid.NewGuid().ToString("N");
        var packageMarker = "package-secret-" + Guid.NewGuid().ToString("N");
        var outputMarker = "output-secret-" + Guid.NewGuid().ToString("N");

        var prompt = ScientificGuidance.BuildPrompt(
            "future-format", outputMarker,
            "{\"studyContext\":{\"comment\":\"" + packageMarker + "\"},\"results\":[]}",
            requestId: requestId);

        var log = AnalysisITC.Core.Application.AppEventHandler.GetLogReport();
        Assert.Contains("stage=prompt-start", log, StringComparison.Ordinal);
        Assert.Contains("stage=prompt-ready", log, StringComparison.Ordinal);
        Assert.Contains(requestId, log, StringComparison.Ordinal);
        Assert.Contains(ScientificGuidance.Hash(prompt.SystemInstructions), log, StringComparison.Ordinal);
        Assert.Contains(prompt.OutputInstructionsFingerprint, log, StringComparison.Ordinal);
        Assert.DoesNotContain(packageMarker, log, StringComparison.Ordinal);
        Assert.DoesNotContain(outputMarker, log, StringComparison.Ordinal);

        var failureRequestId = "request-log-failure-" + Guid.NewGuid().ToString("N");
        var failureMarker = "failure-secret-" + Guid.NewGuid().ToString("N");
        Assert.ThrowsAny<JsonException>(() => ScientificGuidance.BuildPrompt(
            "future-format", failureMarker, "{ malformed " + failureMarker,
            requestId: failureRequestId));
        log = AnalysisITC.Core.Application.AppEventHandler.GetLogReport();
        Assert.Contains("stage=prompt-failed", log, StringComparison.Ordinal);
        Assert.Contains(failureRequestId, log, StringComparison.Ordinal);
        Assert.DoesNotContain(failureMarker, log, StringComparison.Ordinal);
    }

    static AnalysisInterpretationPrompt PromptWithPackage(string modelType)
    {
        var package = "{\"requestedInterpretation\":{\"allowGeneralModelKnowledge\":false},\"results\":[{\"model\":{\"type\":\"" + modelType + "\"}}]}";
        return ScientificGuidance.BuildPrompt("future-format", "Output instructions", package);
    }
}
