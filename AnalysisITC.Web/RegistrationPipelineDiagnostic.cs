using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public enum RegistrationDiagnosticState
{
    Pass,
    Skipped,
    Fail,
}

public sealed record RegistrationDiagnosticStep(int Number, string Name, RegistrationDiagnosticState State, string Detail);

public sealed record RegistrationDiagnosticReport(IReadOnlyList<RegistrationDiagnosticStep> Steps)
{
    public bool HasFailure => Steps.Any(step => step.State == RegistrationDiagnosticState.Fail);
}

/// <summary>
/// Runs a side-effect-free registration pipeline check. It deliberately does not invoke
/// the HTTP endpoint, Turnstile, Resend, registration storage, or operator-code storage.
/// </summary>
public sealed class RegistrationPipelineDiagnostic
{
    const int MaximumRegistrationRequestBytes = 16 * 1024;
    const string ActivationPrefix = "ftitc_act_";
    const string OperatorPrefix = "ftitc_op_";
    static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    readonly RegistrationOptions options;
    readonly string usageDatabasePath;
    readonly RegistrationAvailability registrationAvailability;
    readonly InterpretationServiceAvailability interpretationAvailability;

    public RegistrationPipelineDiagnostic(IOptions<InterpretationOptions> configured,
        RegistrationAvailability registrationAvailability,
        InterpretationServiceAvailability interpretationAvailability)
        : this(configured.Value.Registration, configured.Value.UsageLog.DatabasePath, registrationAvailability, interpretationAvailability) { }

    internal RegistrationPipelineDiagnostic(RegistrationOptions options,
        string usageDatabasePath,
        RegistrationAvailability registrationAvailability,
        InterpretationServiceAvailability interpretationAvailability)
    {
        this.options = options;
        this.usageDatabasePath = usageDatabasePath;
        this.registrationAvailability = registrationAvailability;
        this.interpretationAvailability = interpretationAvailability;
    }

    public RegistrationDiagnosticReport Run()
    {
        var state = ReadAvailability();
        var before = CapturePersistence();
        var steps = new List<RegistrationDiagnosticStep>
        {
            RunStep(1, "Submission boundary", () => SubmissionBoundary(state)),
            RunStep(2, "Request and security validation", () => RequestAndSecurity(state)),
            RunStep(3, "Account and code generation", () => AccountAndCodeGeneration(state)),
            RunStep(4, "Email preparation", () => EmailPreparation(state)),
        };

        steps.Add(RunStep(5, "Persistence guard", () => PersistenceGuard(before, CapturePersistence())));
        return new(steps);
    }

    AvailabilityState ReadAvailability()
    {
        try
        {
            var policy = registrationAvailability.Read();
            var interpretation = interpretationAvailability.Read();
            var enabled = options.Enabled && policy.Enabled && interpretation.Status != "retired";
            var reason = !options.Enabled
                ? "registration is disabled in application configuration"
                : !policy.Enabled
                    ? policy.Message is null ? "registration is disabled by policy" : policy.Message
                    : interpretation.Status == "retired"
                        ? "registration is unavailable because interpretation is retired"
                        : null;
            return new(enabled, reason);
        }
        catch
        {
            return new(false, "registration availability could not be read");
        }
    }

