using AnalysisITC.Core.Viewer;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;

const long MaxUploadBytes = 50L * 1024 * 1024;
const string ViewerBuild = "2026.08.25-correlation.1";

var builder = WebApplication.CreateBuilder(args);
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
        return Problem(StatusCodes.Status400BadRequest, "missing_file", "Choose a non-empty .ftxtc, .ftitc, or .itc file.");
    if (file.Length > MaxUploadBytes)
        return Problem(StatusCodes.Status413PayloadTooLarge, "file_too_large", "The uploaded file must be 50 MB or smaller.");

    var extension = Path.GetExtension(file.FileName);
    ViewerFileFormat format;
    if (string.Equals(extension, ".ftxtc", StringComparison.OrdinalIgnoreCase))
        format = ViewerFileFormat.Ftxtc;
    else if (string.Equals(extension, ".ftitc", StringComparison.OrdinalIgnoreCase))
        format = ViewerFileFormat.Ftitc;
    else if (string.Equals(extension, ".itc", StringComparison.OrdinalIgnoreCase))
        format = ViewerFileFormat.Itc;
    else
        return Problem(StatusCodes.Status415UnsupportedMediaType, "unsupported_extension", "Only .ftxtc/.ftitc project files and .itc raw data files are supported.");

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

static IResult Problem(int status, string code, string detail) => Results.Problem(
    statusCode: status,
    title: status >= 500 ? "Unable to open file" : "File could not be opened",
    detail: detail,
    extensions: new Dictionary<string, object?> { ["code"] = code });

static IResult ViewerIcon(HttpContext context, string fileName)
{
    context.Response.Headers.CacheControl = "no-store, max-age=0";
    var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", fileName);
    return Results.File(path, "image/png");
}

public partial class Program { }
