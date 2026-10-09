using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;
using NoCTF.Application.LiveSolo.Realtime;
using NoCTF.Infrastructure.LiveSolo.Realtime;

namespace NoCTF.API.LiveSolo.Realtime;

public sealed class NatsLiveSoloRealtimeRelay(INatsConnection connection, LiveSoloConnectionGuard guard, IHubContext<LiveSoloHub, ILiveSoloHubClient> hub) : BackgroundService
{
    private readonly TaskCompletionSource subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => subscribed.Task;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = await connection.SubscribeCoreAsync<byte[]>(NatsLiveSoloRealtimePublisher.Subject, cancellationToken: stoppingToken);
        await connection.PingAsync(stoppingToken); subscribed.TrySetResult();
        await foreach (var message in subscription.Msgs.ReadAllAsync(stoppingToken))
        {
            if (message.Data is null) continue;
            LiveSoloMatchChanged? change;
            try { change = JsonSerializer.Deserialize(message.Data, NoCTF.Application.Messaging.NoCtfMessageJsonContext.Default.LiveSoloMatchChanged); }
            catch (JsonException) { continue; }
            if (change is null || change.CompetitionId == Guid.Empty || change.MatchId == Guid.Empty) continue;
            var eligible = await guard.EligibleAsync(change.CompetitionId, change.MatchId, stoppingToken);
            if (eligible.Length != 0) await hub.Clients.Clients(eligible).MatchChanged(change, stoppingToken);
        }
    }
}
