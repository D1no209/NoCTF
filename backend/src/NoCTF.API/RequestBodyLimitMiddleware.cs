using Microsoft.AspNetCore.Http.Features;

namespace NoCTF.API;

internal static class RequestBodyLimits
{
    private const long MultipartOverheadBytes = 256 * 1024;

    public static long? Resolve(HttpRequest request, IConfiguration configuration)
    {
        if (!HttpMethods.IsPost(request.Method) &&
            !HttpMethods.IsPut(request.Method) &&
            !HttpMethods.IsPatch(request.Method))
            return null;

        var segments = request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is null)
            return null;

        if (segments.Length == 6 &&
            segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("competitions", StringComparison.OrdinalIgnoreCase) &&
            segments[3].Equals("challenges", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[5].Equals("submit", StringComparison.OrdinalIgnoreCase))
            {
                var configured = configuration.GetValue<long?>("Submissions:MaxRequestBodyBytes");
                if (configured is > 0)
                    return configured.Value;

                var maxFlagLength = Math.Max(1, configuration.GetValue("Submissions:MaxFlagLength", 1024));
                return checked((long)maxFlagLength * 4 + 4096);
            }

            if (segments[5].Equals("patch", StringComparison.OrdinalIgnoreCase))
            {
                var configured = configuration.GetValue<long?>("PatchUpload:MaxRequestBodyBytes");
                return configured is > 0
                    ? configured.Value
                    : WithMultipartOverhead(configuration.GetValue<long>("PatchUpload:MaxBytes", 5 * 1024 * 1024));
            }
        }

        if (segments.Length == 8 &&
            segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("competitions", StringComparison.OrdinalIgnoreCase) &&
            segments[3].Equals("challenges", StringComparison.OrdinalIgnoreCase) &&
            segments[5].Equals("penetration", StringComparison.OrdinalIgnoreCase) &&
            segments[6].Equals("flags", StringComparison.OrdinalIgnoreCase) &&
            segments[7].Equals("submit", StringComparison.OrdinalIgnoreCase))
        {
            var configured = configuration.GetValue<long?>("Submissions:MaxRequestBodyBytes");
            if (configured is > 0)
                return configured.Value;

            var maxFlagLength = Math.Max(1, configuration.GetValue("Submissions:MaxFlagLength", 1024));
            return checked((long)maxFlagLength * 4 + 4096);
        }

        if (segments.Length == 5 &&
            segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("admin", StringComparison.OrdinalIgnoreCase) &&
            segments[2].Equals("challenges", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[4].Equals("attachment", StringComparison.OrdinalIgnoreCase))
            {
                var configured = configuration.GetValue<long?>("ChallengeAttachmentUpload:MaxRequestBodyBytes");
                return configured is > 0
                    ? configured.Value
                    : WithMultipartOverhead(configuration.GetValue<long>(
                        "ChallengeAttachmentUpload:MaxBytes",
                        100 * 1024 * 1024));
            }

            if (segments[4].Equals("patch-template", StringComparison.OrdinalIgnoreCase))
            {
                var configured = configuration.GetValue<long?>("ChallengePatchTemplateUpload:MaxRequestBodyBytes");
                return configured is > 0
                    ? configured.Value
                    : WithMultipartOverhead(configuration.GetValue<long>(
                        "ChallengePatchTemplateUpload:MaxBytes",
                        25 * 1024 * 1024));
            }
        }

        if (segments.Length == 5 &&
            segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("competitions", StringComparison.OrdinalIgnoreCase) &&
            segments[3].Equals("actions", StringComparison.OrdinalIgnoreCase))
        {
            return PositiveOrDefault(
                configuration.GetValue<long?>("CompetitionActions:MaxRequestBodyBytes"),
                64 * 1024);
        }

