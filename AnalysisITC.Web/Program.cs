using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Viewer;
using AnalysisITC.Web;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

const long MaxUploadBytes = 50L * 1024 * 1024;
const string ViewerBuild = "2026.09.11-preset-descriptions.1";
const string InterpretationRateLimitPolicy = "interpretation-generation";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<InterpretationOptions>()
    .Bind(builder.Configuration.GetSection(InterpretationOptions.SectionName))
    .Validate(options => options.RateLimit is not null, "Interpretation rate-limit settings are required.")
    .Validate(options => options.RateLimit?.PermitLimit > 0, "Interpretation rate-limit permit count must be positive.")
    .Validate(options => options.RateLimit?.WindowSeconds > 0, "Interpretation rate-limit window must be positive.")
    .Validate(options => options.OpenAI is not null, "OpenAI interpretation settings are required.")
    .Validate(options => options.OpenAI?.TimeoutSeconds is > 0 and <= 600, "OpenAI timeout must be between 1 and 600 seconds.")
    .Validate(options => options.OpenAI?.MaxOutputTokens is > 0 and <= 100000, "OpenAI output-token limit must be positive and bounded.")
    .Validate(options => options.OpenAI?.MaxFileSearchResults is > 0 and <= 50, "OpenAI file-search result limit must be between 1 and 50.")
    .Validate(options => string.IsNullOrWhiteSpace(options.OpenAI?.VectorStoreId)
        || (options.OpenAI.VectorStoreId.StartsWith("vs_", StringComparison.Ordinal)
            && options.OpenAI.VectorStoreId.Length > 3), "The OpenAI vector-store ID must begin with 'vs_'.")
    .Validate(options => Uri.TryCreate(options.OpenAI?.Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps, "The OpenAI endpoint must be an absolute HTTPS URI.")
    .ValidateOnStart();
builder.Services.AddSingleton<InterpretationRequestReader>();
builder.Services.AddScoped<InterpretationRelayService>();
builder.Services.AddSingleton<OperatorCodeRegistry>();
builder.Services.AddSingleton<GenerationPresetRegistry>();
builder.Services.AddSingleton<InterpretationUsageStore>();
builder.Services.AddSingleton<InterpretationQuotaService>();
builder.Services.AddSingleton<InterpretationServiceAvailability>();
var openAIConfiguration = builder.Configuration
    .GetSection(InterpretationOptions.SectionName)
    .GetSection(nameof(InterpretationOptions.OpenAI))
    .Get<OpenAIInterpretationOptions>();
if (openAIConfiguration?.IsConfigured == true)
{
    builder.Services.AddHttpClient<OpenAIInterpretationProvider>((services, client) =>
    {
        var settings = services.GetRequiredService<IOptions<InterpretationOptions>>().Value.OpenAI;
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    });
    builder.Services.AddScoped<IAnalysisInterpretationProvider>(services =>
        services.GetRequiredService<OpenAIInterpretationProvider>());
}
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        await Problem(
            StatusCodes.Status429TooManyRequests,
            "interpretation_rate_limited",
            "Too many interpretation requests were received from this network. Try again later.",
            "Interpretation rate limit reached")
            .ExecuteAsync(context.HttpContext);
    };
    options.AddPolicy(InterpretationRateLimitPolicy, context =>
    {
        var settings = context.RequestServices
            .GetRequiredService<IOptions<InterpretationOptions>>()
            .Value.RateLimit;
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true)
            address = address.MapToIPv4();
        var partitionKey = address?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.PermitLimit,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});
builder.Services.AddSingleton<ViewerDocumentReader>();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxUploadBytes + 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxUploadBytes + 1024 * 1024);

var app = builder.Build();

if (args.Length > 0 && (args[0] == "operator-code" || args[0] == "usage-log" || args[0] == "generation-presets" || args[0] == "admin"))
{
    Environment.ExitCode = args[0] == "admin"
        ? await InteractiveAdminTool.RunAsync(app.Services, Console.In, Console.Out)
        : await InterpretationAdminCommands.RunAsync(args, app.Services, Console.Out, Console.Error);
    return;
}

// Caddy connects from loopback, which ForwardedHeadersMiddleware trusts by default.
// Apply these headers before middleware that depends on the public request scheme.
app.UseForwardedHeaders();
app.UseExceptionHandler("/error");

