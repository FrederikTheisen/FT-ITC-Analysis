using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class RegistrationAvailability
{
    readonly string path; readonly object gate = new();
    public RegistrationAvailability(IOptions<InterpretationOptions> options) => path = options.Value.Registration.AvailabilityPolicyPath;
    public bool IsEnabled => Read().Enabled;
    public RegistrationAvailabilitySnapshot Read()
    {
        lock (gate)
        {
            if (!File.Exists(path)) return new(false, null);
            try { var p = JsonSerializer.Deserialize<Policy>(File.ReadAllText(path)); return p is null ? new(false, "Invalid registration policy.") : new(p.Enabled, p.Message); }
            catch { return new(false, "Registration policy could not be read."); }
        }
    }
    public void Set(bool enabled, string? message = null)
    {
        var policy = new Policy { Enabled = enabled, Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(), UpdatedAtUtc = DateTime.UtcNow };
        var dir = Path.GetDirectoryName(path); if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    sealed class Policy { public bool Enabled { get; set; } public string? Message { get; set; } public DateTime UpdatedAtUtc { get; set; } }
}
public sealed record RegistrationAvailabilitySnapshot(bool Enabled, string? Message);
