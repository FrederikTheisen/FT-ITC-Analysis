using AnalysisITC.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AnalysisITC.Web.Tests;

internal static class WebTestHostIsolation
{
    public static void AddIsolatedInterpretationStorage(IConfigurationBuilder configuration, string directory)
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Interpretation:AvailabilityPolicyPath"] = Path.Combine(directory, "interpretation-availability.json"),
            ["Interpretation:StatusEmailConfigurationPath"] = Path.Combine(directory, "status-email.json"),
            ["Interpretation:TombstonePath"] = Path.Combine(directory, "operator-tombstones.json"),
            ["Interpretation:PublicAccessRegistryPath"] = Path.Combine(directory, "public-access.json"),
            ["Interpretation:OperatorAccess:RegistryPath"] = Path.Combine(directory, "operator-codes.json"),
            ["Interpretation:OperatorAccess:PresetRegistryPath"] = Path.Combine(directory, "generation-presets.json"),
            ["Interpretation:UsageLog:DatabasePath"] = Path.Combine(directory, "interpretation-usage.db"),
            ["Interpretation:Registration:DatabasePath"] = Path.Combine(directory, "registration.db"),
            ["Interpretation:Registration:DataProtectionKeysPath"] = Path.Combine(directory, "registration-keys"),
            ["Interpretation:Registration:SecretConfigurationPath"] = Path.Combine(directory, "registration-secret.json"),
            ["Interpretation:Registration:MailConfigurationPath"] = Path.Combine(directory, "registration-mail.json"),
            ["Interpretation:Registration:AvailabilityPolicyPath"] = Path.Combine(directory, "registration-availability.json"),
            ["Interpretation:Registration:OperatorRegistryPath"] = Path.Combine(directory, "registered-operator-codes.json"),
        });
    }

    public static void RemoveRegistrationDeliveryWorker(IServiceCollection services)
    {
        foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                     && descriptor.ImplementationType == typeof(RegistrationDeliveryWorker)).ToArray())
            services.Remove(descriptor);
    }

    public static void ConfigureIsolatedHost(IWebHostBuilder builder, string directory)
    {
        builder.ConfigureAppConfiguration((_, configuration) => AddIsolatedInterpretationStorage(configuration, directory));
        builder.ConfigureServices(RemoveRegistrationDeliveryWorker);
    }
}
