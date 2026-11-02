using System.Net.Http.Headers;
using System.Text.Json;

namespace AnalysisITC.Web;

/// <summary>Read-only authenticated provider checks, executed only by the web process.</summary>
internal static class ProviderApiAccessCheck
{
    internal const string ClientName = "provider-api-access";
    internal const string GroupName = "Provider API access";

    internal static async Task<HealthCheckGroup> RunAsync(InterpretationOptions options, HttpClient http,
        CancellationToken cancellationToken = default)
    {
        var checks = await Task.WhenAll(
            CheckAsync(http, "provider.openai", "OpenAI", options.Enabled, options.OpenAI.Endpoint,
                "/responses", options.OpenAI.ApiKey, cancellationToken),
            CheckAsync(http, "provider.mistral", "Mistral", options.Enabled && options.Mistral.Enabled,
                options.Mistral.Endpoint, "/chat/completions", options.Mistral.ApiKey, cancellationToken));
        return new(GroupName, checks);
    }

    static async Task<HealthCheckResult> CheckAsync(HttpClient http, string id, string name, bool enabled,
        string endpoint, string suffix, string apiKey, CancellationToken cancellationToken)
    {
        if (!enabled) return HealthCheckResult.Skipped(id, name, "not enabled in application configuration");
        if (string.IsNullOrWhiteSpace(apiKey) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0
            || uri.Fragment.Length != 0 || !uri.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal))
            return HealthCheckResult.Fail(id, name, "API credentials or endpoint configuration could not be verified");

        try
        {
            // Retain the configured origin and path prefix, including Mistral's EU endpoint.
            var models = new UriBuilder(uri) { Path = uri.AbsolutePath[..^suffix.Length] + "/models" }.Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, models);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Fail(id, name, $"HTTP {(int)response.StatusCode}; authenticated model-list access check failed");
            if (response.Headers.Age is not null)
                return HealthCheckResult.Fail(id, name, "cached response; current API access could not be verified");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var limited = new SizeLimitedReadStream(stream, 1024 * 1024);
            using var body = await JsonDocument.ParseAsync(limited, cancellationToken: timeout.Token);
            var root = body.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("object", out var kind)
                || kind.ValueKind != JsonValueKind.String || kind.GetString() != "list"
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return HealthCheckResult.Fail(id, name, "API response could not be verified");
            return HealthCheckResult.Pass(id, name, "authenticated model-list request succeeded");
        }
        catch (OperationCanceledException) { return HealthCheckResult.Fail(id, name, "API access check timed out or was interrupted"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException
            or ArgumentException or FormatException or RequestBodyTooLargeException)
        { return HealthCheckResult.Fail(id, name, "API access could not be verified"); }
    }
}
