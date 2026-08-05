using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Events;
using StackExchange.Redis;

namespace NoCTF.API.SignalR.Publishing;

public sealed class RedisCompetitionEventRefreshRelay(
    IConnectionMultiplexer redis,
    IHubContext<CompetitionHub> hub,
    ILogger<RedisCompetitionEventRefreshRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queue = await redis.GetSubscriber().SubscribeAsync(
            RedisChannel.Literal(RedisCompetitionEventRefreshPublisher.Channel));
        queue.OnMessage(async message =>
        {
            try
            {
                var notification = JsonSerializer.Deserialize<CompetitionEventCommitted>(
                    message.Message.ToString());
                if (notification is null)
                    return;
                await hub.Clients
                    .Group($"competition:{notification.CompetitionId:N}")
                    .SendAsync(
                        "competitionEventChanged",
                        notification,
                        stoppingToken);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(
                    exception,
                    "Invalid competition event refresh notification.");
            }
        });
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
