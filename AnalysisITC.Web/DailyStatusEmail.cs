using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

internal sealed record DailyStatusCheck(string Detail, bool HasAttention, bool InterpretationUnavailable = false);
internal sealed record DailyStatusReport(string Text, bool HasAttention, IReadOnlyList<DailyUserActivity>? Users = null);

/// <summary>A metadata-only daily report, run as a command rather than a web request.</summary>
public sealed partial class DailyStatusEmail
{
    const string ZoneId = "Europe/Copenhagen";
    internal const string SenderAddress = "mist@ft-itc.org";
    internal const string ReplyToAddress = "support@ft-itc.org";
    const string RecipientAddress = "admin@ft-itc.org";
    const string LocalUrl = "http://127.0.0.1:5000/api/interpretation/status";
    const string PublicUrl = "https://app.ft-itc.org/api/interpretation/status";
    static readonly string[] Outcomes = ["success", "rejected", "cancelled", "timeout", "provider_error", "server_error"];

    readonly InterpretationUsageStore usage;
    readonly InterpretationServiceAvailability availability;
    readonly string configurationPath;
    readonly TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
    readonly Func<Task<DailyStatusCheck>> serviceCheck;
    readonly Func<string, Task<DailyStatusCheck>> endpointCheck;
    readonly Func<string, string, Task>? sender;
    readonly Func<DateTimeOffset> now;
    readonly Func<Task<HealthReport>> healthCheck;
    readonly OperatorCodeRegistry? operators;
    readonly RegistrationPipelineDiagnostic? registrationDiagnostic;
    readonly RegistrationSummaryService? registrationSummary;
    readonly StorageCapacityService? storageCapacity;
    readonly DailyInterpretationProbe? interpretationDiagnostic;

    public DailyStatusEmail(InterpretationUsageStore usage, InterpretationServiceAvailability availability,
        IOptions<InterpretationOptions> options, OperatorCodeRegistry operators,
        RegistrationPipelineDiagnostic registrationDiagnostic, RegistrationSummaryService registrationSummary,
        StorageCapacityService storageCapacity)
        : this(usage, availability, options.Value.StatusEmailConfigurationPath,
            CheckServiceAsync, CheckEndpointAsync, null, () => DateTimeOffset.UtcNow, operators,
            registrationDiagnostic, registrationSummary, storageCapacity, ServiceHealthCheck.ReadAsync,
            new DailyInterpretationProbe(options.Value, availability, usage)) { }

    internal DailyStatusEmail(InterpretationUsageStore usage, InterpretationServiceAvailability availability,
        string configurationPath, Func<Task<DailyStatusCheck>> serviceCheck, Func<string, Task<DailyStatusCheck>> endpointCheck,
        Func<string, string, Task>? sender, Func<DateTimeOffset> now,
        OperatorCodeRegistry? operators = null, RegistrationPipelineDiagnostic? registrationDiagnostic = null,
        RegistrationSummaryService? registrationSummary = null, StorageCapacityService? storageCapacity = null,
        Func<Task<HealthReport>>? healthCheck = null, DailyInterpretationProbe? interpretationDiagnostic = null)
    {
        this.usage = usage; this.availability = availability; this.configurationPath = configurationPath;
        this.serviceCheck = serviceCheck; this.endpointCheck = endpointCheck; this.sender = sender; this.now = now;
        this.healthCheck = healthCheck ?? (() => Task.FromException<HealthReport>(new InvalidOperationException("Service health check is unavailable.")));
        this.operators = operators; this.registrationDiagnostic = registrationDiagnostic; this.registrationSummary = registrationSummary;
        this.storageCapacity = storageCapacity;
        this.interpretationDiagnostic = interpretationDiagnostic;
    }

    internal static (DateTime StartUtc, DateTime EndUtc) Bounds(DateOnly date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        var start = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (TimeZoneInfo.ConvertTimeToUtc(start, zone), TimeZoneInfo.ConvertTimeToUtc(start.AddDays(1), zone));
    }