    RegistrationDiagnosticStep SubmissionBoundary(AvailabilityState state)
    {
        if (!state.Enabled) return Skipped("registration is intentionally unavailable: " + state.Reason);
        if (string.IsNullOrWhiteSpace(options.SiteKey)) return Fail("Turnstile site-key configuration is missing");
        if (string.IsNullOrWhiteSpace(options.TermsVersion) || string.IsNullOrWhiteSpace(options.PrivacyVersion))
            return Fail("terms and privacy versions are not configured");

        var request = SyntheticRequest();
        var json = JsonSerializer.Serialize(request, WebJson);
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > MaximumRegistrationRequestBytes)
            return Fail("synthetic JSON request exceeds the registration size limit");
        try
        {
            var parsed = JsonSerializer.Deserialize<RegistrationRequest>(json, WebJson);
            return parsed is null
                ? Fail("synthetic JSON request could not be parsed")
                : Pass("synthetic JSON request serialized and parsed with the live web contract; endpoint write was not invoked");
        }
        catch (JsonException) { return Fail("synthetic JSON request could not be parsed"); }
    }

    RegistrationDiagnosticStep RequestAndSecurity(AvailabilityState state)
    {
        if (!state.Enabled) return Skipped("registration is intentionally unavailable: " + state.Reason);
        var request = SyntheticRequest();
        if (!RegistrationRequestValidator.TryValidate(request, options, out var reason))
            return Fail("shared request validation rejected the synthetic request: " + reason);
        if (options.SubmissionPermitLimit <= 0 || options.SubmissionWindowSeconds <= 0
            || options.ActivationPermitLimit <= 0 || options.ActivationWindowSeconds <= 0)
            return Fail("registration rate-limit settings are not positive");

        var turnstile = ReadJson<TurnstileConfiguration>(options.SecretConfigurationPath);
        if (turnstile is null || string.IsNullOrWhiteSpace(turnstile.SecretKey)
            || string.IsNullOrWhiteSpace(turnstile.SiteKey)
            || !string.Equals(turnstile.SiteKey, options.SiteKey, StringComparison.Ordinal))
            return Fail("Turnstile secret configuration is missing or malformed");
        return Pass("shared validation, consent, versions, rate limits, Turnstile configuration, and ft-itc.org hostname policy passed; live token verification was not attempted");
    }

    RegistrationDiagnosticStep AccountAndCodeGeneration(AvailabilityState state)
    {
        if (!state.Enabled) return Skipped("registration is intentionally unavailable: " + state.Reason);
        var activation = ActivationPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var operatorCode = OperatorPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        if (!IsTokenShape(activation, ActivationPrefix) || !IsTokenShape(operatorCode, OperatorPrefix))
            return Fail("ephemeral registration credential shape validation failed");

        var protector = new EphemeralDataProtectionProvider().CreateProtector("FT-ITC.PublicRegistration.Diagnostic.v1");
        var protectedValue = protector.Protect(activation);
        if (!string.Equals(protector.Unprotect(protectedValue), activation, StringComparison.Ordinal))
            return Fail("ephemeral data-protection round trip failed");

        var registryDirectory = Path.GetDirectoryName(options.OperatorRegistryPath);
        if (string.IsNullOrWhiteSpace(registryDirectory) || !Directory.Exists(registryDirectory))
            return Fail("registered-account registry directory is unavailable");
        return Pass("ephemeral account identifiers, credential shapes, and data protection passed; registry was not written");
    }

    RegistrationDiagnosticStep EmailPreparation(AvailabilityState state)
    {
        if (!state.Enabled) return Skipped("registration is intentionally unavailable: " + state.Reason);
        var configuration = ReadJson<RegistrationMailConfiguration>(options.MailConfigurationPath);
        if (configuration is null || string.IsNullOrWhiteSpace(configuration.ApiKey))
            return Fail("registration mail configuration is missing or malformed");
        try
        {
            _ = new MailAddress(configuration.From);
            _ = new MailAddress(configuration.ReplyTo);
            if (!string.IsNullOrWhiteSpace(configuration.ActivationTemplateId)
                && string.IsNullOrWhiteSpace(configuration.ActivationTemplateId.Trim()))
                return Fail("activation mail template configuration is invalid");
            if (!string.IsNullOrWhiteSpace(configuration.AccessCodeTemplateId)
                && string.IsNullOrWhiteSpace(configuration.AccessCodeTemplateId.Trim()))
                return Fail("access-code mail template configuration is invalid");

            var activation = RegistrationMailSender.RenderDiagnosticMessage(options, configuration, RegistrationMessageKinds.Activation);
            var accessCode = RegistrationMailSender.RenderDiagnosticMessage(options, configuration, RegistrationMessageKinds.AccessCode);
            if (JsonSerializer.Serialize(activation).Length == 0 || JsonSerializer.Serialize(accessCode).Length == 0)
                return Fail("registration mail messages could not be rendered");
            return Pass("activation and access-code messages rendered in memory; no message was sent and Resend was not contacted");
        }
        catch (FormatException) { return Fail("registration mail sender or reply-to address is invalid"); }
        catch (JsonException) { return Fail("registration mail message could not be rendered"); }
    }

    RegistrationDiagnosticStep PersistenceGuard(PersistenceSnapshot before, PersistenceSnapshot after)
    {
        if (!before.Readable || !after.Readable) return Fail("persistence state could not be inspected safely");
        if (!before.EquivalentTo(after)) return Fail("diagnostic persistence state changed unexpectedly");
        return Pass("account, delivery, registry, and usage persistence remained unchanged");
    }

    RegistrationRequest SyntheticRequest() => new()
    {
        Name = "FT-ITC diagnostic",
        Email = "registration-diagnostic@example.invalid",
        Organisation = "FT-ITC diagnostic",
        TermsVersion = options.TermsVersion,
        PrivacyVersion = options.PrivacyVersion,
        AcceptedTerms = true,
        AcknowledgedPrivacy = true,
        TurnstileToken = "diagnostic-token-not-submitted",
    };

    PersistenceSnapshot CapturePersistence()
    {
        var paths = new[]
        {
            options.DatabasePath,
            options.DatabasePath + "-wal",
            options.DatabasePath + "-shm",
            usageDatabasePath,
            usageDatabasePath + "-wal",
            usageDatabasePath + "-shm",
            options.OperatorRegistryPath,
            options.OperatorRegistryPath + ".tmp",
        };
        var values = new Dictionary<string, FileStamp?>(StringComparer.Ordinal);
        var readable = true;
        foreach (var path in paths)
        {
            try { values[path] = FileStamp.Read(path); }
            catch { readable = false; values[path] = null; }
        }
        var counts = new Dictionary<string, long?>(StringComparer.Ordinal);
        foreach (var database in new[] { options.DatabasePath, usageDatabasePath }.Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(database)) continue;
            try
            {
                foreach (var table in new[] { "registration_accounts", "registration_delivery", "execution_usage", "requests" })
                {
                    var count = ReadTableCount(database, table);
                    if (count is not null) counts[$"{database}:{table}"] = count;
                }
            }
            catch (SqliteException) { readable = false; }
            catch (IOException) { readable = false; }
            catch (UnauthorizedAccessException) { readable = false; }
        }
        return new(values, counts, readable);
    }

    static long? ReadTableCount(string path, string table)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        db.Open();
        using (var exists = db.CreateCommand())
        {
            exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$name";
            exists.Parameters.AddWithValue("$name", table);
            if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) return null;
        }
        using var count = db.CreateCommand();
        count.CommandText = table switch
        {
            "registration_accounts" => "SELECT count(*) FROM registration_accounts",
            "registration_delivery" => "SELECT count(*) FROM registration_delivery",
            "execution_usage" => "SELECT count(*) FROM execution_usage",
            "requests" => "SELECT count(*) FROM requests",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, WebJson);
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static RegistrationDiagnosticStep RunStep(int number, string name, Func<RegistrationDiagnosticStep> action)
    {
        try { return action() with { Number = number, Name = name }; }
        catch { return new(number, name, RegistrationDiagnosticState.Fail, "check could not be completed"); }
    }

    static RegistrationDiagnosticStep Pass(string detail) => new(0, "", RegistrationDiagnosticState.Pass, detail);
    static RegistrationDiagnosticStep Skipped(string detail) => new(0, "", RegistrationDiagnosticState.Skipped, detail);
    static RegistrationDiagnosticStep Fail(string detail) => new(0, "", RegistrationDiagnosticState.Fail, detail);

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static bool IsTokenShape(string value, string prefix) => value.StartsWith(prefix, StringComparison.Ordinal)
        && value.Length == prefix.Length + 43
        && value[prefix.Length..].All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');

    sealed record AvailabilityState(bool Enabled, string? Reason);
    sealed record PersistenceSnapshot(IReadOnlyDictionary<string, FileStamp?> Files,
        IReadOnlyDictionary<string, long?> Counts, bool Readable)
    {
        public bool EquivalentTo(PersistenceSnapshot other)
            => Files.Count == other.Files.Count
                && Files.All(pair => other.Files.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value))
                && Counts.Count == other.Counts.Count
                && Counts.All(pair => other.Counts.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));
    }

    sealed record FileStamp(bool Exists, long Length, DateTime LastWriteUtc)
    {
        public static FileStamp Read(string path)
        {
            if (!File.Exists(path)) return new(false, 0, DateTime.MinValue);
            var info = new FileInfo(path);
            return new(true, info.Length, info.LastWriteTimeUtc);
        }
    }
}