app.Use(async (context, next) =>
{
    context.Response.Headers["X-FTITC-Viewer-Build"] = ViewerBuild;
    if (HttpMethods.IsGet(context.Request.Method) &&
        (context.Request.Path == "/" || context.Request.Path == "/index.html"))
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Context.Response.Headers.Pragma = "no-cache";
        context.Context.Response.Headers.Expires = "0";
    },
});
app.UseRateLimiter();
app.UseAntiforgery();

app.MapGet("/assets/ft-itc-icon-32.png", (HttpContext context) => ViewerIcon(context, "ft-itc-icon-32.png"));
app.MapGet("/assets/ft-itc-icon-64.png", (HttpContext context) => ViewerIcon(context, "ft-itc-icon-64.png"));
app.MapGet("/assets/ft-itc-icon-256.png", (HttpContext context) => ViewerIcon(context, "ft-itc-icon-256.png"));

app.MapGet("/api/viewer/token", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { requestToken = tokens.RequestToken });
});

app.MapGet("/api/interpretation/status", (
    IOptions<InterpretationOptions> options,
    InterpretationRelayService relay, InterpretationServiceAvailability availability) =>
{
    var policy = availability.Read();
    var status = policy.Status == "retired" ? "retired" : policy.IsAvailable && options.Value.Enabled && relay.IsConfigured ? "available" : "temporarily_unavailable";
    var message = policy.Message ?? (status == "retired" ? "Hosted interpretation generation has ended." : status == "temporarily_unavailable" ? "Interpretation generation is temporarily unavailable." : null);
    return Results.Ok(new { available = status == "available", status, message, updatedAtUtc = policy.UpdatedAtUtc,
        requestSchemaVersion = FtItcInterpretationClient.RequestSchemaVersion, responseSchemaVersion = FtItcInterpretationClient.ResponseSchemaVersion,
        supportedRequestSchemaVersions = new[] { FtItcInterpretationClient.RequestSchemaVersion, FtItcInterpretationClient.PreviousRequestSchemaVersion, FtItcInterpretationClient.LegacyRequestSchemaVersion } });
});

