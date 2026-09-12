using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Competitions.Events;
using StackExchange.Redis;
using NoCTF.API.Endpoints.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.API.SignalR.Publishing;

public sealed class RedisCompetitionEventRefreshRelay(
    IConnectionMultiplexer redis,
    ICompetitionHubAudienceRouter audiences,
    ICompetitionHubAudienceCoordinator coordinator,
    ILogger<RedisCompetitionEventRefreshRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queue = await redis.GetSubscriber().SubscribeAsync(
            RedisChannel.Literal(RedisCompetitionEventRefreshPublisher.Channel));
        queue.OnMessage(async message =>
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var notification = JsonSerializer.Deserialize<CompetitionEventCommitted>(
                    message.Message.ToString());
                if (notification is null)
                    return;
                var clients = notification.Kind == CompetitionEventKind.CompetitionAudienceChanged
                    ? audiences.AllKnown(notification.CompetitionId)
                    : await audiences.CurrentAsync(
                        notification.CompetitionId,
                        stoppingToken);
                if (clients is null)
                    return;
                await clients.CompetitionEventChanged(
                        new CompetitionEventChangedNotification(
                            notification.CompetitionId,
                            notification.EventId,
                            CompetitionEventProtocolMapper.ToProtocol(notification.Kind),
                            CompetitionEventProtocolMapper.ToProtocol(notification.Level),
                            notification.OccurredAt),
                        stoppingToken);
                if (notification.Kind == CompetitionEventKind.CompetitionAudienceChanged)
                    await coordinator.ApplyAsync(notification, stoppingToken);
                NoCtfTelemetry.RecordSignalRPublish(
                    "competition-event",
                    "success",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(
                    exception,
                    "Invalid competition event refresh notification.");
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordSignalRPublish(
                    "competition-event",
                    "failure",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                logger.LogWarning(exception, "Competition event SignalR publish failed.");
            }
        });
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
