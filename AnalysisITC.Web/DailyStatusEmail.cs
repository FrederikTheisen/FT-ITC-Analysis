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

    public DailyStatusEmail(InterpretationUsageStore usage, InterpretationServiceAvailability availability,
        IOptions<InterpretationOptions> options)
        : this(usage, availability, options.Value.StatusEmailConfigurationPath,
            CheckServiceAsync, CheckEndpointAsync, null, () => DateTimeOffset.UtcNow) { }

    internal DailyStatusEmail(InterpretationUsageStore usage, InterpretationServiceAvailability availability,
        string configurationPath, Func<Task<string>> serviceCheck, Func<string, Task<string>> endpointCheck,
        Func<string, string, Task>? sender, Func<DateTimeOffset> now)
    {
        this.usage = usage; this.availability = availability; this.configurationPath = configurationPath;
        this.serviceCheck = serviceCheck; this.endpointCheck = endpointCheck; this.sender = sender; this.now = now;
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
        try { lines.Add("  ftitc-web: " + await serviceCheck()); }
        catch { lines.Add("  ftitc-web: check unavailable"); }
        try { lines.Add("  Interpretation policy: " + availability.Read().Status); }
        catch { lines.Add("  Interpretation policy: check unavailable"); }
        foreach (var (label, url) in new[] { ("Local API", LocalUrl), ("Public API", PublicUrl) })
        {
            try { lines.Add($"  {label}: {await endpointCheck(url)}"); }
            catch { lines.Add($"  {label}: check unavailable"); }
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
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { lines.Add("  Usage database: check unavailable"); }
        return string.Join("\n", lines) + "\n";
    }

    string Format(DateTime utc) => Format(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
    string Format(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    public static async Task<int> RunAsync(string[] args, DailyStatusEmail reporter, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is not ("preview" or "send") || args.Length > 3 ||
            args.Length == 3 && args[1] != "--date")
        { error.WriteLine("Usage: status-email preview|send [--date YYYY-MM-DD]"); return 2; }
        var currentDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reporter.now(), reporter.zone).DateTime);
        var date = currentDate.AddDays(-1);
        if (args.Length == 3 && !DateOnly.TryParseExact(args[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        { error.WriteLine("Invalid date; use YYYY-MM-DD."); return 2; }
        var report = await reporter.CreateReportAsync(date);
        if (args[0] == "preview") { output.Write(report); return 0; }
        try
        {
            var subject = $"FT-ITC MIST daily status — {date:yyyy-MM-dd}";
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