app.MapGet("/api/interpretation/options", (HttpRequest request, IOptions<InterpretationOptions> configured,
    OperatorCodeRegistry registry, GenerationPresetRegistry presets, InterpretationQuotaService quotas) =>
{
    var hasAuthorization = request.Headers.ContainsKey("Authorization");
    var authentication = registry.Authenticate(request.Headers.Authorization.FirstOrDefault());
    if (hasAuthorization && !authentication.IsAuthorized)
        return Problem(403, "operator_access_denied", "The supplied access code is invalid, expired, or revoked.", "Access denied");
    var tier = authentication.IsAuthorized ? authentication.AccessTier : InterpretationAccessTiers.Public;
    var accessRecord = authentication.IsAuthorized
        ? registry.FindActive(request.Headers.Authorization.FirstOrDefault()![7..].Trim())
        : null;
    var value = configured.Value; var presetConfiguration = presets.Read();
    var versionFive = string.Equals(request.Query["requestSchemaVersion"].FirstOrDefault(),
        FtItcInterpretationClient.RequestSchemaVersion, StringComparison.Ordinal);
    var summaryChoices = versionFive
        ? new[] { new { id = "summary", name = presetConfiguration.Summary.DisplayName,
            description = presetConfiguration.Summary.Description, taskType = "summary", quota = (object?)null } }.Cast<object>()
        : Enumerable.Empty<object>();
    var availablePresets = presetConfiguration.Presets
        .Where(item => InterpretationAccessTiers.Presets(tier).Contains(item.Id, StringComparer.Ordinal))
        .ToArray();
    var presetChoices = (versionFive ? availablePresets.Select(item =>
        {
            var quota = quotas.GetStatus(authentication.OperatorCodeId, tier, item.Id);
            return new { id = item.Id, name = item.DisplayName, description = item.Description, taskType = "interpretation",
                quota = quota.IsLimited ? new { limited = true, remainingPercent = quota.RemainingPercent, resetsAtUtc = quota.ResetsAtUtc } : null };
        }).Cast<object>() : availablePresets.Select(item =>
        {
            var quota = quotas.GetStatus(authentication.OperatorCodeId, tier, item.Id);
            return new { id = item.Id, name = item.DisplayName, description = item.Description,
                quota = quota.IsLimited ? new { limited = true, remainingPercent = quota.RemainingPercent, resetsAtUtc = quota.ResetsAtUtc } : null };
        }).Cast<object>());
    var modelChoices = value.AllowedModels.OrderBy(item => item.Key)
        .Select(item => new { id = item.Key, displayName = item.Key, selectionType = "model", description = (string?)null,
            reasoningEfforts = item.Value.ReasoningEfforts }).Cast<object>();
    return Results.Ok(new
    {
        accessTier = tier,
        accessTierName = InterpretationAccessTiers.DisplayName(tier),
        accessDetails = accessRecord is null ? null : new { name = accessRecord.Label, expiresAtUtc = accessRecord.ExpiresAtUtc },
        mode = tier == InterpretationAccessTiers.Administrator ? "custom" : "presets",
        presetRevision = presetConfiguration.Revision,
        maximumRequestBytes = presets.MaximumRequestBytes(tier),
        defaultModel = tier == InterpretationAccessTiers.Administrator ? value.OpenAI.Model : null,
        defaultReasoningEffort = tier == InterpretationAccessTiers.Administrator ? value.OpenAI.ReasoningEffort : null,
        presets = tier == InterpretationAccessTiers.Administrator ? Array.Empty<object>()
            : summaryChoices.Concat(presetChoices).ToArray(),
        models = tier == InterpretationAccessTiers.Administrator
            ? (versionFive
                    ? new[] { new { id = "summary", displayName = "Summary", selectionType = "summary",
                        description = presetConfiguration.Summary.Description,
                        reasoningEfforts = new[] { presetConfiguration.Summary.ReasoningEffort } } }.Cast<object>()
                    : Enumerable.Empty<object>())
                .Concat(modelChoices).ToArray()
            : Array.Empty<object>(),
        guidanceVariants = tier == InterpretationAccessTiers.Administrator && versionFive
            ? ScientificGuidance.Variants.Select(item => new { id = item.Id, displayName = item.DisplayName, revision = item.Revision }).ToArray()
            : Array.Empty<object>(),
        defaultGuidanceVariant = tier == InterpretationAccessTiers.Administrator && versionFive
            ? ScientificGuidance.DefaultVariant : null,
    });
}).DisableAntiforgery();

app.MapGet("/api/interpretation/account", (HttpRequest request,
    OperatorCodeRegistry registry, GenerationPresetRegistry presets, InterpretationQuotaService quotas, InterpretationUsageStore usage) =>
{
    var authorization = request.Headers.Authorization.FirstOrDefault();
    var authentication = registry.Authenticate(authorization);
    if (!authentication.IsAuthorized)
        return Problem(403, "operator_access_denied", "A valid interpretation access code is required.", "Access denied");

    var code = authorization![7..].Trim();
    var account = registry.FindActive(code);
    if (account is null)
        return Problem(403, "operator_access_denied", "A valid interpretation access code is required.", "Access denied");

    var quota = quotas.GetStatus(account.Id, account.EffectiveAccessTier, "shared");
    var accountUsage = usage.GetAccountSnapshot(account.Id);
    var quotaJson = quota.IsLimited
        ? new
        {
            limited = true,
            remainingPercent = (int?)quota.RemainingPercent,
            spentUsd = (decimal?)quota.SpentUsd,
            limitUsd = (decimal?)quota.LimitUsd,
            resetsAtUtc = (DateTime?)quota.ResetsAtUtc,
        }
        : new
        {
            limited = false,
            remainingPercent = (int?)null,
            spentUsd = (decimal?)null,
            limitUsd = (decimal?)null,
            resetsAtUtc = (DateTime?)null,
        };
    object? mostRecentRequest = accountUsage.TotalRequests is > 0
        ? new
        {
            startedAtUtc = accountUsage.MostRecentStartedAtUtc,
            completedAtUtc = accountUsage.MostRecentCompletedAtUtc,
            outcome = accountUsage.MostRecentOutcome,
            httpStatus = accountUsage.MostRecentHttpStatus,
        }
        : null;
    return Results.Ok(new
    {
        status = "verified",
        label = account.Label,
        name = account.Name,
        email = account.Email,
        accessTier = account.EffectiveAccessTier,
        accessTierName = InterpretationAccessTiers.DisplayName(account.EffectiveAccessTier),
        expiresAtUtc = account.ExpiresAtUtc,
        maximumRequestBytes = presets.MaximumRequestBytes(account.EffectiveAccessTier),
        usage = quotaJson,
        totalRequests = accountUsage.TotalRequests,
        mostRecentRequest,
    });
}).DisableAntiforgery();

