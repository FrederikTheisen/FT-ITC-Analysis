using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public static class InterpretationAccessTiers
{
    public const string Public = "public";
    public const string Standard = "standard"; // stable ID; displayed as Registered
    public const string Advanced = "advanced";
    public const string Administrator = "administrator";
    public static readonly string[] Assignable = [Standard, Advanced, Administrator];

    public static string DisplayName(string tier) => tier switch
    {
        Standard => "Registered",
        Advanced => "Advanced",
        Administrator => "Administrator",
        _ => "Public",
    };

    public static IReadOnlyList<string> Presets(string tier) => tier switch
    {
        Standard => ["instant", "fast", "standard"],
        Advanced => ["instant", "fast", "standard", "in-depth"],
        _ => ["instant"],
    };

    public static bool IsAssignable(string value) => Assignable.Contains(value, StringComparer.Ordinal);
}

public sealed class GenerationPresetRegistry
{
    const int CurrentSchemaVersion = 7;
    public const int AbsoluteMaximumRequestKiB = 2048;
    public const int MaximumDescriptionLength = 500;
    readonly InterpretationOptions options;
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public GenerationPresetRegistry(IOptions<InterpretationOptions> options) => this.options = options.Value;

    public GenerationPresetConfiguration Read()
    {
        var path = options.OperatorAccess.PresetRegistryPath;
        var value = File.Exists(path)
            ? JsonSerializer.Deserialize<GenerationPresetConfiguration>(File.ReadAllText(path), JsonOptions)
            : Defaults();
        value = Upgrade(value ?? throw new InvalidDataException("The generation-preset registry is invalid."));
        Validate(value);
        return value;
    }

    public GenerationPresetConfiguration Update(string presetId, string model, string reasoning)
    {
        var value = Read();
        if (presetId == "summary")
        {
            value.Summary.Model = model;
            value.Summary.ReasoningEffort = reasoning;
            Touch(value); Write(value); return value;
        }
        var preset = value.Presets.SingleOrDefault(item => item.Id == presetId)
            ?? throw new ArgumentException("Unknown preset.", nameof(presetId));
        preset.Model = model;
        preset.ReasoningEffort = reasoning;
        Touch(value);
        Write(value);
        return value;
    }

    public GenerationPresetConfiguration UpdateDescription(string presetId, string description)
    {
        var value = Read();
        var preset = presetId == "summary"
            ? value.Summary
            : value.Presets.SingleOrDefault(item => item.Id == presetId)
                ?? throw new ArgumentException("Unknown preset.", nameof(presetId));
        preset.Description = NormalizeDescription(description);
        Touch(value);
        Write(value);
        return value;
    }

    public GenerationPresetConfiguration UpdateQuota(string accessTier, decimal monthlyUsd)
    {
        if (monthlyUsd <= 0) throw new ArgumentOutOfRangeException(nameof(monthlyUsd));
        var value = Read();
        var quota = value.Quotas.SingleOrDefault(item => item.AccessTier == accessTier)
            ?? throw new ArgumentException("Unknown quota policy.");
        quota.MonthlyUsd = monthlyUsd;
        Touch(value);
        Write(value);
        return value;
    }

    public GenerationPresetConfiguration UpdateRequestSizeLimit(string accessTier, int maximumKiB)
    {
        if (maximumKiB is < 1 or > AbsoluteMaximumRequestKiB) throw new ArgumentOutOfRangeException(nameof(maximumKiB));
        var value = Read();
        var limit = value.RequestSizeLimits.SingleOrDefault(item => item.AccessTier == accessTier)
            ?? throw new ArgumentException("Unknown access tier.", nameof(accessTier));
        limit.MaximumKiB = maximumKiB;
        Touch(value); Write(value); return value;
    }

    public long MaximumRequestBytes(string accessTier) =>
        checked((long)Read().RequestSizeLimits.Single(item => item.AccessTier == accessTier).MaximumKiB * 1024L);

