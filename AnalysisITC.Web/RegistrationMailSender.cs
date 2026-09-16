using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class RegistrationMailSender
{
    readonly RegistrationOptions options;
    readonly RegistrationDeliveryOutbox outbox;
    readonly OperatorCodeRegistry registry;
    readonly IHttpClientFactory clients;
    readonly OperatorTombstoneRegistry tombstones;

    public RegistrationMailSender(IOptions<InterpretationOptions> options, RegistrationDeliveryOutbox outbox,
        OperatorCodeRegistry registry, IHttpClientFactory clients, OperatorTombstoneRegistry tombstones)
    { this.options = options.Value.Registration; this.outbox = outbox; this.registry = registry; this.clients = clients; this.tombstones = tombstones; }

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken = default)
    {
        var pending = outbox.GetPending(DateTime.UtcNow);
        if (pending is null) return false;
        if (tombstones.Contains(pending.RegistrationId)) { outbox.Cancel(pending.RegistrationId); return true; }
        try
        {
            var config = await ReadConfigAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", pending.IdempotencyKey);
            request.Content = JsonContent.Create(new
            {
                from = config.From,
                to = new[] { pending.Email },
                reply_to = new[] { config.ReplyTo },
                subject = "Your FT-ITC interpretation access",
                text = $"Hello {pending.Name},\n\nYour FT-ITC Registered access code is:\n{pending.BearerCode}\n\nEnter it in FT-ITC Preferences under AI interpretation access. Keep this code private.\n",
                html = $"<p>Hello {System.Net.WebUtility.HtmlEncode(pending.Name)},</p><p>Your FT-ITC Registered access code is:</p><p><code>{System.Net.WebUtility.HtmlEncode(pending.BearerCode)}</code></p><p>Enter it in FT-ITC Preferences under AI interpretation access. Keep this code private.</p>"
            });
            using var response = await clients.CreateClient("resend-registration").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) { outbox.MarkFailed(pending.RegistrationId, "provider_rejected"); return true; }
            if (tombstones.Contains(pending.RegistrationId)) { outbox.Cancel(pending.RegistrationId); return true; }
            registry.CreateRegisteredWithCode(pending.RegistrationId, pending.Name, pending.Email, pending.Organization, pending.BearerCode);
            outbox.MarkSent(pending.RegistrationId); return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { outbox.MarkRetry(pending.RegistrationId, pending.Attempts + 1, DateTime.UtcNow.AddMinutes(10), "delivery_timeout"); return true; }
        catch (HttpRequestException) { outbox.MarkRetry(pending.RegistrationId, pending.Attempts + 1, DateTime.UtcNow.AddMinutes(10), "provider_unavailable"); return true; }
    }

    async Task<RegistrationMailConfiguration> ReadConfigAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(options.MailConfigurationPath);
        return await JsonSerializer.DeserializeAsync<RegistrationMailConfiguration>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Registration mail configuration is unavailable.");
    }
}

public sealed class RegistrationMailConfiguration
{
    public string ApiKey { get; set; } = "";
    public string From { get; set; } = "FT-ITC Access <no-reply@ft-itc.org>";
    public string ReplyTo { get; set; } = "support@ft-itc.org";
}