app.MapGet("/api/interpretation/operator/options", (HttpRequest request, IOptions<InterpretationOptions> configured, OperatorCodeRegistry registry) =>
{
    var authentication = registry.Authenticate(request.Headers.Authorization.FirstOrDefault());
    if (!authentication.IsAuthorized || authentication.AccessTier != InterpretationAccessTiers.Administrator)
        return Problem(403, "operator_access_denied", "A valid operator code is required.", "Operator access denied");
    var value = configured.Value;
    return Results.Ok(new
    {
        defaultModel = value.OpenAI.Model,
        defaultReasoningEffort = value.OpenAI.ReasoningEffort,
        models = value.AllowedModels.OrderBy(item => item.Key).Select(item => new { id = item.Key, reasoningEfforts = item.Value.ReasoningEfforts }),
        guidanceVariants = ScientificGuidance.Variants.Select(item => new { id = item.Id, displayName = item.DisplayName, revision = item.Revision }),
        defaultGuidanceVariant = ScientificGuidance.DefaultVariant,
    });
}).DisableAntiforgery();

app.MapPost("/api/interpretation/generate", async (
    HttpRequest request,
    InterpretationRequestReader reader,
    InterpretationRelayService relay,
    OperatorCodeRegistry operatorRegistry,
    GenerationPresetRegistry presetRegistry,
    InterpretationUsageStore usageStore,
    InterpretationQuotaService quotaService,
    IOptions<InterpretationOptions> options,
    InterpretationServiceAvailability availability,
    CancellationToken cancellationToken) =>
{
    var started = DateTime.UtcNow;
    var timer = System.Diagnostics.Stopwatch.StartNew();
    AnalysisInterpretationLog.Write("server-received", request.HttpContext.TraceIdentifier, $"bytes={request.ContentLength}");
    var initialAvailability = availability.Read();
    if (initialAvailability.Status == "retired")
    {
        RecordEarly(usageStore, request, started, timer.ElapsedMilliseconds, null, "rejected", 410, "interpretation_retired");
        return Problem(410, "interpretation_retired", initialAvailability.Message ?? "Hosted interpretation generation has ended.", "Interpretation retired");
    }
    if (!initialAvailability.IsAvailable)
    {
        RecordEarly(usageStore, request, started, timer.ElapsedMilliseconds, null, "rejected", 503, "interpretation_unavailable");
        return Problem(StatusCodes.Status503ServiceUnavailable, "interpretation_unavailable",
            initialAvailability.Message ?? "Interpretation generation is temporarily unavailable.", "Interpretation unavailable");
    }
    InterpretationRequestReadResult result;
    try
    {
        result = await reader.ReadAsync(request, cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }

    if (result.Failure is { } failure)
    {
        AnalysisInterpretationLog.Write("server-rejected", request.HttpContext.TraceIdentifier,
            $"http={failure.StatusCode} code={AnalysisInterpretationLog.Token(failure.Code)} fields={string.Join(",", failure.Errors?.Keys.Select(AnalysisInterpretationLog.Token) ?? Enumerable.Empty<string>())}");
        RecordEarly(usageStore, request, started, timer.ElapsedMilliseconds, result.Request?.ClientRequestId,
            "rejected", failure.StatusCode, failure.Code);
        return Problem(
            failure.StatusCode,
            failure.Code,
            failure.Detail,
            failure.Title,
            failure.Errors);
    }

    if (!InterpretationGenerationSelector.TrySelect(request, result.Request!, options.Value, operatorRegistry, presetRegistry, out var selection, out var selectionError))
    {
        RecordEarly(usageStore, request, started, timer.ElapsedMilliseconds, result.Request?.ClientRequestId,
            "rejected", selectionError.Status, selectionError.Code);
        return Problem(selectionError.Status, selectionError.Code, selectionError.Detail, selectionError.Code == "operator_access_denied" ? "Operator access denied" : "Invalid generation override");
    }

    var tierMaximumBytes = presetRegistry.MaximumRequestBytes(selection.AccessTier);
    if (result.BytesRead > tierMaximumBytes)
    {
        Record("rejected", 413, "interpretation_tier_size_exceeded", null);
        return Problem(413, "interpretation_tier_size_exceeded",
            "The interpretation request exceeds the size allowance for this access level.",
            "Interpretation request too large for access level", extra: new Dictionary<string, object?>
            {
                ["maximumRequestBytes"] = tierMaximumBytes,
                ["requestBytes"] = result.BytesRead,
                ["accessTier"] = InterpretationAccessTiers.DisplayName(selection.AccessTier),
            });
    }

    var serviceAvailability = availability.Read();
    if (serviceAvailability.Status == "retired")
    {
        RecordEarly(usageStore, request, started, timer.ElapsedMilliseconds, result.Request?.ClientRequestId, "rejected", 410, "interpretation_retired");
        return Problem(410, "interpretation_retired", serviceAvailability.Message ?? "Hosted interpretation generation has ended.", "Interpretation retired");
    }
    if (!serviceAvailability.IsAvailable || !options.Value.Enabled || !relay.IsConfigured)
    {
        RecordEarly(usageStore, request, started, timer.ElapsedMilliseconds, result.Request?.ClientRequestId,
            "rejected", 503, "interpretation_unavailable");
        return Problem(
            StatusCodes.Status503ServiceUnavailable,
            "interpretation_unavailable",
            "Interpretation generation is not available.",
            "Interpretation unavailable");
    }

    using var quotaLease = quotaService.TryAcquire(selection);
    if (!quotaLease.Acquired)
    {
        Record("rejected", 429, "interpretation_quota_busy", null);
        return Problem(429, "interpretation_quota_busy", "Another quota-limited interpretation is already running for this access code. Try again when it has completed.", "Interpretation already running");
    }
    if (!quotaLease.Status.IsAvailable)
    {
        Record("rejected", 429, "interpretation_quota_exhausted", null);
        return Problem(429, "interpretation_quota_exhausted", "The monthly allowance for this interpretation depth has been used.",
            "Monthly interpretation allowance used", extra: new Dictionary<string, object?>
            {
                ["remainingPercent"] = quotaLease.Status.RemainingPercent,
                ["resetsAtUtc"] = quotaLease.Status.ResetsAtUtc,
            });
    }

    try
    {
        var response = await relay.GenerateAsync(result.Request!, selection, cancellationToken);
        Record("success", 200, null, response);
        return Results.Ok(response);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        Record("cancelled", 499, null, null);
        return Results.StatusCode(499);
    }
    catch (InterpretationProviderResponseException)
    {
        Record("provider_error", 502, "interpretation_provider_invalid_response", null);
        return Problem(
            StatusCodes.Status502BadGateway,
            "interpretation_provider_invalid_response",
            "The interpretation provider returned an invalid response.",
            "Invalid interpretation provider response");
    }
    catch (AnalysisInterpretationProviderException exception)
    {
        AnalysisInterpretationLog.Write("server-failed", result.Request?.ClientRequestId, $"kind={exception.Kind}");
        if (exception.RetryAfter is { } retryAfter)
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            request.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }
        var providerFailure = exception.Kind switch
        {
            AnalysisInterpretationFailureKind.QuotaExceeded => (503,"interpretation_provider_quota","The model provider quota or billing limit has been reached.","Model provider quota exceeded"),
            AnalysisInterpretationFailureKind.RateLimited => (429,"interpretation_provider_rate_limited","The model provider rate limit was reached. Try again later.","Model provider rate limited"),
            AnalysisInterpretationFailureKind.Timeout => (504,"interpretation_provider_timeout","The model provider timed out.","Interpretation timed out"),
            AnalysisInterpretationFailureKind.PayloadRejected => (413,"interpretation_context_too_large","The report evidence exceeds the model context without thermograms. Shorten background or create a smaller report selection.","Interpretation evidence too large"),
            AnalysisInterpretationFailureKind.InvalidResponse => (502,"interpretation_provider_invalid_response","The interpretation provider returned an invalid response.","Invalid interpretation provider response"),
            AnalysisInterpretationFailureKind.Cancelled => (499,"interpretation_cancelled","Interpretation generation was cancelled.","Interpretation cancelled"),
            _ => (503,"interpretation_provider_unavailable","The interpretation provider is temporarily unavailable.","Interpretation provider unavailable"),
        };
        Record(exception.Kind == AnalysisInterpretationFailureKind.Timeout ? "timeout" : exception.Kind == AnalysisInterpretationFailureKind.Cancelled ? "cancelled" : "provider_error", providerFailure.Item1, providerFailure.Item2, null);
        return Problem(providerFailure.Item1, providerFailure.Item2, providerFailure.Item3, providerFailure.Item4);
    }


    void Record(string outcome, int status, string? code, InterpretationRelayResponse? response)
    {
        // Usage bookkeeping must never change the response for an accepted evidence package.
        if (!usageStore.IsEnabled) return;
        try
        {
            var aggregate = usageStore.Aggregate(result.Request?.ClientRequestId ?? request.HttpContext.TraceIdentifier);
            var package = result.Request?.PackageJson;
            var reportId = package is { ValueKind: System.Text.Json.JsonValueKind.Object } root
                && root.TryGetProperty("report", out var report) && report.ValueKind == System.Text.Json.JsonValueKind.Object
                && report.TryGetProperty("reportId", out var reportValue) && reportValue.ValueKind == System.Text.Json.JsonValueKind.String
                ? RecordedMetadataId(reportValue.GetString()) ?? "" : "";
            var analysisIds = package is { ValueKind: System.Text.Json.JsonValueKind.Object } packageRoot
                && packageRoot.TryGetProperty("results", out var results) && results.ValueKind == System.Text.Json.JsonValueKind.Array
                ? string.Join(",", results.EnumerateArray()
                    .Where(value => value.ValueKind == System.Text.Json.JsonValueKind.Object && value.TryGetProperty("resultId", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(value => RecordedMetadataId(value.GetProperty("resultId").GetString()))
                    .Where(value => value is not null)) : "";
            usageStore.RecordRequest(new InterpretationUsageRequest
            {
                RequestId = result.Request?.ClientRequestId ?? request.HttpContext.TraceIdentifier, TraceId = request.HttpContext.TraceIdentifier,
                TaskType = selection.TaskType,
                StartedUtc = started, CompletedUtc = DateTime.UtcNow, OperatorCodeId = selection.OperatorCodeId, ReportId = reportId,
                AnalysisIds = analysisIds, RequestBytes = result.BytesRead, GenerationProfile = result.Request?.GenerationProfile ?? "",
                RequestedPreset = selection.RequestedPreset, EffectivePreset = selection.EffectivePreset,
                AccessTier = selection.AccessTier, PresetRevision = selection.PresetRevision,
                RequestedModel = selection.RequestedModel, RequestedReasoning = selection.RequestedReasoningEffort,
                EffectiveModel = selection.Model, EffectiveReasoning = selection.ReasoningEffort,
                RequestedGuidanceVariant = request.Headers["X-FTITC-Guidance-Variant"].FirstOrDefault(),
                EffectiveGuidanceVariant = selection.TaskType == "summary" ? null : selection.GuidanceVariant,
                GuidanceRevision = response?.ScientificGuidanceRevision
                    ?? (selection.TaskType == "summary" ? SummaryGuidance.Revision : ScientificGuidance.RevisionFor(selection.GuidanceVariant)),
                RequestVersion = result.Request?.RequestSchemaVersion ?? FtItcInterpretationClient.RequestSchemaVersion, ResponseVersion = selection.ResponseSchemaVersion,
                PackageVersion = AnalysisInterpretationPackageBuilder.PackageSchemaVersion, PromptVersion = AnalysisInterpretationPromptBuilder.PromptVersion,
                OutputVersion = result.Request?.OutputFormatVersion ?? "", KnowledgeBaseIds = string.Join(",", response?.KnowledgeBaseIds ?? Array.Empty<string>()),
                LatencyMs = timer.ElapsedMilliseconds, Outcome = outcome, HttpStatus = status, ErrorCode = code,
                ProviderAttempts = aggregate.Attempts, InputTokens = aggregate.Input, CachedInputTokens = aggregate.Cached,
                CacheWriteTokens = aggregate.CacheWrite, OutputTokens = aggregate.Output, ReasoningTokens = aggregate.Reasoning,
                VisibleOutputTokens = aggregate.Visible, TotalTokens = aggregate.Total, EstimatedCost = aggregate.Cost,
            });
        }
        catch (Exception exception)
        {
            AnalysisInterpretationLog.Write("usage-record-failed", result.Request?.ClientRequestId, $"type={exception.GetType().Name}");
        }
    }

    static string? RecordedMetadataId(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
})
    .RequireRateLimiting(InterpretationRateLimitPolicy)
    .DisableAntiforgery();

app.MapPost("/api/viewer/open", async (
    HttpRequest request,
    ViewerDocumentReader reader,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(request.HttpContext);
    }
    catch (AntiforgeryValidationException)
    {
        return Problem(StatusCodes.Status400BadRequest, "antiforgery_validation_failed", "The upload security token is missing or expired. Refresh the page and try again.");
    }

    if (!request.HasFormContentType)
        return Problem(StatusCodes.Status415UnsupportedMediaType, "invalid_content_type", "Upload a file using multipart form data.");

    IFormCollection form;
    try
    {
        form = await request.ReadFormAsync(cancellationToken);
    }
    catch (InvalidDataException)
    {
        return Problem(StatusCodes.Status413PayloadTooLarge, "file_too_large", "The uploaded file must be 50 MB or smaller.");
    }

    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Problem(StatusCodes.Status400BadRequest, "missing_file", "Choose a non-empty .ftxtc file.");
    if (file.Length > MaxUploadBytes)
        return Problem(StatusCodes.Status413PayloadTooLarge, "file_too_large", "The uploaded file must be 50 MB or smaller.");

    var extension = Path.GetExtension(file.FileName);
    ViewerFileFormat format;
    if (string.Equals(extension, ".ftxtc", StringComparison.OrdinalIgnoreCase))
        format = ViewerFileFormat.Ftxtc;
    else
        return Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_extension", "Only .ftxtc project files are supported.");

    try
    {
        await using var stream = file.OpenReadStream();
        var document = await reader.ReadAsync(stream, file.FileName, format, cancellationToken);
        return Results.Ok(document);
    }
    catch (ViewerFileException exception)
    {
        return Problem(StatusCodes.Status400BadRequest, exception.Code, exception.Message);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
});

app.Map("/error", () => Problem(
    StatusCodes.Status500InternalServerError,
    "unexpected_error",
    "The file could not be opened because of an unexpected server error."));

app.MapFallbackToFile("index.html");
app.Run();

static IResult Problem(
    int status,
    string code,
    string detail,
    string? title = null,
    IReadOnlyDictionary<string, string[]>? errors = null,
    IReadOnlyDictionary<string, object?>? extra = null)
{
    var extensions = new Dictionary<string, object?> { ["code"] = code };
    if (errors is { Count: > 0 })
        extensions["errors"] = errors;
    if (extra is not null)
        foreach (var item in extra) extensions[item.Key] = item.Value;

    return Results.Problem(
        statusCode: status,
        title: title ?? (status >= 500 ? "Unable to open file" : "File could not be opened"),
        detail: detail,
        extensions: extensions);
}

static void RecordEarly(InterpretationUsageStore store, HttpRequest request, DateTime started, long latency,
    string? requestId, string outcome, int status, string code) => store.RecordRequest(new InterpretationUsageRequest
{
    RequestId=requestId ?? request.HttpContext.TraceIdentifier, TraceId=request.HttpContext.TraceIdentifier,
    StartedUtc=started, CompletedUtc=DateTime.UtcNow, RequestBytes=request.ContentLength ?? 0,
    Outcome=outcome, HttpStatus=status, ErrorCode=code, RequestVersion=FtItcInterpretationClient.RequestSchemaVersion,
    ResponseVersion=FtItcInterpretationClient.ResponseSchemaVersion, LatencyMs=latency,
});

static IResult ViewerIcon(HttpContext context, string fileName)
{
    context.Response.Headers.CacheControl = "no-store, max-age=0";
    var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", fileName);
    return Results.File(path, "image/png");
}

public partial class Program { }
