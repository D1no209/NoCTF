using System.Diagnostics;
using System.Text.Json;
using NATS.Client.Core;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.API.Endpoints.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.API.SignalR.Publishing;

public sealed class NatsCompetitionEventRefreshRelay(
    INatsConnection connection,
    ICompetitionHubAudienceRouter audiences,
    ICompetitionHubAudienceCoordinator coordinator,
    ILogger<NatsCompetitionEventRefreshRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(
            NatsCompetitionEventRefreshPublisher.Subject,
            cancellationToken: stoppingToken))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (message.Data is null) continue;
                var notification = JsonSerializer.Deserialize(message.Data,
                    NoCtfMessageJsonContext.Default.CompetitionEventCommitted);
                if (notification is null) continue;
                var clients = notification.Kind == CompetitionEventKind.CompetitionAudienceChanged
                    ? await audiences.AllKnownAsync(notification.CompetitionId, stoppingToken)
                    : await audiences.CurrentAsync(notification.CompetitionId, stoppingToken);
                if (clients is null) continue;
                await clients.CompetitionEventChanged(
                    new CompetitionEventChangedNotification(
                        notification.CompetitionId,
                        notification.EventId,
                        CompetitionEventProtocolMapper.ToProtocol(notification.Kind),
                        CompetitionEventProtocolMapper.ToProtocol(notification.Level),
                        notification.OccurredAt), stoppingToken);
                if (notification.Kind == CompetitionEventKind.CompetitionAudienceChanged)
                    await coordinator.ApplyAsync(notification, stoppingToken);
                NoCtfTelemetry.RecordSignalRPublish(
                    "competition-event", "success",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Invalid competition event refresh notification.");
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordSignalRPublish(
                    "competition-event", "failure",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                logger.LogWarning(exception, "Competition event SignalR publish failed.");
            }
        }
    }
}
