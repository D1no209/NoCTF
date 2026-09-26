using System.Text.Json;
using NATS.Client.Core;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.Notifications;

public sealed class NatsGameplayFactStateChangedNotification(INatsConnection connection)
    : IGameplayFactStateChangedNotification
{
    public const string Subject = "noctf.v3.ui.gameplay-fact-states";

    public async Task PublishAsync(
        GameplayFactStateChangedNotification notification,
        CancellationToken cancellationToken) =>
        await connection.PublishAsync(Subject,
            data: JsonSerializer.SerializeToUtf8Bytes(notification,
                NoCtfMessageJsonContext.Default.GameplayFactStateChangedNotification),
            cancellationToken: cancellationToken);
}
