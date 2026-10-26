namespace AnalysisITC.Web;

public sealed class InterpretationOptions
{
    public const string SectionName = "Interpretation";

    public bool Enabled { get; set; } = false;
    public InterpretationRateLimitOptions RateLimit { get; set; } = new();
    public OpenAIInterpretationOptions OpenAI { get; set; } = new();
    public MistralInterpretationOptions Mistral { get; set; } = new();
    public InterpretationOperatorOptions OperatorAccess { get; set; } = new();
    public InterpretationUsageOptions UsageLog { get; set; } = new();
    public string AvailabilityPolicyPath { get; set; } = "/etc/ftitc-web/interpretation-service.json";
    public string AdminDisplayTimeZone { get; set; } = "Europe/Copenhagen";
    public string StatusEmailConfigurationPath { get; set; } = "/etc/ftitc-web/status-email.json";
    public DailyInterpretationProbeOptions DailyProbe { get; set; } = new();
    public RegistrationOptions Registration { get; set; } = new();
    public string TombstonePath { get; set; } = "/etc/ftitc-web/operator-tombstones.json";
    public string PublicAccessRegistryPath { get; set; } = "/var/lib/ftitc-web/public-access.json";
    public Dictionary<string, InterpretationModelOptions> AllowedModels { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, InterpretationPricingOptions> Pricing { get; set; } = new(StringComparer.Ordinal);
}

public sealed class DailyInterpretationProbeOptions
{
    public bool Enabled { get; set; }
    public string OperatorCodeId { get; set; } = "";
    public string CredentialPath { get; set; } = "/etc/ftitc-web/daily-interpretation-probe.json";
    public string StateDirectory { get; set; } = "/var/lib/ftitc-web/daily-interpretation-probe";

    public bool IsProbeAccount(string? id) => Enabled && !string.IsNullOrWhiteSpace(OperatorCodeId)
        && string.Equals(OperatorCodeId, id, StringComparison.Ordinal);
}

public sealed class RegistrationOptions
{
    public bool Enabled { get; set; }
    public int ActivationLifetimeHours { get; set; } = 24;
    public int ResendCooldownMinutes { get; set; } = 15;
    public int SubmissionPermitLimit { get; set; } = 5;
    public int SubmissionWindowSeconds { get; set; } = 900;
    public int ActivationPermitLimit { get; set; } = 10;
    public int ActivationWindowSeconds { get; set; } = 600;
    public string SiteKey { get; set; } = "";
    public string SecretConfigurationPath { get; set; } = "/etc/ftitc-web/registration-turnstile.json";
    public string DatabasePath { get; set; } = "/var/lib/ftitc-web/self-registration.db";
    public string TermsVersion { get; set; } = "ft-itc-terms-1.0";
    public string PrivacyVersion { get; set; } = "ft-itc-privacy-1.1";
    public string TermsPath { get; set; } = "/terms";
    public string PrivacyPath { get; set; } = "/privacy";
    public string MailConfigurationPath { get; set; } = "/etc/ftitc-web/registration-email.json";
    public string DataProtectionKeysPath { get; set; } = "/var/lib/ftitc-web/keys";
    public string OperatorRegistryPath { get; set; } = "/var/lib/ftitc-web/registered-operator-codes.json";
    public string AvailabilityPolicyPath { get; set; } = "/etc/ftitc-web/registration-service.json";
}

public sealed class InterpretationRateLimitOptions
{
    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 600;
}

public sealed class OpenAIInterpretationOptions
{
    public const string DefaultEndpoint = "https://api.openai.com/v1/responses";

    public string Endpoint { get; set; } = DefaultEndpoint;
    public string Model { get; set; } = "";
    public string ReasoningEffort { get; set; } = "medium";
    public string ApiKey { get; set; } = "";
    public string VectorStoreId { get; set; } = "";
    public int MaxFileSearchResults { get; set; } = 8;
    public int TimeoutSeconds { get; set; } = 600;
    public int MaxOutputTokens { get; set; } = 6000;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class InterpretationOperatorOptions
{
    public bool Enabled { get; set; }
    public string RegistryPath { get; set; } = "/etc/ftitc-web/operator-codes.json";
    public string PresetRegistryPath { get; set; } = "/etc/ftitc-web/generation-presets.json";
    public int DefaultLifetimeDays { get; set; } = 30;
}

public sealed class InterpretationUsageOptions
{
    public bool Enabled { get; set; } = true;
    public string DatabasePath { get; set; } = "/var/lib/ftitc-web/interpretation-usage.db";
}

public sealed class InterpretationModelOptions
{
    public string Provider { get; set; } = "openai";
    public int? MaxOutputTokens { get; set; }
    public string[] ReasoningEfforts { get; set; } = Array.Empty<string>();
}

public sealed class MistralInterpretationOptions
{
    public Dictionary<string, GenerationPresetMapping> PresetMappings { get; set; } = new(StringComparer.Ordinal);
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "https://api.eu.mistral.ai/v1/chat/completions";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 600;
    public int MaxOutputTokens { get; set; } = 15000;
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey)
        && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && uri.Scheme == "https";
}

public sealed class InterpretationPricingOptions
{
    public string Revision { get; set; } = "";
    public string EffectiveDate { get; set; } = "";
    public decimal InputPerMillion { get; set; }
    public decimal CachedInputPerMillion { get; set; }
    public decimal CacheWritePerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
    public long? LongContextThreshold { get; set; }
    public decimal? LongInputPerMillion { get; set; }
    public decimal? LongCachedInputPerMillion { get; set; }
    public decimal? LongCacheWritePerMillion { get; set; }
    public decimal? LongOutputPerMillion { get; set; }
    public decimal FileSearchPerCall { get; set; }
}
