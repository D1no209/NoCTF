using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.RuntimeProxy;
using NoCTF.Application.Runtime.Access;
using NoCTF.Infrastructure.Runtime.Access;

namespace NoCTF.Tests.Unit.API;

public sealed class RuntimeTcpProxyMiddlewareTests
{
    [Test]
    public async Task Fragmented_binary_websocket_message_is_forwarded_to_the_resolved_tcp_target(
        CancellationToken cancellationToken)
    {
        var runtimeId = Guid.CreateVersion7();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var target = new RuntimeProxyTarget(
            runtimeId,
            0,
            "127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port,
            null,
            null,
            null,
            false,
            null);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new RuntimeProxyOptions());
        builder.Services.AddSingleton<IRuntimeProxyTargetReader>(
            new FixedTargetReader(target));
        builder.Services.AddSingleton<IRuntimeProxyConnectionGate>(
            new RuntimeProxyConnectionGate(new RuntimeProxyOptions()));
        builder.Services.AddSingleton<IRuntimeTrafficCaptureFactory,
            NullCaptureFactory>();
        await using var app = builder.Build();
        app.UseWebSockets();
        app.UseMiddleware<RuntimeTcpProxyMiddleware>();
        await app.StartAsync(cancellationToken);
        var accept = listener.AcceptTcpClientAsync(cancellationToken).AsTask();
        using var webSocket = await app.GetTestServer().CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost/api/v1/runtime-proxies/{runtimeId:D}/0"),
            cancellationToken);
        using var tcp = await accept;
        var echo = EchoBytesAsync(tcp.GetStream(), 4, cancellationToken);

        await webSocket.SendAsync(
            "ws"u8.ToArray(),
            WebSocketMessageType.Binary,
            false,
            cancellationToken);
        await webSocket.SendAsync(
            "rx"u8.ToArray(),
            WebSocketMessageType.Binary,
            true,
            cancellationToken);
        var buffer = new byte[16];
        var received = await webSocket.ReceiveAsync(buffer, cancellationToken);

        await Assert.That(received.MessageType).IsEqualTo(WebSocketMessageType.Binary);
        await Assert.That(buffer.AsSpan(0, received.Count).ToArray())
            .IsEquivalentTo("wsrx"u8.ToArray());
        await echo;
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            null,
            cancellationToken);
    }

    private static async Task EchoBytesAsync(
        NetworkStream stream,
        int expectedBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[expectedBytes];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(
                buffer.AsMemory(read),
                cancellationToken);
            if (count == 0)
                throw new EndOfStreamException();
            read += count;
        }
        await stream.WriteAsync(buffer, cancellationToken);
    }

    private sealed class FixedTargetReader(RuntimeProxyTarget target)
        : IRuntimeProxyTargetReader
    {
        public Task<RuntimeProxyTarget?> FindAsync(
            Guid runtimeInstanceId,
            int bindingIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeProxyTarget?>(
                runtimeInstanceId == target.RuntimeInstanceId
                    && bindingIndex == target.BindingIndex
                    ? target
                    : null);
    }

    private sealed class NullCaptureFactory : IRuntimeTrafficCaptureFactory
    {
        public Task<IRuntimeTrafficCaptureSession?> CreateAsync(
            RuntimeProxyTarget target,
            IPEndPoint client,
            IPEndPoint destination,
            string connectionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IRuntimeTrafficCaptureSession?>(null);
    }
}
