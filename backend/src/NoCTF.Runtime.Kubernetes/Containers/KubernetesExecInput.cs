using System.Buffers;
using System.Net.WebSockets;

namespace NoCTF.Runtime.Kubernetes.Containers;

internal static class KubernetesExecInput
{
    internal const string Protocol = "v5.channel.k8s.io";

    internal static async Task CopyAsync(WebSocket socket, Stream input, CancellationToken cancellationToken)
    {
        // Every complete binary message starts with its channel. v5 closes
        // stdin without closing stdout/status or the whole connection.
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024 + 1);
        try
        {
            buffer[0] = 0;
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(1, 64 * 1024), cancellationToken)) > 0)
                await socket.SendAsync(buffer.AsMemory(0, count + 1), WebSocketMessageType.Binary,
                    endOfMessage: true, cancellationToken);
            await socket.SendAsync(new byte[] { 255, 0 }.AsMemory(), WebSocketMessageType.Binary,
                endOfMessage: true, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }
}
