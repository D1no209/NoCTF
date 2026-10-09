using System.Text.Json;
using NATS.Client.Core;
using NoCTF.Application.LiveSolo.Realtime;

namespace NoCTF.Infrastructure.LiveSolo.Realtime;

public sealed class NatsLiveSoloRealtimePublisher(INatsConnection connection) : ILiveSoloRealtimePublisher
{
    public const string Subject = "noctf.live-solo.match.changed.v1";
    public async Task PublishAsync(LiveSoloMatchChanged change, CancellationToken ct)
        => await connection.PublishAsync(Subject, JsonSerializer.SerializeToUtf8Bytes(change,
            NoCTF.Application.Messaging.NoCtfMessageJsonContext.Default.LiveSoloMatchChanged), cancellationToken: ct);
}
