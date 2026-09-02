using System;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;

namespace AnalysisITC.Core.Presentation
{

/// <summary>
/// Human-readable formatting for the compact profile-likelihood summary shown
/// in result inspectors. Detailed diagnostics remain available to persistence
/// and solver status consumers, but are intentionally not rendered here.
/// </summary>
public static class ProfileLikelihoodDisplayFormatter
{
    public static string Status(ErrorEstimationOutcome outcome) => outcome switch
    {
        ErrorEstimationOutcome.Completed => "Complete",
        ErrorEstimationOutcome.PartialFailure => "Incomplete",
        ErrorEstimationOutcome.Cancelled => "Cancelled",
        ErrorEstimationOutcome.NotRun => "Not run",
        ErrorEstimationOutcome.CompleteFailure => "Unavailable",
        _ => "Unavailable",
    };

    public static string Endpoints(int endpointsFound, int totalSides)
    {
        var total = Math.Max(0, totalSides);
        if (total == 0) return "Not applicable";
        var found = Math.Max(0, Math.Min(total, endpointsFound));
        return found == total ? $"All {total} found" : $"{found} of {total} found";
    }

    public static string Duration(TimeSpan elapsed, IFormatProvider provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        var milliseconds = Math.Max(0, elapsed.TotalMilliseconds);
        if (milliseconds < 1000)
            return $"{milliseconds:0} ms";

        var seconds = Math.Max(0, elapsed.TotalSeconds);
        if (seconds < 60)
            return $"{seconds.ToString("0.0", provider)} s";

        var minutes = (int)(seconds / 60);
        var remainder = seconds - minutes * 60;
        return $"{minutes.ToString(provider)} min {remainder.ToString("0.0", provider)} s";
    }

    public static string Status(ProfileLikelihoodSummary summary)
        => summary == null ? "Unavailable" : Status(summary.Outcome);

    public static string Endpoints(ProfileLikelihoodSummary summary)
        => summary == null ? "Not applicable" : Endpoints(summary.EndpointsFound, summary.TotalSides);

    public static string Duration(ProfileLikelihoodSummary summary, IFormatProvider provider = null)
        => summary == null ? "Not applicable" : Duration(summary.Elapsed, provider);

    public static string CompactSummary(ProfileLikelihoodSummary summary, IFormatProvider provider = null)
        => summary == null
            ? "Profile status: Unavailable | 95% CI endpoints: Not applicable | Profile calculation time: Not applicable"
            : $"Profile status: {Status(summary)} | 95% CI endpoints: {Endpoints(summary)} | Profile calculation time: {Duration(summary, provider)}";

    public static string CompactSummary(ProfileLikelihoodRunResult run, IFormatProvider provider = null)
    {
        if (run == null)
            return "Profile status: Unavailable | 95% CI endpoints: Not applicable | Profile calculation time: Not applicable";

        var endpointsFound = run.Coordinates.Sum(c =>
            (c.Lower.IsEndpointFound ? 1 : 0) + (c.Upper.IsEndpointFound ? 1 : 0));
        return $"Profile status: {Status(run.Outcome)} | 95% CI endpoints: {Endpoints(endpointsFound, 2 * run.ParameterCount)} | Profile calculation time: {Duration(run.Elapsed, provider)}";
    }
}
}
