using AnalysisITC.Core.DataReaders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace AnalysisITC.Web;

public sealed class ViewerUploadOptions
{
    public const string SectionName = "ViewerUpload";
    public int ActiveUploads { get; set; } = 1;
    public int QueueLimit { get; set; } = 5;
    public int QueueWaitTimeoutSeconds { get; set; } = 30;
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

public sealed class ViewerUploadAdmission : IDisposable
{
    readonly ConcurrencyLimiter limiter;

    public ViewerUploadAdmission(IOptions<ViewerUploadOptions> options)
    {
        var configured = options.Value;
        limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = configured.ActiveUploads,
            QueueLimit = configured.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    public ValueTask<RateLimitLease> AcquireAsync(CancellationToken cancellationToken = default) =>
        limiter.AcquireAsync(1, cancellationToken);

    public void Dispose() => limiter.Dispose();
}

internal sealed class ViewerUploadAdmissionMetadata { }

public sealed class ViewerUploadAdmissionMiddleware
{
    readonly RequestDelegate next;
    public ViewerUploadAdmissionMiddleware(RequestDelegate next) => this.next = next;
    public async Task InvokeAsync(HttpContext context, ViewerUploadAdmission admission,
        IOptions<ViewerUploadOptions> configured, TimeProvider timeProvider)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ViewerUploadAdmissionMetadata>() is null)
        {
            await next(context);
            return;
        }

        RateLimitLease? lease = null;
        var timedOut = false;
        using (var timeout = new CancellationTokenSource(
                   TimeSpan.FromSeconds(configured.Value.QueueWaitTimeoutSeconds), timeProvider))
        using (var acquisition = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, timeout.Token))
        {
            try
            {
                lease = await admission.AcquireAsync(acquisition.Token);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                timedOut = true;
            }
        }

        if (context.RequestAborted.IsCancellationRequested)
        {
            lease?.Dispose();
            return;
        }

        if (timedOut)
        {
            await WriteRejected(context, "viewer_queue_timeout",
                "The viewer remained busy for too long. Please try opening the project again.");
            return;
        }

        var acquiredLease = lease ?? throw new InvalidOperationException("Upload admission completed without a lease.");

        if (!acquiredLease.IsAcquired)
        {
            acquiredLease.Dispose();
            await WriteRejected(context, "viewer_busy",
                "Another project is currently being opened. Try again shortly.");
            return;
        }

        if (context.RequestAborted.IsCancellationRequested)
        {
            acquiredLease.Dispose();
            return;
        }

        try { await next(context); }
        finally { acquiredLease.Dispose(); }
    }

    static async Task WriteRejected(HttpContext context, string code, string detail)
    {
        if (context.RequestAborted.IsCancellationRequested) return;
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "1";
        await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: code == "viewer_busy" ? "Viewer busy" : "Viewer queue timed out",
            detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
    }
}
