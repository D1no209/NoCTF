using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Notifications;
using NoCTF.Infrastructure.Notifications;
using StackExchange.Redis;
using NoCTF.API.SignalR.Hubs;

namespace NoCTF.API.SignalR.Publishing;

public sealed class RedisSubmissionResultRelay(
    IConnectionMultiplexer redis,
    IHubContext<CompetitionHub> hub,
    ILogger<RedisSubmissionResultRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisSubmissionResultNotification.Channel),
            (channel, value) =>
            {
                try
                {
                    var notification = JsonSerializer.Deserialize<SubmissionResultNotification>(value.ToString());
                    if (notification is null) return;
                    _ = hub.Clients.User(notification.UserId.ToString()).SendAsync(
                        "submissionResult",
                        notification.Result,
                        stoppingToken);
                }
                catch (JsonException exception)
                {
                    logger.LogWarning(exception, "Invalid submission result notification.");
                }
            });
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
