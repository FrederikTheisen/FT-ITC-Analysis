using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class InterpretationAvailabilityPolicy
{
    public string Status { get; set; } = "active";
    public string? Message { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record InterpretationAvailabilitySnapshot(string Status, string? Message, DateTime? UpdatedAtUtc, bool PolicyValid)
{
    public bool IsAvailable => PolicyValid && Status == "active";
}

public sealed class InterpretationServiceAvailability
{
    readonly string path;
    readonly object gate = new();
    InterpretationAvailabilitySnapshot current;

    public InterpretationServiceAvailability(IOptions<InterpretationOptions> options)
    {
        path = options.Value.AvailabilityPolicyPath;
        current = Load();
    }

    public InterpretationAvailabilitySnapshot Read()
    {
        lock (gate) current = Load();
        return current;
    }

    public void Set(string status, string? message)
    {
        if (status is not ("active" or "paused" or "retired")) throw new ArgumentException("Status must be active, paused, or retired.", nameof(status));
        var policy = new InterpretationAvailabilityPolicy { Status = status, Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(), UpdatedAtUtc = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true });
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, true);
        lock (gate) current = new InterpretationAvailabilitySnapshot(policy.Status, policy.Message, policy.UpdatedAtUtc, true);
    }

    InterpretationAvailabilitySnapshot Load()
    {
        if (!File.Exists(path)) return new("active", null, null, true);
        try
        {
            var policy = JsonSerializer.Deserialize<InterpretationAvailabilityPolicy>(File.ReadAllText(path));
            if (policy is null || policy.Status is not ("active" or "paused" or "retired"))
                return new("paused", "Service availability policy is invalid; generation is temporarily unavailable.", null, false);
            return new(policy.Status, policy.Message, policy.UpdatedAtUtc == default ? File.GetLastWriteTimeUtc(path) : policy.UpdatedAtUtc, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return new("paused", "Service availability could not be read; generation is temporarily unavailable.", null, false); }
    }
}
