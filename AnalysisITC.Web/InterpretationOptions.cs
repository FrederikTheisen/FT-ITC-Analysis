namespace AnalysisITC.Web;

public sealed class InterpretationOptions
{
    public const string SectionName = "Interpretation";

    public bool Enabled { get; set; } = false;
    public InterpretationRateLimitOptions RateLimit { get; set; } = new();
    public OpenAIInterpretationOptions OpenAI { get; set; } = new();
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
    public string ApiKey { get; set; } = "";
    public string VectorStoreId { get; set; } = "";
    public int MaxFileSearchResults { get; set; } = 8;
    public int TimeoutSeconds { get; set; } = 600;
    public int MaxOutputTokens { get; set; } = 6000;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
