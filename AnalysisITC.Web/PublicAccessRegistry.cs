using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

/// <summary>Persistent anonymous installation identities. Raw bearer codes are returned once and never persisted.</summary>
public sealed class PublicAccessRegistry
{
    const string Prefix = "ftitc_pub_";
    readonly string path;
    static readonly SemaphoreSlim Gate = new(1, 1);
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public PublicAccessRegistry(IOptions<InterpretationOptions> options) => path = options.Value.PublicAccessRegistryPath;

    public (PublicAccessRecord Record, string Code) Create()
    {
        Gate.Wait();
        try
        {
            var code = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
            var record = new PublicAccessRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                CodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant(),
                CreatedAtUtc = DateTime.UtcNow,
            };
            var records = Read(); records.Add(record); Write(records); return (record, code);
        }
        finally { Gate.Release(); }
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
        Gate.Wait();
        try { var records = Read(); var record = records.SingleOrDefault(x => x.Id == id); if (record is null || record.RevokedAtUtc is not null) return false; record.LastUseAtUtc = DateTime.UtcNow; Write(records); return true; }
        finally { Gate.Release(); }
    }

    public IReadOnlyList<PublicAccessRecord> List() => Read();

    public bool Revoke(string id)
    {
        Gate.Wait();
        try { var records = Read(); var record = records.SingleOrDefault(x => x.Id == id); if (record is null) return false; record.RevokedAtUtc ??= DateTime.UtcNow; Write(records); return true; }
        finally { Gate.Release(); }
    }

    List<PublicAccessRecord> Read()
    {
        if (!File.Exists(path)) return [];
        return JsonSerializer.Deserialize<List<PublicAccessRecord>>(File.ReadAllText(path), JsonOptions) ?? [];
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

public sealed class PublicAccessRecord
{
    public string Id { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUseAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
