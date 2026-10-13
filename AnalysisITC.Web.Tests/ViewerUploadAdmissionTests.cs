using AnalysisITC.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class ViewerUploadAdmissionTests : IDisposable
{
    readonly List<ServiceProvider> serviceProviders = [];
    static IOptions<ViewerUploadOptions> Options(int queue = 5, int timeoutSeconds = 30) =>
        Microsoft.Extensions.Options.Options.Create(new ViewerUploadOptions
        {
            ActiveUploads = 1,
            QueueLimit = queue,
            QueueWaitTimeoutSeconds = timeoutSeconds,
        });

    HttpContext UploadContext(CancellationToken cancellationToken = default)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/viewer/open";
        context.RequestAborted = cancellationToken;
        context.Response.Body = new MemoryStream();
        var services = new ServiceCollection().AddLogging().AddOptions().AddProblemDetails().BuildServiceProvider();
        serviceProviders.Add(services);
        context.RequestServices = services;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ViewerUploadAdmissionMetadata()), "upload"));
        return context;
    }

    static Task Invoke(HttpContext context, ViewerUploadAdmission admission, ViewerUploadOptions options,
        TimeProvider timeProvider, RequestDelegate next) =>
        new ViewerUploadAdmissionMiddleware(next).InvokeAsync(context, admission,
            Microsoft.Extensions.Options.Options.Create(options), timeProvider);

    [Fact]
    public async Task QueueOverflowAndFifoKeepAtMostOneRequestActive()
    {
        using var admission = new ViewerUploadAdmission(Options());
        var time = new ManualTimeProvider();
        var firstEntered = Signal();
        var releaseFirst = Signal();
        var order = new List<int>();
        var active = 0;
        var maximumActive = 0;

        async Task Next(HttpContext context)
        {
            var id = (int)context.Items["id"]!;
            var nowActive = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, nowActive);
            lock (order) order.Add(id);
            if (id == 0)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            }
            Interlocked.Decrement(ref active);
        }

        var first = UploadContext(); first.Items["id"] = 0;
        var firstTask = Invoke(first, admission, new ViewerUploadOptions(), time, Next);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var queued = new List<Task>();
        for (var i = 1; i <= 5; i++)
        {
            var context = UploadContext(); context.Items["id"] = i;
            queued.Add(Invoke(context, admission, new ViewerUploadOptions(), time, Next));
        }
        var overflow = UploadContext(); overflow.Items["id"] = 6;
        await Invoke(overflow, admission, new ViewerUploadOptions(), time, Next).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, overflow.Response.StatusCode);
        Assert.Equal("1", overflow.Response.Headers.RetryAfter);
        Assert.Contains("viewer_busy", await ResponseText(overflow).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { 0 }, order);

        releaseFirst.TrySetResult();
        await firstTask.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(queued).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, order);
        Assert.Equal(1, maximumActive);
    }

    [Fact]
    public async Task WaitingRequestDoesNotInvokeDownstreamUntilLeaseIsReleased()
    {
        using var admission = new ViewerUploadAdmission(Options());
        var held = await admission.AcquireAsync();
        var time = new ManualTimeProvider();
        var body = new CountingStream();
        var context = UploadContext();
        context.Request.Body = body;
        var pending = Invoke(context, admission, new ViewerUploadOptions(), time, async ctx =>
        {
            var buffer = new byte[1];
            Assert.Equal(1, await ctx.Request.Body.ReadAsync(buffer));
        });

        Assert.False(pending.IsCompleted);
        Assert.Equal(0, body.ReadCount);
        held.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, body.ReadCount);
    }

    [Fact]
    public async Task WaitTimeoutReturnsBusyProblemAndDoesNotExpireActiveLease()
    {
        using var admission = new ViewerUploadAdmission(Options(queue: 1));
        var active = await admission.AcquireAsync();
        var time = new ManualTimeProvider();
        var context = UploadContext();
        var invoked = false;
        var pending = Invoke(context, admission, new ViewerUploadOptions(), time, _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(pending.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(invoked);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("1", context.Response.Headers.RetryAfter);
        Assert.Contains("viewer_queue_timeout", await ResponseText(context).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("The viewer remained busy for too long. Please try opening the project again.", await ResponseText(context).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(active.IsAcquired);
        var waitingAfterTimeout = admission.AcquireAsync().AsTask();
        Assert.False(waitingAfterTimeout.IsCompleted);
        active.Dispose();
        using var recovered = await waitingAfterTimeout.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(recovered.IsAcquired);
    }

    [Fact]
    public async Task TimeoutIsDisposedAfterAdmissionAndDoesNotCancelProcessing()
    {
        using var admission = new ViewerUploadAdmission(Options());
        var time = new ManualTimeProvider();
        var entered = Signal();
        var finish = Signal();
        var context = UploadContext();
        var pending = Invoke(context, admission, new ViewerUploadOptions(), time, async _ =>
        {
            entered.TrySetResult();
            await finish.Task;
        });

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(pending.IsCompleted);
        finish.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        using var nextLease = await admission.AcquireAsync();
        Assert.True(nextLease.IsAcquired);
    }

    [Fact]
    public async Task TransferDeadlineStartsAfterQueueWaiting()
    {
        using var admission = new ViewerUploadAdmission(Options());
        using var held = await admission.AcquireAsync();
        var time = new ManualTimeProvider();
        var entered = Signal();
        using var body = new ControlledReadStream(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });
        var context = UploadContext();
        context.Request.Body = body;
        var pending = Invoke(context, admission, new ViewerUploadOptions(), time,
            async ctx => Assert.Equal(0, await ctx.Request.Body.ReadAsync(new byte[1])));

        time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(entered.Task.IsCompleted);
        held.Dispose();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(299));
        Assert.False(pending.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(StatusCodes.Status408RequestTimeout, context.Response.StatusCode);
        Assert.Contains("viewer_upload_timeout", await ResponseText(context));
        Assert.Same(body, context.Request.Body);
        Assert.False(body.Disposed);
        Assert.Null(context.Features.Get<ViewerUploadTransferDeadline>());
    }

    [Fact]
    public async Task TransferProgressDoesNotRestartAbsoluteDeadline()
    {
        using var admission = new ViewerUploadAdmission(Options());
        var time = new ManualTimeProvider();
        var stalled = Signal();
        var reads = 0;
        using var body = new ControlledReadStream(async (_, token) =>
        {
            if (++reads < 3) return 1;
            stalled.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });
        var context = UploadContext();
        context.Request.Body = body;
        var pending = Invoke(context, admission, new ViewerUploadOptions(), time, async ctx =>
        {
            var buffer = new byte[1];
            Assert.Equal(1, await ctx.Request.Body.ReadAsync(buffer, 0, 1));
            time.Advance(TimeSpan.FromSeconds(149));
            Assert.Equal(1, await ctx.Request.Body.ReadAsync(buffer));
            time.Advance(TimeSpan.FromSeconds(150));
            Assert.Equal(0, await ctx.Request.Body.ReadAsync(buffer));
        });

        await stalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, reads);
        Assert.Equal(StatusCodes.Status408RequestTimeout, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedTransferStopsDeadlineBeforeProcessingButRetainsAdmission(bool raceTimerCallback)
    {
        using var admission = new ViewerUploadAdmission(Options());
        var time = new ManualTimeProvider();
        var processing = Signal();
        var finish = Signal();
        var context = UploadContext();
        using var body = new MemoryStream([42]);
        context.Request.Body = body;
        var originalAbort = context.RequestAborted;
        var pending = Invoke(context, admission, new ViewerUploadOptions(), time, async ctx =>
        {
            var bufferedBody = ctx.Request.Body;
            Assert.Equal(1, await ctx.Request.Body.ReadAsync(new byte[1]));
            time.FireCallbacksOnDispose = raceTimerCallback;
            ctx.Features.Get<ViewerUploadTransferDeadline>()!.Complete();
            time.FireCallbacksOnDispose = false;
            Assert.Same(body, ctx.Request.Body);
            processing.TrySetResult();
            await finish.Task;
            bufferedBody.Position = 0;
            Assert.Equal(1, await bufferedBody.ReadAsync(new byte[1]));
        });

        await processing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var successor = admission.AcquireAsync().AsTask();
        time.Advance(TimeSpan.FromMinutes(6));
        Assert.False(pending.IsCompleted);
        Assert.False(successor.IsCompleted);
        Assert.Equal(originalAbort, context.RequestAborted);
        Assert.False(context.RequestAborted.IsCancellationRequested);
        finish.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        using var nextLease = await successor.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(nextLease.IsAcquired);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletingTransferAtExpiredDeadlineCannotClearTimeout(bool deliverTimerCallback)
    {
        using var admission = new ViewerUploadAdmission(Options());
        var time = new ManualTimeProvider();
        var context = UploadContext();
        await Invoke(context, admission, new ViewerUploadOptions(), time, ctx =>
        {
            time.Advance(TimeSpan.FromMinutes(5), deliverTimerCallback);
            ctx.Features.Get<ViewerUploadTransferDeadline>()!.Complete();
            return Task.CompletedTask;
        });

        Assert.Equal(StatusCodes.Status408RequestTimeout, context.Response.StatusCode);
        Assert.Contains("viewer_upload_timeout", await ResponseText(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredReadRetainsSlotUntilCancellationUnwindsOrIgnoredReadExits(bool ignoreCancellation)
    {
        using var admission = new ViewerUploadAdmission(Options());
        var time = new ManualTimeProvider();
        var reading = Signal();
        var cancelled = Signal();
        var exit = Signal();
        using var body = new ControlledReadStream(async (_, token) =>
        {
            reading.TrySetResult();
            if (!ignoreCancellation)
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    await exit.Task;
                    throw;
                }
            }
            await exit.Task;
            return 1;
        });
        var context = UploadContext();
        context.Request.Body = body;
        var afterRead = false;
        var pending = Invoke(context, admission, new ViewerUploadOptions(), time, async ctx =>
        {
            Assert.Equal(1, await ctx.Request.Body.ReadAsync(new byte[1]));
            afterRead = true;
            ctx.Features.Get<ViewerUploadTransferDeadline>()!.Complete();
        });
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var successor = admission.AcquireAsync().AsTask();
        time.Advance(TimeSpan.FromMinutes(5));
        if (!ignoreCancellation) await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        Assert.False(successor.IsCompleted);

        exit.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        using var nextLease = await successor.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(nextLease.IsAcquired);
        Assert.False(afterRead);
        Assert.Equal(StatusCodes.Status408RequestTimeout, context.Response.StatusCode);
        Assert.Same(body, context.Request.Body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientAbortTakesPrecedenceOverTransferExpiry(bool expireDeadline)
    {
        using var admission = new ViewerUploadAdmission(Options());
        using var abort = new CancellationTokenSource();
        var time = new ManualTimeProvider();
        var reading = Signal();
        var exit = Signal();
        using var body = new ControlledReadStream(async (_, token) =>
        {
            reading.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { await exit.Task; }
            return 0;
        });
        var context = UploadContext(abort.Token);
        context.Request.Body = body;
        var pending = Invoke(context, admission, new ViewerUploadOptions(), time,
            async ctx => Assert.Equal(0, await ctx.Request.Body.ReadAsync(new byte[1])));
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        abort.Cancel();
        if (expireDeadline) time.Advance(TimeSpan.FromMinutes(5));
        exit.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(await ResponseText(context));
        Assert.Empty(context.Response.Headers);
        Assert.Equal(abort.Token, context.RequestAborted);
        Assert.Same(body, context.Request.Body);
        using var nextLease = await admission.AcquireAsync();
        Assert.True(nextLease.IsAcquired);
    }

    [Fact]
    public async Task ExpiryAfterResponseStartsAbortsWithoutWritingAnotherResponse()
    {
        using var admission = new ViewerUploadAdmission(Options());
        var time = new ManualTimeProvider();
        var context = UploadContext();
        var lifetime = new RecordingLifetimeFeature();
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        await Invoke(context, admission, new ViewerUploadOptions(), time, _ =>
        {
            time.Advance(TimeSpan.FromMinutes(5));
            throw new OperationCanceledException();
        });

        Assert.Equal(1, lifetime.AbortCount);
        Assert.Empty(await ResponseText(context));
        Assert.Empty(context.Response.Headers);
        using var nextLease = await admission.AcquireAsync();
        Assert.True(nextLease.IsAcquired);
    }

    [Fact]
    public async Task QueuedCancellationFreesCapacityWithoutRunningHandlerOrWritingResponse()
    {
        using var admission = new ViewerUploadAdmission(Options(queue: 1));
        var held = await admission.AcquireAsync();
        using var abort = new CancellationTokenSource();
        var cancelled = UploadContext(abort.Token);
        var called = false;
        var pending = Invoke(cancelled, admission, new ViewerUploadOptions(), new ManualTimeProvider(), _ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        abort.Cancel();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(called);
        Assert.Empty(await ResponseText(cancelled).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(cancelled.Response.Headers);

        var successor = UploadContext();
        var successorPending = Invoke(successor, admission, new ViewerUploadOptions(), new ManualTimeProvider(), _ => Task.CompletedTask);
        held.Dispose();
        await successorPending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(StatusCodes.Status200OK, successor.Response.StatusCode);
    }

    [Fact]
    public async Task AbortRacingWithGrantedLeaseDisposesLeaseBeforeDownstream()
    {
        using var admission = new ViewerUploadAdmission(Options());
        using var abort = new CancellationTokenSource();
        var time = new ManualTimeProvider(abort.Cancel);
        var context = UploadContext(abort.Token);
        var called = false;

        await Invoke(context, admission, new ViewerUploadOptions(), time, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        Assert.False(called);
        Assert.Empty(await ResponseText(context).WaitAsync(TimeSpan.FromSeconds(10)));
        using var available = await admission.AcquireAsync();
        Assert.True(available.IsAcquired);
    }

    [Fact]
    public async Task DownstreamExceptionAndNonUploadEndpointsReleaseOrBypassAdmission()
    {
        using var admission = new ViewerUploadAdmission(Options(queue: 0));
        var time = new ManualTimeProvider();
        var first = UploadContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(first, admission, new ViewerUploadOptions { QueueLimit = 0 }, time,
            _ => throw new InvalidOperationException("expected")));

        var lease = await admission.AcquireAsync();
        Assert.True(lease.IsAcquired);

        var unrelated = new DefaultHttpContext();
        unrelated.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "other"));
        var reached = false;
        await Invoke(unrelated, admission, new ViewerUploadOptions { QueueLimit = 0 }, time, _ => { reached = true; return Task.CompletedTask; }).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(reached);
        lease.Dispose();

        var cancelledDuringProcessing = UploadContext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(cancelledDuringProcessing, admission,
            new ViewerUploadOptions { QueueLimit = 0 }, time,
            _ => throw new OperationCanceledException("expected")));
        using var releasedAfterCancellation = await admission.AcquireAsync();
        Assert.True(releasedAfterCancellation.IsAcquired);
    }

    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    static async Task<string> ResponseText(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    public void Dispose()
    {
        foreach (var services in serviceProviders) services.Dispose();
    }

    sealed class CountingStream : MemoryStream
    {
        public CountingStream() : base([42]) { Position = 0; }
        public int ReadCount { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    sealed class ControlledReadStream(Func<Memory<byte>, CancellationToken, ValueTask<int>> read) : Stream
    {
        public bool Disposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => read(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    sealed class RecordingLifetimeFeature : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public int AbortCount { get; private set; }
        public void Abort() => AbortCount++;
    }

    sealed class ManualTimeProvider : TimeProvider
    {
        readonly object gate = new();
        readonly List<ManualTimer> timers = [];
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        long timestamp;

        readonly Action? onTimerDisposed;
        public bool FireCallbacksOnDispose { get; set; }

        public ManualTimeProvider(Action? onTimerDisposed = null) => this.onTimerDisposed = onTimerDisposed;

        public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
        public override long GetTimestamp() { lock (gate) return timestamp; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (gate)
            {
                var timer = new ManualTimer(this, callback, state, onTimerDisposed);
                timers.Add(timer);
                timer.Change(dueTime, period);
                return timer;
            }
        }

        public void Advance(TimeSpan amount, bool deliverTimerCallbacks = true)
        {
            List<ManualTimer> due;
            lock (gate)
            {
                now += amount;
                timestamp += amount.Ticks;
                due = timers.Where(timer => timer.IsDue(now)).ToList();
                foreach (var timer in due) timer.MoveNext(now);
            }
            if (deliverTimerCallbacks)
                foreach (var timer in due) timer.Fire();
        }

        sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, Action? onDisposed) : ITimer
        {
            DateTimeOffset? due;
            TimeSpan period;
            bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan nextPeriod)
            {
                lock (owner.gate)
                {
                    if (disposed) return false;
                    due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.now + dueTime;
                    period = nextPeriod;
                    return true;
                }
            }
            public bool IsDue(DateTimeOffset current) => !disposed && due is not null && due <= current;
            public void MoveNext(DateTimeOffset current) => due = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : current + period;
            public void Fire() => callback(state);
            public void Dispose()
            {
                lock (owner.gate)
                {
                    if (disposed) return;
                    disposed = true;
                    due = null;
                }
                // Model an already-dispatched callback racing with cancellation-source disposal.
                if (owner.FireCallbacksOnDispose) callback(state);
                onDisposed?.Invoke();
            }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
