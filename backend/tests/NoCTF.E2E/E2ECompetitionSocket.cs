using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace NoCTF.E2E;

internal sealed class E2ECompetitionSocket : IDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly HttpMessageInvoker transport = new(E2EHttpClient.CreateHandler());
    private readonly Queue<string> messages = new();
    private string partial = "";

    internal async Task JoinAsync(string baseUrl, string token, Guid competitionId, CancellationToken ct)
    {
        socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        var url = new UriBuilder(baseUrl) { Scheme = new Uri(baseUrl).Scheme == "https" ? "wss" : "ws", Path = "/hubs/v1/competitions" };
        await socket.ConnectAsync(url.Uri, transport, ct);
        await SendAsync(new { protocol = "json", version = 1 }, ct);
        using (var handshake = await NextAsync(ct))
            if (handshake.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException("SignalR handshake failed.");
        await SendAsync(new { type = 1, invocationId = "join", target = "JoinCompetition", arguments = new[] { competitionId } }, ct);
        while (true)
        {
            using var response = await NextAsync(ct);
            if (response.RootElement.TryGetProperty("invocationId", out var id) && id.GetString() == "join")
            {
                if (response.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException("SignalR subscription failed.");
                return;
            }
        }
    }

    internal async Task WaitForScoreboardAsync(Guid competitionId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var response = await NextAsync(timeout.Token);
            if (response.RootElement.TryGetProperty("target", out var target) && target.GetString() == "scoreboardUpdated"
                && response.RootElement.GetProperty("arguments")[0].GetProperty("competitionId").GetGuid() == competitionId) return;
        }
    }

    private async Task SendAsync(object value, CancellationToken ct) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + '\u001e').AsMemory(), WebSocketMessageType.Text, true, ct);

    private async Task<JsonDocument> NextAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (messages.Count == 0)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("SignalR closed before expected notification.");
            partial += Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (partial.Length > 256 * 1024) throw new InvalidOperationException("SignalR acceptance frame exceeded its bound.");
            var records = partial.Split('\u001e');
            foreach (var record in records[..^1]) if (record.Length > 0) messages.Enqueue(record);
            partial = records[^1];
        }
        return JsonDocument.Parse(messages.Dequeue());
    }

    public void Dispose() { socket.Dispose(); transport.Dispose(); }
}
