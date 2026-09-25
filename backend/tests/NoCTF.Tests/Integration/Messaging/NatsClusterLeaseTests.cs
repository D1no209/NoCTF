using DotNet.Testcontainers.Builders;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration"), NotInParallel]
public sealed class NatsClusterLeaseTests
{
    [Test, Timeout(300_000)]
    public async Task Lease_uses_revision_fencing_and_allows_takeover_after_release(
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
            var manager = new NatsClusterLeaseManager(connection);
            var key = "lease-" + Guid.NewGuid().ToString("N");

            var first = await manager.TryAcquireAsync(key, "runner-a", cancellationToken);
            await Assert.That(first).IsNotNull();
            await Assert.That(await manager.TryAcquireAsync(key, "runner-b", cancellationToken))
                .IsNull();

            var initialRevision = first!.FencingToken;
            await first.RenewAsync(cancellationToken);
            await Assert.That(first.FencingToken).IsGreaterThan(initialRevision);
            await first.DisposeAsync();

            var replacement = await manager.TryAcquireAsync(key, "runner-b", cancellationToken);
            await Assert.That(replacement).IsNotNull();
            await Assert.That(replacement!.FencingToken).IsGreaterThan(first.FencingToken);
            await Assert.That(() => first.RenewAsync(cancellationToken))
                .Throws<NatsKVException>();
            await replacement.DisposeAsync();
        });
    }
}
