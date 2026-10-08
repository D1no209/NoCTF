using System.Buffers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Claims;
using NoCTF.Application.Runtime.Access;

namespace NoCTF.API.RuntimeProxy;

public sealed class RuntimeTcpProxyMiddleware(
    RequestDelegate next,
    ILogger<RuntimeTcpProxyMiddleware> logger,
    RuntimeProxyOptions? configuredOptions = null,
    TimeProvider? configuredClock = null)
{
    private static readonly PathString RoutePrefix = "/api/v1/runtime-proxies";
    private readonly RuntimeProxyOptions options = configuredOptions ?? new();

    public async Task InvokeAsync(
        HttpContext context,
        IRuntimeProxyTargetReader targetReader,
        IRuntimeProxyConnectionGate connectionGate,
        IRuntimeTrafficCaptureFactory captureFactory,
        IExecutionScopeAccess executionAccess)
    {
        if (!context.WebSockets.IsWebSocketRequest
            || !TryReadRoute(context.Request.Path, out var runtimeInstanceId, out var bindingIndex))
        {
            await next(context);
            return;
        }

        var target = await targetReader.FindAsync(
            runtimeInstanceId,
            bindingIndex,
            context.RequestAborted);
        if (target is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (target.ExecutionScopeId is Guid executionScope)
        {
            var actorText = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
            if (target.CompetitionId is not Guid competitionId || target.CompetitionChallengeId is not Guid challengeId
                || !Guid.TryParse(actorText, out var actorId) || executionAccess is null
                || !await executionAccess.CanAccessAsync(new(executionScope, competitionId, challengeId, target.TeamId,
                    actorId, ExecutionScopeOperation.Read, (configuredClock ?? TimeProvider.System).GetUtcNow()), context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await using var lease = await connectionGate.TryAcquireAsync(
            runtimeInstanceId,
            context.RequestAborted);
        if (lease is null)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted);
        lifetime.CancelAfter(TimeSpan.FromMinutes(options.MaximumConnectionMinutes));
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            connect.CancelAfter(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));
            await socket.ConnectAsync(target.Host, target.Port, connect.Token);
        }
        catch (Exception exception) when (
            exception is SocketException or OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Runtime proxy could not connect to Runtime {RuntimeInstanceId} binding {BindingIndex}.",
                runtimeInstanceId,
                bindingIndex);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var client = new System.Net.IPEndPoint(
            context.Connection.RemoteIpAddress ?? System.Net.IPAddress.Loopback,
            context.Connection.RemotePort is >= 1 and <= 65535
                ? context.Connection.RemotePort
                : 1);
        var destination = socket.RemoteEndPoint as System.Net.IPEndPoint
            ?? throw new InvalidOperationException(
                "Runtime proxy socket did not resolve an IP endpoint.");
        await using var capture = await captureFactory.CreateAsync(
            target,
            client,
            destination,
            context.Connection.Id,
            lifetime.Token);
        var upstream = CopyWebSocketToTcpAsync(
            webSocket,
            stream,
            capture,
            lifetime.Token);
        var downstream = CopyTcpToWebSocketAsync(
            stream,
            webSocket,
            capture,
            lifetime.Token);
        var completed = await Task.WhenAny(upstream, downstream);
        // Cancelling a pending WebSocket receive aborts its transport. Send the
        // close frame after the final TCP bytes, then briefly allow the peer's
        // acknowledgement before cancelling the remaining copy direction.
        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await webSocket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    null,
                    closing.Token);
                if (completed == downstream)
                    await ObserveAsync(upstream).WaitAsync(closing.Token);
            }
            catch (WebSocketException)
            {
                // The peer already closed the transport.
            }
            catch (OperationCanceledException) when (closing.IsCancellationRequested)
            {
                // A peer that does not acknowledge cannot hold the proxy lease.
            }
        }
        await lifetime.CancelAsync();
        await ObserveAsync(upstream);
        await ObserveAsync(downstream);
    }

    private async Task CopyWebSocketToTcpAsync(
        WebSocket webSocket,
        Stream stream,
        IRuntimeTrafficCaptureSession? capture,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(options.BufferSizeBytes);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(
                    buffer.AsMemory(0, options.BufferSizeBytes),
                    cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    await webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.InvalidMessageType,
                        "Runtime proxy accepts binary frames only.",
                        cancellationToken);
                    return;
                }
                if (result.Count > 0)
                {
                    if (capture is not null)
                    {
                        await capture.RecordAsync(
                            RuntimeTrafficDirection.ClientToRuntime,
                            buffer.AsMemory(0, result.Count),
                            cancellationToken);
                    }
                    await stream.WriteAsync(
                        buffer.AsMemory(0, result.Count),
                        cancellationToken);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task CopyTcpToWebSocketAsync(
        Stream stream,
        WebSocket webSocket,
        IRuntimeTrafficCaptureSession? capture,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(options.BufferSizeBytes);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(
                    buffer.AsMemory(0, options.BufferSizeBytes),
                    cancellationToken);
                if (count == 0)
                    return;
                if (capture is not null)
                {
                    await capture.RecordAsync(
                        RuntimeTrafficDirection.RuntimeToClient,
                        buffer.AsMemory(0, count),
                        cancellationToken);
                }
                await webSocket.SendAsync(
                    buffer.AsMemory(0, count),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException
                or SocketException or WebSocketException)
        {
            // Connection teardown is expected to interrupt one copy direction.
        }
    }

    private static bool TryReadRoute(
        PathString path,
        out Guid runtimeInstanceId,
        out int bindingIndex)
    {
        runtimeInstanceId = Guid.Empty;
        bindingIndex = -1;
        if (!path.StartsWithSegments(RoutePrefix, out var remaining))
            return false;
        var segments = remaining.Value?.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments is { Length: 2 }
            && Guid.TryParse(segments[0], out runtimeInstanceId)
            && int.TryParse(
                segments[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out bindingIndex)
            && bindingIndex >= 0;
    }
}
