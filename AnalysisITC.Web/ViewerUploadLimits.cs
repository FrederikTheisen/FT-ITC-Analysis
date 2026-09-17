using AnalysisITC.Core.DataReaders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class ViewerUploadOptions
{
    public const string SectionName = "ViewerUpload";
    public int ActiveUploads { get; set; } = 1;
    public int QueueLimit { get; set; } = 0;
    public long ExpandedArchiveBytes { get; set; } = 256L * 1024 * 1024;
    public long IndividualMaterializedPayloadBytes { get; set; } = 32L * 1024 * 1024;
    public long IndividualJsonPayloadBytes { get; set; } = 8L * 1024 * 1024;
    public long CumulativeJsonBytes { get; set; } = 32L * 1024 * 1024;
    public long CumulativeBinaryBytes { get; set; } = 128L * 1024 * 1024;
    public long TotalRestoredSamples { get; set; } = 1_000_000;
    public long RootComponents { get; set; } = 1_000;
    public long BootstrapReplicates { get; set; } = 20_000;
    public long InjectionRecords { get; set; } = 500_000;
    public long ViewerArrayBytes { get; set; } = 64L * 1024 * 1024;

    public FtxtcReadLimits ToReadLimits() => new()
    {
        ExpandedArchiveBytes = ExpandedArchiveBytes,
        IndividualMaterializedPayloadBytes = IndividualMaterializedPayloadBytes,
        IndividualJsonPayloadBytes = IndividualJsonPayloadBytes,
        CumulativeJsonBytes = CumulativeJsonBytes,
        CumulativeBinaryBytes = CumulativeBinaryBytes,
        TotalRestoredSamples = TotalRestoredSamples,
        RootComponents = RootComponents,
        BootstrapReplicates = BootstrapReplicates,
        InjectionRecords = InjectionRecords,
        ViewerArrayBytes = ViewerArrayBytes,
    };
}

public sealed class ViewerUploadAdmission
{
    readonly SemaphoreSlim semaphore;
    public ViewerUploadAdmission(IOptions<ViewerUploadOptions> options) => semaphore = new(Math.Max(1, options.Value.ActiveUploads), Math.Max(1, options.Value.ActiveUploads));
    public bool TryAcquire() => semaphore.Wait(0);
    public void Release() => semaphore.Release();
}

public sealed class ViewerUploadAdmissionMiddleware
{
    readonly RequestDelegate next;
    public ViewerUploadAdmissionMiddleware(RequestDelegate next) => this.next = next;
    public async Task InvokeAsync(HttpContext context, ViewerUploadAdmission admission)
    {
        if (!string.Equals(context.Request.Path.Value, "/api/viewer/open", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        if (!admission.TryAcquire())
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "1";
            await Results.Problem(statusCode: 503, title: "Viewer busy", detail: "Another project is currently being opened. Try again shortly.", extensions: new Dictionary<string, object?> { ["code"] = "viewer_busy" }).ExecuteAsync(context);
            return;
        }
        try { await next(context); }
        finally { admission.Release(); }
    }
}
