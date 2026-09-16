using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class TurnstileVerifier
{
    const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    readonly RegistrationOptions options;
    readonly IHttpClientFactory clients;

    public TurnstileVerifier(IOptions<InterpretationOptions> options, IHttpClientFactory clients)
    { this.options = options.Value.Registration; this.clients = clients; }

    public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || !File.Exists(options.SecretConfigurationPath)) return false;
        var configuration = await ReadConfigurationAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(configuration.SecretKey)) return false;
        using var request = new HttpRequestMessage(HttpMethod.Post, VerifyUrl)
        {
            Content = JsonContent.Create(new { secret = configuration.SecretKey, response = token, remoteip = remoteIp })
        };
        using var response = await clients.CreateClient("turnstile").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken: cancellationToken);
        return result?.Success == true && (result.Hostname is null || result.Hostname.Equals("app.ft-itc.org", StringComparison.OrdinalIgnoreCase));
    }

    async Task<TurnstileConfiguration> ReadConfigurationAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(options.SecretConfigurationPath);
        return await System.Text.Json.JsonSerializer.DeserializeAsync<TurnstileConfiguration>(stream, cancellationToken: cancellationToken)
            ?? new TurnstileConfiguration();
    }

    sealed class TurnstileResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("hostname")] public string? Hostname { get; set; }
    }
}

public sealed class TurnstileConfiguration
{
    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
}
