using System.Text.Json;
using DotNet.Testcontainers.Builders;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Notifications;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration"), NotInParallel]
public sealed class NotificationChangeNatsTests
{
    [Test, Timeout(300_000)]
    public async Task Notification_invalidation_crosses_NATS_without_notification_content(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await nats.StartAsync(cancellationToken);
            await using var connection = new NatsConnection(new NatsOpts
            {
                Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"
            });
            var audience = new NotificationAudience(
                NotificationTargetType.User, Guid.NewGuid(), Guid.NewGuid());
            var received = Task.Run(async () =>
            {
                await foreach (var message in connection.SubscribeAsync<byte[]>(
                    NatsNotificationChangePublisher.Subject,
                    cancellationToken: cancellationToken))
                    return message.Data;
                return null;
            }, cancellationToken);
            await Task.Delay(100, cancellationToken);

            await new NatsNotificationChangePublisher(connection).PublishAsync(
                new([audience]), cancellationToken);

            var data = await received.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            await Assert.That(data).IsNotNull();
            var decoded = JsonSerializer.Deserialize(data!,
                NoCtfMessageJsonContext.Default.NotificationChanged);
            await Assert.That(decoded).IsNotNull();
            await Assert.That(decoded!.Audiences).IsEquivalentTo([audience]);
            await Assert.That(System.Text.Encoding.UTF8.GetString(data!))
                .DoesNotContain("Body");
        });
    }
}
