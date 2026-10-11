using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Messaging;
using NoCTF.Application.Theory.Realtime;
using NoCTF.Infrastructure.Theory.Realtime;

namespace NoCTF.API.SignalR.Publishing;

public sealed class LocalTheoryChangePublisher(TheoryChangeAudienceResolver audience,
    IHubContext<NotificationHub, INotificationHubClient> hub, MfaConnectionGuard guard) : ITheoryChangePublisher
{
    public async Task PublishAsync(TheoryChanged change, CancellationToken ct)
    {
        var users = await audience.ResolveAsync(change, ct);
        if (users.Count == 0) return;
        var connections = await guard.EligibleAsync(MfaHubKind.Notifications, users, ct);
        await hub.Clients.Clients(connections).TheoryChanged(change.ActivityId, ct);
    }
}

public sealed class NatsTheoryChangeRelay(INatsConnection connection, LocalTheoryChangePublisher delivery,
    ILogger<NatsTheoryChangeRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(NatsTheoryChangePublisher.Subject, cancellationToken: stoppingToken))
        {
            try
            {
                if (message.Data is null) continue;
                var change = JsonSerializer.Deserialize(message.Data, NoCtfMessageJsonContext.Default.TheoryChanged);
                if (change is not null) await delivery.PublishAsync(change, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(exception, "Theory refresh signal could not be delivered."); }
        }
    }
}
