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
    [InlineData(12.5, "13 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(999.49, "999 ms")]
    [InlineData(999.5, "1.0 s")]
    [InlineData(999.6, "1.0 s")]
    [InlineData(1000, "1.0 s")]
    [InlineData(1250, "1.3 s")]
    [InlineData(59900, "59.9 s")]
    [InlineData(59949, "59.9 s")]
    [InlineData(59950, "1 min 0.0 s")]
    [InlineData(59970, "1 min 0.0 s")]
    [InlineData(60000, "1 min 0.0 s")]
    [InlineData(61250, "1 min 1.3 s")]
    [InlineData(119949, "1 min 59.9 s")]
    [InlineData(119950, "2 min 0.0 s")]
    [InlineData(119970, "2 min 0.0 s")]
    [InlineData(120000, "2 min 0.0 s")]
    [InlineData(120050, "2 min 0.1 s")]
    [InlineData(0, "0 ms")]
    [InlineData(-10, "0 ms")]
    public void DurationUsesCompactHumanUnits(double milliseconds, string expected)
        => Assert.Equal(expected, ProfileLikelihoodDisplayFormatter.Duration(
            TimeSpan.FromMilliseconds(milliseconds), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(999.49, "999 ms")]
    [InlineData(999.6, "1,0 s")]
    [InlineData(1250, "1,3 s")]
    [InlineData(59949, "59,9 s")]
    [InlineData(59970, "1 min 0,0 s")]
    [InlineData(61250, "1 min 1,3 s")]
    [InlineData(119949, "1 min 59,9 s")]
    [InlineData(119970, "2 min 0,0 s")]
    public void DurationUsesExplicitOrCurrentCulture(double milliseconds, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        var culture = CultureInfo.GetCultureInfo("da-DK");
        var elapsed = TimeSpan.FromMilliseconds(milliseconds);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, ProfileLikelihoodDisplayFormatter.Duration(elapsed, culture));
            CultureInfo.CurrentCulture = culture;
            Assert.Equal(expected, ProfileLikelihoodDisplayFormatter.Duration(elapsed));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
