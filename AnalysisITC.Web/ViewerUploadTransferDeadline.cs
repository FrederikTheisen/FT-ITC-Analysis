namespace AnalysisITC.Web;

// The deadline covers receiving the form, including antiforgery's tokenless form read.
// Keep its token out of ReadFormAsync: that API aborts the connection on cancellation.
internal sealed class ViewerUploadTransferDeadline : IDisposable
{
    readonly HttpContext context;
    readonly Stream originalBody;
    readonly CancellationToken requestAborted;
    readonly TimeProvider timeProvider;
    readonly TimeSpan duration;
    readonly long started;
    readonly CancellationTokenSource timeout;
    readonly CancellationToken timeoutToken;
    readonly CancellationTokenSource readCancellation;
    bool completed;

    public ViewerUploadTransferDeadline(HttpContext context, int timeoutSeconds, TimeProvider timeProvider)
    {
        this.context = context;
        originalBody = context.Request.Body;
        requestAborted = context.RequestAborted;
        this.timeProvider = timeProvider;
        duration = TimeSpan.FromSeconds(timeoutSeconds);
        started = timeProvider.GetTimestamp();
        timeout = new CancellationTokenSource(duration, timeProvider);
        timeoutToken = timeout.Token;
        readCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, timeoutToken);
        context.Request.Body = new DeadlineReadStream(originalBody, this);
    }

    public bool IsExpired => !completed &&
        (timeoutToken.IsCancellationRequested || timeProvider.GetElapsedTime(started) >= duration);

    CancellationToken ReadToken => completed ? requestAborted : readCancellation.Token;

    public void ThrowIfExpired()
    {
        requestAborted.ThrowIfCancellationRequested();
        if (IsExpired) throw new OperationCanceledException("The upload transfer deadline expired.", timeoutToken);
    }

    public void Complete()
    {
        // An overdue read must not erase its deadline by finishing late. Checking elapsed
        // time also covers a timer callback that has not yet been scheduled to run.
        ThrowIfExpired();
        completed = true;
        timeout.Dispose();
        context.Request.Body = originalBody;
    }

    public void Dispose()
    {
        context.Request.Body = originalBody;
        readCancellation.Dispose();
        timeout.Dispose();
    }

    sealed class DeadlineReadStream(Stream body, ViewerUploadTransferDeadline deadline) : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            deadline.ThrowIfExpired();
            // Buffered form files can retain this stream after Complete. Their later
            // server-side reads observe client cancellation, never the transfer deadline.
            using var read = CancellationTokenSource.CreateLinkedTokenSource(deadline.ReadToken, cancellationToken);
            var count = await body.ReadAsync(buffer, read.Token);
            deadline.ThrowIfExpired();
            cancellationToken.ThrowIfCancellationRequested();
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            deadline.ThrowIfExpired();
            var read = body.Read(buffer, offset, count);
            deadline.ThrowIfExpired();
            return read;
        }

        public override bool CanRead => body.CanRead;
        public override bool CanSeek => body.CanSeek;
        public override bool CanWrite => false;
        public override long Length => body.Length;
        public override long Position { get => body.Position; set => body.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => body.Seek(offset, origin);
        public override void Flush() => body.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        // The host owns the original request stream; disposing this wrapper leaves it open.
    }
}
