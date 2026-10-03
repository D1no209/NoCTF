using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.RuntimeProxy;
using NoCTF.Application.Runtime.Access;
using NoCTF.Infrastructure.Runtime.Access;

namespace NoCTF.Tests.Unit.API;

public sealed class RuntimeTcpProxyMiddlewareTests
{
    [Test, Timeout(30_000)]
    public async Task Tcp_eof_delivers_all_bytes_and_a_normal_close_through_real_Kestrel(CancellationToken ct)
    {
        var runtimeId = Guid.NewGuid();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var target = new RuntimeProxyTarget(runtimeId, 0, "127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, null, null, null, false, null);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(new RuntimeProxyOptions());
        builder.Services.AddSingleton<IRuntimeProxyTargetReader>(new FixedTargetReader(target));
        builder.Services.AddSingleton<IRuntimeProxyConnectionGate>(new RuntimeProxyConnectionGate(new RuntimeProxyOptions()));
        builder.Services.AddSingleton<IRuntimeTrafficCaptureFactory, NullCaptureFactory>();
        await using var app = builder.Build();
        app.UseWebSockets();
        app.UseMiddleware<RuntimeTcpProxyMiddleware>();
        await app.StartAsync(ct);
        var address = new UriBuilder(app.Urls.Single()) { Scheme = "ws", Path = $"/api/v1/runtime-proxies/{runtimeId}/0" };
        var expected = Enumerable.Range(0, 131_089).Select(index => (byte)(index % 251)).ToArray();
        var accept = listener.AcceptTcpClientAsync(ct).AsTask();
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(address.Uri, ct);
        var reply = Task.Run(async () =>
        {
            using var tcp = await accept;
            var stream = tcp.GetStream();
            await stream.ReadExactlyAsync(new byte[4], ct);
            await stream.WriteAsync(expected, ct);
            tcp.Client.Shutdown(SocketShutdown.Send);
        }, ct);
        await socket.SendAsync("ping"u8.ToArray().AsMemory(), WebSocketMessageType.Binary, true, ct);
        using var actual = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (received.MessageType == WebSocketMessageType.Close) break;
            await actual.WriteAsync(buffer.AsMemory(0, received.Count), ct);
        }
        await Assert.That(socket.CloseStatus).IsEqualTo(WebSocketCloseStatus.NormalClosure);
        await Assert.That(actual.ToArray().SequenceEqual(expected)).IsTrue();
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct);
        await reply;
    }

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
