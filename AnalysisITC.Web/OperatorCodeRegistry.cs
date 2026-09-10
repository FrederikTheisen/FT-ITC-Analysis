using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using AnalysisITC.Core.Interpretation;

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
                ? new(true, record.Id, record.EffectiveAccessTier) : OperatorAuthentication.Denied;
        }
        return OperatorAuthentication.Denied;
    }

    public (OperatorCodeRecord Record, string Code) Create(string label, int? expiresDays, bool noExpiry,
        string accessTier = InterpretationAccessTiers.Administrator, string? name = null, string? email = null, string? organization = null)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A non-empty label is required.");
        if (!InterpretationAccessTiers.IsAssignable(accessTier)) throw new ArgumentException("Unknown access tier.", nameof(accessTier));
        var days = expiresDays ?? options.DefaultLifetimeDays;
        if (!noExpiry && days <= 0) throw new ArgumentOutOfRangeException(nameof(expiresDays));
        var code = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        var record = new OperatorCodeRecord
        {
            Id = Guid.NewGuid().ToString("N"), Label = label.Trim(), CreatedAtUtc = now,
            Name = Clean(name), Email = ValidateEmail(email), Organization = Clean(organization),
            ExpiresAtUtc = noExpiry ? null : now.AddDays(days),
            AccessTier = accessTier,
            CodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant(),
        };
        var records = ReadStrict(); records.Add(record); Write(records);
        return (record, code);
    }

    public OperatorCodeRecord? FindActive(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        var now = DateTime.UtcNow;
        foreach (var record in ReadSafe())
        {
            byte[] stored;
            try { stored = Convert.FromHexString(record.CodeHash); } catch (FormatException) { continue; }
            if (stored.Length == hash.Length && CryptographicOperations.FixedTimeEquals(stored, hash)
                && record.RevokedAtUtc is null && (record.ExpiresAtUtc is null || record.ExpiresAtUtc > now)) return record;
        }
        return null;
    }

    public IReadOnlyList<OperatorCodeRecord> List() => ReadStrict();

    public bool Revoke(string id)
    {
        var records = ReadStrict();
        var record = records.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal));
        if (record is null) return false;
        record.RevokedAtUtc ??= DateTime.UtcNow; Write(records); return true;
    }

    public bool ChangeTier(string id, string accessTier)
    {
        if (!InterpretationAccessTiers.IsAssignable(accessTier)) throw new ArgumentException("Unknown access tier.", nameof(accessTier));
        var records = ReadStrict(); var record = records.SingleOrDefault(value => value.Id == id);
        if (record is null) return false; record.AccessTier = accessTier; Write(records); return true;
    }

    public bool ChangeDetails(string id, string? name, string? email, string? organization)
    {
        var records = ReadStrict(); var record = records.SingleOrDefault(value => value.Id == id);
        if (record is null) return false;
        record.Name = Clean(name); record.Email = ValidateEmail(email); record.Organization = Clean(organization);
        Write(records); return true;
    }

    public bool ChangeQuota(string id, decimal? monthlyUsd, bool unlimited)
    {
        if (!unlimited && monthlyUsd is <= 0) throw new ArgumentOutOfRangeException(nameof(monthlyUsd));
        var records = ReadStrict(); var record = records.SingleOrDefault(value => value.Id == id);
        if (record is null) return false;
        record.MonthlyQuotaUsdOverride = unlimited ? null : monthlyUsd;
        record.QuotaUnlimited = unlimited;
        Write(records); return true;
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
    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    static string? ValidateEmail(string? value)
    {
        var email = Clean(value);
        if (email is null) return null;
        try { _ = new MailAddress(email); return email; }
        catch (FormatException) { throw new ArgumentException("The email address is invalid.", nameof(value)); }
    }
}

public sealed class OperatorCodeRecord
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string CodeHash { get; set; } = "";
    public string? AccessTier { get; set; }
    public string EffectiveAccessTier => InterpretationAccessTiers.IsAssignable(AccessTier ?? "") ? AccessTier! : InterpretationAccessTiers.Administrator;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public decimal? MonthlyQuotaUsdOverride { get; set; }
    public bool QuotaUnlimited { get; set; }
}

public readonly record struct OperatorAuthentication(bool IsAuthorized, string? OperatorCodeId, string AccessTier)
{ public static OperatorAuthentication Denied => new(false, null, InterpretationAccessTiers.Public); }

