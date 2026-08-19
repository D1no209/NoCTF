using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Notifications;
using NoCTF.Infrastructure.Notifications;
using StackExchange.Redis;

namespace NoCTF.API.SignalR.Publishing;

public sealed class RedisLeaderboardRefreshRelay(
    IConnectionMultiplexer redis,
    IHubContext<CompetitionHub> hub,
    ILogger<RedisLeaderboardRefreshRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var queue = await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisLeaderboardRefreshPublisher.Channel));
        queue.OnMessage(async message =>
        {
            try
            {
                var notification = JsonSerializer.Deserialize<ScoreboardUpdated>(
                    message.Message.ToString());
                if (notification is null)
                    return;
                await hub.Clients.Group($"competition:{notification.CompetitionId:N}").SendAsync(
                    "scoreboardUpdated",
                    notification,
                    stoppingToken);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Invalid leaderboard refresh notification.");
            }
        });
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
