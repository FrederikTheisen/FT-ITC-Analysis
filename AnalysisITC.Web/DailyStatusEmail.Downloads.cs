using System.Globalization;

namespace AnalysisITC.Web;

public sealed partial class DailyStatusEmail
{
    async Task<bool> AddDownloadStatisticsAsync(List<string> lines, bool collect)
    {
        if (downloadStatistics is null) return false;
        lines.Add(""); lines.Add("App downloads");
        if (!downloadStatistics.Enabled)
        { lines.Add("  Collection: disabled"); return false; }
        var attention = false;
        DownloadSnapshot? snapshot;
        if (collect)
        {
            try { snapshot = await downloadStatistics.CollectAsync(now()); }
            catch
            {
                attention = true;
                lines.Add("  Collection: FAILED — GitHub counts could not be saved; previous history retained");
                try { snapshot = downloadStatistics.ReadLatest(); }
                catch { snapshot = null; }
            }
        }
        else
        {
            try { snapshot = downloadStatistics.ReadLatest(); }
            catch { attention = true; snapshot = null; }
        }
        if (snapshot is null)
        { lines.Add("  Snapshot: unavailable — download history is missing or unreadable"); return true; }
        if (now() - snapshot.ObservedUtc > TimeSpan.FromHours(36))
        { attention = true; lines.Add("  Freshness: ATTENTION — latest snapshot is over 36 hours old"); }
        DownloadSnapshot? previous = null;
        var comparisonUnavailable = false;
        try { previous = downloadStatistics.ReadPrevious(snapshot.Day); }
        catch
        {
            attention = comparisonUnavailable = true;
            lines.Add("  Comparison: unavailable — previous snapshot could not be read");
        }
        var currentAssets = snapshot.Assets.Select(item => item.Installer).ToArray();
        var previousAssets = previous?.Assets.Select(item => item.Installer).ToArray();
        foreach (var distribution in new[] { "macOS", "Windows", "Linux" })
        {
            var currentGroup = currentAssets.Where(asset => asset.Distribution == distribution).ToArray();
            var previousGroup = previousAssets?.Where(asset => asset.Distribution == distribution).ToArray();
            if (currentGroup.Length == 0 && previousGroup is not { Length: > 0 }) continue;
            lines.Add($"  {distribution}: " + DownloadSummary(currentGroup, previousGroup, comparisonUnavailable));
        }
        lines.Add("  Total: " + DownloadSummary(currentAssets, previousAssets, comparisonUnavailable));
        lines.Add("  Snapshot: " + Format(snapshot.ObservedUtc));
        if (previous is not null)
        {
            lines.Add("  Change since: " + Format(previous.ObservedUtc));
            var days = snapshot.Day.DayNumber - previous.Day.DayNumber;
            if (days > 1) lines.Add($"  Interval: {days.ToString(CultureInfo.InvariantCulture)} days; daily breakdown unavailable");
            if (!currentAssets.Select(asset => asset.AssetId).ToHashSet().SetEquals(previousAssets!.Select(asset => asset.AssetId)))
                lines.Add("  Comparison: change unavailable where the installer list changed");
        }
        if (snapshot.Assets.Any(item => item.IntervalStatus == "counter_decreased"))
        { attention = true; lines.Add("  Comparison: counter decreased; affected changes unavailable"); }
        return attention;
    }

    static string DownloadSummary(IReadOnlyList<InstallerDownload> current, IReadOnlyList<InstallerDownload>? previous, bool unavailable)
    {
        var total = current.Sum(asset => asset.Count).ToString(CultureInfo.InvariantCulture);
        if (unavailable) return total + " (change unavailable)";
        if (previous is null) return total + " (baseline)";
        var previousCounts = previous.ToDictionary(asset => asset.AssetId, asset => asset.Count);
        // Compare the same assets across the immediately preceding snapshot. New,
        // replaced or removed installers cannot supply a complete group increase.
        if (!previousCounts.Keys.ToHashSet().SetEquals(current.Select(asset => asset.AssetId))
            || current.Any(asset => asset.Count < previousCounts[asset.AssetId]))
            return total + " (change unavailable)";
        var increase = current.Sum(asset => asset.Count - previousCounts[asset.AssetId]);
        return total + " (+" + increase.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