    public async Task<string> CreateReportAsync(DateOnly date) => (await CreateStatusReportAsync(date)).Text;

    internal async Task<DailyStatusReport> CreateStatusReportAsync(DateOnly date, bool liveChecks = false)
    {
        var hasAttention = false;
        IReadOnlyList<DailyUserActivity>? users = null;
        var (start, end) = Bounds(date);
        var lines = new List<string>
        {
            $"FT-ITC MIST daily status — {date:yyyy-MM-dd}",
            $"Period: {Format(start)} to {Format(end)} (end exclusive)",
            "",
            "Service",
            "  Checks started: " + Format(now()),
        };
        try
        {
            var service = await serviceCheck();
            hasAttention |= service.HasAttention;
            lines.Add("  ftitc-web: " + service.Detail);
        }
        catch { hasAttention = true; lines.Add("  ftitc-web: check unavailable"); }
        var intentionallyUnavailable = false;
        try
        {
            var policy = availability.Read();
            hasAttention |= !policy.PolicyValid;
            intentionallyUnavailable = policy.PolicyValid && policy.Status is "paused" or "retired";
            lines.Add("  Interpretation policy: " + policy.Status);
        }
        catch { hasAttention = true; lines.Add("  Interpretation policy: check unavailable"); }
        lines.Add("  Direct HTTP checks originate on MIST.");
        foreach (var (label, endpoint) in await CheckEndpointsAsync(
            ("Local API", LocalUrl), ("Public API (from MIST)", PublicUrl),
            ("Local registration endpoint", "http://127.0.0.1:5000/api/registration/status"),
            ("Local viewer endpoint", "http://127.0.0.1:5000/api/viewer/token")))
        {
            hasAttention |= endpoint.HasAttention;
            // A valid paused/retired policy is intentional; transport and
            // protocol failures still require attention in that state.
            hasAttention |= endpoint.InterpretationUnavailable && !intentionallyUnavailable;
            lines.Add($"  {label}: {endpoint.Detail}");
        }

        try
        {
            // Always supplied by the live web process in production. Never fall back
            // to root's filesystem access if that process cannot return its checks.
            var report = await healthCheck();
            hasAttention |= report.HasAttention;
            lines.Add(""); lines.Add("Local readiness (web service)");
            lines.Add("  Configuration, storage and assets; public access is checked separately.");
            foreach (var group in report.Groups)
            {
                var details = group.Checks.Where(check => check.State != HealthCheckState.Pass).ToArray();
                lines.Add($"  {group.Name}: {(details.Length == 0 ? "PASS — local checks passed" : string.Join("; ", details.Select(check => $"{check.State.ToString().ToUpperInvariant()} — {check.Name}: {check.Reason}")))}");
            }
        }
        catch { hasAttention = true; lines.Add("\nLocal readiness (web service)\n  FAIL — service health checks unavailable"); }

        lines.Add(""); lines.Add("External access (Cloudflare to MIST)");
        lines.Add("  Checks started: " + Format(now()));
        lines.Add("  The website proxy opens an outside connection into MIST for each check.");
        lines.Add("  These checks test endpoint reachability, not interpretation generation or project uploads.");
        foreach (var (label, endpoint) in await CheckEndpointsAsync(
            ("Registration endpoint", "https://ft-itc.org/api/registration/status"),
            ("Viewer endpoint", "https://ft-itc.org/api/viewer/token")))
        {
            hasAttention |= endpoint.HasAttention;
            lines.Add($"  {label}: {(endpoint.HasAttention ? "FAIL" : "PASS")} — {endpoint.Detail}");
        }

        DailyInterpretationProbeResult? probe = null;
        if (interpretationDiagnostic is not null)
        {
            try { probe = await interpretationDiagnostic.RunAsync(liveChecks, now()); }
            catch
            {
                probe = new(DailyInterpretationProbe.Day(now()), now(), HealthCheckState.Fail, "diagnostic check unavailable");
            }
            hasAttention |= probe.State is HealthCheckState.Fail or HealthCheckState.Warning;
        }

        lines.Add("");
        lines.Add("Server storage");
        if (storageCapacity is null)
        {
            hasAttention = true; lines.Add("  Server storage: check unavailable");
        }
        else
        {
            try
            {
                var storage = storageCapacity.Read();
                hasAttention |= storage.HasAttention;
                if (storage.Status == StorageCapacityStatus.Unavailable)
                {
                    hasAttention = true; lines.Add("  Server storage: check unavailable");
                }
                else
                {
                    lines.Add($"  Server storage: {(storage.HasAttention ? "ATTENTION" : "ok")}");
                    foreach (var volume in storage.Volumes)
                    {
                        lines.Add($"  {volume.MountPoint}: {StorageCapacityService.FormatBytes(volume.AvailableBytes)} available of {StorageCapacityService.FormatBytes(volume.TotalBytes)}; {StorageCapacityService.FormatBytes(volume.UsedBytes)} used ({volume.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture)}% used)");
                    }
                    lines.Add($"  Low-space threshold: {StorageCapacityService.FormatBytes(StorageCapacityService.LowSpaceThresholdBytes)} available");
                }
            }
            catch { hasAttention = true; lines.Add("  Server storage: check unavailable"); }
        }

        lines.Add("");
        lines.Add("Registration pipeline");
        if (registrationDiagnostic is null)
        {
            hasAttention = true; lines.Add("  Registration pipeline: check unavailable");
        }
        else
        {
            try
            {
                var diagnostic = registrationDiagnostic.Run();
                hasAttention |= diagnostic.HasFailure;
                foreach (var step in diagnostic.Steps)
                {
                    var state = step.State.ToString().ToUpperInvariant();
                    lines.Add(step.State == RegistrationDiagnosticState.Pass
                        ? $"  {step.Name}: PASS"
                        : $"  {step.Name}: {state} — {step.Detail}");
                }
            }
            catch { hasAttention = true; lines.Add("  Registration pipeline: check unavailable"); }
        }

        lines.Add("");
        lines.Add("Registration account overview");
        if (registrationSummary is null)
        {
            hasAttention = true; lines.Add("  Registration account overview: check unavailable");
        }
        else
        {
            try
            {
                var summary = registrationSummary.Build(date);
                hasAttention |= summary.HasAttention || summary.Status == RegistrationSummaryStatus.Unavailable;
                switch (summary.Status)
                {
                    case RegistrationSummaryStatus.Skipped:
                        lines.Add("  Registration account overview: SKIPPED — registration is intentionally unavailable");
                        break;
                    case RegistrationSummaryStatus.Unavailable:
                        lines.Add("  Registration account overview: check unavailable — registration database could not be read");
                        break;
                    default:
                        if (summary.HasAttention)
                            lines.Add("  Registration account overview: ATTENTION");
                        lines.Add($"  Total records: {summary.TotalRecords}");
                        lines.Add($"  Active accounts: {summary.ActiveAccounts}");
                        lines.Add($"  Awaiting confirmation: {summary.AwaitingEmailVerification}");
                        lines.Add($"  Access-code delivery pending: {summary.AccessCodeDeliveryPending}");
                        lines.Add($"  Access-code delivery failed: {summary.AccessCodeDeliveryFailed}");
                        lines.Add($"  Failed registrations: {summary.Failed}");
                        lines.Add($"  Scrubbed records: {summary.Scrubbed}");
                        lines.Add($"  Unknown states: {summary.Unknown}");
                        lines.Add($"  New registrations yesterday: {summary.PreviousDayCreated}");
                        break;
                }
            }
            catch { hasAttention = true; lines.Add("  Registration account overview: check unavailable"); }
        }

        lines.Add(""); lines.Add("Previous-day usage");
        try
        {
            using var db = usage.OpenForCommand();
            using (var query = db.CreateCommand())
            {
                query.CommandText = "SELECT count(*),coalesce(sum(provider_attempts),0),coalesce(sum(known_cost),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage WHERE coalesce(effective_preset,'') <> 'daily-diagnostic' AND started_utc >= $start AND started_utc < $end";
                query.Parameters.AddWithValue("$start", start.ToString("O", CultureInfo.InvariantCulture));
                query.Parameters.AddWithValue("$end", end.ToString("O", CultureInfo.InvariantCulture));
                using var reader = query.ExecuteReader(); reader.Read();
                lines.Add($"  Requests: {reader.GetInt64(0)}");
                lines.Add($"  Provider attempts: {reader.GetInt64(1)}");
                var known = Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture);
                var unresolved = reader.GetInt64(3); var waived = reader.GetInt64(4);
                hasAttention |= unresolved > 0;
                lines.Add(unresolved + waived > 0
                    ? $"  Cost: unknown (known subtotal ${known.ToString("0.0000", CultureInfo.InvariantCulture)}; unresolved {unresolved}, waived unknown {waived})"
                    : $"  Estimated cost: ${known.ToString("0.0000", CultureInfo.InvariantCulture)}");
            }
            using (var query = db.CreateCommand())
            {
                query.CommandText = "SELECT outcome,count(*) FROM execution_usage WHERE coalesce(effective_preset,'') <> 'daily-diagnostic' AND started_utc >= $start AND started_utc < $end GROUP BY outcome";
                query.Parameters.AddWithValue("$start", start.ToString("O", CultureInfo.InvariantCulture));
                query.Parameters.AddWithValue("$end", end.ToString("O", CultureInfo.InvariantCulture));
                using var reader = query.ExecuteReader();
                var counts = new Dictionary<string, long>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    var outcome = reader.IsDBNull(0) ? "unknown" : reader.GetString(0);
                    outcome = Outcomes.Contains(outcome, StringComparer.Ordinal) ? outcome : "unknown";
                    counts[outcome] = counts.GetValueOrDefault(outcome) + reader.GetInt64(1);
                }
                foreach (var outcome in Outcomes.Append("unknown"))
                    if (counts.TryGetValue(outcome, out var count)) lines.Add($"  {outcome}: {count}");
            }
            using (var query = db.CreateCommand())
            {
                query.CommandText = "SELECT max(started_utc) FROM execution_usage WHERE coalesce(effective_preset,'') <> 'daily-diagnostic'";
                var last = query.ExecuteScalar() as string;
                lines.Add("  Last request: " + (DateTimeOffset.TryParse(last, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
                    ? Format(instant) : "none"));
            }
            users = ReadUserActivity(db, start, end);
            AddUserActivity(lines, users);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { hasAttention = true; lines.Add("  Usage database: check unavailable"); }
        if (probe is not null)
        {
            lines.Add(""); lines.Add("Interpretation diagnostic");
            lines.Add($"  Daily public submission result: {probe.State.ToString().ToUpperInvariant()} — {probe.Reason}");
            lines.Add("  Checked: " + Format(probe.CheckedAtUtc));
            lines.Add("  This result applies only to the checked time. Saved runs are reused for that day.");
            lines.Add("  Latest test cost: " + (probe.KnownCost is decimal cost
                ? "$" + cost.ToString("0.0000000", CultureInfo.InvariantCulture)
                    + (probe.UnresolvedCosts + probe.WaivedCosts > 0 ? " known subtotal; total unknown" : " estimated")
                : "unavailable"));
            try
            {
                using var db = usage.OpenForCommand(); using var query = db.CreateCommand();
                query.CommandText = """
                    SELECT coalesce(sum(request_count_eligible),0),coalesce(sum(provider_attempts),0),coalesce(sum(known_cost),0),
                      coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0)
                    FROM execution_usage WHERE effective_preset=$preset AND started_utc >= $start AND started_utc < $end
                    """;
                query.Parameters.AddWithValue("$preset", DailyInterpretationProbe.PresetId);
                query.Parameters.AddWithValue("$start", start.ToString("O", CultureInfo.InvariantCulture));
                query.Parameters.AddWithValue("$end", end.ToString("O", CultureInfo.InvariantCulture));
                using var row = query.ExecuteReader(); row.Read();
                lines.Add($"  Previous-day submissions: {row.GetInt64(0)}");
                lines.Add($"  Previous-day provider attempts: {row.GetInt64(1)}");
                var unresolved = row.GetInt64(3); var waived = row.GetInt64(4);
                hasAttention |= unresolved > 0;
                lines.Add("  Previous-day diagnostic cost: $" + row.GetDecimal(2).ToString("0.0000000", CultureInfo.InvariantCulture)
                    + (unresolved + waived > 0 ? $" known subtotal; total unknown (unresolved {unresolved}, waived unknown {waived})" : " estimated"));
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
            { hasAttention = true; lines.Add("  Diagnostic accounting: check unavailable"); }
        }
        lines.Insert(2, $"Overall status: {(hasAttention ? "ATTENTION" : "OK")}");
        return new(string.Join("\n", lines) + "\n", hasAttention, users);
    }

    string Format(DateTime utc) => Format(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
    string Format(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    public static async Task<int> RunAsync(string[] args, DailyStatusEmail reporter, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is not ("preview" or "send" or "check") || args.Length > 5 ||
            args.Any(value => value is not ("preview" or "send" or "check" or "--live-checks" or "--date" or "--html") && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        { error.WriteLine("Usage: status-email preview|send|check [--live-checks] [--date YYYY-MM-DD] [--html (preview only)]"); return 2; }
        if (args.Contains("--live-checks", StringComparer.Ordinal) && args[0] == "preview")
        { error.WriteLine("Preview runs non-billable checks only."); return 2; }
        if (args.Contains("--html", StringComparer.Ordinal) && args[0] != "preview")
        { error.WriteLine("HTML output is available for preview only."); return 2; }
        var dateIndex = Array.IndexOf(args, "--date");
        if (dateIndex >= 0 && (dateIndex + 1 >= args.Length || !DateOnly.TryParseExact(args[dateIndex + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        { error.WriteLine("Invalid date; use YYYY-MM-DD."); return 2; }
        var currentDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reporter.now(), reporter.zone).DateTime);
        var date = currentDate.AddDays(-1);
        if (dateIndex >= 0) DateOnly.TryParseExact(args[dateIndex + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        var report = await reporter.CreateStatusReportAsync(date, args[0] == "check" || args.Contains("--live-checks", StringComparer.Ordinal));
        if (args[0] is "preview" or "check") { output.Write(args.Contains("--html", StringComparer.Ordinal) ? RenderHtml(report) : report.Text); return args[0] == "check" && report.HasAttention ? 1 : 0; }
        try
        {
            var subject = $"[{(report.HasAttention ? "ATTENTION" : "OK")}] FT-ITC MIST daily status — {date:yyyy-MM-dd}";
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    if (reporter.sender is not null) await reporter.sender(subject, RenderHtml(report));
                    else await reporter.SendSmtpAsync(subject, report);
                    output.WriteLine($"Status email delivered for {date:yyyy-MM-dd}.");
                    return 0;
                }
                catch when (attempt < 3) { await Task.Delay(TimeSpan.FromSeconds(attempt)); }
            }
        }
        catch { /* The final failure is reported below without leaking provider details. */ }
        error.WriteLine($"Status email delivery failed for {date:yyyy-MM-dd}; check mail configuration and provider availability.");
        return 1;
    }

    async Task SendSmtpAsync(string subject, DailyStatusReport report)
    {
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(configurationPath);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new InvalidOperationException("Mail configuration permissions are too broad.");
        }
        var configuration = JsonSerializer.Deserialize<StatusMailConfiguration>(File.ReadAllText(configurationPath))
            ?? throw new InvalidOperationException("Mail configuration is unavailable.");
        if (string.IsNullOrWhiteSpace(configuration.Host) || configuration.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(configuration.Username) || string.IsNullOrWhiteSpace(configuration.Password))
            throw new InvalidOperationException("Mail configuration is incomplete.");
        using var message = CreateMailMessage(subject, report);
        using var client = new SmtpClient(configuration.Host, configuration.Port)
        { EnableSsl = true, Credentials = new NetworkCredential(configuration.Username, configuration.Password), Timeout = 15000 };
        await client.SendMailAsync(message);
    }

    internal static MailMessage CreateMailMessage(string subject, DailyStatusReport report)
    {
        var message = new MailMessage(SenderAddress, RecipientAddress, subject, report.Text)
        { BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        message.ReplyToList.Add(new MailAddress(ReplyToAddress));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            RenderHtml(report), Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Html));
        return message;
    }

    sealed class StatusMailConfiguration
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    internal static async Task<DailyStatusCheck> CheckServiceAsync()
    {
        Process? process = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            process = Process.Start(new ProcessStartInfo("systemctl", "is-active ftitc-web")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (process is null) return new("check unavailable", true);
            var state = (await process.StandardOutput.ReadToEndAsync(timeout.Token)).Trim();
            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode == 0 ? "active" : state is "inactive" or "failed" ? state : "check unavailable", process.ExitCode != 0);
        }
        catch { return new("check unavailable", true); }
        finally
        {
            if (process is { HasExited: false })
                try { process.Kill(entireProcessTree: true); } catch { }
            process?.Dispose();
        }
    }

    // Bound each group by one HTTP timeout rather than multiplying the timeout
    // by its endpoint count within the scheduled service's time budget.
    async Task<(string Label, DailyStatusCheck Check)[]> CheckEndpointsAsync(params (string Label, string Url)[] endpoints) =>
        await Task.WhenAll(endpoints.Select(async endpoint =>
        {
            try { return (endpoint.Label, await endpointCheck(endpoint.Url)); }
            catch { return (endpoint.Label, new DailyStatusCheck("check unavailable", true)); }
        }));

    static async Task<DailyStatusCheck> CheckEndpointAsync(string url)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
        return await CheckEndpointAsync(http, url);
    }

    internal static async Task<DailyStatusCheck> CheckEndpointAsync(HttpClient http, string url)
    {
        try
        {
            var uri = new Uri(url);
            // The website's existing Worker proxies these paths to MIST. A unique
            // query plus revalidation headers prevents a cached status from passing.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                url + (uri.Query.Length == 0 ? "?" : "&") + "mist-status-check=" + Guid.NewGuid().ToString("N"));
            request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new($"HTTP {(int)response.StatusCode}; endpoint check failed", true);
            if (response.Headers.Age is not null || response.Headers.TryGetValues("CF-Cache-Status", out var cacheStates)
                && cacheStates.Any(state => state.Equals("HIT", StringComparison.OrdinalIgnoreCase)
                    || state.Equals("STALE", StringComparison.OrdinalIgnoreCase) || state.Equals("UPDATING", StringComparison.OrdinalIgnoreCase)))
                return new("cached response; current reachability could not be verified", true);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (uri.AbsolutePath == "/api/viewer/token")
            {
                var token = body.RootElement.GetProperty("requestToken");
                return token.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(token.GetString())
                    ? new($"HTTP {(int)response.StatusCode}; viewer token endpoint reachable", false)
                    : new("viewer token response invalid", true);
            }
            var value = body.RootElement.GetProperty("available");
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new("check unavailable", true);
            var available = value.GetBoolean();
            if (uri.AbsolutePath == "/api/registration/status")
                return new($"HTTP {(int)response.StatusCode}; registration endpoint reachable; registration {(available ? "available" : "unavailable")}", false);
            return new($"HTTP {(int)response.StatusCode}; interpretation {(available ? "available" : "unavailable")}",
                !response.IsSuccessStatusCode, !available);
        }
        catch { return new("check unavailable", true); }
    }
}
