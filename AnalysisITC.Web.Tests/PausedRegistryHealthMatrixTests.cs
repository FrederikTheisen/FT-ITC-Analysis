using System.Text.Json;
using AnalysisITC.Web;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

/// <summary>Checks each request-path registry through the same reads used by non-billable health.</summary>
public sealed class PausedRegistryHealthMatrixTests
{
    static readonly (string Name, string Id)[] Registries =
    [
        ("presets", "interpretation.presets"),
        ("public", "interpretation.public-access"),
        ("operator", "interpretation.operator-codes"),
        ("registered", "interpretation.registered-operators"),
    ];

    public static IEnumerable<object[]> MatrixCases()
    {
        foreach (var (name, id) in Registries)
            foreach (var state in new[] { "valid", "absent", "malformed", "invalid", "unreadable" })
                yield return [name, id, state];
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void PausedHealthReadsEveryRequestPathRegistry(string registryName, string checkId, string state)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ftitc-paused-registry-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = CreateOptions(directory);
            WriteValidRegistries(options);
            var path = RegistryPath(options, registryName);
            ApplyState(path, registryName, state);

            var report = RunHealth(options);
            var checks = report.Groups.SelectMany(group => group.Checks).ToDictionary(check => check.Id);

            Assert.Equal(state is "valid" or "absent" ? HealthCheckState.Pass : HealthCheckState.Fail, checks[checkId].State);
            Assert.Equal(HealthCheckState.Skipped, checks["interpretation.transactional"].State);
            var expectedAttention = checks.Values.Any(check => check.State is HealthCheckState.Warning or HealthCheckState.Fail);
            Assert.Equal(expectedAttention ? "attention" : "healthy", report.Overall);
            Assert.Equal(expectedAttention, report.HasAttention);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    static InterpretationOptions CreateOptions(string directory)
    {
        var options = new InterpretationOptions
        {
            Enabled = true,
            AvailabilityPolicyPath = Path.Combine(directory, "availability.json"),
            PublicAccessRegistryPath = Path.Combine(directory, "public.json"),
            TombstonePath = Path.Combine(directory, "tombstones.json"),
            StatusEmailConfigurationPath = Path.Combine(directory, "status-email.json"),
            UsageLog = new() { Enabled = false, DatabasePath = Path.Combine(directory, "usage.db") },
            Registration = new()
            {
                Enabled = false,
                DatabasePath = Path.Combine(directory, "registration.db"),
                DataProtectionKeysPath = Path.Combine(directory, "keys"),
                OperatorRegistryPath = Path.Combine(directory, "registered.json"),
                AvailabilityPolicyPath = Path.Combine(directory, "registration-availability.json"),
            },
            OperatorAccess = new()
            {
                RegistryPath = Path.Combine(directory, "operator.json"),
                PresetRegistryPath = Path.Combine(directory, "presets.json"),
            },
        };

        // These are the built-in defaults' model/reasoning pairs, so the valid
        // preset file is accepted by the same allowlist validation as production.
        options.AllowedModels = new(StringComparer.Ordinal)
        {
            ["gpt-5.6-luna"] = new() { ReasoningEfforts = ["low", "high", "medium"] },
            ["gpt-5.6-terra"] = new() { ReasoningEfforts = ["high"] },
            ["gpt-5.6-sol"] = new() { ReasoningEfforts = ["high"] },
        };
        File.WriteAllText(options.AvailabilityPolicyPath, "{\"Status\":\"paused\"}");
        File.WriteAllText(options.StatusEmailConfigurationPath, "{}");
        Directory.CreateDirectory(options.Registration.DataProtectionKeysPath);
        return options;
    }

    static void WriteValidRegistries(InterpretationOptions options)
    {
        File.WriteAllText(options.PublicAccessRegistryPath, "[]");
        File.WriteAllText(options.OperatorAccess.RegistryPath, "[]");
        File.WriteAllText(options.Registration.OperatorRegistryPath, "[]");
        var presetOptions = Options.Create(options);
        var defaults = new GenerationPresetRegistry(presetOptions).Read();
        File.WriteAllText(options.OperatorAccess.PresetRegistryPath, JsonSerializer.Serialize(defaults,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    static void ApplyState(string path, string registryName, string state)
    {
        switch (state)
        {
            case "valid": return;
            case "absent": File.Delete(path); return;
            case "malformed": File.WriteAllText(path, "{ not json"); return;
            case "invalid":
                File.WriteAllText(path, registryName == "presets"
                    ? "{\"schemaVersion\":11,\"presets\":[]}" : "[{\"id\":\"bad\"}]");
                return;
            case "unreadable":
                File.Delete(path);
                Directory.CreateDirectory(path); // The real read APIs cannot read a directory as a registry file.
                return;
            default: throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    static string RegistryPath(InterpretationOptions options, string registryName) => registryName switch
    {
        "presets" => options.OperatorAccess.PresetRegistryPath,
        "public" => options.PublicAccessRegistryPath,
        "operator" => options.OperatorAccess.RegistryPath,
        "registered" => options.Registration.OperatorRegistryPath,
        _ => throw new ArgumentOutOfRangeException(nameof(registryName)),
    };

    static HealthReport RunHealth(InterpretationOptions options)
    {
        var configured = Options.Create(options);
        var interpretation = new InterpretationServiceAvailability(configured);
        var registration = new RegistrationAvailability(configured);
        return new HealthCheckService(configured, interpretation, registration).RunNonBillable();
    }
}
