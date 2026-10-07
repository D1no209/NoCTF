using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.API.SignalR.Publishing;

public sealed class NatsPlatformLogRelay(
    INatsConnection connection,
    IHubContext<PlatformLogHub, IPlatformLogHubClient> hub,
    ILogger<NatsPlatformLogRelay> logger,
    MfaConnectionGuard guard) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(
            PlatformLogBroadcastQueue.Subject,
            cancellationToken: stoppingToken))
        {
            try
            {
                if (message.Data is null) continue;
                var view = JsonSerializer.Deserialize(message.Data,
                    NoCtfWebMessageJsonContext.Default.PlatformLogView);
                if (view is null) continue;
                await hub.Clients.Clients(await guard.EligibleAsync(MfaHubKind.PlatformLogs, null, stoppingToken))
                    .PlatformLogReceived(PlatformLogMapping.ToResponse(view), stoppingToken);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Invalid platform log notification.");
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Platform log SignalR publish failed.");
            }
        }
    }
}
