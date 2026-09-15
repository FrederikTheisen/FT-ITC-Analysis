using System.Linq;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisResultValidityReasonFormatterTests
{
    [Fact]
    public void GroupsRepeatedProcessingChanges()
    {
        var formatted = Format(
            "one: baseline correction changed.",
            "two: baseline correction changed.",
            "three: integration windows changed.",
            "four: cell concentration changed.");

        Assert.Equal(new[]
        {
            "Baseline or integration regions were changed in 3 of 4 experiments.",
            "four: cell concentration changed."
        }, formatted);
    }

    [Fact]
    public void KeepsDetailsWhenCategoryAffectsAtMostTwoExperiments()
    {
        var reasons = new[]
        {
            "one: baseline correction changed.",
            "two: baseline correction changed."
        };

        Assert.Equal(reasons, Format(reasons));
    }

    [Fact]
    public void GroupsMultipleCategoriesAndKeepsUncompressedDetails()
    {
        var formatted = Format(
            "one: baseline correction changed; cell concentration changed.",
            "two: baseline correction changed.",
            "three: baseline correction changed.",
            "four: injection inclusion changed.",
            "five: injection inclusion changed.",
            "six: injection inclusion changed; future validity reason changed.");

        Assert.Equal(new[]
        {
            "Baseline or integration regions were changed in 3 of 6 experiments.",
            "one: cell concentration changed.",
            "Injection inclusion was changed in 3 of 6 experiments.",
            "six: future validity reason changed."
        }, formatted);
    }

    [Fact]
    public void GroupsExperimentDetailsAndAttributes()
    {
        var formatted = Format(
            "one: cell concentration changed.",
            "two: syringe concentration changed.",
            "three: cell volume changed.",
            "four: ligand concentration attribute changed.",
            "five: buffer subtraction settings changed.",
            "six: fit-relevant experiment attributes changed.");

        Assert.Equal(new[]
        {
            "Experiment properties were changed in 3 of 6 experiments.",
            "Experiment attributes were changed in 3 of 6 experiments."
        }, formatted);
    }

    [Fact]
    public void GroupsMissingExperimentsAndPreservesResultLevelReasons()
    {
        var formatted = Format(
            "Experiment missing: one.",
            "Experiment missing: two.",
            "Experiment missing: three.",
            "Included experiment set changed.");

        Assert.Equal(new[]
        {
            "3 of 4 experiments are missing.",
            "Included experiment set changed."
        }, formatted);
    }

    [Fact]
    public void PreservesUnknownReasons()
    {
        const string unknown = "one: a future validity reason changed.";

        Assert.Equal(new[] { unknown }, Format(unknown));
    }

    static string[] Format(params string[] reasons)
    {
        return AnalysisResultValidityReasonFormatter.Format(
                AnalysisResultValidityReport.Invalid(reasons),
                reasons.Length)
            .ToArray();
    }
}
