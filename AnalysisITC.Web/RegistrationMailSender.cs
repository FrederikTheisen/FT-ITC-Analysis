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
                subject = "Activate your FT-ITC interpretation access",
                text = $"Hello {pending.Name},\n\nActivate your FT-ITC Registered interpretation access within 24 hours:\nhttps://ft-itc.org/activate#token={Uri.EscapeDataString(pending.BearerCode)}\n\nDo not share this link. If you did not request access, you can ignore this message.\n\nTerms: https://ft-itc.org/terms\nPrivacy: https://ft-itc.org/privacy\n",
                html = $"<div style='font-family:system-ui,sans-serif;max-width:600px'><h1>FT-ITC Analysis</h1><p>Hello {System.Net.WebUtility.HtmlEncode(pending.Name)},</p><p>Confirm your email address to activate Registered interpretation access.</p><p><a href='https://ft-itc.org/activate#token={Uri.EscapeDataString(pending.BearerCode)}' style='display:inline-block;padding:12px 18px;background:#1769aa;color:white;text-decoration:none;border-radius:6px'>Activate my account</a></p><p>This link expires after 24 hours and should not be shared.</p><p><a href='https://ft-itc.org/terms'>Terms</a> · <a href='https://ft-itc.org/privacy'>Privacy</a> · <a href='mailto:support@ft-itc.org'>Support</a></p></div>"
            });
            using var response = await clients.CreateClient("resend-registration").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) { outbox.MarkFailed(pending.RegistrationId, "provider_rejected"); return true; }
            if (tombstones.Contains(pending.RegistrationId)) { outbox.Cancel(pending.RegistrationId); return true; }
            outbox.MarkSent(pending.RegistrationId); return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { outbox.MarkRetry(pending.RegistrationId, pending.Attempts + 1, DateTime.UtcNow.AddMinutes(10), "delivery_timeout"); return true; }
        catch (HttpRequestException) { outbox.MarkRetry(pending.RegistrationId, pending.Attempts + 1, DateTime.UtcNow.AddMinutes(10), "provider_unavailable"); return true; }
    }

    public async Task<bool> SendAccessCodeAsync(PendingRegistrationDelivery pending, string bearerCode, CancellationToken cancellationToken = default)
    {
        var config = await ReadConfigAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "ftitc-access-" + pending.RegistrationId);
        request.Content = JsonContent.Create(new { from = config.From, to = new[] { pending.Email }, reply_to = new[] { config.ReplyTo }, subject = "Your FT-ITC interpretation access is ready", text = $"Hello {pending.Name},\n\nYour Registered FT-ITC access code is:\n{bearerCode}\n\nEnter it in FT-ITC Preferences under AI interpretation access. Do not share it.\n", html = $"<div style='font-family:system-ui,sans-serif;max-width:600px'><h1>Your access is ready</h1><p>Hello {System.Net.WebUtility.HtmlEncode(pending.Name)},</p><p>Your FT-ITC Registered interpretation access code is:</p><p style='font-size:1.1em'><code>{System.Net.WebUtility.HtmlEncode(bearerCode)}</code></p><p>Enter it in Preferences under AI interpretation access. Keep this code private.</p></div>" });
        using var response = await clients.CreateClient("resend-registration").SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
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
