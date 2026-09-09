using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class OperatorCodeRegistry
{
    const string Prefix = "ftitc_op_";
    readonly InterpretationOperatorOptions options;
    readonly ILogger<OperatorCodeRegistry> logger;
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public OperatorCodeRegistry(IOptions<InterpretationOptions> options, ILogger<OperatorCodeRegistry> logger)
    { this.options = options.Value.OperatorAccess; this.logger = logger; }

    public OperatorAuthentication Authenticate(string? authorization)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return OperatorAuthentication.Denied;
        var code = authorization[7..].Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal)) return OperatorAuthentication.Denied;
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        foreach (var record in ReadSafe())
        {
            byte[] stored;
            try { stored = Convert.FromHexString(record.CodeHash); }
            catch (FormatException) { continue; }
            if (stored.Length != supplied.Length || !CryptographicOperations.FixedTimeEquals(stored, supplied)) continue;
            var now = DateTime.UtcNow;
            return record.RevokedAtUtc is null && (record.ExpiresAtUtc is null || record.ExpiresAtUtc > now)
                ? new(true, record.Id) : OperatorAuthentication.Denied;
        }
        return OperatorAuthentication.Denied;
    }

    public (OperatorCodeRecord Record, string Code) Create(string label, int? expiresDays, bool noExpiry)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A non-empty label is required.");
        var days = expiresDays ?? options.DefaultLifetimeDays;
        if (!noExpiry && days <= 0) throw new ArgumentOutOfRangeException(nameof(expiresDays));
        var code = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        var record = new OperatorCodeRecord
        {
            Id = Guid.NewGuid().ToString("N"), Label = label.Trim(), CreatedAtUtc = now,
            ExpiresAtUtc = noExpiry ? null : now.AddDays(days),
            CodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant(),
        };
        var records = ReadStrict(); records.Add(record); Write(records);
        return (record, code);
    }

    public IReadOnlyList<OperatorCodeRecord> List() => ReadStrict();

    public bool Revoke(string id)
    {
        var records = ReadStrict();
        var record = records.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal));
        if (record is null) return false;
        record.RevokedAtUtc ??= DateTime.UtcNow; Write(records); return true;
    }

    List<OperatorCodeRecord> ReadSafe()
    {
        try { return ReadStrict(); }
        catch (Exception ex) { logger.LogWarning(ex, "Operator-code registry could not be read; access was denied."); return new(); }
    }

    List<OperatorCodeRecord> ReadStrict()
    {
        if (!File.Exists(options.RegistryPath)) return new();
        var value = JsonSerializer.Deserialize<List<OperatorCodeRecord>>(File.ReadAllText(options.RegistryPath), JsonOptions);
        return value ?? throw new InvalidDataException("The operator-code registry is invalid.");
    }

    void Write(List<OperatorCodeRecord> records)
    {
        var directory = Path.GetDirectoryName(options.RegistryPath) ?? throw new InvalidOperationException("Registry path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = options.RegistryPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(records, JsonOptions));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            File.Move(temporary, options.RegistryPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class OperatorCodeRecord
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

public readonly record struct OperatorAuthentication(bool IsAuthorized, string? OperatorCodeId)
{ public static OperatorAuthentication Denied => new(false, null); }

public readonly record struct InterpretationGenerationSelection(
    string Model, string ReasoningEffort, string? RequestedModel, string? RequestedReasoningEffort, string? OperatorCodeId);

public static class InterpretationGenerationSelector
{
    public static bool TrySelect(HttpRequest request, InterpretationOptions options, OperatorCodeRegistry registry,
        out InterpretationGenerationSelection selection, out (int Status, string Code, string Detail) error)
    {
        var model = request.Headers["X-FTITC-Model"].FirstOrDefault();
        var reasoning = request.Headers["X-FTITC-Reasoning-Effort"].FirstOrDefault();
        var hasOverride = !string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(reasoning);
        var auth = registry.Authenticate(request.Headers.Authorization.FirstOrDefault());
        if (hasOverride && !auth.IsAuthorized)
        { selection = default; error = (403, "operator_access_denied", "A valid operator code is required for generation overrides."); return false; }
        var effectiveModel = model ?? options.OpenAI.Model;
        var effectiveReasoning = reasoning ?? options.OpenAI.ReasoningEffort;
        if (hasOverride && (!options.AllowedModels.TryGetValue(effectiveModel, out var allowed)
            || !allowed.ReasoningEfforts.Contains(effectiveReasoning, StringComparer.Ordinal)))
        { selection = default; error = (400, "invalid_generation_override", "The requested model and reasoning combination is not allowed."); return false; }
        selection = new(effectiveModel, effectiveReasoning, model, reasoning, auth.IsAuthorized ? auth.OperatorCodeId : null);
        error = default; return true;
    }
}
