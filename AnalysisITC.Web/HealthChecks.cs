using System.Security.Cryptography;
using System.Text.Json;
using AnalysisITC.Core.Viewer;
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
    readonly SelfRegistrationStore? registrations;
    readonly ViewerDocumentReader? viewer;

    public HealthCheckService(IOptions<InterpretationOptions> options,
        InterpretationServiceAvailability interpretation,
        RegistrationAvailability registrationAvailability,
        SelfRegistrationStore? registrations = null,
        ViewerDocumentReader? viewer = null)
    {
        this.options = options.Value;
        this.interpretation = interpretation;
        this.registrationAvailability = registrationAvailability;
        this.registrations = registrations;
        this.viewer = viewer;
    }

    public HealthReport RunNonBillable()
    {
        var groups = new List<HealthCheckGroup>
        {
            Interpretation(), Registration(), Viewer(), Storage()
        };
        return new(DateTimeOffset.UtcNow, groups.SelectMany(g => g.Checks).Any(c => c.State == HealthCheckState.Fail) ? "attention" : "healthy", groups);
    }

    HealthCheckGroup Interpretation()
    {
        var checks = new List<HealthCheckResult>();
        var policy = interpretation.Read();
        if (!options.Enabled || policy.Status is "paused" or "retired" || !policy.IsAvailable)
        {
            checks.Add(HealthCheckResult.Skipped("interpretation.transactional", "Transactional interpretation", "interpretation is intentionally paused or unavailable"));
            checks.Add(CheckUsageDatabase());
            return new("Interpretation", checks);
        }

        checks.Add(CheckConfiguration());
        checks.Add(CheckUsageDatabase());
        try
        {
            checks.Add(HealthCheckResult.Pass("interpretation.registry", "Interpretation registries", "configuration registries are readable"));
        }
        catch (Exception ex) { checks.Add(HealthCheckResult.Fail("interpretation.registry", "Interpretation registries", SafeReason(ex))); }
        return new("Interpretation", checks);
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

    HealthCheckGroup Registration()
    {
        var checks = new List<HealthCheckResult>();
        var configured = options.Registration;
        if (!configured.Enabled || !registrationAvailability.IsEnabled)
        {
            checks.Add(HealthCheckResult.Skipped("registration.transactional", "Registration diagnostics", "registration is intentionally paused"));
            checks.Add(CheckFile("registration.database", "Registration database", configured.DatabasePath, false));
            return new("Registration", checks);
        }
        checks.Add(CheckFile("registration.database", "Registration database", configured.DatabasePath, false));
        checks.Add(File.Exists(configured.DatabasePath)
            ? CheckWritableFile("registration.database-write", "Registration database write access", configured.DatabasePath)
            : CheckWritableDirectory("registration.database-directory-write", "Registration database directory write access", Path.GetDirectoryName(configured.DatabasePath) ?? "."));
        checks.Add(CheckJsonFile("registration.turnstile", "Turnstile configuration", configured.SecretConfigurationPath));
        checks.Add(CheckJsonFile("registration.mail", "Registration mail configuration", configured.MailConfigurationPath));
        checks.Add(CheckJsonFile("registration.policy", "Registration availability policy", configured.AvailabilityPolicyPath));
        checks.Add(File.Exists(configured.OperatorRegistryPath)
            ? CheckWritableFile("registration.operator-registry-write", "Registered-account registry write access", configured.OperatorRegistryPath)
            : CheckWritableDirectory("registration.operator-registry-directory-write", "Registered-account registry directory write access", Path.GetDirectoryName(configured.OperatorRegistryPath) ?? "."));
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
        try
        {
            var path = options.UsageLog.DatabasePath;
            if (!File.Exists(path)) return HealthCheckResult.Fail("interpretation.usage", "Usage database", "usage database is unavailable");
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());
            db.Open();
            using var integrity = db.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
            if (!string.Equals(integrity.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                return HealthCheckResult.Fail("interpretation.usage", "Usage database", "usage database integrity check failed");
            using var tx = db.BeginTransaction(); tx.Rollback();
            var writable = CheckWritableFile("interpretation.usage-write", "Usage database write access", path);
            if (writable.State != HealthCheckState.Pass) return writable;
            foreach (var sidecar in new[] { path + "-wal", path + "-shm" })
            {
                if (File.Exists(sidecar))
                {
                    var sidecarResult = CheckWritableFile("interpretation.usage-sidecar", "Usage database journal access", sidecar);
                    if (sidecarResult.State != HealthCheckState.Pass) return sidecarResult;
                }
            }
            return HealthCheckResult.Pass("interpretation.usage", "Usage database");
        }
        catch (Exception ex) { return HealthCheckResult.Fail("interpretation.usage", "Usage database", SafeReason(ex)); }
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

    static HealthCheckResult CheckDirectory(string id, string name, string path)
    {
        try { return Directory.Exists(path) ? HealthCheckResult.Pass(id, name) : HealthCheckResult.Warning(id, name, "directory is unavailable"); }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    static HealthCheckResult CheckWritableDirectory(string id, string name, string path)
    {
        try
        {
            if (!Directory.Exists(path)) return HealthCheckResult.Warning(id, name, "directory is unavailable");
            var probe = Path.Combine(path, ".ftitc-health-write-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                return HealthCheckResult.Pass(id, name);
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); } catch { }
            }
        }
        catch (UnauthorizedAccessException) { return HealthCheckResult.Fail(id, name, "directory is not writable by the service"); }
        catch (IOException) { return HealthCheckResult.Fail(id, name, "directory could not be written"); }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    static HealthCheckResult CheckWritableFile(string id, string name, string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return HealthCheckResult.Pass(id, name);
        }
        catch (UnauthorizedAccessException) { return HealthCheckResult.Fail(id, name, "file is not writable by the service"); }
        catch (IOException) { return HealthCheckResult.Fail(id, name, "file could not be opened for writing"); }
        catch (Exception ex) { return HealthCheckResult.Fail(id, name, SafeReason(ex)); }
    }

    static string SafeReason(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "access was denied",
        IOException => "storage could not be read",
        SqliteException => "database could not be read",
        CryptographicException => "protected data could not be read",
        _ => "check could not be completed"
    };
}
