using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Notifications;
using NoCTF.API.SignalR.Hubs;
using NoCTF.API.Endpoints.GameplayFacts;

namespace NoCTF.API.SignalR.Publishing;

public sealed class NatsGameplayFactStateRelay(
    INatsConnection connection,
    IHubContext<CompetitionHub, ICompetitionHubClient> hub,
    ILogger<NatsGameplayFactStateRelay> logger,
    MfaConnectionGuard guard) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(
            NatsGameplayFactStateChangedNotification.Subject,
            cancellationToken: stoppingToken))
        {
            try
            {
                if (message.Data is null) continue;
                var notification = JsonSerializer.Deserialize(message.Data,
                    NoCtfMessageJsonContext.Default.GameplayFactStateChangedNotification);
                if (notification is null) continue;
                await hub.Clients.Clients(await guard.EligibleAsync(MfaHubKind.Competition, new HashSet<Guid> { notification.UserId }, stoppingToken)).GameplayFactStateChanged(
                    GameplayFactMapper.ToStatusResponse(notification.Result),
                    stoppingToken);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Invalid gameplay fact state notification.");
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Gameplay fact SignalR publish failed.");
            }
        }
    }
}
