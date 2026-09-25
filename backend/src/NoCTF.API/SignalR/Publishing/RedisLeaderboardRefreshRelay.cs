using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Notifications;
using StackExchange.Redis;

namespace NoCTF.API.SignalR.Publishing;

public sealed class RedisLeaderboardRefreshRelay(
    IConnectionMultiplexer redis,
    ICompetitionHubAudienceRouter audiences,
    ILogger<RedisLeaderboardRefreshRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var queue = await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisLeaderboardRefreshPublisher.Channel));
        queue.OnMessage(async message =>
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var notification = JsonSerializer.Deserialize(
                    message.Message.ToString(),
                    NoCtfMessageJsonContext.Default.ScoreboardUpdated);
                if (notification is null)
                    return;
                var clients = await audiences.CurrentAsync(
                    notification.CompetitionId,
                    stoppingToken);
                if (clients is null)
                    return;
                await clients.ScoreboardUpdated(
                    notification,
                    stoppingToken);
                NoCtfTelemetry.RecordSignalRPublish(
                    "leaderboard",
                    "success",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Invalid leaderboard refresh notification.");
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordSignalRPublish(
                    "leaderboard",
                    "failure",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                logger.LogWarning(exception, "Leaderboard refresh SignalR publish failed.");
            }
        });
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
