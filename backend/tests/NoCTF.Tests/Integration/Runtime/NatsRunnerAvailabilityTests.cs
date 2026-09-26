using DotNet.Testcontainers.Builders;
using System.Text.Json;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class NatsRunnerAvailabilityTests
{
    [Test, Timeout(180_000)]
    public async Task Schema_three_heartbeat_registration_and_admission_are_separate(
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
            var registry = new NatsRunnerAvailabilityRegistry(connection, TimeProvider.System);
            await using var owner = await new NatsClusterLeaseManager(connection)
                .TryAcquireAsync(NatsClusterLeaseManager.ResourceDomainKey("runner-a"),
                    "runner-a", ct);
            await Assert.That(owner).IsNotNull();
            var registration = CurrentRunnerRegistration.Create(
                "test", "runner-a", new(1024, 100, 10),
                resourceDomainFencingToken: owner!.FencingToken);
            await registry.PublishHeartbeatAsync("test", "runner-a",
                RuntimeProvider.Docker, TimeSpan.FromSeconds(10), ct);
            await Assert.That(await registry.RegisterAsync(
                registration with { Reconciled = false }, ct))
                .IsEqualTo(RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted);
            await Assert.That(await registry.ReadEligibleAsync("test", ct)).IsEmpty();

            var store = await connection.CreateKeyValueStoreContext()
                .CreateOrUpdateStoreAsync(new NatsKVConfig("NOCTF_RUNNER_AVAILABILITY_V3")
                {
                    History = 1
                }, ct);
            await store.PutAsync("registration.runner-a",
                JsonSerializer.SerializeToUtf8Bytes(
                    new RunnerRegistrationState(2, registration,
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    RunnerAvailabilityJsonContext.Default.RunnerRegistrationState),
                cancellationToken: ct);
            await Assert.That(await registry.ReadEligibleAsync("test", ct)).IsEmpty();
            await Assert.That(await registry.RegisterAsync(registration, ct))
                .IsEqualTo(RunnerAvailabilityRegistrationOutcome.Online);
            await Assert.That(await registry.ReadEligibleAsync("test", ct))
                .HasSingleItem();
            await owner!.RenewAsync(ct);
            await Assert.That(await registry.ReadEligibleAsync("test", ct)).IsEmpty();
            registration = registration with
            {
                ResourceDomainFencingToken = owner.FencingToken
            };
            await registry.RegisterAsync(registration, ct);
            await Assert.That(await registry.ReadEligibleAsync("test", ct))
                .HasSingleItem();
            await Assert.That(await registry.GetHeartbeatAsync(
                "test", "runner-a", ct)).IsEqualTo(RunnerHeartbeatStatus.Online);
            await Assert.That((await registry.GetPoolInventoryAsync("test", ct)).RunnerIds)
                .IsEquivalentTo(["runner-a"]);

            await Assert.That(await registry.RegisterAsync(registration with
            {
                Admission = new RunnerAdmissionSnapshot(
                    RunnerAdmissionState.Starting,
                    RunnerAdmissionFailure.ObservationStale,
                    registration.Admission.Observation)
            }, ct)).IsEqualTo(RunnerAvailabilityRegistrationOutcome.OfflineAdmissionBlocked);
            await Assert.That(await registry.ReadEligibleAsync("test", ct)).IsEmpty();
        });
    }
}
