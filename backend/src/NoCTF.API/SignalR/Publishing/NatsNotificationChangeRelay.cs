using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Notifications;

namespace NoCTF.API.SignalR.Publishing;

public sealed class NatsNotificationChangeRelay(
    INatsConnection connection,
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub, INotificationHubClient> hub,
    ILogger<NatsNotificationChangeRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(
            NatsNotificationChangePublisher.Subject,
            cancellationToken: stoppingToken))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (message.Data is null) continue;
                var change = JsonSerializer.Deserialize(message.Data,
                    NoCtfMessageJsonContext.Default.NotificationChanged);
                if (change?.Audiences is not { Length: > 0 }) continue;

                await using var scope = scopes.CreateAsyncScope();
                var users = await scope.ServiceProvider
                    .GetRequiredService<NotificationChangeAudienceResolver>()
                    .ResolveAsync(change.Audiences, stoppingToken);
                if (users.Count == 0) continue;
                await hub.Clients.Users(users.Select(id => id.ToString()).ToArray())
                    .NotificationChanged(stoppingToken);
                NoCtfTelemetry.RecordSignalRPublish(
                    "notifications", "success",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Invalid notification refresh signal.");
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                NoCtfTelemetry.RecordSignalRPublish(
                    "notifications", "failure",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                logger.LogWarning(exception, "Notification SignalR refresh failed.");
            }
        }
    }
}
