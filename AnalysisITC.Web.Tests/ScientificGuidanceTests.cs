using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class ScientificGuidanceTests
{
    [Fact]
    public void StandardThreePointThreeIsActiveAndStructuredGuidanceIsSeparatelyAddressable()
    {
        Assert.Equal("itc-scientific-guidance-3.3", ScientificGuidance.Revision);
        var standard = ScientificGuidance.BuildPrompt("future-format", "Output instructions", "{\"results\":[]}");
        var structured = ScientificGuidance.BuildPrompt("future-format", "Output instructions", "{\"results\":[]}",
            variant: ScientificGuidance.StructuredVariant);

        Assert.Equal(ScientificGuidance.Revision, standard.PromptVersion);
        Assert.Equal(ScientificGuidance.StructuredRevision, structured.PromptVersion);
        Assert.NotEqual(standard.SystemInstructions, structured.SystemInstructions);
        Assert.NotEqual(standard.InputFingerprint, structured.InputFingerprint);
    }

    [Fact]
    public void CompactThermogramsAreDescribedAsIntervalBoundsWithoutEndpoints()
    {
        Assert.Contains("uniform-minmax-v1", ScientificGuidance.Text);
        Assert.Contains("Extrema have no recorded occurrence times or within-interval order", ScientificGuidance.Text);
        Assert.Contains("Baseline bounds are calculated independently", ScientificGuidance.Text);
        Assert.Contains("They cannot alone establish precise settling or integration adequacy", ScientificGuidance.Text);
        Assert.DoesNotContain("separately preserved endpoints", ScientificGuidance.Text);
    }

    [Fact]
    public void VersionedGuidanceRetainsCoreScientificClauses()
    {
        var text = ScientificGuidance.Text;

        Assert.Contains("Modest departures alone need no warning", text, StringComparison.Ordinal);
        Assert.Contains("not universally required", text, StringComparison.Ordinal);
        Assert.Contains("Smooth drift is normally manageable", text, StringComparison.Ordinal);
        Assert.Contains("adding one likelihood parameter beyond fitted model parameters", text, StringComparison.Ordinal);
        Assert.Contains("interval extent, bounds, profiles", text, StringComparison.Ordinal);
        Assert.Contains("log association affinity", text, StringComparison.Ordinal);
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
    public void PromptDiagnosticsAreReadableWithoutIdentifiersOrPrivateContent()
    {
        var requestId = "request-log-test-" + Guid.NewGuid().ToString("N");
        var packageMarker = "package-secret-" + Guid.NewGuid().ToString("N");
        var outputMarker = "output-secret-" + Guid.NewGuid().ToString("N");

        var prompt = ScientificGuidance.BuildPrompt(
            "future-format", outputMarker,
            "{\"studyContext\":{\"comment\":\"" + packageMarker + "\"},\"results\":[]}",
            requestId: requestId);

        var log = AnalysisITC.Core.Application.AppEventHandler.GetLogReport();
        Assert.Contains("AI prompt prepared:", log, StringComparison.Ordinal);
        Assert.Contains("Knowledge retrieval available.", log, StringComparison.Ordinal);
        Assert.DoesNotContain(requestId, log, StringComparison.Ordinal);
        Assert.DoesNotContain(prompt.OutputInstructionsFingerprint, log, StringComparison.Ordinal);
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
