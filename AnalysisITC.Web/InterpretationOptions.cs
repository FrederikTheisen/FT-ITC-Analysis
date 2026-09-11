namespace AnalysisITC.Web;

public sealed class InterpretationOptions
{
    public const string SectionName = "Interpretation";

    public bool Enabled { get; set; } = false;
    public InterpretationRateLimitOptions RateLimit { get; set; } = new();
    public OpenAIInterpretationOptions OpenAI { get; set; } = new();
    public InterpretationOperatorOptions OperatorAccess { get; set; } = new();
    public InterpretationUsageOptions UsageLog { get; set; } = new();
    public string AvailabilityPolicyPath { get; set; } = "/etc/ftitc-web/interpretation-service.json";
    public Dictionary<string, InterpretationModelOptions> AllowedModels { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, InterpretationPricingOptions> Pricing { get; set; } = new(StringComparer.Ordinal);
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
    public string[] ReasoningEfforts { get; set; } = Array.Empty<string>();
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
