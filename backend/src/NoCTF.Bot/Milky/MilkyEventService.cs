using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Commands;
using NoCTF.Bot.Configuration;

namespace NoCTF.Bot.Milky;

public sealed class MilkyEventService(
    IOptions<MilkyOptions> options,
    BotCommandProcessor commands,
    ILogger<MilkyEventService> logger) : BackgroundService
{
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1)
    ];
    private readonly MilkyOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failureCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", $"Bearer {options.AccessToken}");
                await socket.ConnectAsync(EventUri(options.BaseUrl), stoppingToken);
                failureCount = 0;
                logger.LogInformation("Connected to the Milky event stream.");
                await ReadEventsAsync(socket, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var delay = ReconnectDelays[Math.Min(failureCount, ReconnectDelays.Length - 1)];
                failureCount++;
                logger.LogWarning(
                    "Milky event stream is unavailable ({ErrorType}); retrying in {DelaySeconds}s.",
                    exception.GetType().Name,
                    delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task ReadEventsAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(rented, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("Milky closed the event stream.");
                if (result.MessageType != WebSocketMessageType.Text)
                    continue;
                message.Write(rented, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
                message.SetLength(0);
                var groupMessage = MilkyEventParser.ParseGroupMessage(json);
                if (groupMessage is not null && groupMessage.SenderId != groupMessage.SelfId)
                    await commands.ProcessAsync(groupMessage, ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static Uri EventUri(Uri baseUrl)
    {
        var builder = new UriBuilder(new Uri(baseUrl, "event"))
        {
            Scheme = baseUrl.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
        };
        return builder.Uri;
    }
}
