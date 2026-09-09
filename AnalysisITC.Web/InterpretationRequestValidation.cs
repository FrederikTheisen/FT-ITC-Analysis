using System.Text.Json;
using AnalysisITC.Core.Interpretation;

namespace AnalysisITC.Web;

// Scientific evidence is opaque JSON: future fields, enum values and numeric literals
// are relayed exactly, while the envelope remains strictly versioned.
public sealed class InterpretationRequestReader
{
    public const long MaxRequestBytes = 2L * 1024 * 1024;
    public async Task<InterpretationRequestReadResult> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType()) return Fail(415, "invalid_content_type", "Invalid content type", "Send the interpretation request as application/json.");
        if (request.ContentLength > MaxRequestBytes) return TooLarge();
        try
        {
            using var stream = new SizeLimitedReadStream(request.Body, MaxRequestBytes);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail(422, "invalid_interpretation_request", "Invalid interpretation request", "The request must be an object.", bytesRead: stream.BytesRead);
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var errors = new Dictionary<string, string[]>();
            var schema = Text("requestSchemaVersion"); var output = Text("outputInstructions"); var format = Text("outputFormatVersion"); var profile = Text("generationProfile"); var id = Text("clientRequestId");
            if (schema != FtItcInterpretationClient.RequestSchemaVersion && schema != FtItcInterpretationClient.LegacyRequestSchemaVersion) errors["requestSchemaVersion"] = new[] { "The supplied value is not supported by this API version." };
            if (string.IsNullOrWhiteSpace(output)) errors["outputInstructions"] = new[] { "This field is required." };
            if (string.IsNullOrWhiteSpace(format)) errors["outputFormatVersion"] = new[] { "This field is required." };
            var profiles = schema == FtItcInterpretationClient.LegacyRequestSchemaVersion
                ? new[] { "fast" } : new[] { "instant", "fast", "standard", "in-depth", "custom" };
            if (profile is null || !profiles.Contains(profile, StringComparer.Ordinal)) errors["generationProfile"] = new[] { "The supplied value is not supported." };
            if (id is null || id.Length != 32 || id.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) errors["clientRequestId"] = new[] { "Use exactly 32 lowercase hexadecimal characters." };
            if (!root.TryGetProperty("package", out var package) || package.ValueKind != JsonValueKind.Object) errors["package"] = new[] { "A package object is required." };
            else if (!package.TryGetProperty("packageSchemaVersion", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != AnalysisInterpretationPackageBuilder.PackageSchemaVersion) errors["package.packageSchemaVersion"] = new[] { "The supplied value is not supported by this API version." };
            if (errors.Count > 0) return new(null, new(422, "invalid_interpretation_request", "Invalid interpretation request", "The interpretation request failed validation.", errors), stream.BytesRead);
            return new(new(schema!, id!, profile!, format!, output!, package.Clone()), null, stream.BytesRead);
        }
        catch (InterpretationRequestTooLargeException) { return TooLarge(); }
        catch (JsonException) { return Fail(400, "invalid_interpretation_json", "Invalid interpretation JSON", "The request body is malformed.", new Dictionary<string, string[]> { ["$"] = new[] { "Provide valid JSON." } }); }
    }
    static InterpretationRequestReadResult TooLarge() => Fail(413, "interpretation_request_too_large", "Interpretation request too large", "The interpretation request must be 2 MB or smaller.");
    static InterpretationRequestReadResult Fail(int status, string code, string title, string detail, IReadOnlyDictionary<string, string[]>? errors = null, long bytesRead = 0) => new(null, new(status, code, title, detail, errors), bytesRead);
}
public sealed record InterpretationRequestFailure(int StatusCode, string Code, string Title, string Detail, IReadOnlyDictionary<string, string[]>? Errors);
public sealed class InterpretationRequestReadResult { public InterpretationRequestReadResult(ValidatedInterpretationRequest? request, InterpretationRequestFailure? failure, long bytesRead = 0) { Request = request; Failure = failure; BytesRead = bytesRead; } public ValidatedInterpretationRequest? Request { get; } public InterpretationRequestFailure? Failure { get; } public long BytesRead { get; } }
public sealed record ValidatedInterpretationRequest(string RequestSchemaVersion, string ClientRequestId, string GenerationProfile, string OutputFormatVersion, string OutputInstructions, JsonElement PackageJson);
sealed class InterpretationRequestTooLargeException : Exception;

sealed class SizeLimitedReadStream : Stream
{
    readonly Stream inner;
    readonly long maximumBytes;
    long bytesRead;

    public SizeLimitedReadStream(Stream inner, long maximumBytes)
    {
        this.inner = inner;
        this.maximumBytes = maximumBytes;
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public long BytesRead => bytesRead;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, AllowedLength(count));
        Count(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer[..AllowedLength(buffer.Length)]);
        Count(read);
        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer, offset, AllowedLength(count), cancellationToken);
        Count(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer[..AllowedLength(buffer.Length)], cancellationToken);
        Count(read);
        return read;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    int AllowedLength(int requestedLength)
    {
        var remainingWithSentinel = maximumBytes - bytesRead + 1;
        return (int)Math.Min(requestedLength, remainingWithSentinel);
    }

    void Count(int count)
    {
        bytesRead += count;
        if (bytesRead > maximumBytes)
            throw new InterpretationRequestTooLargeException();
    }
}
