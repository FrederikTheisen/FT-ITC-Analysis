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
const string ViewerBuild = "2026.09.04-openai-provider.1";
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
    InterpretationRelayService relay) => Results.Ok(new
{
    available = options.Value.Enabled && relay.IsConfigured,
    requestSchemaVersion = FtItcInterpretationClient.RequestSchemaVersion,
    responseSchemaVersion = FtItcInterpretationClient.ResponseSchemaVersion,
}));

app.MapPost("/api/interpretation/generate", async (
    HttpRequest request,
    InterpretationRequestReader reader,
    InterpretationRelayService relay,
    IOptions<InterpretationOptions> options,
    CancellationToken cancellationToken) =>
{
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
        return Problem(
            failure.StatusCode,
            failure.Code,
            failure.Detail,
            failure.Title,
            failure.Errors);
    }

    if (!options.Value.Enabled || !relay.IsConfigured)
    {
        return Problem(
            StatusCodes.Status503ServiceUnavailable,
            "interpretation_unavailable",
            "Interpretation generation is not available.",
            "Interpretation unavailable");
    }

    try
    {
        var response = await relay.GenerateAsync(result.Request!, cancellationToken);
        return Results.Ok(response);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (InterpretationProviderResponseException)
    {
        return Problem(
            StatusCodes.Status502BadGateway,
            "interpretation_provider_invalid_response",
            "The interpretation provider returned an invalid response.",
            "Invalid interpretation provider response");
    }
    catch (AnalysisInterpretationProviderException exception)
    {
        if (exception.Kind == AnalysisInterpretationFailureKind.PayloadRejected)
            return Problem(StatusCodes.Status413PayloadTooLarge, "interpretation_context_too_large",
                "The report evidence exceeds the model context without thermograms. Shorten background or create a smaller report selection.", "Interpretation evidence too large");
        if (exception.Kind == AnalysisInterpretationFailureKind.InvalidResponse)
        {
            return Problem(
                StatusCodes.Status502BadGateway,
                "interpretation_provider_invalid_response",
                "The interpretation provider returned an invalid response.",
                "Invalid interpretation provider response");
        }
        if (exception.RetryAfter is { } retryAfter)
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            request.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }
        return Problem(
            StatusCodes.Status503ServiceUnavailable,
            "interpretation_provider_unavailable",
            "The interpretation provider is temporarily unavailable.",
            "Interpretation provider unavailable");
    }
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
    IReadOnlyDictionary<string, string[]>? errors = null)
{
    var extensions = new Dictionary<string, object?> { ["code"] = code };
    if (errors is { Count: > 0 })
        extensions["errors"] = errors;

    return Results.Problem(
        statusCode: status,
        title: title ?? (status >= 500 ? "Unable to open file" : "File could not be opened"),
        detail: detail,
        extensions: extensions);
}

static IResult ViewerIcon(HttpContext context, string fileName)
{
    context.Response.Headers.CacheControl = "no-store, max-age=0";
    var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", fileName);
    return Results.File(path, "image/png");
}

public partial class Program { }
