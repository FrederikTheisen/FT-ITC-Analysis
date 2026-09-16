using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

/// <summary>Minimal registry of scrubbed account identifiers. It deliberately contains no identity data.</summary>
public sealed class OperatorTombstoneRegistry
{
    readonly string path;
    static readonly object Gate = new();
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public OperatorTombstoneRegistry(IOptions<InterpretationOptions> options) => path = options.Value.TombstonePath;
    public bool Contains(string id)
    {
        lock (Gate) return Read().Any(x => x.Id == id);
    }
    public DateTime? Add(string id)
    {
        lock (Gate)
        {
            var entries = Read();
            var existing = entries.FirstOrDefault(x => x.Id == id);
            if (existing is not null) return existing.ScrubbedAtUtc;
            var now = DateTime.UtcNow; entries.Add(new(id, now)); Write(entries); return now;
        }
    }
    List<Tombstone> Read()
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<Tombstone>>(File.ReadAllText(path), JsonOptions) ?? new(); }
        catch { return new(); }
    }
    void Write(List<Tombstone> entries)
    {
        var dir = Path.GetDirectoryName(path) ?? "."; Directory.CreateDirectory(dir);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, JsonOptions));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public sealed record Tombstone(string Id, DateTime ScrubbedAtUtc);
}
