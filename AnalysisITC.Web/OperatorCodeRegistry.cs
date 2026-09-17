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
    readonly string registrationRegistryPath;
    readonly ILogger<OperatorCodeRegistry> logger;
    readonly OperatorTombstoneRegistry tombstones;
    static readonly SemaphoreSlim MutationLock = new(1, 1);
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public OperatorCodeRegistry(IOptions<InterpretationOptions> options, ILogger<OperatorCodeRegistry> logger, OperatorTombstoneRegistry tombstones)
    { this.options = options.Value.OperatorAccess; this.registrationRegistryPath = options.Value.Registration.OperatorRegistryPath; this.logger = logger; this.tombstones = tombstones; }

    // Kept for existing embedders and unit tests; the host uses the DI constructor above.
    public OperatorCodeRegistry(IOptions<InterpretationOptions> options, ILogger<OperatorCodeRegistry> logger)
        : this(options, logger, new OperatorTombstoneRegistry(options)) { }

    public OperatorAuthentication Authenticate(string? authorization)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return OperatorAuthentication.Denied;
        var code = authorization[7..].Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal)) return OperatorAuthentication.Denied;
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        foreach (var record in ReadCombinedSafe())
        {
            if (tombstones.Contains(record.Id)) continue;
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
            Id = Guid.NewGuid().ToString("N"), Label = CleanHumanText(label, nameof(label), 200)!, CreatedAtUtc = now,
            Name = CleanHumanText(name, nameof(name), 120), Email = ValidateEmail(email), Organization = CleanHumanText(organization, nameof(organization), 200),
            ExpiresAtUtc = noExpiry ? null : now.AddDays(days),
            AccessTier = accessTier,
            CodeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant(),
        };
        var records = ReadStrict(); records.Add(record); Write(records);
        return (record, code);
    }

    public OperatorCodeRecord CreateRegisteredWithCode(string id, string name, string email, string? organization, string code)
    {
        MutationLock.Wait();
        try
        {
            var records = ReadCombinedStrict();
            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
            var byId = records.SingleOrDefault(record => string.Equals(record.Id, id, StringComparison.Ordinal));
            if (byId is not null)
            {
                byte[] stored;
                try { stored = Convert.FromHexString(byId.CodeHash); }
                catch (FormatException exception) { throw new InvalidDataException("The existing registered credential is invalid.", exception); }
                if (stored.Length != expectedHash.Length || !CryptographicOperations.FixedTimeEquals(stored, expectedHash))
                    throw new InvalidDataException("The registration already has a different credential.");
                return byId;
            }
            var canonicalEmail = ValidateEmail(email)!;
            if (records.Any(record => record.Email is not null
                && string.Equals(ValidateEmail(record.Email), canonicalEmail, StringComparison.Ordinal)))
                throw new InvalidDataException("The email address belongs to another operator account.");
            var record = new OperatorCodeRecord
            {
                Id = id, Label = CleanHumanText(name, nameof(name), 120)!, Name = CleanHumanText(name, nameof(name), 120), Email = canonicalEmail, Organization = CleanHumanText(organization, nameof(organization), 200),
                CreatedAtUtc = DateTime.UtcNow, AccessTier = InterpretationAccessTiers.Standard,
                CodeHash = Convert.ToHexString(expectedHash).ToLowerInvariant(),
            };
            var registered = ReadRegistrationStrict(); registered.Add(record); WriteRegistration(registered); return record;
        }
        finally { MutationLock.Release(); }
    }

    public OperatorCodeRecord? FindActive(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        var now = DateTime.UtcNow;
        foreach (var record in ReadCombinedSafe())
        {
            if (tombstones.Contains(record.Id)) continue;
            byte[] stored;
            try { stored = Convert.FromHexString(record.CodeHash); } catch (FormatException) { continue; }
            if (stored.Length == hash.Length && CryptographicOperations.FixedTimeEquals(stored, hash)
                && record.RevokedAtUtc is null && (record.ExpiresAtUtc is null || record.ExpiresAtUtc > now)) return record;
        }
        return null;
    }

    public IReadOnlyList<OperatorCodeRecord> List() => ReadCombinedStrict();

    public bool Revoke(string id)
    {
        var records = ReadCombinedStrict();
        var record = records.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal));
        if (record is null) return false;
        record.RevokedAtUtc ??= DateTime.UtcNow; WriteMatching(records); return true;
    }

    public OperatorScrubResult? Scrub(string id, SelfRegistrationStore? registrations = null, InterpretationUsageStore? usage = null)
    {
        MutationLock.Wait();
        try
        {
            var records = ReadCombinedStrict();
            var record = records.SingleOrDefault(x => x.Id == id);
            if (record is null) return null;
            var scrubbed = tombstones.Add(id) ?? DateTime.UtcNow;
            record.Label = "Scrubbed account"; record.Name = null; record.Email = null; record.Organization = null;
            record.CodeHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            record.RevokedAtUtc ??= scrubbed; record.ScrubbedAtUtc ??= scrubbed;
            WriteMatching(records);
            var deliveryCancelled = registrations?.Scrub(id, scrubbed) ?? false;
            usage?.ScrubAccount(id);
            return new(id, scrubbed, deliveryCancelled);
        }
        finally { MutationLock.Release(); }
    }

    public bool ChangeTier(string id, string accessTier)
    {
        if (!InterpretationAccessTiers.IsAssignable(accessTier)) throw new ArgumentException("Unknown access tier.", nameof(accessTier));
        var records = ReadCombinedStrict(); var record = records.SingleOrDefault(value => value.Id == id);
        if (record is null) return false; record.AccessTier = accessTier; WriteMatching(records); return true;
    }

    public bool ChangeDetails(string id, string? name, string? email, string? organization)
    {
        var records = ReadCombinedStrict(); var record = records.SingleOrDefault(value => value.Id == id);
        if (record is null) return false;
        record.Name = CleanHumanText(name, nameof(name), 120); record.Email = ValidateEmail(email); record.Organization = CleanHumanText(organization, nameof(organization), 200);
        WriteMatching(records); return true;
    }

    public bool ChangeQuota(string id, decimal? monthlyUsd, bool unlimited)
    {
        if (!unlimited && monthlyUsd is <= 0) throw new ArgumentOutOfRangeException(nameof(monthlyUsd));
        var records = ReadCombinedStrict(); var record = records.SingleOrDefault(value => value.Id == id);
        if (record is null) return false;
        record.MonthlyQuotaUsdOverride = unlimited ? null : monthlyUsd;
        record.QuotaUnlimited = unlimited;
        WriteMatching(records); return true;
    }

    List<OperatorCodeRecord> ReadSafe()
    {
        try { return ReadStrict(); }
        catch (Exception ex) { logger.LogWarning(ex, "Operator-code registry could not be read; access was denied."); return new(); }
    }

    List<OperatorCodeRecord> ReadCombinedSafe()
    {
        try { return ReadCombinedStrict(); }
        catch (Exception ex) { logger.LogWarning(ex, "Operator-code registry could not be read; access was denied."); return new(); }
    }

    List<OperatorCodeRecord> ReadCombinedStrict() => ReadStrict().Concat(ReadRegistrationStrict()).ToList();

    List<OperatorCodeRecord> ReadRegistrationStrict()
    {
        if (!File.Exists(registrationRegistryPath)) return new();
        var value = JsonSerializer.Deserialize<List<OperatorCodeRecord>>(File.ReadAllText(registrationRegistryPath), JsonOptions);
        return value ?? throw new InvalidDataException("The registration operator-code registry is invalid.");
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

    void WriteRegistration(List<OperatorCodeRecord> records)
    {
        var directory = Path.GetDirectoryName(registrationRegistryPath) ?? throw new InvalidOperationException("Registration registry path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = registrationRegistryPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(records, JsonOptions));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            File.Move(temporary, registrationRegistryPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    void WriteMatching(List<OperatorCodeRecord> records)
    {
        var registeredIds = ReadRegistrationStrict().Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        Write(records.Where(x => !registeredIds.Contains(x.Id)).ToList());
        if (registeredIds.Count > 0 || File.Exists(registrationRegistryPath))
            WriteRegistration(records.Where(x => registeredIds.Contains(x.Id)).ToList());
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    static string? CleanHumanText(string? value, string field, int maximum)
    {
        var cleaned = Clean(value);
        if (cleaned is null) return null;
        if (cleaned.Length > maximum) throw new ArgumentException($"The {field} is too long.", field);
        if (TerminalText.ContainsUnsafe(cleaned)) throw new ArgumentException($"The {field} contains unsupported control characters.", field);
        return cleaned;
    }
    static string? ValidateEmail(string? value)
    {
        var email = Clean(value);
        if (email is null) return null;
        try { return new MailAddress(email).Address.Trim().ToLowerInvariant(); }
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
    public DateTime? ScrubbedAtUtc { get; set; }
    public decimal? MonthlyQuotaUsdOverride { get; set; }
    public bool QuotaUnlimited { get; set; }
}

public sealed record OperatorScrubResult(string AccountId, DateTime ScrubbedAtUtc, bool PendingDeliveryCancelled);

public readonly record struct OperatorAuthentication(bool IsAuthorized, string? OperatorCodeId, string AccessTier)
{ public static OperatorAuthentication Denied => new(false, null, InterpretationAccessTiers.Public); }

public readonly record struct InterpretationGenerationSelection(
    string Model, string ReasoningEffort, string? RequestedModel, string? RequestedReasoningEffort,
    string? OperatorCodeId, string RequestedPreset, string EffectivePreset, string AccessTier,
    string PresetRevision, string ResponseSchemaVersion, string TaskType, string GuidanceVariant,
    bool OmitScientificGuidance);

public static class InterpretationGenerationSelector
{
    public static bool TrySelect(HttpRequest request, ValidatedInterpretationRequest validated, InterpretationOptions options,
        OperatorCodeRegistry registry, GenerationPresetRegistry presets,
        out InterpretationGenerationSelection selection, out (int Status, string Code, string Detail) error)
    {
        var model = request.Headers["X-FTITC-Model"].FirstOrDefault();
        var reasoning = request.Headers["X-FTITC-Reasoning-Effort"].FirstOrDefault();
        var requestedGuidance = request.Headers["X-FTITC-Guidance-Variant"].FirstOrDefault();
        var hasGuidanceOverride = !string.IsNullOrWhiteSpace(requestedGuidance);
        var hasOverride = !string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(reasoning);
        var auth = registry.Authenticate(request.Headers.Authorization.FirstOrDefault());
        if (request.Headers.ContainsKey("Authorization") && !auth.IsAuthorized)
        { selection=default; error=(403,"operator_access_denied","The supplied access code is invalid, expired, or revoked."); return false; }
        var config = presets.Read();
        if (validated.RequestSchemaVersion == FtItcInterpretationClient.LegacyRequestSchemaVersion)
        {
            if (hasGuidanceOverride)
            { selection=default; error=(400,"invalid_guidance_override","Guidance selection requires relay version 5 or later."); return false; }
            if (hasOverride && (!auth.IsAuthorized || auth.AccessTier != InterpretationAccessTiers.Administrator))
            { selection=default; error=(403,"operator_access_denied","Administrator access is required for generation overrides."); return false; }
            if (hasOverride)
            {
                var effectiveModel=model ?? options.OpenAI.Model; var effectiveReasoning=reasoning ?? options.OpenAI.ReasoningEffort;
                if (!Allowed(effectiveModel,effectiveReasoning,options)) { selection=default; error=(400,"invalid_generation_override","The requested model and reasoning combination is not allowed."); return false; }
                selection=new(effectiveModel,effectiveReasoning,model,reasoning,auth.OperatorCodeId,"fast","custom",auth.AccessTier,config.Revision,FtItcInterpretationClient.LegacyResponseSchemaVersion,"interpretation",config.DefaultGuidanceVariant,false); error=default; return true;
            }
            var instant=config.Presets.Single(x=>x.Id=="instant");
            selection=new(instant.Model,instant.ReasoningEffort,null,null,auth.IsAuthorized?auth.OperatorCodeId:null,"fast","instant",auth.IsAuthorized?auth.AccessTier:InterpretationAccessTiers.Public,config.Revision,FtItcInterpretationClient.LegacyResponseSchemaVersion,"interpretation",config.DefaultGuidanceVariant,false); error=default; return true;
        }
        var tier=auth.IsAuthorized?auth.AccessTier:InterpretationAccessTiers.Public;
        var responseVersion = validated.RequestSchemaVersion == FtItcInterpretationClient.RequestSchemaVersion
            ? FtItcInterpretationClient.ResponseSchemaVersion
            : validated.RequestSchemaVersion == FtItcInterpretationClient.PreviousRequestSchemaVersion
                ? FtItcInterpretationClient.PreviousResponseSchemaVersion
                : FtItcInterpretationClient.TransitionalResponseSchemaVersion;
        if (hasGuidanceOverride && validated.RequestSchemaVersion != FtItcInterpretationClient.RequestSchemaVersion
            && validated.RequestSchemaVersion != FtItcInterpretationClient.PreviousRequestSchemaVersion)
        { selection=default; error=(400,"invalid_guidance_override","Guidance selection requires relay version 5 or later."); return false; }
        if (validated.TaskType == "summary")
        {
            if (validated.OmitScientificGuidance)
            { selection=default; error=(400,"invalid_guidance_override","Summary always uses its summary-specific guidance."); return false; }
            if (hasOverride || hasGuidanceOverride)
            { selection=default; error=(400,"invalid_generation_override","Summary uses its server-defined model and reasoning setting."); return false; }
            var summary=config.Summary;
            selection=new(summary.Model,summary.ReasoningEffort,null,null,
                auth.IsAuthorized?auth.OperatorCodeId:null,"summary","summary",tier,
                config.Revision,responseVersion,"summary",config.DefaultGuidanceVariant,false); error=default; return true;
        }
        if (validated.OmitScientificGuidance && (!auth.IsAuthorized || tier != InterpretationAccessTiers.Administrator))
        { selection=default; error=(403,"operator_access_denied","Administrator access is required to omit scientific guidance."); return false; }
        if (validated.OmitScientificGuidance && hasGuidanceOverride)
        { selection=default; error=(400,"invalid_guidance_override","Choose either a scientific-guidance variant or guidance omission, not both."); return false; }
        var guidance = validated.OmitScientificGuidance
            ? ScientificGuidance.NoGuidanceVariant
            : requestedGuidance ?? config.DefaultGuidanceVariant;
        if (hasGuidanceOverride && (!auth.IsAuthorized || tier != InterpretationAccessTiers.Administrator))
        { selection=default; error=(403,"operator_access_denied","Administrator access is required for scientific-guidance selection."); return false; }
        if (guidance != ScientificGuidance.NoGuidanceVariant && !ScientificGuidance.IsKnownVariant(guidance))
        { selection=default; error=(400,"invalid_guidance_override","The requested scientific-guidance variant is not available."); return false; }
        if (tier==InterpretationAccessTiers.Administrator)
        {
            if (validated.GenerationProfile!="custom" || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(reasoning))
            { selection=default; error=(400,"invalid_generation_override","Administrator requests require custom profile, model, and reasoning headers."); return false; }
            if (!Allowed(model,reasoning,options)) { selection=default; error=(400,"invalid_generation_override","The requested model and reasoning combination is not allowed."); return false; }
            selection=new(model,reasoning,model,reasoning,auth.OperatorCodeId,"custom","custom",tier,config.Revision,responseVersion,"interpretation",guidance,validated.OmitScientificGuidance); error=default; return true;
        }
        if (hasOverride) { selection=default; error=(403,"operator_access_denied","Administrator access is required for model and reasoning controls."); return false; }
        if (!InterpretationAccessTiers.Presets(tier).Contains(validated.GenerationProfile,StringComparer.Ordinal))
        { selection=default; error=(403,"generation_preset_denied","The selected interpretation depth is not available with this access level."); return false; }
        var preset=config.Presets.Single(x=>x.Id==validated.GenerationProfile);
        selection=new(preset.Model,preset.ReasoningEffort,null,null,auth.IsAuthorized?auth.OperatorCodeId:null,validated.GenerationProfile,preset.Id,tier,config.Revision,responseVersion,"interpretation",config.DefaultGuidanceVariant,false);
        error = default; return true;
    }

    static bool Allowed(string model,string reasoning,InterpretationOptions options) => options.AllowedModels.TryGetValue(model,out var allowed)&&allowed.ReasoningEfforts.Contains(reasoning,StringComparer.Ordinal);
}
