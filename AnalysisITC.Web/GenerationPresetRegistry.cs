using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public static class InterpretationAccessTiers
{
    public const string Public = "public";
    public const string Standard = "standard";
    public const string Advanced = "advanced";
    public const string Administrator = "administrator";
    public static readonly string[] Assignable = [Standard, Advanced, Administrator];
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
    readonly InterpretationOptions options;
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public GenerationPresetRegistry(IOptions<InterpretationOptions> options) => this.options = options.Value;

    public GenerationPresetConfiguration Read()
    {
        var path = options.OperatorAccess.PresetRegistryPath;
        var value = File.Exists(path)
            ? JsonSerializer.Deserialize<GenerationPresetConfiguration>(File.ReadAllText(path), JsonOptions)
            : Defaults();
        Validate(value ?? throw new InvalidDataException("The generation-preset registry is invalid."));
        return value!;
    }

    public GenerationPresetConfiguration Update(string presetId, string model, string reasoning)
    {
        var value = Read();
        var preset = value.Presets.SingleOrDefault(item => item.Id == presetId)
            ?? throw new ArgumentException("Unknown preset.", nameof(presetId));
        preset.Model = model; preset.ReasoningEffort = reasoning;
        value.Revision = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        value.ModifiedAtUtc = DateTime.UtcNow;
        Validate(value); Write(value); return value;
    }

    public void EnsureFile()
    {
        if (!File.Exists(options.OperatorAccess.PresetRegistryPath)) Write(Defaults());
    }

    void Validate(GenerationPresetConfiguration value)
    {
        var ids = value.Presets.Select(x => x.Id).ToArray();
        string[] required = ["instant", "fast", "standard", "in-depth"];
        if (ids.Length != required.Length || !ids.SequenceEqual(required, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(value.Revision))
            throw new InvalidDataException("The generation-preset registry must contain the four fixed presets in display order.");
        foreach (var preset in value.Presets)
            if (!options.AllowedModels.TryGetValue(preset.Model, out var model) || !model.ReasoningEfforts.Contains(preset.ReasoningEffort, StringComparer.Ordinal))
                throw new InvalidDataException($"Preset '{preset.Id}' uses an unsupported model/reasoning combination.");
    }

    void Write(GenerationPresetConfiguration value)
    {
        var path = options.OperatorAccess.PresetRegistryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Preset registry path has no directory."));
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static GenerationPresetConfiguration Defaults() => new()
    {
        Revision = "initial-1",
        ModifiedAtUtc = DateTime.UtcNow,
        Presets =
        [
            new() { Id="instant", DisplayName="Instant", Model="gpt-5.6-luna", ReasoningEffort="none" },
            new() { Id="fast", DisplayName="Fast", Model="gpt-5.6-luna", ReasoningEffort="medium" },
            new() { Id="standard", DisplayName="Standard", Model="gpt-5.6-terra", ReasoningEffort="medium" },
            new() { Id="in-depth", DisplayName="In-depth", Model="gpt-5.6-sol", ReasoningEffort="high" },
        ],
    };
}

public sealed class GenerationPresetConfiguration
{
    public string Revision { get; set; } = "";
    public DateTime ModifiedAtUtc { get; set; }
    public List<GenerationPreset> Presets { get; set; } = [];
}

public sealed class GenerationPreset
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Model { get; set; } = "";
    public string ReasoningEffort { get; set; } = "";
}
