using Microsoft.Extensions.Configuration;

namespace AnalysisITC.Web;

/// <summary>
/// Loads only the configuration needed by the standalone status-email process.
/// The web service keeps its settings in interpretation.env, but that file also
/// contains the OpenAI credential.  The scheduled report must not load or bind
/// that credential.
/// </summary>
internal static class StatusEmailConfigurationLoader
{
    internal const string SharedEnvironmentPath = "/etc/ftitc-web/interpretation.env";

    static readonly IReadOnlyDictionary<string, string> SafeKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Interpretation__Registration__Enabled"] = "Interpretation:Registration:Enabled",
        ["Interpretation__Registration__ActivationLifetimeHours"] = "Interpretation:Registration:ActivationLifetimeHours",
        ["Interpretation__Registration__ResendCooldownMinutes"] = "Interpretation:Registration:ResendCooldownMinutes",
        ["Interpretation__Registration__SubmissionPermitLimit"] = "Interpretation:Registration:SubmissionPermitLimit",
        ["Interpretation__Registration__SubmissionWindowSeconds"] = "Interpretation:Registration:SubmissionWindowSeconds",
        ["Interpretation__Registration__ActivationPermitLimit"] = "Interpretation:Registration:ActivationPermitLimit",
        ["Interpretation__Registration__ActivationWindowSeconds"] = "Interpretation:Registration:ActivationWindowSeconds",
        ["Interpretation__Registration__SiteKey"] = "Interpretation:Registration:SiteKey",
        ["Interpretation__Registration__SecretConfigurationPath"] = "Interpretation:Registration:SecretConfigurationPath",
        ["Interpretation__Registration__DatabasePath"] = "Interpretation:Registration:DatabasePath",
        ["Interpretation__Registration__TermsVersion"] = "Interpretation:Registration:TermsVersion",
        ["Interpretation__Registration__PrivacyVersion"] = "Interpretation:Registration:PrivacyVersion",
        ["Interpretation__Registration__TermsPath"] = "Interpretation:Registration:TermsPath",
        ["Interpretation__Registration__PrivacyPath"] = "Interpretation:Registration:PrivacyPath",
        ["Interpretation__Registration__MailConfigurationPath"] = "Interpretation:Registration:MailConfigurationPath",
        ["Interpretation__Registration__DataProtectionKeysPath"] = "Interpretation:Registration:DataProtectionKeysPath",
        ["Interpretation__Registration__OperatorRegistryPath"] = "Interpretation:Registration:OperatorRegistryPath",
        ["Interpretation__Registration__AvailabilityPolicyPath"] = "Interpretation:Registration:AvailabilityPolicyPath",
        ["Interpretation__UsageLog__Enabled"] = "Interpretation:UsageLog:Enabled",
        ["Interpretation__UsageLog__DatabasePath"] = "Interpretation:UsageLog:DatabasePath",
        ["Interpretation__StatusEmailConfigurationPath"] = "Interpretation:StatusEmailConfigurationPath",
    };

    internal static IConfiguration Build(string? sourcePath = null)
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddInMemoryCollection(ReadSafeEnvironment(sourcePath ?? SharedEnvironmentPath))
            .Build();
    }

    internal static IReadOnlyDictionary<string, string?> ReadSafeEnvironment(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var name = line[..separator].Trim();
            if (!SafeKeys.TryGetValue(name, out var configurationKey)) continue;
            values[configurationKey] = Unquote(line[(separator + 1)..].Trim());
        }

        return values;
    }

    static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }
}
