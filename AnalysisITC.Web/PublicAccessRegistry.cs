using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AnalysisITC.Web;

/// <summary>Persistent anonymous installation identities. Raw bearer codes are returned once and never persisted.</summary>
public sealed class PublicAccessRegistry
{
    const string Prefix = "ftitc_pub_";
    readonly string path;
    readonly ILogger<PublicAccessRegistry>? logger;
    static readonly ConcurrentDictionary<string, long> touchLockTimeouts = new(StringComparer.Ordinal);
    string MetricKey => Path.GetFullPath(path);
    public long TouchLockTimeoutCount => touchLockTimeouts.TryGetValue(MetricKey, out var count) ? count : 0;
    internal static void ResetTouchLockTimeouts() => touchLockTimeouts.Clear();
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public PublicAccessRegistry(IOptions<InterpretationOptions> options) => path = options.Value.PublicAccessRegistryPath;
    public PublicAccessRegistry(IOptions<InterpretationOptions> options, ILogger<PublicAccessRegistry> logger)
    { path = options.Value.PublicAccessRegistryPath; this.logger = logger; }

    public (PublicAccessRecord Record, string Code) Create()
    {
        using var registryLock = AcquireLock();
            var code = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
            var record = new PublicAccessRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                CodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant(),
                CreatedAtUtc = DateTime.UtcNow,
            };
            var records = Read(); records.Add(record); Write(records); return (record, code);
    }

    public PublicAccessRecord? FindActive(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        foreach (var record in Read())
        {
            byte[] stored;
            try { stored = Convert.FromHexString(record.CodeHash); } catch (FormatException) { continue; }
            if (stored.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(stored, supplied)
                && record.RevokedAtUtc is null) return record;
        }
        return null;
    }

    public bool TryTouch(string id)
    {
        IDisposable registryLock;
        try { registryLock = AcquireLock(); }
        catch (PublicAccessRegistryUnavailableException ex) when (ex.LockTimedOut)
        {
            logger?.LogWarning("Could not update public access last-use time because the registry lock timed out.");
            touchLockTimeouts.AddOrUpdate(MetricKey, 1, static (_, count) => count + 1);
            return true; // Authentication already confirmed this record was active.
        }
        using (registryLock)
        {
            var records = Read(); var record = records.SingleOrDefault(x => x.Id == id);
            if (record is null || record.RevokedAtUtc is not null) return false;
            record.LastUseAtUtc = DateTime.UtcNow; Write(records); return true;
        }
    }

    public IReadOnlyList<PublicAccessRecord> List() => Read();

    public bool Revoke(string id)
    {
        using var registryLock = AcquireLock();
        var records = Read(); var record = records.SingleOrDefault(x => x.Id == id); if (record is null) return false;
        record.RevokedAtUtc ??= DateTime.UtcNow; Write(records); return true;
    }

    List<PublicAccessRecord> Read()
    {
        List<PublicAccessRecord>? records;
        try { using var stream = File.OpenRead(path); records = JsonSerializer.Deserialize<List<PublicAccessRecord>>(stream, JsonOptions); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        if (records is null) throw new InvalidDataException("The public-access registry is invalid.");
        if (records.Any(record => record is null || string.IsNullOrWhiteSpace(record.Id) || record.Id.Length != 32
                || !record.Id.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(record.CodeHash) || record.CodeHash.Length != 64 || !record.CodeHash.All(Uri.IsHexDigit))
            || records.Select(record => record.Id).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("The public-access registry contains invalid records.");
        return records;
    }

    public IReadOnlySet<string> ReadVerifiedInstallationIds() => Read().Select(record => record.Id).ToHashSet(StringComparer.Ordinal);

    IDisposable AcquireLock()
    {
        try { return RegistryFileLock.Acquire(path); }
        catch (RegistryLockTimeoutException) { throw new PublicAccessRegistryUnavailableException(lockTimedOut: true); }
    }

    void Write(List<PublicAccessRecord> records)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Public access path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(records, JsonOptions));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class PublicAccessRegistryUnavailableException : IOException
{
    public bool LockTimedOut { get; }
    public PublicAccessRegistryUnavailableException(bool lockTimedOut = false) : base("Public access is temporarily unavailable.")
        => LockTimedOut = lockTimedOut;
}

public sealed class PublicAccessRecord
{
    public string Id { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUseAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
