using System.Globalization;
using System.Net;
using System.Text.Json;
using AnalysisITC.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class DownloadStatisticsTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-downloads-" + Guid.NewGuid().ToString("N"));
    static readonly DateTimeOffset Start = new(2026, 10, 24, 6, 0, 0, TimeSpan.Zero);
    string Database => Path.Combine(directory, "downloads.db");
    DownloadStatisticsOptions Configuration => new() { DatabasePath = Database };

    [Fact]
    public async Task CountsInstallersAcrossStableAndPrereleasesWithoutDownloadingFiles()
    {
        using var http = Client(request => request.RequestUri!.AbsolutePath switch
        {
            "/repos/FrederikTheisen/FT-ITC-Analysis/releases" => Json(new[] { Release(1), Release(2, prerelease: true), Release(3, draft: true) }),
            "/repos/FrederikTheisen/FT-ITC-Analysis/releases/1/assets" => Json(new[]
            {
                Asset(11, "ft-itc-analyis_1.4.2_win-x64-setup.exe", 51),
                Asset(12, "ft-itc-analysis_1.4.2_amd64.deb", 16),
                Asset(13, "ft-itc-analysis_1.4.2_macos-universal.dmg", 30),
                Asset(14, "ft-itc-analysis_1.4.2_amd64.deb.asc", 14), Asset(15, "SHA256SUMS", 20),
                Asset(16, "source.zip", 99), Asset(17, "app.dmg.sha256", 20), Asset(18, "unfinished.dmg", 3, "starter"),
            }),
            "/repos/FrederikTheisen/FT-ITC-Analysis/releases/2/assets" => Json(new[] { Asset(21, "FT-ITC_linux-arm64.AppImage", 4), Asset(22, "FT-ITC_Installer.dmg", 2) }),
            _ => throw new InvalidOperationException("Unexpected request: " + request.RequestUri),
        });

        var snapshot = (await new DownloadStatisticsService(Configuration, http).CollectAsync(Start))!;

        Assert.Equal(5, snapshot.Assets.Count);
        Assert.Equal(103, snapshot.Assets.Sum(asset => asset.Installer.Count));
        Assert.Equal("Windows", Find(snapshot, 11).Installer.Distribution);
        Assert.Equal("x64", Find(snapshot, 11).Installer.Architecture);
        Assert.Equal("x64", Find(snapshot, 12).Installer.Architecture);
        Assert.Equal("universal", Find(snapshot, 13).Installer.Architecture);
        Assert.Equal("arm64", Find(snapshot, 21).Installer.Architecture);
        Assert.Equal("appimage", Find(snapshot, 21).Installer.Package);
        Assert.True(Find(snapshot, 21).Installer.Prerelease);
        Assert.Equal("unspecified", Find(snapshot, 22).Installer.Architecture);
        Assert.All(snapshot.Assets, observation => Assert.Null(observation.Increase));
        Assert.All(snapshot.Assets, observation => Assert.Equal("baseline", observation.IntervalStatus));
    }

    [Fact]
    public async Task FollowsBothReleaseAndAssetPagination()
    {
        var calls = new List<string>();
        using var http = Client(request =>
        {
            var uri = request.RequestUri!;
            calls.Add(uri.PathAndQuery);
            if (uri.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
                return Json(uri.Query.EndsWith("page=1", StringComparison.Ordinal)
                    ? Enumerable.Range(1, 100).Select(id => Release(id, draft: true)).ToArray()
                    : new[] { Release(101) });
            return Json(uri.Query.EndsWith("page=1", StringComparison.Ordinal)
                ? Enumerable.Range(1, 100).Select(id => Asset(id, $"app-{id}.dmg", id)).ToArray()
                : new[] { Asset(101, "app-final.dmg", 9) });
        });

        var snapshot = (await new DownloadStatisticsService(Configuration, http).CollectAsync(Start))!;

        Assert.Equal(101, snapshot.Assets.Count);
        Assert.Equal(5059, snapshot.Assets.Sum(asset => asset.Installer.Count));
        Assert.Equal(4, calls.Count);
        Assert.Contains(calls, path => path.EndsWith("/releases?per_page=100&page=2", StringComparison.Ordinal));
        Assert.Contains(calls, path => path.EndsWith("/releases/101/assets?per_page=100&page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FirstSuccessfulDailySnapshotSurvivesRestartsAndCannotBeCountedTwice()
    {
        using var firstHttp = SingleRelease(8);
        var first = (await new DownloadStatisticsService(Configuration, firstHttp).CollectAsync(Start))!;
        using var noNetwork = Client(_ => throw new InvalidOperationException("No request expected"));
        var restarted = new DownloadStatisticsService(Configuration, noNetwork);

        var repeated = (await restarted.CollectAsync(Start.AddHours(12)))!;

        Assert.Equal(first.ObservedUtc, repeated.ObservedUtc);
        Assert.Equal(8, Assert.Single(repeated.Assets).Installer.Count);
        using var nextHttp = SingleRelease(13);
        var next = (await new DownloadStatisticsService(Configuration, nextHttp).CollectAsync(Start.AddDays(1)))!;
        Assert.Equal(5, Assert.Single(next.Assets).Increase);
        Assert.Equal(Start, Assert.Single(next.Assets).PreviousObservedUtc);
        var csv = Csv(restarted);
        Assert.Equal(3, csv.Count); // Header and two daily observations.
        Assert.Equal("", csv[1][11]);
        Assert.Equal("5", csv[2][11]);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("invalid-counter")]
    [InlineData("missing-counter")]
    [InlineData("invalid-json")]
    [InlineData("duplicate-asset")]
    [InlineData("cancelled")]
    public async Task FailureDoesNotSavePartialOrZeroCountsAndCanBeRetried(string failure)
    {
        using var baselineHttp = SingleRelease(8);
        await new DownloadStatisticsService(Configuration, baselineHttp).CollectAsync(Start);
        var baselineCsv = CsvText(new(Configuration, baselineHttp));
        using var failedHttp = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
                return Json(new[] { Release(1), Release(2) });
            if (request.RequestUri.AbsolutePath.EndsWith("/1/assets", StringComparison.Ordinal)) return Json(new[] { Asset(11, "app.dmg", 50) });
            return failure switch
            {
                "http" => new(HttpStatusCode.TooManyRequests),
                "invalid-counter" => Json(new[] { Asset(12, "app.deb", -1) }),
                "missing-counter" => Json(new[] { new { id = 12, name = "app.deb", state = "uploaded" } }),
                "invalid-json" => new(HttpStatusCode.OK) { Content = new StringContent("{") },
                "duplicate-asset" => Json(new[] { Asset(11, "app.deb", 3) }),
                _ => throw new OperationCanceledException(),
            };
        });
        var failed = new DownloadStatisticsService(Configuration, failedHttp);

        await Assert.ThrowsAnyAsync<Exception>(() => failed.CollectAsync(Start.AddDays(1)));

        Assert.Equal(baselineCsv, CsvText(failed));
        Assert.Equal(Start, failed.ReadLatest()!.ObservedUtc);
        using var retryHttp = SingleRelease(15);
        var retry = (await new DownloadStatisticsService(Configuration, retryHttp).CollectAsync(Start.AddDays(1)))!;
        Assert.Equal(7, Assert.Single(retry.Assets).Increase);
    }

    [Fact]
    public void KeepsExactIntervalsAcrossDowntimeCounterDecreasesReplacementsAndDeletedAssets()
    {
        var store = new DownloadStatisticsStore(Database);
        store.Save(new(2026, 10, 24), Start, [Installer(11, 100), Installer(12, 50)]);
        var resumed = store.Save(new(2026, 10, 28), Start.AddDays(4), [Installer(11, 107), Installer(13, 3)]);
        Assert.Equal(7, Find(resumed, 11).Increase);
        Assert.Equal(Start, Find(resumed, 11).PreviousObservedUtc);
        Assert.Null(Find(resumed, 13).Increase); // Same filename, different GitHub asset ID.
        var reset = store.Save(new(2026, 10, 29), Start.AddDays(5), [Installer(11, 2), Installer(12, 53)]);
        Assert.Null(Find(reset, 11).Increase);
        Assert.Equal("counter_decreased", Find(reset, 11).IntervalStatus);
        Assert.Equal(3, Find(reset, 12).Increase);
        Assert.Equal(Start, Find(reset, 12).PreviousObservedUtc); // Absent in intervening snapshot.
        var subsequent = store.Save(new(2026, 10, 30), Start.AddDays(6), [Installer(11, 4)]);
        Assert.Equal(2, Assert.Single(subsequent.Assets).Increase);
        using var noNetwork = Client(_ => throw new InvalidOperationException());
        Assert.Equal(8, Csv(new(Configuration, noNetwork)).Count); // Deleted/replaced assets remain in history.
    }

    [Fact]
    public void FailedTransactionRollsBackEveryRowAndTheFirstSnapshotCanBeRetried()
    {
        var store = new DownloadStatisticsStore(Database);
        Assert.Throws<SqliteException>(() => store.Save(new(2026, 10, 24), Start, [Installer(11, 3), Installer(11, 4)]));
        Assert.Null(store.ReadLatest());
        store.Save(new(2026, 10, 24), Start, [Installer(11, 3)]);
        Assert.Throws<SqliteException>(() => store.Save(new(2026, 10, 25), Start.AddDays(1), [Installer(11, 8), Installer(11, 9)]));
        Assert.Equal(Start, store.ReadLatest()!.ObservedUtc);
        var retry = store.Save(new(2026, 10, 25), Start.AddDays(1), [Installer(11, 8)]);
        Assert.Equal(5, Assert.Single(retry.Assets).Increase);
    }

    [Fact]
    public async Task CsvPreservesQuotedNamesAndUsesInvariantNumbersAndExplicitBaseline()
    {
        var store = new DownloadStatisticsStore(Database);
        store.Save(new(2026, 10, 24), Start, [Installer(11, 1234) with { Version = "v1,\"test\"", FileName = "FT, \"test\".dmg" }]);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            using var noNetwork = Client(_ => throw new InvalidOperationException());
            var output = new StringWriter();
            Assert.Equal(0, await DownloadStatisticsCommands.RunAsync(["export", "--database", Database], Configuration, output, new StringWriter(), noNetwork));
            var rows = ParseCsv(output.ToString());
            Assert.Equal("v1,\"test\"", rows[1][4]);
            Assert.Equal("FT, \"test\".dmg", rows[1][9]);
            Assert.Equal("1234", rows[1][10]);
            Assert.Equal("", rows[1][11]);
            Assert.Equal("baseline", rows[1][12]);
            Assert.Equal(Start.ToString("O", CultureInfo.InvariantCulture), rows[1][0]);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Fact]
    public async Task PreviewReadsHistoryWithoutCollectingAndSendIncludesCountsInBothEmailForms()
    {
        using var http = SingleRelease(8);
        var service = new DownloadStatisticsService(Configuration, http);
        string? sent = null;
        var reporter = Reporter(service, (_, body) => { sent = body; return Task.CompletedTask; });
        var preview = new StringWriter();
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["preview"], reporter, preview, new StringWriter()));
        Assert.False(File.Exists(Database));
        Assert.Contains("Snapshot: unavailable", preview.ToString());
        Assert.Null(sent);

        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));

        Assert.NotNull(service.ReadLatest());
        var plain = await reporter.CreateReportAsync(new(2026, 10, 23));
        var visible = DailyStatusEmailLayoutTests.VisibleText(sent!);
        Assert.Contains("App downloads", visible);
        Assert.Contains("macOS: 8 (baseline)", visible);
        Assert.Contains("Total: 8 (baseline)", visible);
        Assert.Contains("Snapshot: 2026-10-24 08:00:00 +02:00", plain);
        Assert.Contains("Overall status: OK", plain);
        using var message = DailyStatusEmail.CreateMailMessage("Test", await reporter.CreateStatusReportAsync(new(2026, 10, 23)));
        Assert.Contains("macOS: 8 (baseline)", message.Body);
        Assert.Single(message.AlternateViews);
    }

    [Fact]
    public async Task CollectionFailureKeepsLastCountsAndMakesEmailNeedAttention()
    {
        var store = new DownloadStatisticsStore(Database);
        store.Save(new(2026, 10, 20), Start.AddDays(-4), [Installer(11, 90)]);
        using var offline = Client(_ => throw new HttpRequestException("private connection details"));
        string? subject = null, html = null;
        var reporter = Reporter(new(Configuration, offline), (title, body) => { subject = title; html = body; return Task.CompletedTask; });

        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));

        Assert.StartsWith("[ATTENTION]", subject);
        var visible = DailyStatusEmailLayoutTests.VisibleText(html!);
        Assert.Contains("Collection: FAILED", visible);
        Assert.Contains("over 36 hours old", visible);
        Assert.Contains("macOS: 90 (baseline)", visible);
        Assert.DoesNotContain("private connection", visible);
        Assert.Equal(Start.AddDays(-4), store.ReadLatest()!.ObservedUtc);
    }

    [Fact]
    public async Task SavedCountsSurviveMailDeliveryFailure()
    {
        using var http = SingleRelease(8);
        var service = new DownloadStatisticsService(Configuration, http);
        var reporter = Reporter(service, (_, _) => throw new IOException("mail failed"));

        Assert.Equal(1, await DailyStatusEmail.RunAsync(["send"], reporter, new StringWriter(), new StringWriter()));

        Assert.Equal(8, Assert.Single(service.ReadLatest()!.Assets).Installer.Count);
    }

    [Fact]
    public async Task CompactEmailCombinesVersionsArchitecturesAndPackagesIntoPlatformTotals()
    {
        var store = new DownloadStatisticsStore(Database);
        InstallerDownload[] previous =
        [
            Installer(11, 20),
            Installer(12, 5) with { Version = "v2", Prerelease = true, Architecture = "arm64", Package = "pkg" },
            Installer(21, 40) with { Distribution = "Windows", Package = "exe" },
            Installer(22, 3) with { Distribution = "Windows", Version = "v3", Architecture = "arm64", Package = "msi" },
            Installer(31, 10) with { Distribution = "Linux", Package = "deb" },
        ];
        store.Save(new(2026, 10, 23), Start.AddDays(-1), previous);
        store.Save(new(2026, 10, 24), Start,
        [previous[0] with { Count = 22 }, previous[1] with { Count = 6 }, previous[2] with { Count = 43 },
            previous[3], previous[4] with { Count = 11 }]);
        using var offline = Client(_ => throw new InvalidOperationException("Preview must not collect"));

        var report = await Reporter(new(Configuration, offline)).CreateStatusReportAsync(new(2026, 10, 23));

        Assert.False(report.HasAttention);
        Assert.Contains("macOS: 28 (+3)", report.Text);
        Assert.Contains("Windows: 46 (+3)", report.Text);
        Assert.Contains("Linux: 11 (+1)", report.Text);
        Assert.Contains("Total: 85 (+7)", report.Text);
        Assert.Contains("Change since: 2026-10-23 08:00:00 +02:00", report.Text);
        Assert.DoesNotContain("File:", report.Text);
        Assert.DoesNotContain("v2", report.Text);
        var html = System.Xml.Linq.XDocument.Parse(DailyStatusEmail.RenderHtml(report));
        var platformRow = Assert.Single(html.Descendants("tr"), row => row.Element("th")?.Value.Trim() == "macOS:");
        Assert.Equal("28 (+3)", platformRow.Element("td")!.Value);
        var totalRow = Assert.Single(html.Descendants("tr"), row => row.Element("th")?.Value.Trim() == "Total:");
        Assert.Equal("85 (+7)", totalRow.Element("td")!.Value);
        var csv = Csv(new(Configuration, offline));
        Assert.Contains(csv, row => row[4] == "v2" && row[5] == "true" && row[7] == "arm64" && row[8] == "pkg");
        Assert.Equal(11, csv.Count); // Per-file, per-version history is retained.
    }

    [Theory]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("replaced")]
    [InlineData("decreased")]
    public async Task CompactEmailDoesNotInventGroupIncreasesWhenTheFilesAreNotComparable(string change)
    {
        var store = new DownloadStatisticsStore(Database);
        var windows = Installer(21, 40) with { Distribution = "Windows", Package = "exe" };
        store.Save(new(2026, 10, 23), Start.AddDays(-1), [Installer(11, 20), Installer(12, 5), windows]);
        var currentMac = change switch
        {
            "added" => new[] { Installer(11, 22), Installer(12, 6), Installer(13, 8) },
            "removed" => new[] { Installer(11, 22) },
            "replaced" => new[] { Installer(11, 22), Installer(13, 6) },
            _ => new[] { Installer(11, 22), Installer(12, 1) },
        };
        store.Save(new(2026, 10, 24), Start, [..currentMac, windows with { Count = 43 }]);
        using var offline = Client(_ => throw new InvalidOperationException());

        var report = await Reporter(new(Configuration, offline)).CreateStatusReportAsync(new(2026, 10, 23));

        Assert.Contains("macOS: " + (change == "added" ? "36" : change == "removed" ? "22" : change == "replaced" ? "28" : "23") + " (change unavailable)", report.Text);
        Assert.Contains("Windows: 43 (+3)", report.Text);
        Assert.Contains("(change unavailable)", report.Text.Split('\n').Single(line => line.StartsWith("  Total:", StringComparison.Ordinal)));
        Assert.Equal(change == "decreased", report.HasAttention);
        Assert.Contains(change == "decreased" ? "counter decreased" : "installer list changed", report.Text);
    }

    [Fact]
    public async Task CompactEmailKeepsAPlatformThatDisappearedVisibleWithoutNegativeDownloads()
    {
        var store = new DownloadStatisticsStore(Database);
        store.Save(new(2026, 10, 23), Start.AddDays(-1),
            [Installer(11, 20), Installer(31, 10) with { Distribution = "Linux", Package = "deb" }]);
        store.Save(new(2026, 10, 24), Start, [Installer(11, 22)]);
        using var offline = Client(_ => throw new InvalidOperationException());

        var report = await Reporter(new(Configuration, offline)).CreateReportAsync(new(2026, 10, 23));

        Assert.Contains("macOS: 22 (+2)", report);
        Assert.Contains("Linux: 0 (change unavailable)", report);
        Assert.Contains("Total: 22 (change unavailable)", report);
        Assert.DoesNotContain("(-", report);
    }

    [Theory]
    [InlineData("2026-10-23T06:00:00Z", "2026-10-27T07:00:00Z", "2026-10-23 08:00:00 +02:00", "4 days; daily breakdown unavailable")]
    [InlineData("2026-10-24T06:00:00Z", "2026-10-25T07:00:00Z", "2026-10-24 08:00:00 +02:00", null)]
    public async Task CompactEmailLabelsTheActualIntervalAcrossMissedDaysAndDaylightSaving(string previousInstant, string currentInstant, string expectedPrevious, string? expectedInterval)
    {
        var store = new DownloadStatisticsStore(Database);
        var previous = DateTimeOffset.Parse(previousInstant, CultureInfo.InvariantCulture);
        var current = DateTimeOffset.Parse(currentInstant, CultureInfo.InvariantCulture);
        store.Save(DownloadStatisticsService.Day(previous), previous, [Installer(11, 20)]);
        store.Save(DownloadStatisticsService.Day(current), current, [Installer(11, 29)]);
        using var offline = Client(_ => throw new InvalidOperationException());

        var report = await Reporter(new(Configuration, offline), now: current).CreateReportAsync(new(2026, 10, 26));

        Assert.Contains("macOS: 29 (+9)", report);
        Assert.Contains("Total: 29 (+9)", report);
        Assert.Contains("Change since: " + expectedPrevious, report);
        if (expectedInterval is null) Assert.DoesNotContain("Interval:", report);
        else Assert.Contains("Interval: " + expectedInterval, report);
    }

    [Fact]
    public async Task DisabledCollectionIsInertAndEmptyExportDoesNotCreateDatabase()
    {
        var options = Configuration;
        options.Enabled = false;
        using var noNetwork = Client(_ => throw new InvalidOperationException());
        var service = new DownloadStatisticsService(options, noNetwork);
        Assert.Null(await service.CollectAsync(Start));
        Assert.Null(service.ReadLatest());
        Assert.Single(Csv(service));
        Assert.False(File.Exists(Database));
        var report = await Reporter(service).CreateReportAsync(new(2026, 10, 23));
        Assert.Contains("Collection: disabled", report);
        Assert.Contains("Overall status: OK", report);
        Assert.Equal(0, await DownloadStatisticsCommands.RunAsync(["collect"], options, new StringWriter(), new StringWriter(), noNetwork));
        Assert.Equal(2, await DownloadStatisticsCommands.RunAsync(["export", "--invalid", "value"], options, new StringWriter(), new StringWriter(), noNetwork));
        Assert.False(File.Exists(Database));
    }

    [Fact]
    public async Task CommandRecordsCountsWithoutRunningTheWebHostOrSendingEmail()
    {
        using var http = SingleRelease(8);
        var output = new StringWriter();
        Assert.Equal(0, await DownloadStatisticsCommands.RunAsync(["collect", "--database", Database], new(), output, new StringWriter(), http, () => Start));
        Assert.Contains("1 installers", output.ToString());
        using var offline = Client(_ => throw new HttpRequestException());
        Assert.Equal(1, await DownloadStatisticsCommands.RunAsync(["collect"], Configuration, output, new StringWriter(), offline, () => Start.AddDays(1)));
        Assert.Equal(Start, new DownloadStatisticsStore(Database).ReadLatest()!.ObservedUtc);
    }

    [Theory]
    [InlineData("2026-10-24T21:59:59Z", "2026-10-24")]
    [InlineData("2026-10-24T22:00:00Z", "2026-10-25")]
    [InlineData("2026-10-25T22:59:59Z", "2026-10-25")]
    [InlineData("2026-10-25T23:00:00Z", "2026-10-26")]
    public void DailySamplingUsesTheSameCopenhagenCalendarAsTheTimer(string instant, string day)
        => Assert.Equal(DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture), DownloadStatisticsService.Day(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));

    DailyStatusEmail Reporter(DownloadStatisticsService downloads, Func<string, string, Task>? sender = null, DateTimeOffset? now = null)
    {
        Directory.CreateDirectory(directory);
        var configured = new InterpretationOptions
        {
            AvailabilityPolicyPath = Path.Combine(directory, "availability.json"),
            UsageLog = new() { DatabasePath = Path.Combine(directory, "usage.db") },
            Registration = new() { Enabled = false, AvailabilityPolicyPath = Path.Combine(directory, "registration.json") },
        };
        var options = Options.Create(configured);
        var availability = new InterpretationServiceAvailability(options);
        var registration = new RegistrationAvailability(options);
        return new(new InterpretationUsageStore(options, NullLogger<InterpretationUsageStore>.Instance), availability, "unused",
            () => Task.FromResult(new DailyStatusCheck("active", false)),
            _ => Task.FromResult(new DailyStatusCheck("HTTP 200", false)), sender, () => now ?? Start,
            registrationDiagnostic: new(configured.Registration, Path.Combine(directory, "unused.db"), registration, availability),
            registrationSummary: new(options, registration, availability),
            storageCapacity: new([directory], _ => new("/", 100L << 30, 40L << 30)),
            healthCheck: () => Task.FromResult(new HealthReport(now ?? Start, "healthy",
            [
                new("Storage", [HealthCheckResult.Pass("test", "Storage")]),
                new(ProviderApiAccessCheck.GroupName,
                [HealthCheckResult.Pass("provider.openai", "OpenAI"), HealthCheckResult.Pass("provider.mistral", "Mistral")]),
            ])),
            downloadStatistics: downloads);
    }

    static InstallerDownload Installer(long id, long count) => new(1, id, "v1", false, "FT-ITC.dmg", "macOS", "universal", "dmg", count);
    static DownloadObservation Find(DownloadSnapshot snapshot, long id) => Assert.Single(snapshot.Assets, asset => asset.Installer.AssetId == id);
    static object Release(long id, bool prerelease = false, bool draft = false) => new { id, tag_name = "v" + id, prerelease, draft, published_at = draft ? null : "2026-10-20T06:00:00Z" };
    static object Asset(long id, string name, long count, string state = "uploaded") => new { id, name, state, download_count = count };
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    static HttpClient SingleRelease(long count) => Client(request => request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal)
        ? Json(new[] { Release(1) }) : Json(new[] { Asset(11, "FT-ITC_macos-universal.dmg", count) }));
    static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Handler(response));
    static string CsvText(DownloadStatisticsService service) { var output = new StringWriter(); service.ExportCsv(output); return output.ToString(); }
    static List<string[]> Csv(DownloadStatisticsService service) => ParseCsv(CsvText(service));
    static List<string[]> ParseCsv(string text)
    {
        using var parser = new TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData) rows.Add(parser.ReadFields()!);
        return rows;
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("FT-ITC-Download-Statistics", request.Headers.UserAgent.ToString());
            Assert.Equal("2026-03-10", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
            return Task.FromResult(response(request));
        }
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
