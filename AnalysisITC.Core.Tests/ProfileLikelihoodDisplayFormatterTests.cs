using System;
using System.Globalization;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ProfileLikelihoodDisplayFormatterTests
{
    [Theory]
    [InlineData(ErrorEstimationOutcome.Completed, "Complete")]
    [InlineData(ErrorEstimationOutcome.PartialFailure, "Incomplete")]
    [InlineData(ErrorEstimationOutcome.CompleteFailure, "Unavailable")]
    [InlineData(ErrorEstimationOutcome.Cancelled, "Cancelled")]
    [InlineData(ErrorEstimationOutcome.NotRun, "Not run")]
    public void StatusUsesReadableOutcomeLabels(ErrorEstimationOutcome outcome, string expected)
        => Assert.Equal(expected, ProfileLikelihoodDisplayFormatter.Status(outcome));

    [Theory]
    [InlineData(0, 0, "Not applicable")]
    [InlineData(2, 2, "All 2 found")]
    [InlineData(1, 2, "1 of 2 found")]
    [InlineData(-1, 2, "0 of 2 found")]
    [InlineData(7, 2, "All 2 found")]
    public void EndpointsUsesTotalSideCount(int found, int total, string expected)
        => Assert.Equal(expected, ProfileLikelihoodDisplayFormatter.Endpoints(found, total));

    [Theory]
    [InlineData(12, "12 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1.0 s")]
    [InlineData(59900, "59.9 s")]
    [InlineData(60000, "1 min 0.0 s")]
    [InlineData(61250, "1 min 1.3 s")]
    [InlineData(-10, "0 ms")]
    public void DurationUsesCompactHumanUnits(double milliseconds, string expected)
        => Assert.Equal(expected, ProfileLikelihoodDisplayFormatter.Duration(
            TimeSpan.FromMilliseconds(milliseconds), CultureInfo.InvariantCulture));
}
