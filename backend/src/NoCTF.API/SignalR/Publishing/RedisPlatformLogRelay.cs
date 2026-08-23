using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Observability;
using StackExchange.Redis;

namespace NoCTF.API.SignalR.Publishing;

public sealed class RedisPlatformLogRelay(
    IConnectionMultiplexer redis,
    IHubContext<PlatformLogHub, IPlatformLogHubClient> hub,
    ILogger<RedisPlatformLogRelay> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queue = await redis.GetSubscriber().SubscribeAsync(
            RedisChannel.Literal(RedisPlatformLoggerProvider.Channel));
        queue.OnMessage(async message =>
        {
            try
            {
                var view = JsonSerializer.Deserialize<PlatformLogView>(
                    message.Message.ToString(),
                    JsonOptions);
                if (view is null)
                    return;
                await hub.Clients.Group(PlatformLogHub.AdministratorsGroup).PlatformLogReceived(
                    PlatformLogMapping.ToResponse(view),
                    stoppingToken);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Invalid platform log notification.");
            }
        });
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
