using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalysisITC.Core.Interpretation;
using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

internal sealed record DailyInterpretationProbeResult(DateOnly Day, DateTimeOffset CheckedAtUtc,
    HealthCheckState State, string Reason, string? ServerExecutionId = null,
    int? ProviderAttempts = null, decimal? KnownCost = null, int UnresolvedCosts = 0, int WaivedCosts = 0);

/// <summary>One real, invented-evidence submission per Copenhagen day. Never stores generated text or credentials.</summary>
internal sealed class DailyInterpretationProbe
{
    internal const string PresetId = "daily-diagnostic";
    internal const int MaximumOutputTokens = 256;
    internal const int MaximumRequestBytes = 4096;
    internal const string RequestHeader = "X-FTITC-Daily-Probe";
    internal const string Endpoint = "https://app.ft-itc.org/api/interpretation/generate";
    static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    readonly DailyInterpretationProbeOptions options;
    readonly InterpretationServiceAvailability availability;
    readonly InterpretationUsageStore usage;
    readonly HttpClient? client;

    internal DailyInterpretationProbe(InterpretationOptions options, InterpretationServiceAvailability availability,
        InterpretationUsageStore usage, HttpClient? client = null)
    { this.options = options.DailyProbe; this.availability = availability; this.usage = usage; this.client = client; }

    internal static DateOnly Day(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);

