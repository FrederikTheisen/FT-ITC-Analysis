using System.Text;
using System.Text.Json;
using AnalysisITC.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class ViewerUploadTransferEndpointTests
{
    static readonly TimeSpan TestWait = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StalledAdmittedBodyTimesOutAndReleasesSlot(bool useHeaderToken)
    {
        var time = new TransferTimeProvider();
        using var factory = new TransferFactory(time);
        var security = await GetSecurity(factory);
        var multipart = await Multipart(security.Token);
        using var body = new GatedBody(multipart.Bytes);
        var upload = Upload(factory, security, multipart.ContentType, body, useHeaderToken);
        try
        {
            await body.ReadStarted.Task.WaitAsync(TestWait);
            using (var occupied = await factory.Services.GetRequiredService<ViewerUploadAdmission>().AcquireAsync())
                Assert.False(occupied.IsAcquired);

            time.Advance(TimeSpan.FromSeconds(1));
            var completed = await Task.WhenAny(upload, Task.Delay(TestWait)) == upload;
            if (!completed)
            {
                using var unrecovered = await factory.Services.GetRequiredService<ViewerUploadAdmission>().AcquireAsync();
                Assert.True(completed,
                    $"The admitted body did not return a transfer timeout; the upload slot is still held: {!unrecovered.IsAcquired}.");
            }

            var response = await upload.WaitAsync(TestWait);
            Assert.Equal(StatusCodes.Status408RequestTimeout, response.Status);
            Assert.Equal("viewer_upload_timeout", response.Code);
            Assert.True(body.CancellationObserved.Task.IsCompleted);

            using var completeBody = new MemoryStream(multipart.Bytes);
            var next = await Upload(factory, security, multipart.ContentType, completeBody, useHeaderToken).WaitAsync(TestWait);
            Assert.Equal(StatusCodes.Status200OK, next.Status);
        }
        finally
        {
            body.Release();
            await upload.WaitAsync(TestWait);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransferCancellationRetainsSlotUntilTheBodyReadUnwinds(bool useHeaderToken)
    {
        var time = new TransferTimeProvider();
        using var factory = new TransferFactory(time);
        var security = await GetSecurity(factory);
        var multipart = await Multipart(security.Token);
        using var body = new GatedBody(multipart.Bytes, holdCancellation: true);
        var upload = Upload(factory, security, multipart.ContentType, body, useHeaderToken);
        try
        {
            await body.ReadStarted.Task.WaitAsync(TestWait);
            time.Advance(TimeSpan.FromSeconds(1));
            await body.CancellationObserved.Task.WaitAsync(TestWait);

            using (var occupied = await factory.Services.GetRequiredService<ViewerUploadAdmission>().AcquireAsync())
                Assert.False(occupied.IsAcquired);
            Assert.False(upload.IsCompleted);

            body.FinishCancellation();
            var response = await upload.WaitAsync(TestWait);
            Assert.Equal(StatusCodes.Status408RequestTimeout, response.Status);
            Assert.Equal("viewer_upload_timeout", response.Code);

            using var completeBody = new MemoryStream(multipart.Bytes);
            var next = await Upload(factory, security, multipart.ContentType, completeBody, useHeaderToken).WaitAsync(TestWait);
            Assert.Equal(StatusCodes.Status200OK, next.Status);
        }
        finally
        {
            body.Release();
            body.FinishCancellation();
            await upload.WaitAsync(TestWait);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClientDisconnectWinsOverDeadlineWhileBodyCancellationUnwinds(bool useHeaderToken)
    {
        var time = new TransferTimeProvider();
        using var factory = new TransferFactory(time);
        var security = await GetSecurity(factory);
        var multipart = await Multipart(security.Token);
        using var body = new GatedBody(multipart.Bytes, holdCancellation: true);
        using var disconnect = new CancellationTokenSource();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HttpContext? serverContext = null;
        var upload = Upload(factory, security, multipart.ContentType, body, useHeaderToken, disconnect.Token, context =>
        {
            serverContext = context;
            context.Response.OnCompleted(() => { completed.TrySetResult(); return Task.CompletedTask; });
        });
        try
        {
            await body.ReadStarted.Task.WaitAsync(TestWait);
            disconnect.Cancel();
            await body.CancellationObserved.Task.WaitAsync(TestWait);
            time.Advance(TimeSpan.FromSeconds(1));

            using (var occupied = await factory.Services.GetRequiredService<ViewerUploadAdmission>().AcquireAsync())
                Assert.False(occupied.IsAcquired);

            body.FinishCancellation();
            var failure = await Record.ExceptionAsync(() => upload.WaitAsync(TestWait));
            Assert.NotNull(failure);
            Assert.IsNotType<TimeoutException>(failure);
            await completed.Task.WaitAsync(TestWait);
            Assert.NotEqual(StatusCodes.Status408RequestTimeout, serverContext!.Response.StatusCode);

            using var completeBody = new MemoryStream(multipart.Bytes);
            var next = await Upload(factory, security, multipart.ContentType, completeBody, useHeaderToken).WaitAsync(TestWait);
            Assert.Equal(StatusCodes.Status200OK, next.Status);
        }
        finally
        {
            body.Release();
            body.FinishCancellation();
            await Record.ExceptionAsync(() => upload.WaitAsync(TestWait));
        }
    }

    [Theory]
    [InlineData(true, StatusCodes.Status413PayloadTooLarge, "file_too_large")]
    [InlineData(false, StatusCodes.Status400BadRequest, "antiforgery_validation_failed")]
    public async Task CompletedMalformedMultipartPreservesItsExistingError(bool useHeaderToken, int status, string code)
    {
        using var factory = new TransferFactory(new TransferTimeProvider());
        var security = await GetSecurity(factory);
        using var body = new MemoryStream(Encoding.UTF8.GetBytes(
            "--expected-boundary\r\nnot-a-header\r\n\r\npayload\r\n--expected-boundary--\r\n"));
        var response = await Upload(factory, security, "multipart/form-data; boundary=expected-boundary", body, useHeaderToken)
            .WaitAsync(TestWait);

        Assert.Equal(status, response.Status);
        Assert.Equal(code, response.Code);
        var multipart = await Multipart(security.Token);
        using var completeBody = new MemoryStream(multipart.Bytes);
        var next = await Upload(factory, security, multipart.ContentType, completeBody, useHeaderToken: true).WaitAsync(TestWait);
        Assert.Equal(StatusCodes.Status200OK, next.Status);
    }

    [Fact]
    public async Task InvalidHeaderTokenRejectsWithoutReadingSlowBodyAndReleasesAdmission()
    {
        using var factory = new TransferFactory(new TransferTimeProvider());
        var security = await GetSecurity(factory);
        var multipart = await Multipart(security.Token);
        using var body = new GatedBody(multipart.Bytes);
        // The valid form token must not override an explicitly invalid header token.
        var rejected = await Upload(factory, security with { Token = "invalid-token" },
            multipart.ContentType, body, useHeaderToken: true).WaitAsync(TestWait);
        Assert.Equal(StatusCodes.Status400BadRequest, rejected.Status);
        Assert.Equal("antiforgery_validation_failed", rejected.Code);
        Assert.False(body.ReadStarted.Task.IsCompleted);
        using (var lease = await factory.Services.GetRequiredService<ViewerUploadAdmission>().AcquireAsync())
            Assert.True(lease.IsAcquired);
        using var validBody = new MemoryStream(multipart.Bytes);
        var next = await Upload(factory, security, multipart.ContentType, validBody, useHeaderToken: true).WaitAsync(TestWait);
        Assert.Equal(StatusCodes.Status200OK, next.Status);
    }

    [Fact]
    public async Task CompletedBodyWithoutTokenPreservesAntiforgeryRejection()
    {
        using var factory = new TransferFactory(new TransferTimeProvider());
        var security = await GetSecurity(factory);
        var multipart = await Multipart(token: null);
        using var body = new MemoryStream(multipart.Bytes);
        var response = await Upload(factory, security, multipart.ContentType, body, useHeaderToken: false).WaitAsync(TestWait);

        Assert.Equal(StatusCodes.Status400BadRequest, response.Status);
        Assert.Equal("antiforgery_validation_failed", response.Code);
        var validMultipart = await Multipart(security.Token);
        using var validBody = new MemoryStream(validMultipart.Bytes);
        var next = await Upload(factory, security, validMultipart.ContentType, validBody, useHeaderToken: false).WaitAsync(TestWait);
        Assert.Equal(StatusCodes.Status200OK, next.Status);
    }

    static async Task<SecurityTokens> GetSecurity(TransferFactory factory)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
        using var response = await client.GetAsync("/api/viewer/token");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
        return new SecurityTokens(json.RootElement.GetProperty("requestToken").GetString()!, cookie);
    }

    static async Task<MultipartBytes> Multipart(string? token)
    {
        using var content = new MultipartFormDataContent();
        if (token is not null) content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StreamContent(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "jors.ftxtc"))),
            "file", "jors.ftxtc");
        return new MultipartBytes(await content.ReadAsByteArrayAsync(), content.Headers.ContentType!.ToString());
    }

    static async Task<UploadResponse> Upload(TransferFactory factory, SecurityTokens security,
        string contentType, Stream body, bool useHeaderToken, CancellationToken cancellationToken = default,
        Action<HttpContext>? configureContext = null)
    {
        // Supply the server stream directly so client-side buffering cannot hide a stalled transfer.
        // Routing, admission, antiforgery and form parsing all remain the application's real pipeline.
        var response = await factory.Server.SendAsync(context =>
        {
            context.Request.Scheme = "https";
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/viewer/open";
            context.Request.ContentType = contentType;
            context.Request.Headers.Cookie = security.Cookie;
            if (useHeaderToken) context.Request.Headers["X-CSRF-TOKEN"] = security.Token;
            context.Request.Body = body;
            configureContext?.Invoke(context);
        }, cancellationToken);
        using var reader = new StreamReader(response.Response.Body);
        var text = await reader.ReadToEndAsync(cancellationToken);
        using var json = JsonDocument.Parse(text);
        var code = json.RootElement.TryGetProperty("code", out var value) ? value.GetString() : null;
        return new UploadResponse(response.Response.StatusCode, code);
    }

    sealed record SecurityTokens(string Token, string Cookie);
    sealed record MultipartBytes(byte[] Bytes, string ContentType);
    sealed record UploadResponse(int Status, string? Code);

    sealed class TransferFactory(TimeProvider time) : WebApplicationFactory<Program>
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-upload-transfer-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(directory);
            WebTestHostIsolation.ConfigureIsolatedHost(builder, directory);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ViewerUpload:QueueLimit"] = "0",
                ["ViewerUpload:TransferTimeoutSeconds"] = "1",
            }));
            builder.ConfigureTestServices(services => services.AddSingleton(time));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    sealed class GatedBody(byte[] bytes, bool holdCancellation = false) : Stream
    {
        readonly MemoryStream body = new(bytes);
        readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource finishCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => release.TrySetResult();
        public void FinishCancellation() => finishCancellation.TrySetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            try
            {
                await release.Task.WaitAsync(cancellationToken);
                return await body.ReadAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                if (holdCancellation) await finishCancellation.Task;
                throw;
            }
        }
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
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Release(); FinishCancellation(); body.Dispose(); }
            base.Dispose(disposing);
        }
    }

    sealed class TransferTimeProvider : TimeProvider
    {
        readonly object gate = new();
        readonly List<TransferTimer> timers = [];
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
        public override long GetTimestamp() { lock (gate) return now.UtcTicks; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (gate)
            {
                var timer = new TransferTimer(this, callback, state);
                timers.Add(timer);
                timer.Change(dueTime, period);
                return timer;
            }
        }
        public void Advance(TimeSpan amount)
        {
            List<TransferTimer> due;
            lock (gate)
            {
                now += amount;
                due = timers.Where(timer => !timer.Disposed && timer.Due is not null && timer.Due <= now).ToList();
                foreach (var timer in due)
                    timer.Due = timer.Period == Timeout.InfiniteTimeSpan || timer.Period == TimeSpan.Zero ? null : now + timer.Period;
            }
            foreach (var timer in due) timer.Fire();
        }
        sealed class TransferTimer(TransferTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Due;
            public TimeSpan Period;
            public bool Disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.gate)
                {
                    if (Disposed) return false;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.now + dueTime;
                    Period = period;
                    return true;
                }
            }
            public void Fire() => callback(state);
            public void Dispose() { lock (owner.gate) { Disposed = true; Due = null; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
