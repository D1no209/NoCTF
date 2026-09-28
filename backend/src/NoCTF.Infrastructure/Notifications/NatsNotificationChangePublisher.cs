using System.Text.Json;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;

namespace NoCTF.Infrastructure.Notifications;

public sealed class NatsNotificationChangePublisher(INatsConnection connection)
    : INotificationChangePublisher
{
    public const string Subject = "noctf.v3.ui.notifications";

    public async Task PublishAsync(
        NotificationChanged change,
        CancellationToken cancellationToken) =>
        await connection.PublishAsync(Subject,
            data: JsonSerializer.SerializeToUtf8Bytes(change,
                NoCtfMessageJsonContext.Default.NotificationChanged),
            cancellationToken: cancellationToken);
}

public sealed class NoOpNotificationChangePublisher : INotificationChangePublisher
{
    public Task PublishAsync(
        NotificationChanged change,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
