using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

/// <summary>Minimal registry of scrubbed account identifiers. It deliberately contains no identity data.</summary>
public sealed class OperatorTombstoneRegistry
{
    readonly string path;
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public OperatorTombstoneRegistry(IOptions<InterpretationOptions> options) => path = options.Value.TombstonePath;
    public bool Contains(string id)
    {
        return Read().Any(x => x.Id == id);
    }
    public DateTime? Add(string id)
    {
        // Scrub holds the operator locks first; standalone additions hold only this lock.
        using var registryLock = RegistryFileLock.Acquire(path);
        var entries = Read();
        var existing = entries.FirstOrDefault(x => x.Id == id);
        if (existing is not null) return existing.ScrubbedAtUtc;
        var now = DateTime.UtcNow; entries.Add(new(id, now)); Write(entries); return now;
    }
    internal IReadOnlySet<string> ReadIds() => Read().Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
    internal void ValidateReadable() => _ = Read();
    List<Tombstone> Read()
    {
        try
        {
            using var stream = File.OpenRead(path);
            var entries = JsonSerializer.Deserialize<List<Tombstone>>(stream, JsonOptions);
            if (entries is null || entries.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id)))
                throw new InvalidDataException("The account deletion markers are invalid.");
            return entries;
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { throw new OperatorTombstoneRegistryUnavailableException(exception); }
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

internal sealed class OperatorTombstoneRegistryUnavailableException(Exception innerException)
    : IOException("Account deletion markers could not be read; access and changes are unavailable.", innerException);
