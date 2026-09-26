using System.Diagnostics;
using System.Text.Json;
using NATS.Client.Core;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Notifications;

namespace NoCTF.API.SignalR.Publishing;

public sealed class NatsLeaderboardRefreshRelay(
    INatsConnection connection,
    ICompetitionHubAudienceRouter audiences,
    ILogger<NatsLeaderboardRefreshRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(
            NatsLeaderboardRefreshPublisher.Subject,
            cancellationToken: stoppingToken))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (message.Data is null) continue;
                var notification = JsonSerializer.Deserialize(message.Data,
                    NoCtfMessageJsonContext.Default.ScoreboardUpdated);
                if (notification is null) continue;
                var clients = await audiences.CurrentAsync(
                    notification.CompetitionId, stoppingToken);
                if (clients is null) continue;
                await clients.ScoreboardUpdated(notification, stoppingToken);
                NoCtfTelemetry.RecordSignalRPublish(
                    "leaderboard", "success",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Invalid leaderboard refresh notification.");
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordSignalRPublish(
                    "leaderboard", "failure",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                logger.LogWarning(exception, "Leaderboard refresh SignalR publish failed.");
            }
        }
    }
}
