using System.Xml.Linq;
using AnalysisITC.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class DailyStatusEmailLayoutTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-email-layout-" + Guid.NewGuid().ToString("N"));
    readonly InterpretationOptions options;
    readonly InterpretationUsageStore store;
    readonly OperatorCodeRegistry registry;
    static readonly DateOnly Day = new(2026, 10, 25);

    public DailyStatusEmailLayoutTests()
    {
        Directory.CreateDirectory(directory);
        options = new()
        {
            AvailabilityPolicyPath = Path.Combine(directory, "availability.json"),
            PublicAccessRegistryPath = Path.Combine(directory, "public.json"),
            TombstonePath = Path.Combine(directory, "tombstones.json"),
            OperatorAccess = new() { Enabled = true, RegistryPath = Path.Combine(directory, "operators.json") },
            Registration = new() { OperatorRegistryPath = Path.Combine(directory, "registered.json") },
            UsageLog = new() { Enabled = true, DatabasePath = Path.Combine(directory, "usage.db") },
        };
        store = new(Options.Create(options), NullLogger<InterpretationUsageStore>.Instance);
        registry = new(Options.Create(options), NullLogger<OperatorCodeRegistry>.Instance);
    }

    [Fact]
    public async Task EachAccountOwnsItsActionsAndDailyTotalsAcrossTheDstBoundary()
    {
        var first = registry.Create("First lab", null, true, "standard", "Alex <Chen> & team");
        var second = registry.Create("Second lab", null, true, "standard", "Alex <Chen> & team");
        // The Copenhagen day is 25 hours: 24 Oct 22:00Z through 25 Oct 23:00Z, exclusive.
        Seed(first.Record.Id, new(2026, 10, 24, 21, 59, 59, DateTimeKind.Utc), "summary", "old-model", "success", .1m);
        Seed(first.Record.Id, new(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc), "interpretation", "model-a", "success", .2m);
        Seed(first.Record.Id, new(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), "interpretation", "model-a", "success", .3m);
        Seed(first.Record.Id, new(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc), "summary", "model-b", "provider_error", null);
        Seed(first.Record.Id, new(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc), "summary", "next-day-model", "success", .4m);
        Seed(first.Record.Id, new(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc), "interpretation", "probe-model", "success", 5m, diagnostic: true);
        Seed(second.Record.Id, new(2026, 10, 25, 22, 59, 59, DateTimeKind.Utc), "summary", "mistral-test", "success", .7m, provider: "mistral");

        var report = await Reporter().CreateStatusReportAsync(Day);

        var a = Assert.Single(report.Users!, user => user.Id == first.Record.Id);
        var b = Assert.Single(report.Users!, user => user.Id == second.Record.Id);
        Assert.Equal(3, a.Requests);
        Assert.Equal(new DailyActivityCost(.5m, 1, 0), a.Cost);
        Assert.Equal(5, a.TotalRequests);
        Assert.Equal(new DailyActivityCost(1m, 1, 0), a.TotalCost);
        Assert.Equal("2026-10-25 00:00:00 +02:00", a.FirstRequest);
        Assert.Equal("2026-10-25 02:30:00 +01:00", a.LastRequest);
        Assert.Equal(new DailyUserAction("interpretation", "openai", "model-a", "success", 2), a.Actions[0]);
        Assert.Equal(new DailyUserAction("summary", "openai", "model-b", "provider_error", 1), a.Actions[1]);
        Assert.Single(b.Actions);
        Assert.Equal(new DailyUserAction("summary", "mistral", "mistral-test", "success", 1), b.Actions[0]);
        Assert.Equal("2026-10-25 23:59:59 +01:00", b.LastRequest);

        var html = DailyStatusEmail.RenderHtml(report);
        var document = XDocument.Parse(html);
        var cards = document.Descendants("table").Where(table => (string?)table.Attribute("class") == "user-card").ToArray();
        Assert.Equal(2, cards.Length);
        var firstCard = Assert.Single(cards, card => card.Value.Contains(first.Record.Id));
        Assert.Contains("Alex <Chen> & team", firstCard.Value);
        Assert.Contains("cost unknown (known subtotal $0.5000; unresolved 1", firstCard.Value);
        Assert.Contains("model-a", firstCard.Value);
        Assert.Contains("Provider error", firstCard.Value);
        Assert.DoesNotContain("mistral-test", firstCard.Value);
        Assert.DoesNotContain("old-model", html);
        Assert.DoesNotContain("next-day-model", html);
        Assert.DoesNotContain("probe-model", html);
        Assert.Empty(document.Descendants("script"));
        Assert.DoesNotContain(first.Code, html);
        Assert.DoesNotContain("private-report", html);
        Assert.True(html.IndexOf("User activity", StringComparison.Ordinal) < html.IndexOf("Operational details", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublicAndMissingAccountsStayDistinctWithoutInventedIdentities()
    {
        var stamp = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        Seed(null, stamp, "interpretation", "model-a", "success", 0m);
        Seed("public-installation", stamp, "summary", "model-b", "rejected", 0m, tier: "public");
        Seed("missing-account", stamp, "summary", "model-c", "cancelled", 0m);

        var report = await Reporter().CreateStatusReportAsync(Day);

        Assert.Equal(3, report.Users!.Count);
        Assert.Equal("Unattributed public activity", Assert.Single(report.Users, user => user.Id is null).Name);
        Assert.Equal("Public installation", Assert.Single(report.Users, user => user.Id == "public-installation").Name);
        Assert.Equal("Unavailable account", Assert.Single(report.Users, user => user.Id == "missing-account").Name);
        var html = DailyStatusEmail.RenderHtml(report);
        Assert.Contains("Rejected", html);
        Assert.Contains("Cancelled", html);
    }

    [Fact]
    public async Task EmptyAndUnavailableUsageHaveDifferentHtmlMessages()
    {
        var empty = await Reporter().CreateStatusReportAsync(Day);
        Assert.Empty(empty.Users!);
        Assert.Contains("No user activity.", DailyStatusEmail.RenderHtml(empty));
        options.UsageLog.DatabasePath = directory;
        var unavailable = await Reporter().CreateStatusReportAsync(Day);
        Assert.Null(unavailable.Users);
        var html = DailyStatusEmail.RenderHtml(unavailable);
        Assert.Contains("User activity unavailable.", html);
        Assert.DoesNotContain("No user activity.", html);
        Assert.Contains("Unavailable", html);
    }

    [Fact]
    public async Task HtmlPreviewMatchesTheSentBodyAndMailIncludesPlainTextFallback()
    {
        var stamp = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        Seed("public-installation", stamp, "interpretation", "test-model", "success", .25m, tier: "public");
        string? sent = null;
        var reporter = Reporter((_, body) => { sent = body; return Task.CompletedTask; });
        var output = new StringWriter();
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["preview", "--html", "--date", "2026-10-25"], reporter, output, new StringWriter()));
        Assert.Null(sent);
        Assert.Equal(0, await DailyStatusEmail.RunAsync(["send", "--date", "2026-10-25"], reporter, new StringWriter(), new StringWriter()));
        Assert.Equal(output.ToString(), sent);

        var report = await reporter.CreateStatusReportAsync(Day);
        using var message = DailyStatusEmail.CreateMailMessage("Test status", report);
        Assert.Equal(report.Text, message.Body);
        Assert.False(message.IsBodyHtml);
        var html = Assert.Single(message.AlternateViews);
        Assert.Equal("text/html", html.ContentType.MediaType);
        using var reader = new StreamReader(html.ContentStream);
        Assert.Equal(sent, await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData("send", "--html")]
    [InlineData("check", "--html")]
    [InlineData("preview", "--live-checks", "--html")]
    public async Task HtmlPreviewCannotSubmitDiagnosticsOrSendMail(params string[] args)
    {
        var sent = false;
        var reporter = Reporter((_, _) => { sent = true; return Task.CompletedTask; });
        Assert.Equal(2, await DailyStatusEmail.RunAsync(args, reporter, new StringWriter(), new StringWriter()));
        Assert.False(sent);
    }

    internal static string VisibleText(string html) => string.Concat(XDocument.Parse(html).DescendantNodes().OfType<XText>().Select(node => node.Value));

    DailyStatusEmail Reporter(Func<string, string, Task>? sender = null) => new(store,
        new InterpretationServiceAvailability(Options.Create(options)), "unused",
        () => Task.FromResult(new DailyStatusCheck("active", false)),
        _ => Task.FromResult(new DailyStatusCheck("HTTP 200; interpretation available", false)), sender,
        () => new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.Zero), operators: registry);

    void Seed(string? id, DateTime stamp, string task, string model, string outcome, decimal? cost,
        bool diagnostic = false, string provider = "openai", string tier = "standard")
    {
        var execution = Guid.NewGuid().ToString("N");
        var request = new InterpretationUsageRequest
        {
            ServerExecutionId = execution, RequestId = execution, ClientRequestId = execution,
            OperatorCodeId = id, TaskType = task, EffectiveModel = model, Provider = provider,
            StartedUtc = stamp, CompletedUtc = stamp, Outcome = outcome, HttpStatus = outcome == "success" ? 200 : 503,
            AccessTier = tier, EffectivePreset = diagnostic ? "daily-diagnostic" : "standard", ReportId = "private-report",
        };
        Assert.Equal(InterpretationAdmissionStatus.Admitted, store.TryAdmit(request, null, DateTime.MinValue).Status);
        store.BeginAttempt(execution, 1);
        store.RecordAttempt(new InterpretationUsageAttempt
        {
            ServerExecutionId = execution, AttemptNumber = 1, TimestampUtc = stamp, TaskType = task,
            Model = model, Provider = provider, CombinedCost = cost, Outcome = outcome, HttpStatus = request.HttpStatus,
        });
        store.FinalizeRequest(request);
    }

    public void Dispose() { try { Directory.Delete(directory, recursive: true); } catch { } }
}