    public void EnsureFile()
    {
        var path = options.OperatorAccess.PresetRegistryPath;
        if (!File.Exists(path)) { Write(Defaults()); return; }
        var stored = JsonSerializer.Deserialize<GenerationPresetConfiguration>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The generation-preset registry is invalid.");
        if (stored.SchemaVersion < CurrentSchemaVersion) Write(Upgrade(stored));
    }

    void Validate(GenerationPresetConfiguration value)
    {
        string[] required = ["instant", "fast", "standard", "in-depth"];
        var ids = value.Presets.Select(x => x.Id).ToArray();
        if (value.SchemaVersion != CurrentSchemaVersion || ids.Length != required.Length
            || !ids.SequenceEqual(required, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(value.Revision))
            throw new InvalidDataException("The generation-preset registry must contain the four fixed presets in display order.");
        if (value.Summary is null || value.Summary.Id != "summary" || value.Summary.DisplayName != "Summary"
            || !IsValidDescription(value.Summary.Description)
            || !options.AllowedModels.TryGetValue(value.Summary.Model, out var summaryModel)
            || !summaryModel.ReasoningEfforts.Contains(value.Summary.ReasoningEffort, StringComparer.Ordinal))
            throw new InvalidDataException("The Summary generation task is invalid.");
        foreach (var preset in value.Presets)
            if (string.IsNullOrWhiteSpace(preset.DisplayName) || !IsValidDescription(preset.Description)
                || !options.AllowedModels.TryGetValue(preset.Model, out var model)
                || !model.ReasoningEfforts.Contains(preset.ReasoningEffort, StringComparer.Ordinal))
                throw new InvalidDataException($"Preset '{preset.Id}' is invalid.");
        if (value.Quotas.Count != 2 || value.Quotas.Any(x => x.MonthlyUsd <= 0)
            || !value.Quotas.Any(x => x.AccessTier == InterpretationAccessTiers.Standard)
            || !value.Quotas.Any(x => x.AccessTier == InterpretationAccessTiers.Advanced))
            throw new InvalidDataException("The registry must contain the Registered and Advanced account quota policies.");
        string[] tiers = [InterpretationAccessTiers.Public, InterpretationAccessTiers.Standard,
            InterpretationAccessTiers.Advanced, InterpretationAccessTiers.Administrator];
        if (value.RequestSizeLimits.Count != tiers.Length
            || !value.RequestSizeLimits.Select(x => x.AccessTier).SequenceEqual(tiers, StringComparer.Ordinal)
            || value.RequestSizeLimits.Any(x => x.MaximumKiB is < 1 or > AbsoluteMaximumRequestKiB))
            throw new InvalidDataException("The registry must contain valid request-size limits for all four access tiers.");
    }

    void Write(GenerationPresetConfiguration value)
    {
        Validate(value);
        var path = options.OperatorAccess.PresetRegistryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Preset registry path has no directory."));
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static void Touch(GenerationPresetConfiguration value)
    {
        value.Revision = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        value.ModifiedAtUtc = DateTime.UtcNow;
    }

    static GenerationPresetConfiguration Upgrade(GenerationPresetConfiguration value)
    {
        if (value.SchemaVersion >= CurrentSchemaVersion) return value;
        var upgraded = Defaults();
        if (value.SchemaVersion >= 2)
        {
            upgraded.Presets = value.Presets;
            upgraded.QuotaAccountingStartedAtUtc = value.QuotaAccountingStartedAtUtc;
        }
        if (value.SchemaVersion >= 3) upgraded.Quotas = value.Quotas;
        if (value.SchemaVersion >= 4) upgraded.RequestSizeLimits = value.RequestSizeLimits;
        if (value.SchemaVersion >= 5) upgraded.Summary = value.Summary;
        foreach (var preset in upgraded.Presets)
        {
            preset.DisplayName = preset.Id switch
            {
                "instant" => "Fast",
                "fast" => "Default",
                "standard" => "Advanced",
                "in-depth" => "Comprehensive",
                _ => preset.DisplayName,
            };
            if (string.IsNullOrWhiteSpace(preset.Description)) preset.Description = DefaultDescription(preset.Id);
        }
        if (string.IsNullOrWhiteSpace(upgraded.Summary.Description))
            upgraded.Summary.Description = DefaultDescription("summary");
        upgraded.Revision = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        return upgraded;
    }

    static GenerationPresetConfiguration Defaults()
    {
        var now = DateTime.UtcNow;
        return new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Revision = "presets-7",
            ModifiedAtUtc = now,
            QuotaAccountingStartedAtUtc = now,
            Presets =
            [
                new() { Id = "instant", DisplayName = "Fast", Description = DefaultDescription("instant"), Model = "gpt-5.6-luna", ReasoningEffort = "low" },
                new() { Id = "fast", DisplayName = "Default", Description = DefaultDescription("fast"), Model = "gpt-5.6-luna", ReasoningEffort = "high" },
                new() { Id = "standard", DisplayName = "Advanced", Description = DefaultDescription("standard"), Model = "gpt-5.6-terra", ReasoningEffort = "high" },
                new() { Id = "in-depth", DisplayName = "Comprehensive", Description = DefaultDescription("in-depth"), Model = "gpt-5.6-sol", ReasoningEffort = "high" },
            ],
            Summary = new() { Id = "summary", DisplayName = "Summary", Description = DefaultDescription("summary"), Model = "gpt-5.6-luna", ReasoningEffort = "medium" },
            Quotas =
            [
                new() { AccessTier = InterpretationAccessTiers.Standard, MonthlyUsd = 1m },
                new() { AccessTier = InterpretationAccessTiers.Advanced, MonthlyUsd = 3m },
            ],
            RequestSizeLimits =
            [
                new() { AccessTier = InterpretationAccessTiers.Public, MaximumKiB = 128 },
                new() { AccessTier = InterpretationAccessTiers.Standard, MaximumKiB = 512 },
                new() { AccessTier = InterpretationAccessTiers.Advanced, MaximumKiB = 1024 },
                new() { AccessTier = InterpretationAccessTiers.Administrator, MaximumKiB = 2048 },
            ],
        };
    }

    static string NormalizeDescription(string description)
    {
        var value = description?.Trim() ?? "";
        if (!IsValidDescription(value))
            throw new ArgumentException($"Description must contain 1–{MaximumDescriptionLength} characters and no control characters.", nameof(description));
        return value;
    }

    static bool IsValidDescription(string? description) =>
        !string.IsNullOrWhiteSpace(description)
        && description.Length <= MaximumDescriptionLength
        && string.Equals(description, description.Trim(), StringComparison.Ordinal)
        && !description.Any(char.IsControl);

    static string DefaultDescription(string id) => id switch
    {
        "summary" => "A concise factual summary of the supplied results. Uses summary-specific guidance and avoids adding substantial scientific interpretation.",
        "instant" => "A quick analysis and interpretation of the supplied data package.",
        "fast" => "A balanced analysis with additional reasoning and interpretation.",
        "standard" => "A deeper scientific analysis using more capable reasoning.",
        "in-depth" => "The most extensive investigation of the supplied data package, using advanced reasoning.",
        _ => throw new ArgumentException("Unknown preset ID.", nameof(id)),
    };
}

public sealed class GenerationPresetConfiguration
{
    public int SchemaVersion { get; set; }
    public string Revision { get; set; } = "";
    public DateTime ModifiedAtUtc { get; set; }
    public DateTime QuotaAccountingStartedAtUtc { get; set; }
    public List<GenerationPreset> Presets { get; set; } = [];
    public GenerationPreset Summary { get; set; } = new();
    public List<GenerationQuotaPolicy> Quotas { get; set; } = [];
    public List<TierRequestSizeLimit> RequestSizeLimits { get; set; } = [];
}

public sealed class GenerationPreset
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Model { get; set; } = "";
    public string ReasoningEffort { get; set; } = "";
}

public sealed class GenerationQuotaPolicy
{
    public string AccessTier { get; set; } = "";
    public decimal MonthlyUsd { get; set; }
}

public sealed class TierRequestSizeLimit
{
    public string AccessTier { get; set; } = "";
    public int MaximumKiB { get; set; }
}
