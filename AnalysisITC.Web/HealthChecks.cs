using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public enum HealthCheckState { Pass, Warning, Fail, Skipped }

public sealed record HealthCheckResult(string Id, string Name, HealthCheckState State, string Reason)
{
    public static HealthCheckResult Pass(string id, string name, string reason = "ok") => new(id, name, HealthCheckState.Pass, reason);
    public static HealthCheckResult Warning(string id, string name, string reason) => new(id, name, HealthCheckState.Warning, reason);
    public static HealthCheckResult Fail(string id, string name, string reason) => new(id, name, HealthCheckState.Fail, reason);
    public static HealthCheckResult Skipped(string id, string name, string reason) => new(id, name, HealthCheckState.Skipped, reason);
}

public sealed record HealthCheckGroup(string Name, IReadOnlyList<HealthCheckResult> Checks);

public sealed record HealthReport(DateTimeOffset CheckedAtUtc, string Overall, IReadOnlyList<HealthCheckGroup> Groups)
{
    public bool HasAttention => Groups.SelectMany(group => group.Checks).Any(check => check.State is HealthCheckState.Warning or HealthCheckState.Fail);
}

/// <summary>Safe, metadata-only operational checks. It never logs file contents, secrets, or user data.</summary>
public sealed class HealthCheckService
{
    readonly InterpretationOptions options;
    readonly InterpretationServiceAvailability interpretation;
    readonly RegistrationAvailability registrationAvailability;

    public HealthCheckService(IOptions<InterpretationOptions> options,
        InterpretationServiceAvailability interpretation,
        RegistrationAvailability registrationAvailability)
    {
        this.options = options.Value;
        this.interpretation = interpretation;
        this.registrationAvailability = registrationAvailability;
    }

    public HealthReport RunNonBillable()
    {
        var groups = new List<HealthCheckGroup>
        {
            Interpretation(), Registration(), Viewer(), Storage()
        };
        return new(DateTimeOffset.UtcNow, groups.SelectMany(g => g.Checks).Any(c => c.State is HealthCheckState.Warning or HealthCheckState.Fail) ? "attention" : "healthy", groups);
    }

    HealthCheckGroup Interpretation()
    {
        var checks = new List<HealthCheckResult>();
        checks.Add(CheckGenerationPresetRegistry());
        checks.Add(CheckPublicAccessRegistry());
        checks.Add(CheckOperatorRegistry(false));
        checks.Add(CheckOperatorRegistry(true));
        checks.Add(CheckTombstones());
        var policy = interpretation.Read();
        if (!options.Enabled || policy.Status is "paused" or "retired" || !policy.IsAvailable)
        {
            checks.Add(HealthCheckResult.Skipped("interpretation.transactional", "Transactional interpretation", "interpretation is intentionally paused or unavailable"));
            checks.Add(CheckUsageDatabase());
            return new("Interpretation", checks);
        }

        checks.Add(CheckConfiguration());
        if (options.Mistral.Enabled)
        {
            var valid = options.Mistral.IsConfigured && options.AllowedModels.Any(value => value.Value.Provider == "mistral");
            checks.Add(valid ? HealthCheckResult.Pass("interpretation.mistral", "Mistral configuration")
                : HealthCheckResult.Fail("interpretation.mistral", "Mistral configuration", "Mistral credentials, HTTPS endpoint or models are incomplete"));
        }
        checks.Add(CheckUsageDatabase());
        return new("Interpretation", checks);
    }

    HealthCheckResult CheckGenerationPresetRegistry()
    {
        try { _ = new GenerationPresetRegistry(Microsoft.Extensions.Options.Options.Create(options)).Read(); return HealthCheckResult.Pass("interpretation.presets", "Generation presets"); }
        catch (Exception ex) { return HealthCheckResult.Fail("interpretation.presets", "Generation presets", SafeReason(ex)); }
    }

    HealthCheckResult CheckPublicAccessRegistry()
    {
        try
        {
            var registry = new PublicAccessRegistry(Microsoft.Extensions.Options.Options.Create(options));
            _ = registry.List();
            return registry.TouchLockTimeoutCount > 0
                ? HealthCheckResult.Warning("interpretation.public-access", "Public access registry", "one or more last-use updates timed out")
                : HealthCheckResult.Pass("interpretation.public-access", "Public access registry");
        }
        catch (Exception ex) { return HealthCheckResult.Fail("interpretation.public-access", "Public access registry", SafeReason(ex)); }
    }