    internal static string RequestId(string accountId, DateTimeOffset now) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ftitc-daily-interpretation:" + accountId + ":"
            + Day(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))))[..32].ToLowerInvariant();

    internal async Task<DailyInterpretationProbeResult> RunAsync(bool submit, DateTimeOffset now)
    {
        var day = Day(now);
        DailyInterpretationProbeResult Result(HealthCheckState state, string reason) => new(day, now, state, reason);
        if (!options.Enabled) return Result(HealthCheckState.Skipped, "diagnostic is not enabled");
        var path = Path.Combine(options.StateDirectory, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");
        if (File.Exists(path)) return Read(path, day, now);
        var policy = availability.Read();
        if (policy.PolicyValid && policy.Status is "paused" or "retired")
            return Result(HealthCheckState.Skipped, "interpretation is intentionally " + policy.Status);
        if (!submit) return Result(HealthCheckState.Skipped, "not run today; live checks are disabled for this command");
        if (string.IsNullOrWhiteSpace(options.OperatorCodeId)) return Result(HealthCheckState.Fail, "diagnostic account is not configured");

        // CreateNew is the cross-process claim, including failures and interrupted
        // runs. Save it durably before HTTP; never automatically resubmit this day.
        var initial = Result(HealthCheckState.Warning, "submission started; no completed result is available");
        try
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(options.StateDirectory);
            else Directory.CreateDirectory(options.StateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var claim = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(initial, Json);
            claim.Write(bytes); claim.Flush(flushToDisk: true);
        }
        catch (IOException) when (File.Exists(path)) { return Read(path, day, now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Result(HealthCheckState.Fail, "daily diagnostic state could not be saved; submission was not sent"); }

        var outcome = initial;
        try
        {
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(options.CredentialPath)
                & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new UnauthorizedAccessException();
            using var credential = JsonDocument.Parse(File.ReadAllText(options.CredentialPath));
            var code = credential.RootElement.GetProperty("accessCode").GetString();
            if (code?.StartsWith("ftitc_op_", StringComparison.Ordinal) != true) throw new InvalidDataException();
            using var ownedClient = client is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = TimeSpan.FromSeconds(90) } : null;
            var http = client ?? ownedClient!;
            var requestId = RequestId(options.OperatorCodeId, now);
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", code);
            request.Headers.Add(RequestHeader, "1");
            request.Content = new StringContent(BuildRequest(requestId, now).ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            var execution = response.Headers.TryGetValues("X-FTITC-Server-Execution-Id", out var ids) ? ids.FirstOrDefault() : null;
            var accounting = ReadAccounting(execution, day, now);
            outcome = accounting with
            {
                State = HealthCheckState.Fail,
                Reason = "public submission returned HTTP " + (int)response.StatusCode,
            };
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > FtItcInterpretationClient.MaximumResponseBytes) throw new InvalidDataException();
                var body = JsonSerializer.Deserialize<InterpretationRelayResponse>(bytes, Json);
                var valid = body is not null && body.ResponseSchemaVersion == FtItcInterpretationClient.ResponseSchemaVersion
                    && body.TaskType == "interpretation" && body.RequestId == requestId && body.EffectivePreset == PresetId
                    && !string.IsNullOrWhiteSpace(body.InterpretationMarkdown)
                    && body.KnowledgeBaseIds is { Count: 0 } && body.RetrievedSourceIds is { Count: 0 }
                    && !string.IsNullOrWhiteSpace(body.ScientificGuidanceRevision)
                    && body.EffectiveInputFingerprint?.Length == 64 && body.ScientificInstructionsFingerprint?.Length == 64
                    && body.OutputInstructionsFingerprint?.Length == 64;
                outcome = outcome with
                {
                    State = valid && accounting.State == HealthCheckState.Pass && outcome.ProviderAttempts == 1 && outcome.KnownCost is not null
                        && outcome.UnresolvedCosts == 0 && outcome.WaivedCosts == 0 ? HealthCheckState.Pass : HealthCheckState.Fail,
                    Reason = valid ? "public response and accounting checked; retrieval disabled"
                        : "public response did not match the diagnostic contract",
                };
                if (valid && outcome.State == HealthCheckState.Fail)
                    outcome = outcome with { Reason = "response received, but complete single-attempt accounting could not be verified" };
            }
        }
        catch (OperationCanceledException) { outcome = outcome with { State = HealthCheckState.Fail, Reason = "public submission timed out; no automatic retry" }; }
        catch (HttpRequestException) { outcome = outcome with { State = HealthCheckState.Fail, Reason = "public submission could not be completed; no automatic retry" }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or SqliteException)
        { outcome = outcome with { State = HealthCheckState.Fail, Reason = "diagnostic credentials, response or accounting could not be verified" }; }

        try { Save(path, outcome); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return outcome with { State = HealthCheckState.Fail, Reason = "diagnostic result could not be saved; submission will not be repeated today" }; }
        return outcome;
    }

    DailyInterpretationProbeResult ReadAccounting(string? execution, DateOnly day, DateTimeOffset now)
    {
        var result = new DailyInterpretationProbeResult(day, now, HealthCheckState.Fail, "accounting is unavailable", execution);
        if (string.IsNullOrWhiteSpace(execution)) return result;
        using var db = usage.OpenForCommand();
        using var query = db.CreateCommand();
        query.CommandText = """
            SELECT provider_attempts,known_cost,unresolved_cost_count,waived_unknown_count,outcome,http_status,
              (SELECT count(*) FROM attempt_receipts a WHERE a.execution_id=u.request_id
                AND (a.file_search_enabled<>0 OR coalesce(a.file_search_calls,0)<>0))
            FROM execution_usage u WHERE request_id=$execution AND operator_code_id=$account AND effective_preset=$preset
            """;
        query.Parameters.AddWithValue("$execution", execution);
        query.Parameters.AddWithValue("$account", options.OperatorCodeId);
        query.Parameters.AddWithValue("$preset", PresetId);
        using var row = query.ExecuteReader();
        if (!row.Read()) return result;
        return result with
        {
            ProviderAttempts = row.GetInt32(0), KnownCost = row.GetDecimal(1),
            UnresolvedCosts = row.GetInt32(2), WaivedCosts = row.GetInt32(3),
            // A 200 response is insufficient if finalization failed or retrieval ran.
            State = !row.IsDBNull(4) && row.GetString(4) == "success" && !row.IsDBNull(5) && row.GetInt32(5) == 200
                && row.GetInt32(6) == 0 ? HealthCheckState.Pass : HealthCheckState.Fail,
        };
    }

    static DailyInterpretationProbeResult Read(string path, DateOnly day, DateTimeOffset now)
    {
        try
        {
            var result = JsonSerializer.Deserialize<DailyInterpretationProbeResult>(File.ReadAllText(path), Json);
            if (result is not null && result.Day == day && Enum.IsDefined(result.State)) return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new(day, now, HealthCheckState.Fail, "saved diagnostic result is unavailable; submission will not be repeated today");
    }

    static void Save(string path, DailyInterpretationProbeResult result)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(result, Json)); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static JsonObject BuildRequest(string requestId, DateTimeOffset now)
    {
        using var stream = typeof(DailyInterpretationProbe).Assembly.GetManifestResourceStream("AnalysisITC.Web.Diagnostics.daily-interpretation-probe.json")!;
        var request = JsonNode.Parse(stream)!.AsObject();
        request["clientRequestId"] = requestId;
        request["package"]!["report"]!["dateUtc"] = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        return request;
    }
}
