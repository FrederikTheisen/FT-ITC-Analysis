using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnalysisITC.Web;

public sealed class DownloadStatisticsOptions
{
    public bool Enabled { get; set; } = true;
    public string DatabasePath { get; set; } = "/var/lib/ftitc-web/download-statistics.db";
}

internal sealed record InstallerDownload(long ReleaseId, long AssetId, string Version, bool Prerelease,
    string FileName, string Distribution, string Architecture, string Package, long Count);

internal sealed record DownloadObservation(DateTimeOffset ObservedUtc, InstallerDownload Installer,
    DateTimeOffset? PreviousObservedUtc = null, long? PreviousCount = null)
{
    public long? Increase => PreviousCount is long previous && Installer.Count >= previous ? Installer.Count - previous : null;
    public string IntervalStatus => PreviousCount is null ? "baseline" : Increase is null ? "counter_decreased" : "observed";
}

internal sealed record DownloadSnapshot(DateOnly Day, DateTimeOffset ObservedUtc, IReadOnlyList<DownloadObservation> Assets);

/// <summary>Reads aggregate GitHub counters; never proxies or changes a download.</summary>
internal sealed class DownloadStatisticsService(DownloadStatisticsOptions options, HttpClient? http = null)
{
    internal const string RepositoryApi = "https://api.github.com/repos/FrederikTheisen/FT-ITC-Analysis";
    static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
    readonly DownloadStatisticsStore store = new(options.DatabasePath);
    readonly HttpClient client = http ?? SharedClient;

    public bool Enabled => options.Enabled;
    public static DateOnly Day(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
    public DownloadSnapshot? ReadLatest() => store.ReadLatest();
    public DownloadSnapshot? ReadPrevious(DateOnly day) => store.ReadPrevious(day);
    public void ExportCsv(TextWriter output) => store.ExportCsv(output);

    public async Task<DownloadSnapshot?> CollectAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return null;
        var latest = store.ReadLatest();
        // The first successful snapshot of a Copenhagen day is immutable, including on retries/restarts.
        if (latest is not null && latest.Day >= Day(now)) return latest;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var assets = new List<InstallerDownload>();
        var releases = await ReadPagesAsync("/releases", deadline.Token);
        foreach (var release in releases)
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            // A draft has no published date; a malformed published release must not silently disappear.
            _ = release.GetProperty("published_at").GetDateTimeOffset();
            var releaseId = PositiveId(release, "id");
            var version = RequiredText(release, "tag_name");
            var prerelease = release.GetProperty("prerelease").GetBoolean();
            foreach (var asset in await ReadPagesAsync($"/releases/{releaseId}/assets", deadline.Token))
            {
                if (RequiredText(asset, "state") != "uploaded") continue;
                var name = RequiredText(asset, "name");
                var package = Path.GetExtension(name).ToLowerInvariant();
                var distribution = package switch
                {
                    ".dmg" or ".pkg" => "macOS",
                    ".exe" or ".msi" or ".msix" => "Windows",
                    ".deb" or ".rpm" or ".appimage" => "Linux",
                    _ => null,
                };
                if (distribution is null) continue;
                var count = asset.GetProperty("download_count").GetInt64();
                if (count < 0) throw new InvalidDataException("A GitHub download counter is negative.");
                assets.Add(new(releaseId, PositiveId(asset, "id"), version, prerelease, name,
                    distribution, Architecture(name), package[1..], count));
            }
        }
        if (assets.Select(asset => asset.AssetId).Distinct().Count() != assets.Count)
            throw new InvalidDataException("GitHub returned duplicate release assets.");
        // No writes until every page and counter has been validated.
        return store.Save(Day(now), now.ToUniversalTime(), assets);
    }

    async Task<List<JsonElement>> ReadPagesAsync(string path, CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{RepositoryApi}{path}?per_page=100&page={page}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("FT-ITC-Download-Statistics/1.0");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub did not return a release list.");
            foreach (var item in document.RootElement.EnumerateArray()) items.Add(item.Clone());
            if (document.RootElement.GetArrayLength() < 100) return items;
        }
    }

    static long PositiveId(JsonElement item, string property)
    {
        var value = item.GetProperty(property).GetInt64();
        return value > 0 ? value : throw new InvalidDataException("A GitHub identifier is invalid.");
    }

    static string RequiredText(JsonElement item, string property) => item.GetProperty(property).GetString() is { Length: > 0 } text
        ? text : throw new InvalidDataException("GitHub metadata is incomplete.");

    static string Architecture(string name)
    {
        foreach (var (pattern, label) in new[]
        {
            ("universal", "universal"), ("arm64|aarch64", "arm64"),
            ("x64|amd64|x86_64", "x64"), ("x86|i386|i686", "x86"),
        })
            if (Regex.IsMatch(name, $"(?:^|[-_.])(?:{pattern})(?:[-_.]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return label;
        return "unspecified";
    }
}