public readonly record struct InterpretationGenerationSelection(
    string Model, string ReasoningEffort, string? RequestedModel, string? RequestedReasoningEffort,
    string? OperatorCodeId, string RequestedPreset, string EffectivePreset, string AccessTier,
    string PresetRevision, string ResponseSchemaVersion);

public static class InterpretationGenerationSelector
{
    public static bool TrySelect(HttpRequest request, ValidatedInterpretationRequest validated, InterpretationOptions options,
        OperatorCodeRegistry registry, GenerationPresetRegistry presets,
        out InterpretationGenerationSelection selection, out (int Status, string Code, string Detail) error)
    {
        var model = request.Headers["X-FTITC-Model"].FirstOrDefault();
        var reasoning = request.Headers["X-FTITC-Reasoning-Effort"].FirstOrDefault();
        var hasOverride = !string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(reasoning);
        var auth = registry.Authenticate(request.Headers.Authorization.FirstOrDefault());
        if (request.Headers.ContainsKey("Authorization") && !auth.IsAuthorized)
        { selection=default; error=(403,"operator_access_denied","The supplied access code is invalid, expired, or revoked."); return false; }
        var config = presets.Read();
        if (validated.RequestSchemaVersion == FtItcInterpretationClient.LegacyRequestSchemaVersion)
        {
            if (hasOverride && (!auth.IsAuthorized || auth.AccessTier != InterpretationAccessTiers.Administrator))
            { selection=default; error=(403,"operator_access_denied","Administrator access is required for generation overrides."); return false; }
            if (hasOverride)
            {
                var effectiveModel=model ?? options.OpenAI.Model; var effectiveReasoning=reasoning ?? options.OpenAI.ReasoningEffort;
                if (!Allowed(effectiveModel,effectiveReasoning,options)) { selection=default; error=(400,"invalid_generation_override","The requested model and reasoning combination is not allowed."); return false; }
                selection=new(effectiveModel,effectiveReasoning,model,reasoning,auth.OperatorCodeId,"fast","custom",auth.AccessTier,config.Revision,FtItcInterpretationClient.LegacyResponseSchemaVersion); error=default; return true;
            }
            var instant=config.Presets.Single(x=>x.Id=="instant");
            selection=new(instant.Model,instant.ReasoningEffort,null,null,auth.IsAuthorized?auth.OperatorCodeId:null,"fast","instant",auth.IsAuthorized?auth.AccessTier:InterpretationAccessTiers.Public,config.Revision,FtItcInterpretationClient.LegacyResponseSchemaVersion); error=default; return true;
        }
        var tier=auth.IsAuthorized?auth.AccessTier:InterpretationAccessTiers.Public;
        if (tier==InterpretationAccessTiers.Administrator)
        {
            if (validated.GenerationProfile!="custom" || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(reasoning))
            { selection=default; error=(400,"invalid_generation_override","Administrator requests require custom profile, model, and reasoning headers."); return false; }
            if (!Allowed(model,reasoning,options)) { selection=default; error=(400,"invalid_generation_override","The requested model and reasoning combination is not allowed."); return false; }
            selection=new(model,reasoning,model,reasoning,auth.OperatorCodeId,"custom","custom",tier,config.Revision,FtItcInterpretationClient.ResponseSchemaVersion); error=default; return true;
        }
        if (hasOverride) { selection=default; error=(403,"operator_access_denied","Administrator access is required for model and reasoning controls."); return false; }
        if (!InterpretationAccessTiers.Presets(tier).Contains(validated.GenerationProfile,StringComparer.Ordinal))
        { selection=default; error=(403,"generation_preset_denied","The selected interpretation depth is not available with this access level."); return false; }
        var preset=config.Presets.Single(x=>x.Id==validated.GenerationProfile);
        selection=new(preset.Model,preset.ReasoningEffort,null,null,auth.IsAuthorized?auth.OperatorCodeId:null,validated.GenerationProfile,preset.Id,tier,config.Revision,FtItcInterpretationClient.ResponseSchemaVersion);
        error = default; return true;
    }

    static bool Allowed(string model,string reasoning,InterpretationOptions options) => options.AllowedModels.TryGetValue(model,out var allowed)&&allowed.ReasoningEfforts.Contains(reasoning,StringComparer.Ordinal);
}
