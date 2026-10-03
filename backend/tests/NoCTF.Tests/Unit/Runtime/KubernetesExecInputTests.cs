using System.Net.WebSockets;
using NoCTF.Runtime.Kubernetes.Containers;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class KubernetesExecInputTests
{
    [Test]
    public async Task Large_stdin_uses_complete_channel_messages_and_explicit_v5_eof()
    {
        var payload = Enumerable.Range(0, 131_089).Select(index => (byte)(index % 251)).ToArray();
        using var input = new MemoryStream(payload);
        using var socket = new RecordingSocket();
        await KubernetesExecInput.CopyAsync(socket, input, CancellationToken.None);
        await Assert.That(socket.Messages.Count).IsEqualTo(4);
        await Assert.That(socket.Messages.All(message => message.Complete)).IsTrue();
        await Assert.That(socket.Messages.All(message => message.Type == WebSocketMessageType.Binary)).IsTrue();
        await Assert.That(socket.Messages.Take(3).All(message => message.Data[0] == 0)).IsTrue();
        var actual = socket.Messages.Take(3).SelectMany(message => message.Data.Skip(1)).ToArray();
        await Assert.That(actual.SequenceEqual(payload)).IsTrue();
        await Assert.That(socket.Messages[^1].Data.SequenceEqual(new byte[] { 255, 0 })).IsTrue();
        await Assert.That(input.CanRead).IsTrue();
    }

    private sealed class RecordingSocket : WebSocket
    {
        public List<(byte[] Data, WebSocketMessageType Type, bool Complete)> Messages { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string SubProtocol => KubernetesExecInput.Protocol;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            Messages.Add((buffer.ToArray(), messageType, endOfMessage));
            return Task.CompletedTask;
        }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override void Abort() => throw new NotSupportedException();
        public override void Dispose() { }
    }
}
