using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Notifications;
using StackExchange.Redis;
using NoCTF.API.SignalR.Hubs;
using NoCTF.API.Endpoints.GameplayFacts;

namespace NoCTF.API.SignalR.Publishing;

public sealed class RedisGameplayFactStateRelay(
    IConnectionMultiplexer redis,
    IHubContext<CompetitionHub, ICompetitionHubClient> hub,
    ILogger<RedisGameplayFactStateRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisGameplayFactStateChangedNotification.Channel),
            (channel, value) =>
            {
                try
                {
                    var notification = JsonSerializer.Deserialize(
                        value.ToString(),
                        NoCtfMessageJsonContext.Default.GameplayFactStateChangedNotification);
                    if (notification is null) return;
                    _ = hub.Clients.User(notification.UserId.ToString()).GameplayFactStateChanged(
                        GameplayFactMapper.ToStatusResponse(notification.Result),
                        stoppingToken);
                }
                catch (JsonException exception)
                {
                    logger.LogWarning(exception, "Invalid gameplay fact state notification.");
                }
            });
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
