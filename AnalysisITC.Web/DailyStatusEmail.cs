using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

/// <summary>A metadata-only daily report, run as a command rather than a web request.</summary>
public sealed class DailyStatusEmail
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
    readonly Func<Task<string>> serviceCheck;
    readonly Func<string, Task<string>> endpointCheck;
    readonly Func<string, string, Task>? sender;
    readonly Func<DateTimeOffset> now;
    readonly HealthCheckService? health;
    readonly OperatorCodeRegistry? operators;
    readonly RegistrationPipelineDiagnostic? registrationDiagnostic;
    readonly RegistrationSummaryService? registrationSummary;
    readonly StorageCapacityService? storageCapacity;

    public DailyStatusEmail(InterpretationUsageStore usage, InterpretationServiceAvailability availability,
        IOptions<InterpretationOptions> options, HealthCheckService health, OperatorCodeRegistry operators,
        RegistrationPipelineDiagnostic registrationDiagnostic, RegistrationSummaryService registrationSummary,
        StorageCapacityService storageCapacity)
        : this(usage, availability, options.Value.StatusEmailConfigurationPath,
            CheckServiceAsync, CheckEndpointAsync, null, () => DateTimeOffset.UtcNow, health, operators,
            registrationDiagnostic, registrationSummary, storageCapacity) { }

    internal DailyStatusEmail(InterpretationUsageStore usage, InterpretationServiceAvailability availability,
        string configurationPath, Func<Task<string>> serviceCheck, Func<string, Task<string>> endpointCheck,
        Func<string, string, Task>? sender, Func<DateTimeOffset> now, HealthCheckService? health = null,
        OperatorCodeRegistry? operators = null, RegistrationPipelineDiagnostic? registrationDiagnostic = null,
        RegistrationSummaryService? registrationSummary = null, StorageCapacityService? storageCapacity = null)
    {
        this.usage = usage; this.availability = availability; this.configurationPath = configurationPath;
        this.serviceCheck = serviceCheck; this.endpointCheck = endpointCheck; this.sender = sender; this.now = now; this.health = health;
        this.operators = operators; this.registrationDiagnostic = registrationDiagnostic; this.registrationSummary = registrationSummary;
        this.storageCapacity = storageCapacity;
    }

    internal static (DateTime StartUtc, DateTime EndUtc) Bounds(DateOnly date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        var start = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (TimeZoneInfo.ConvertTimeToUtc(start, zone), TimeZoneInfo.ConvertTimeToUtc(start.AddDays(1), zone));
    }

    public async Task<string> CreateReportAsync(DateOnly date)
    {
        var (start, end) = Bounds(date);
        var lines = new List<string>
        {
            $"FT-ITC MIST daily status — {date:yyyy-MM-dd}",
            $"Period: {Format(start)} to {Format(end)} (end exclusive)",
            "",
            "Service",
        };
        try
        {
            var report = health?.RunNonBillable();
            if (report is not null)
            {
                lines.Add(""); lines.Add("Health");
                foreach (var group in report.Groups)
                {
                    var attention = group.Checks.Where(check => check.State is HealthCheckState.Warning or HealthCheckState.Fail).ToArray();
                    lines.Add($"  {group.Name}: {(attention.Length == 0 ? "ok" : string.Join("; ", attention.Select(check => $"{check.Name}: {check.Reason}")))}");
                }
            }
        }
        catch { lines.Add("\nHealth\n  unavailable: check could not be completed"); }
        try { lines.Add("  ftitc-web: " + await serviceCheck()); }
        catch { lines.Add("  ftitc-web: check unavailable"); }
        try { lines.Add("  Interpretation policy: " + availability.Read().Status); }
        catch { lines.Add("  Interpretation policy: check unavailable"); }
        foreach (var (label, url) in new[] { ("Local API", LocalUrl), ("Public API", PublicUrl) })
        {
            try { lines.Add($"  {label}: {await endpointCheck(url)}"); }
            catch { lines.Add($"  {label}: check unavailable"); }
        }

        lines.Add("");
        lines.Add("Server storage");
        if (storageCapacity is null)
        {
            lines.Add("  Server storage: check unavailable");
        }
        else
        {
            try
            {
                var storage = storageCapacity.Read();
                if (storage.Status == StorageCapacityStatus.Unavailable)
                {
                    lines.Add("  Server storage: check unavailable");
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
            catch { lines.Add("  Server storage: check unavailable"); }
        }

        lines.Add("");
        lines.Add("Registration pipeline dry-run");
        if (registrationDiagnostic is null)
        {
            lines.Add("  Registration pipeline: check unavailable");
        }
        else
        {
            try
            {
                var diagnostic = registrationDiagnostic.Run();
                lines.Add($"  Registration pipeline: {(diagnostic.HasFailure ? "ATTENTION" : "ok")}");
                foreach (var step in diagnostic.Steps)
                    lines.Add($"  Step {step.Number} — {step.Name}: {step.State.ToString().ToUpperInvariant()} — {step.Detail}");
            }
            catch { lines.Add("  Registration pipeline: check unavailable"); }
        }

        lines.Add("");
        lines.Add("Registration summary");
        if (registrationSummary is null)
        {
            lines.Add("  Registration summary: check unavailable");
        }
        else
        {
            try
            {
                var summary = registrationSummary.Build(date);
                switch (summary.Status)
                {
                    case RegistrationSummaryStatus.Skipped:
                        lines.Add("  Registration summary: SKIPPED — registration is intentionally unavailable");
                        break;
                    case RegistrationSummaryStatus.Unavailable:
                        lines.Add("  Registration summary: check unavailable — registration database could not be read");
                        break;
                    default:
                        lines.Add($"  Registration summary: {(summary.HasAttention ? "ATTENTION" : "ok")}");
                        lines.Add($"  Total registration records: {summary.TotalRecords}");
                        lines.Add($"  Active Registered accounts: {summary.ActiveAccounts}");
                        lines.Add($"  Email verified (current state estimate): {summary.EmailVerified}");
                        lines.Add($"  Awaiting email verification: {summary.AwaitingEmailVerification}");
                        lines.Add($"  Access-code delivery pending: {summary.AccessCodeDeliveryPending}");
                        lines.Add($"  Failed: {summary.Failed}");
                        lines.Add($"  Scrubbed: {summary.Scrubbed}");
                        lines.Add($"  Unknown/unrecognized states: {summary.Unknown}");
                        lines.Add($"  Created previous day: {summary.PreviousDayCreated}");
                        foreach (var state in summary.PreviousDayByState
                                     .Where(pair => RegistrationSummaryReport.KnownStates.Contains(pair.Key))
                                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                            lines.Add($"    Created previous day — {state.Key}: {state.Value}");
                        if (summary.PreviousDayByState.TryGetValue("(unknown)", out var unknownPreviousDay))
                            lines.Add($"    Created previous day — unknown/unrecognized: {unknownPreviousDay}");
                        lines.Add("  Verification is inferred from current lifecycle state; no verification timestamp is recorded.");
                        break;
                }
            }
            catch { lines.Add("  Registration summary: check unavailable"); }
        }

        lines.Add(""); lines.Add("Previous-day usage");
        try
        {
            using var db = usage.OpenForCommand();
            using (var query = db.CreateCommand())
            {
                query.CommandText = "SELECT count(*),coalesce(sum(provider_attempts),0),coalesce(sum(known_cost),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage WHERE started_utc >= $start AND started_utc < $end";
                query.Parameters.AddWithValue("$start", start.ToString("O", CultureInfo.InvariantCulture));
                query.Parameters.AddWithValue("$end", end.ToString("O", CultureInfo.InvariantCulture));
                using var reader = query.ExecuteReader(); reader.Read();
                lines.Add($"  Requests: {reader.GetInt64(0)}");
                lines.Add($"  Provider attempts: {reader.GetInt64(1)}");
                var known = Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture);
                var unresolved = reader.GetInt64(3); var waived = reader.GetInt64(4);
                lines.Add(unresolved + waived > 0
                    ? $"  Cost: unknown (known subtotal ${known.ToString("0.0000", CultureInfo.InvariantCulture)}; unresolved {unresolved}, waived unknown {waived})"
                    : $"  Estimated cost: ${known.ToString("0.0000", CultureInfo.InvariantCulture)}");
            }
            using (var query = db.CreateCommand())
            {
                query.CommandText = "SELECT outcome,count(*) FROM execution_usage WHERE started_utc >= $start AND started_utc < $end GROUP BY outcome";
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
                query.CommandText = "SELECT max(started_utc) FROM execution_usage";
                var last = query.ExecuteScalar() as string;
                lines.Add("  Last request: " + (DateTimeOffset.TryParse(last, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
                    ? Format(instant) : "none"));
            }
            AddUserActivity(lines, db, start, end);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { lines.Add("  Usage database: check unavailable"); }
        return string.Join("\n", lines) + "\n";
    }

    void AddUserActivity(List<string> lines, SqliteConnection db, DateTime start, DateTime end)
    {
        var accounts = new Dictionary<string, OperatorCodeRecord>(StringComparer.Ordinal);
        try
        {
            if (operators is not null)
                accounts = operators.List().ToDictionary(record => record.Id, StringComparer.Ordinal);
        }
        catch
        {
            // Usage remains reportable even when identity metadata is unavailable.
        }

        lines.Add("");
        lines.Add("User activity");
        using var query = db.CreateCommand();
        query.CommandText = """
            WITH period AS (
              SELECT operator_code_id,coalesce(max(access_tier),'public') AS access_tier,count(*) AS prompts,
                     coalesce(sum(known_cost),0) AS known_cost,coalesce(sum(unresolved_cost_count),0) AS unresolved,
                     coalesce(sum(waived_unknown_count),0) AS waived
              FROM execution_usage WHERE started_utc >= $start AND started_utc < $end
              GROUP BY operator_code_id
            ), totals AS (
              SELECT operator_code_id,count(*) AS prompts,coalesce(sum(known_cost),0) AS known_cost,
                     coalesce(sum(unresolved_cost_count),0) AS unresolved,coalesce(sum(waived_unknown_count),0) AS waived
              FROM execution_usage GROUP BY operator_code_id
            )
            SELECT period.operator_code_id,period.access_tier,period.prompts,period.known_cost,period.unresolved,period.waived,
                   totals.prompts,totals.known_cost,totals.unresolved,totals.waived
            FROM period JOIN totals ON period.operator_code_id IS totals.operator_code_id
            ORDER BY period.prompts DESC,period.operator_code_id
            """;
        query.Parameters.AddWithValue("$start", start.ToString("O", CultureInfo.InvariantCulture));
        query.Parameters.AddWithValue("$end", end.ToString("O", CultureInfo.InvariantCulture));
        using var reader = query.ExecuteReader();
        var any = false;
        while (reader.Read())
        {
            any = true;
            var id = reader.IsDBNull(0) ? null : reader.GetString(0);
            var tier = reader.IsDBNull(1) ? "unknown" : reader.GetString(1);
            var label = id is not null && accounts.TryGetValue(id, out var account)
                ? account.Name ?? account.Label
                : id is null ? "Unattributed public activity" : tier == InterpretationAccessTiers.Public ? "Public installation" : "Unavailable account";
            lines.Add(id is null ? $"  {label}" : $"  {label} — {id} ({tier})");
            lines.Add($"    Previous day: {reader.GetInt64(2)} prompt(s); {FormatCost(reader, 3, 4, 5)}");
            lines.Add($"    All time: {reader.GetInt64(6)} prompt(s); {FormatCost(reader, 7, 8, 9)}");
        }
        if (!any) lines.Add("  No user activity.");
    }

    static string FormatCost(SqliteDataReader reader, int knownIndex, int unresolvedIndex, int waivedIndex)
    {
        var known = Convert.ToDecimal(reader.GetValue(knownIndex), CultureInfo.InvariantCulture);
        var unresolved = reader.GetInt64(unresolvedIndex);
        var waived = reader.GetInt64(waivedIndex);
        return unresolved + waived > 0
            ? $"cost unknown (known subtotal ${known.ToString("0.0000", CultureInfo.InvariantCulture)}; unresolved {unresolved}, waived unknown {waived})"
            : $"estimated cost ${known.ToString("0.0000", CultureInfo.InvariantCulture)}";
    }

    string Format(DateTime utc) => Format(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
    string Format(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    public static async Task<int> RunAsync(string[] args, DailyStatusEmail reporter, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is not ("preview" or "send") || args.Length > 4 ||
            args.Any(value => value is not ("preview" or "send" or "--live-checks" or "--date") && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        { error.WriteLine("Usage: status-email preview|send [--live-checks] [--date YYYY-MM-DD]"); return 2; }
        if (args.Contains("--live-checks", StringComparer.Ordinal) && args[0] == "preview")
        { error.WriteLine("Preview runs non-billable checks only."); return 2; }
        var dateIndex = Array.IndexOf(args, "--date");
        if (dateIndex >= 0 && (dateIndex + 1 >= args.Length || !DateOnly.TryParseExact(args[dateIndex + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        { error.WriteLine("Invalid date; use YYYY-MM-DD."); return 2; }
        var currentDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reporter.now(), reporter.zone).DateTime);
        var date = currentDate.AddDays(-1);
        if (dateIndex >= 0) DateOnly.TryParseExact(args[dateIndex + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        var report = await reporter.CreateReportAsync(date);
        if (args[0] == "preview") { output.Write(report); return 0; }
        try
        {
            var subject = $"[{(report.Contains("unavailable:", StringComparison.Ordinal) || report.Contains("check unavailable", StringComparison.Ordinal) || report.Contains(": access was", StringComparison.Ordinal) || report.Contains(": file is unavailable", StringComparison.Ordinal) || report.Contains(": database", StringComparison.Ordinal) || report.Contains("Server storage: ATTENTION", StringComparison.Ordinal) || report.Contains("Registration pipeline: ATTENTION", StringComparison.Ordinal) || report.Contains("Registration summary: ATTENTION", StringComparison.Ordinal) ? "ATTENTION" : "OK")}] FT-ITC MIST daily status — {date:yyyy-MM-dd}";
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    if (reporter.sender is not null) await reporter.sender(subject, report);
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

    async Task SendSmtpAsync(string subject, string report)
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
        using var message = new MailMessage(SenderAddress, RecipientAddress, subject, report)
        { BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8, IsBodyHtml = false };
        message.ReplyToList.Add(new MailAddress(ReplyToAddress));
        using var client = new SmtpClient(configuration.Host, configuration.Port)
        { EnableSsl = true, Credentials = new NetworkCredential(configuration.Username, configuration.Password), Timeout = 15000 };
        await client.SendMailAsync(message);
    }

    sealed class StatusMailConfiguration
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    static async Task<string> CheckServiceAsync()
    {
        Process? process = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            process = Process.Start(new ProcessStartInfo("systemctl", "is-active ftitc-web")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (process is null) return "check unavailable";
            var state = (await process.StandardOutput.ReadToEndAsync(timeout.Token)).Trim();
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? "active" : state is "inactive" or "failed" ? state : "check unavailable";
        }
        catch { return "check unavailable"; }
        finally
        {
            if (process is { HasExited: false })
                try { process.Kill(entireProcessTree: true); } catch { }
            process?.Dispose();
        }
    }

    static async Task<string> CheckEndpointAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await http.GetAsync(url);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var available = body.RootElement.TryGetProperty("available", out var value) && value.ValueKind == JsonValueKind.True;
            return $"HTTP {(int)response.StatusCode}; interpretation {(available ? "available" : "unavailable")}";
        }
        catch { return "check unavailable"; }
    }
}
