using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class RegistrationDeliveryWorker : BackgroundService
{
    readonly IServiceProvider services;
    readonly IOptions<InterpretationOptions> options;
    readonly RegistrationAvailability availability;
    public RegistrationDeliveryWorker(IServiceProvider services, IOptions<InterpretationOptions> options, RegistrationAvailability availability)
    { this.services = services; this.options = options; this.availability = availability; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.Value.Registration.Enabled && availability.IsEnabled)
            {
                try { await services.GetRequiredService<RegistrationMailSender>().ProcessOneAsync(stoppingToken); }
                catch (Exception exception) { services.GetRequiredService<ILogger<RegistrationDeliveryWorker>>().LogWarning(exception, "Registration delivery attempt failed safely."); }
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