        return PositiveOrDefault(
            configuration.GetValue<long?>("ApiRequests:MaxRequestBodyBytes"),
            2 * 1024 * 1024);
    }

    public static long MaximumMultipartBodyLength(IConfiguration configuration)
        => new[]
        {
            WithMultipartOverhead(configuration.GetValue<long>("PatchUpload:MaxBytes", 5 * 1024 * 1024)),
            WithMultipartOverhead(configuration.GetValue<long>("ChallengeAttachmentUpload:MaxBytes", 100 * 1024 * 1024)),
            WithMultipartOverhead(configuration.GetValue<long>("ChallengePatchTemplateUpload:MaxBytes", 25 * 1024 * 1024))
        }.Max();

    private static long WithMultipartOverhead(long maxFileBytes)
    {
        var normalized = Math.Max(1, maxFileBytes);
        return normalized > long.MaxValue - MultipartOverheadBytes
            ? long.MaxValue
            : normalized + MultipartOverheadBytes;
    }

    private static long PositiveOrDefault(long? configured, long fallback)
        => configured is > 0 ? configured.Value : fallback;
}

internal sealed class RequestBodyLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        var limit = RequestBodyLimits.Resolve(context.Request, configuration);
        if (limit is null)
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength is > 0 && context.Request.ContentLength > limit)
        {
            await RejectAsync(context);
            return;
        }

        var maxBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySizeFeature is { IsReadOnly: false })
            maxBodySizeFeature.MaxRequestBodySize = limit;

        var originalBody = context.Request.Body;
        context.Request.Body = new SizeLimitedReadStream(originalBody, limit.Value);
        try
        {
            await next(context);
        }
        catch (RequestBodyTooLargeException) when (!context.Response.HasStarted)
        {
            await RejectAsync(context);
        }
        catch (BadHttpRequestException ex) when (
            ex.StatusCode == StatusCodes.Status413PayloadTooLarge &&
            !context.Response.HasStarted)
        {
            await RejectAsync(context);
        }
        catch (InvalidDataException ex) when (
            IsRequestBodyTooLarge(ex) &&
            !context.Response.HasStarted)
        {
            await RejectAsync(context);
        }
        finally
        {
            context.Request.Body = originalBody;
        }
    }

    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("request_body_too_large", CancellationToken.None);
    }

    private static bool IsRequestBodyTooLarge(Exception exception)
        => exception is RequestBodyTooLargeException ||
           exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } ||
           exception.InnerException is not null && IsRequestBodyTooLarge(exception.InnerException);
}

internal sealed class SizeLimitedReadStream(Stream inner, long limit) : Stream
{
    private long _remaining = limit >= 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
            throw new ArgumentException("The buffer is too small.", nameof(buffer));
        if (count == 0)
            return 0;

        var allowed = AllowedReadLength(count);
        var read = inner.Read(buffer, offset, allowed);
        Consume(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
            return 0;

        var allowed = AllowedReadLength(buffer.Length);
        var read = inner.Read(buffer[..allowed]);
        Consume(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
            return 0;

        var allowed = AllowedReadLength(buffer.Length);
        var read = await inner.ReadAsync(buffer[..allowed], cancellationToken);
        Consume(read);
        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
            throw new ArgumentException("The buffer is too small.", nameof(buffer));
        if (count == 0)
            return 0;

        var allowed = AllowedReadLength(count);
        var read = await inner.ReadAsync(buffer.AsMemory(offset, allowed), cancellationToken);
        Consume(read);
        return read;
    }

    public override int ReadByte()
    {
        var value = inner.ReadByte();
        if (value >= 0)
            Consume(1);
        return value;
    }

    private void Consume(int bytesRead)
    {
        if (bytesRead > _remaining)
            throw new RequestBodyTooLargeException();

        _remaining -= bytesRead;
    }

    private int AllowedReadLength(int requested)
        => _remaining >= int.MaxValue
            ? requested
            : (int)Math.Min(requested, _remaining + 1);

    public override void Flush() => throw new NotSupportedException();
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.FromException(new NotSupportedException());
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class RequestBodyTooLargeException : IOException
{
}
