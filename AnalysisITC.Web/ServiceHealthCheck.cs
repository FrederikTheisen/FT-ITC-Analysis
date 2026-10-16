using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnalysisITC.Web;

/// <summary>Read checks executed by the web service, with its identity and filesystem restrictions.</summary>
internal static class ServiceHealthCheck
{
    internal const string Url = "http://127.0.0.1:5000/api/internal/health";
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter<HealthCheckState>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    internal static async Task<HealthReport> ReadAsync()
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(15) };
        return await ReadAsync(http);
    }

    internal static async Task<HealthReport> ReadAsync(HttpClient http)
    {
        using var response = await http.GetAsync(Url);
        response.EnsureSuccessStatusCode();
        var report = JsonSerializer.Deserialize<HealthReport>(await response.Content.ReadAsStringAsync(), JsonOptions);
        if (report is null || report.CheckedAtUtc == default || report.Groups is null
            || new[] { "Interpretation", "Registration", "Viewer", "Storage" }
                .Any(name => !report.Groups.Any(group => group?.Name == name))
            || report.Groups.Any(group => group is null || group.Checks is null || group.Checks.Count == 0
                || group.Checks.Any(check => check is null || !Enum.IsDefined(check.State)
                    || string.IsNullOrWhiteSpace(check.Id) || string.IsNullOrWhiteSpace(check.Name) || check.Reason is null)))
            throw new InvalidDataException("The web service health report is incomplete.");
        return report;
    }
}
