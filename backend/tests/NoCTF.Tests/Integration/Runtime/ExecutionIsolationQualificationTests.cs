using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NSubstitute;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Runtime.Access;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class ExecutionIsolationQualificationTests
{
    [Test, Timeout(300_000)]
    public async Task Preparation_requires_fresh_fenced_capability_and_running_requires_the_matching_provider_receipt(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await using var nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await nats.StartAsync(ct);
            await using var connection = new NatsConnection(new NatsOpts { Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}" });
            var registry = new NatsRunnerAvailabilityRegistry(connection, TimeProvider.System);
            var placement = Substitute.For<IRuntimePlacementPolicy>(); placement.Resolve(RuntimeKind.Container).Returns(new RuntimePlacement(RuntimeProvider.Docker, "test"));
            var assessor = new RunnerExecutionRuntimeIsolation(fixture.Db, registry, placement);
            var scope = Guid.NewGuid(); var request = new ExecutionIsolationRequest(RuntimeKind.Container, RuntimeProvider.Docker, scope, fixture.LeftTeam.Id, null);
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Unverified);
            await using var owner = await new NatsClusterLeaseManager(connection).TryAcquireAsync(NatsClusterLeaseManager.ResourceDomainKey("runner"), "runner", ct);
            var registration = CurrentRunnerRegistration.Create("test", "runner", new(1_073_741_824, 1000, 512), resourceDomainFencingToken: owner!.FencingToken);
            await registry.PublishHeartbeatAsync("test", "runner", RuntimeProvider.Docker, TimeSpan.FromSeconds(30), ct);
            await registry.RegisterAsync(registration, ct);
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Unverified);
            registration = registration with { ExecutionIsolation = RuntimeIsolationState.Verified };
            await registry.RegisterAsync(registration, ct);
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Verified);
            var runtime = new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, CompetitionChallengeId = fixture.Entries[0].Id,
                TeamId = fixture.LeftTeam.Id, ExecutionScopeId = scope, RunnerId = "runner", RuntimeProvider = RuntimeProvider.Docker, RuntimeKind = RuntimeKind.Container,
                State = RuntimeState.Running, AccessMode = RuntimeAccessMode.WsrxOnly, CreatedAt = fixture.Now, RunningAt = fixture.Now };
            fixture.Db.RuntimeInstances.Add(runtime); await fixture.Db.SaveChangesAsync(ct);
            request = request with { RuntimeInstanceId = runtime.Id };
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Unverified);
            var receipt = new ContainerRuntimeReceipt { RuntimeInstanceId = runtime.Id, OperationId = runtime.Id, Provider = RuntimeProvider.Docker,
                ProjectName = $"noctf-rt-{runtime.Id:N}", PublicHost = "localhost", ExecutionScopeId = scope, CreatedAt = fixture.Now };
            runtime.ProviderReceipt = receipt; fixture.Db.Set<RuntimeReceipt>().Add(receipt); await fixture.Db.SaveChangesAsync(ct);
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Unverified);
            receipt.IsolationState = RuntimeIsolationState.Verified; await fixture.Db.SaveChangesAsync(ct);
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Verified);
            await Assert.That((await assessor.AssessAsync(request with { TeamId = fixture.RightTeam.Id }, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Unverified);
            receipt.ExecutionScopeId = Guid.NewGuid(); await fixture.Db.SaveChangesAsync(ct);
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Unverified);
            receipt.ExecutionScopeId = scope; await fixture.Db.SaveChangesAsync(ct);
            await owner.RenewAsync(ct);
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Unverified);
            await registry.RegisterAsync(registration with { ResourceDomainFencingToken = owner.FencingToken }, ct);
            await Assert.That((await assessor.AssessAsync(request, ct)).Status).IsEqualTo(ExecutionIsolationStatus.Verified);
        });
    }
}
