using NATS.Client.Core;
using NoCTF.API.SignalR.Hubs;

namespace NoCTF.API.SignalR.Publishing;

public sealed class NatsMfaAuthenticationRelay(INatsConnection connection, MfaConnectionGuard guard) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>("noctf.auth.mfa.changed.v1", cancellationToken: stoppingToken))
            await guard.RevalidateAsync(stoppingToken);
    }
}
