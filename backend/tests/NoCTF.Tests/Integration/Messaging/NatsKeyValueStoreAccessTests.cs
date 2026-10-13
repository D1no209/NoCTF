using DotNet.Testcontainers.Builders;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration"), NotInParallel]
public sealed class NatsKeyValueStoreAccessTests
{
    [Test, Timeout(300_000)]
    public async Task Warm_operations_avoid_stream_updates_and_deleted_bucket_failure_is_visible_before_recovery(
        CancellationToken ct)
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
            await nats.StartAsync(ct);
            await using var connection = new NatsConnection(new NatsOpts
            {
                Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"
            });
            const string bucket = "NOCTF_STORE_ACCESS_TEST";
            await using var updates = await connection.SubscribeCoreAsync<byte[]>(
                "$JS.API.STREAM.UPDATE.KV_" + bucket, cancellationToken: ct);
            await connection.PingAsync(ct);
            var context = connection.CreateKeyValueStoreContext();
            var access = new NatsKeyValueStoreAccess(token =>
                context.CreateOrUpdateStoreAsync(new NatsKVConfig(bucket), token));

            await access.ExecuteAsync(async store =>
                await store.PutAsync("value", 42, cancellationToken: ct), ct);
            var reads = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
                access.ExecuteAsync(async store =>
                    (await store.GetEntryAsync<int>("value", cancellationToken: ct)).Value, ct)));
            await Assert.That(reads.All(value => value == 42)).IsTrue();
            await connection.PingAsync(ct);
            var updateCount = 0;
            while (updates.Msgs.TryRead(out _)) updateCount++;
            await Assert.That(updateCount).IsEqualTo(1);

            await context.DeleteStoreAsync(bucket, ct);
            await Assert.That(() => access.ExecuteAsync(async store =>
                await store.GetEntryAsync<int>("value", cancellationToken: ct), ct))
                .Throws<NatsException>();

            await access.ExecuteAsync(async store =>
                await store.PutAsync("value", 84, cancellationToken: ct), ct);
            await Assert.That(await access.ExecuteAsync(async store =>
                (await store.GetEntryAsync<int>("value", cancellationToken: ct)).Value, ct))
                .IsEqualTo(84);
            await connection.PingAsync(ct);
            updateCount = 0;
            while (updates.Msgs.TryRead(out _)) updateCount++;
            await Assert.That(updateCount).IsEqualTo(1);
        });
    }
}