    HealthCheckResult CheckOperatorRegistry(bool registered)
    {
        var id = registered ? "interpretation.registered-operators" : "interpretation.operator-codes";
        var name = registered ? "Registered operator codes" : "Operator codes";
        try
        {
            var registry = new OperatorCodeRegistry(Microsoft.Extensions.Options.Options.Create(options),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<OperatorCodeRegistry>.Instance,
                new OperatorTombstoneRegistry(Microsoft.Extensions.Options.Options.Create(options)),
                new PublicAccessRegistry(Microsoft.Extensions.Options.Options.Create(options)));
            _ = registry.ValidateConfiguredRegistryForHealth(registered);
            return HealthCheckResult.Pass(id, name);
        }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    HealthCheckResult CheckConfiguration()
    {
        var openAi = options.OpenAI;
        if (!openAi.IsConfigured || !Uri.TryCreate(openAi.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            return HealthCheckResult.Fail("interpretation.configuration", "Provider configuration", "provider configuration is incomplete or endpoint is not HTTPS");
        if (!options.AllowedModels.TryGetValue(openAi.Model, out var model) || !model.ReasoningEfforts.Contains(openAi.ReasoningEffort, StringComparer.Ordinal))
            return HealthCheckResult.Fail("interpretation.configuration", "Provider configuration", "selected model and reasoning combination is not allowlisted");
        if (!options.Pricing.ContainsKey(openAi.Model))
            return HealthCheckResult.Fail("interpretation.configuration", "Provider configuration", "pricing is unavailable for the selected model");
        if (string.IsNullOrWhiteSpace(openAi.VectorStoreId))
            return HealthCheckResult.Warning("interpretation.configuration", "Provider configuration", "vector-store configuration is missing");
        return HealthCheckResult.Pass("interpretation.configuration", "Provider configuration");
    }

    HealthCheckResult CheckTombstones()
    {
        const string id = "interpretation.deletion-markers";
        const string name = "Account deletion markers";
        try
        {
            new OperatorTombstoneRegistry(Microsoft.Extensions.Options.Options.Create(options)).ValidateReadable();
            return HealthCheckResult.Pass(id, name);
        }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    internal static HealthCheckResult CheckRegistrationMail(string path)
    {
        const string id = "registration.mail";
        const string name = "Registration mail configuration";
        try { _ = RegistrationMailConfiguration.Read(path); return HealthCheckResult.Pass(id, name); }
        catch (Exception) { return HealthCheckResult.Fail(id, name, "mail settings are unavailable or invalid; queued delivery is waiting for repair"); }
    }

    HealthCheckGroup Registration()
    {
        var checks = new List<HealthCheckResult>();
        var configured = options.Registration;
        // Public submissions can be paused while previously requested mail is still delivered.
        if (configured.Enabled) checks.Add(CheckRegistrationMail(configured.MailConfigurationPath));
        if (!configured.Enabled || !registrationAvailability.IsEnabled)
        {
            checks.Add(HealthCheckResult.Skipped("registration.transactional", "Registration diagnostics", "registration is intentionally paused"));
            checks.Add(CheckFile("registration.database", "Registration database", configured.DatabasePath, false));
            return new("Registration", checks);
        }
        checks.Add(CheckDatabase("registration.database", "Registration database", configured.DatabasePath));
        checks.Add(CheckJsonFile("registration.turnstile", "Turnstile configuration", configured.SecretConfigurationPath));
        checks.Add(CheckJsonFile("registration.policy", "Registration availability policy", configured.AvailabilityPolicyPath));
        checks.Add(CheckRegistryStorage(configured.OperatorRegistryPath));
        return new("Registration", checks);
    }

    HealthCheckGroup Viewer()
    {
        var checks = new List<HealthCheckResult>();
        foreach (var (id, name, path) in new[] { ("viewer.html", "Viewer HTML", "wwwroot/index.html"), ("viewer.javascript", "Viewer JavaScript", "wwwroot/app.js") })
            checks.Add(File.Exists(Path.Combine(AppContext.BaseDirectory, path)) ? HealthCheckResult.Pass(id, name) : HealthCheckResult.Fail(id, name, "viewer asset is unavailable"));
        checks.Add(HealthCheckResult.Pass("viewer.antiforgery", "Viewer antiforgery", "antiforgery service is registered"));
        return new("Viewer", checks);
    }

    HealthCheckGroup Storage()
    {
        var checks = new List<HealthCheckResult>();
        checks.Add(CheckFile("storage.status-email", "Status-email configuration", options.StatusEmailConfigurationPath, false));
        checks.Add(CheckWritableDirectory("storage.data-protection", "Data-protection key ring", options.Registration.DataProtectionKeysPath));
        checks.Add(CheckWritableDirectory("storage.application-data", "Application data directory", Path.GetDirectoryName(options.UsageLog.DatabasePath) ?? "."));
        return new("Storage", checks);
    }

    HealthCheckResult CheckUsageDatabase()
    {
        if (!options.UsageLog.Enabled) return HealthCheckResult.Skipped("interpretation.usage", "Usage database", "usage accounting is disabled");
        return CheckDatabase("interpretation.usage", "Usage database", options.UsageLog.DatabasePath);
    }

    // Write to the main database so SQLite exercises the real locking/journal path.
    // The private table and its row are rolled back; application records are untouched.
    internal static HealthCheckResult CheckDatabase(string id, string name, string path)
    {
        try
        {
            if (!File.Exists(path)) return HealthCheckResult.Fail(id, name, "database is unavailable");
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadWrite, DefaultTimeout = 3 }.ToString());
            db.Open();
            using var integrity = db.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
            if (!string.Equals(integrity.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                return HealthCheckResult.Fail(id, name, "database integrity check failed");
            using var tx = db.BeginTransaction(deferred: false);
            using var write = db.CreateCommand();
            write.Transaction = tx;
            var table = "ftitc_health_probe_" + Guid.NewGuid().ToString("N");
            write.CommandText = $"CREATE TABLE {table} (value INTEGER NOT NULL); INSERT INTO {table} VALUES (1);";
            write.ExecuteNonQuery();
            tx.Rollback();
            return HealthCheckResult.Pass(id, name);
        }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    internal static HealthCheckResult CheckRegistryStorage(string path)
    {
        const string id = "registration.operator-registry-write";
        const string name = "Registered-account registry storage";
        try
        {
            // Registration reads the existing registry, then writes a sibling and renames it.
            // Opening the existing registry for writing would test a different permission.
            if (File.Exists(path))
            {
                using var existing = File.OpenRead(path);
            }
            ProbeAtomicReplacement(Path.GetDirectoryName(path) ?? ".");
            return HealthCheckResult.Pass(id, name);
        }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    static HealthCheckResult CheckFile(string id, string name, string path, bool required)
    {
        try
        {
            if (!File.Exists(path)) return required ? HealthCheckResult.Fail(id, name, "required file is unavailable") : HealthCheckResult.Warning(id, name, "file is unavailable");
            return HealthCheckResult.Pass(id, name);
        }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    static HealthCheckResult CheckJsonFile(string id, string name, string path)
    {
        var result = CheckFile(id, name, path, true);
        if (result.State != HealthCheckState.Pass) return result;
        try { using var stream = File.OpenRead(path); using var _ = JsonDocument.Parse(stream); return result; }
        catch (JsonException) { return HealthCheckResult.Fail(id, name, "configuration file is malformed"); }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    static HealthCheckResult CheckWritableDirectory(string id, string name, string path)
    {
        try
        {
            ProbeAtomicReplacement(path);
            return HealthCheckResult.Pass(id, name);
        }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    static void ProbeAtomicReplacement(string directory)
    {
        var probe = Path.Combine(directory, ".ftitc-health-write-probe-" + Guid.NewGuid().ToString("N"));
        var replacement = probe + ".tmp";
        try
        {
            WriteProbe(probe, "[]");
            WriteProbe(replacement, "[1]");
            File.Move(replacement, probe, true);
            if (File.ReadAllText(probe) != "[1]") throw new IOException("Replacement probe could not be read back.");
        }
        finally
        {
            try { File.Delete(replacement); }
            finally { File.Delete(probe); }
        }
    }

    static void WriteProbe(string path, string content)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream)) writer.Write(content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
    }

    static string SafeReason(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "access was denied",
        IOException => "storage operation failed",
        SqliteException => "database write or integrity check failed",
        CryptographicException => "protected data could not be read",
        _ => "check could not be completed"
    };
}
