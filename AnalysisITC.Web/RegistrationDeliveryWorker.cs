using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class RegistrationDeliveryWorker : BackgroundService
{
    readonly IServiceProvider services;
    readonly IOptions<InterpretationOptions> options;
    DateTime nextRetentionRunUtc = DateTime.MinValue;
    public RegistrationDeliveryWorker(IServiceProvider services, IOptions<InterpretationOptions> options)
    { this.services = services; this.options = options; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Retention runs independently of registration availability and before the mail gate.
            // Running at startup also catches up after a host was offline during its daily run.
            var now = DateTime.UtcNow;
            if (now >= nextRetentionRunUtc)
            {
                nextRetentionRunUtc = now.AddDays(1);
                try { services.GetRequiredService<RegistrationRetentionService>().Run(now); }
                catch (Exception exception)
                {
                    services.GetRequiredService<ILogger<RegistrationDeliveryWorker>>()
                        .LogWarning("Registration retention cleanup failed safely ({ExceptionType}).", exception.GetType().Name);
                }
            }

            // Pausing public registration stops new submissions, not delivery or activation
            // already requested by a user.
            if (options.Value.Registration.Enabled)
            {
                try { await services.GetRequiredService<RegistrationMailSender>().ProcessOneAsync(stoppingToken); }
                catch (Exception exception)
                {
                    services.GetRequiredService<ILogger<RegistrationDeliveryWorker>>()
                        .LogWarning("Registration delivery attempt failed safely ({ExceptionType}).", exception.GetType().Name);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
