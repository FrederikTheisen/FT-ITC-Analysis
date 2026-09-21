using System.Globalization;
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
            if (pending.Kind == RegistrationMessageKinds.AccessCode)
                registry.CreateRegisteredWithCode(pending.RegistrationId, pending.Name, pending.Email, pending.Organization, pending.Secret);
            var config = await ReadConfigAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", pending.IdempotencyKey);
            request.Content = JsonContent.Create(Message(config, pending));
            using var response = await clients.CreateClient("resend-registration").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (pending.Kind == RegistrationMessageKinds.AccessCode
                    || response.StatusCode is System.Net.HttpStatusCode.RequestTimeout
                    or System.Net.HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500)
                    RetryOrFail(pending, "provider_unavailable");
                else outbox.MarkFailed(pending.RegistrationId, "provider_rejected");
                return true;
            }
            if (tombstones.Contains(pending.RegistrationId)) { outbox.Cancel(pending.RegistrationId); return true; }
            outbox.MarkSent(pending.RegistrationId, pending.Kind, DateTime.UtcNow); return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { RetryOrFail(pending, "delivery_timeout"); return true; }
        catch (HttpRequestException) { RetryOrFail(pending, "provider_unavailable"); return true; }
        catch (InvalidDataException) { outbox.MarkFailed(pending.RegistrationId, "account_conflict"); return true; }
    }

    internal static object RenderDiagnosticMessage(RegistrationOptions options, RegistrationMailConfiguration config, string kind)
    {
        var pending = new PendingRegistrationDelivery(
            "diagnostic-registration", kind,
            kind == RegistrationMessageKinds.Activation ? "ftitc_act_diagnostic" : "ftitc_op_diagnostic",
            "diagnostic-idempotency-key", 0, "diagnostic name", "diagnostic@example.invalid", null);
        return Message(options, config, pending);
    }

    object Message(RegistrationMailConfiguration config, PendingRegistrationDelivery pending)
        => Message(options, config, pending);

    static object Message(RegistrationOptions options, RegistrationMailConfiguration config, PendingRegistrationDelivery pending)
    {
        if (pending.Kind == RegistrationMessageKinds.AccessCode
            && !string.IsNullOrWhiteSpace(config.AccessCodeTemplateId))
            return TemplateMessage(config, pending, config.AccessCodeTemplateId, new Dictionary<string, object?>
            {
                ["NAME"] = pending.Name,
                ["ACCESS_CODE"] = pending.Secret,
            });
        if (pending.Kind == RegistrationMessageKinds.Activation
            && !string.IsNullOrWhiteSpace(config.ActivationTemplateId))
            return TemplateMessage(config, pending, config.ActivationTemplateId, new Dictionary<string, object?>
            {
                ["NAME"] = pending.Name,
                ["ACTIVATION_URL"] = "https://ft-itc.org/activate#token=" + Uri.EscapeDataString(pending.Secret),
                ["EXPIRY_HOURS"] = options.ActivationLifetimeHours.ToString(CultureInfo.InvariantCulture),
            });
        return pending.Kind == RegistrationMessageKinds.AccessCode
            ? AccessCodeMessage(config, pending)
            : ActivationMessage(options, config, pending);
    }

    static object TemplateMessage(RegistrationMailConfiguration config, PendingRegistrationDelivery pending,
        string templateId, object variables) => new
    {
        from = config.From,
        to = new[] { pending.Email },
        reply_to = new[] { config.ReplyTo },
        template = new { id = templateId.Trim(), variables },
    };

    public async Task<bool> SendAccessCodeAsync(OperatorCodeRecord account, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.Email)) return false;
        try
        {
            var config = await ReadConfigAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Headers.TryAddWithoutValidation("Idempotency-Key", $"ftitc-admin-access-{account.Id}-{Guid.NewGuid():N}");
            request.Content = JsonContent.Create(AccessCodeMessage(config, account.Name ?? account.Label, account.Email, code));
            using var response = await clients.CreateClient("resend-registration").SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (IOException) { return false; }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    static object ActivationMessage(RegistrationOptions options, RegistrationMailConfiguration config, PendingRegistrationDelivery pending)
    {
        var link = "https://ft-itc.org/activate#token=" + Uri.EscapeDataString(pending.Secret);
        var hours = options.ActivationLifetimeHours;
        return new
        {
            from = config.From, to = new[] { pending.Email }, reply_to = new[] { config.ReplyTo },
            subject = "Activate your FT-ITC interpretation access",
            text = $"Hello {pending.Name},\n\nConfirm your email address to activate FT-ITC Registered interpretation access. This provides additional hosted AI interpretation options and quota in the FT-ITC desktop application.\n\nActivate my account:\n{link}\n\nThis single-use link expires after {hours} hours. Do not share it. If you did not request access, you can ignore this message.\n\nTerms: https://ft-itc.org/terms\nPrivacy: https://ft-itc.org/privacy\nSupport: support@ft-itc.org\n",
            html = $"<div style='font-family:system-ui,-apple-system,sans-serif;max-width:600px;color:#172033'><p style='font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#52647a'>FT-ITC Analysis</p><h1 style='font-size:28px'>Activate interpretation access</h1><p>Hello {System.Net.WebUtility.HtmlEncode(pending.Name)},</p><p>Confirm your email address to activate Registered access to additional hosted AI interpretation options and quota in the FT-ITC desktop application.</p><p style='margin:28px 0'><a href='{System.Net.WebUtility.HtmlEncode(link)}' style='display:inline-block;padding:13px 20px;background:#1769aa;color:#fff;text-decoration:none;border-radius:7px;font-weight:600'>Activate my account</a></p><p>This single-use link expires after {hours} hours. Do not share it. If you did not request access, you can ignore this message.</p><p><a href='https://ft-itc.org/terms'>Terms</a> · <a href='https://ft-itc.org/privacy'>Privacy</a> · <a href='mailto:support@ft-itc.org'>Support</a></p></div>"
        };
    }

    static object AccessCodeMessage(RegistrationMailConfiguration config, PendingRegistrationDelivery pending)
        => AccessCodeMessage(config, pending.Name, pending.Email, pending.Secret);

    static object AccessCodeMessage(RegistrationMailConfiguration config, string name, string email, string code) => new
    {
        from = config.From, to = new[] { email }, reply_to = new[] { config.ReplyTo },
        subject = "Your FT-ITC interpretation access is ready",
        text = $"Hello {name},\n\nYour FT-ITC interpretation access code is:\n\n{code}\n\nEnter it in FT-ITC Preferences under AI interpretation access. This replaces your previous code. Keep it private.\n\nSupport: support@ft-itc.org\n",
        html = $"<div style='font-family:system-ui,-apple-system,sans-serif;max-width:600px;color:#172033'><p style='font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#52647a'>FT-ITC Analysis</p><h1 style='font-size:28px'>Your access code was renewed</h1><p>Hello {System.Net.WebUtility.HtmlEncode(name)},</p><p>Your new FT-ITC interpretation access code is:</p><p style='padding:14px;background:#f0f4f8;border-radius:7px;font-size:17px;overflow-wrap:anywhere'><code>{System.Net.WebUtility.HtmlEncode(code)}</code></p><p>This replaces your previous code. Enter it in FT-ITC Preferences under AI interpretation access and keep it private.</p><p><a href='mailto:support@ft-itc.org'>Contact support</a></p></div>"
    };

    void RetryOrFail(PendingRegistrationDelivery pending, string safeFailureCode)
    {
        const int maximumAttempts = 5;
        var attempts = pending.Attempts + 1;
        if (attempts >= maximumAttempts) outbox.MarkFailed(pending.RegistrationId, safeFailureCode);
        else outbox.MarkRetry(pending.RegistrationId, attempts, DateTime.UtcNow.AddMinutes(10), safeFailureCode);
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
    public string? ActivationTemplateId { get; set; }
    public string? AccessCodeTemplateId { get; set; }
}
