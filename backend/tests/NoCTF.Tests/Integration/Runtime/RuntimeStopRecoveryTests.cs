using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class RuntimeStopRecoveryTests
{
    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Releasing_a_stopping_runtime_preserves_the_current_slot_owner(
        bool replaced, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(1, ct);
            await fixture.AddOwnerAsync("runner", "domain", new(1024, 100, 100), 2, ct);
            var originalId = fixture.RuntimeIds[0];
            await fixture.ClaimAsync(originalId, "runner", ct);
            var ownerId = originalId;
            await using (var reset = new NoCtfDbContext(fixture.Options))
            {
                var original = await reset.RuntimeInstances.SingleAsync(item => item.Id == originalId, ct);
                original.State = RuntimeState.Stopping;
                if (replaced)
                {
                    ownerId = Guid.NewGuid();
                    reset.RuntimeInstances.Add(new TemplateTestRuntimeInstance
                    {
                        Id = ownerId, ChallengeId = original.ChallengeId,
                        RuntimeKind = original.RuntimeKind, RuntimeProvider = original.RuntimeProvider,
                        State = RuntimeState.Queued, CreatedAt = original.CreatedAt.AddSeconds(1),
                        TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired,
                        TestFlagState = RuntimeTestFlagState.NotRequired
                    });
                }
                await reset.SaveChangesAsync(ct);
            }

            // Cleanup uses a separate context that does not track the replacement owner.
            await Assert.That(await fixture.ReleaseAsync(originalId, "runner", ct))
                .IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            await using var verify = new NoCtfDbContext(fixture.Options);
            await Assert.That(await verify.ActiveRuntimeSlots.Select(slot => slot.RuntimeInstanceId).SingleAsync(ct))
                .IsEqualTo(ownerId);
            var originalAfter = await verify.RuntimeInstances.SingleAsync(item => item.Id == originalId, ct);
            await Assert.That(originalAfter.CapacityAllocations.Items).IsEmpty();
            await Assert.That(originalAfter.ActiveSlot is null).IsEqualTo(replaced);
            originalAfter.State = RuntimeState.Stopped;
            await verify.SaveChangesAsync(ct);
            await Assert.That(await verify.ActiveRuntimeSlots.CountAsync(ct)).IsEqualTo(replaced ? 1 : 0);
        });
    }

    [Test, Arguments(RuntimeState.Stopping), Arguments(RuntimeState.Failed), Timeout(300_000)]
    public async Task Cleanup_reservations_keep_resource_budgets_without_occupying_startup_slots(
        RuntimeState cleanupState, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(4, ct);
            await fixture.AddOwnerAsync("runner", "domain", new(384, 100, 100), 2, ct);
            foreach (var id in fixture.RuntimeIds.Take(2))
                await Assert.That((await fixture.ClaimAsync(id, "runner", ct)).State)
                    .IsEqualTo(RunnerCapacityClaimState.Acquired);
            await Assert.That((await fixture.ClaimAsync(fixture.RuntimeIds[2], "runner", ct)).Failure)
                .IsEqualTo(RunnerAdmissionFailure.StartupConcurrencyLimited);
            await using (var setup = new NoCtfDbContext(fixture.Options))
            {
                foreach (var runtime in await setup.RuntimeInstances
                             .Where(item => fixture.RuntimeIds.Take(2).Contains(item.Id)).ToArrayAsync(ct))
                    runtime.State = cleanupState;
                await setup.SaveChangesAsync(ct);
            }

            await Assert.That((await fixture.ClaimAsync(fixture.RuntimeIds[2], "runner", ct)).State)
                .IsEqualTo(RunnerCapacityClaimState.Acquired);
            await Assert.That((await fixture.ClaimAsync(fixture.RuntimeIds[3], "runner", ct)).Failure)
                .IsEqualTo(RunnerAdmissionFailure.MemoryActualCapacityInsufficient);
            await using var verify = new NoCtfDbContext(fixture.Options);
            await Assert.That(await verify.RuntimeInstances.SelectMany(item => item.CapacityAllocationEntries)
                    .SumAsync(item => item.LimitMemoryBytes, ct))
                .IsEqualTo(384L);
        });
    }

    [Test, Arguments(RunnerHeartbeatStatus.Online), Arguments(RunnerHeartbeatStatus.Offline), Timeout(300_000)]
    public async Task Assignment_recovery_redispatches_stops_without_stopping_healthy_runtimes(
        RunnerHeartbeatStatus heartbeat, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(2, ct);
            await using (var setup = new NoCtfDbContext(fixture.Options))
            {
                var runtimes = await setup.RuntimeInstances.OrderBy(item => item.Id).ToArrayAsync(ct);
                foreach (var runtime in runtimes) runtime.RunnerId = "runner";
                runtimes[0].State = RuntimeState.Stopping;
                runtimes[1].State = RuntimeState.Running;
                await setup.SaveChangesAsync(ct);
            }
            var stoppingId = fixture.RuntimeIds.Order().First();
            var capacity = Substitute.For<IRunnerCapacityGate>();
            capacity.GetHeartbeatAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(heartbeat);
            capacity.GetPoolInventoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new RunnerPoolInventory(RunnerPoolInventoryAvailability.Available, []));
            var placement = Substitute.For<IRuntimePlacementPolicy>();
            placement.Resolve(Arg.Any<RuntimeKind>()).Returns(new RuntimePlacement(RuntimeProvider.Docker, "domain-tests"));

            // Repeated scans must continue recovering a lost stop while the node is alive.
            for (var pass = 0; pass < 2; pass++)
            {
                var publisher = Substitute.For<IPostCommitMessagePublisher>();
                await using var db = new NoCtfDbContext(fixture.Options);
                await BackendMessageOperations.ExecuteRunnerAssignmentReconciliationAsync(
                    new ReconcileRunnerAssignments(DateTimeOffset.UtcNow), db, capacity, placement,
                    publisher, TimeProvider.System, ct);
                await publisher.Received(1).PublishToRunnerNodeAsync(
                    Arg.Is<StopContainerRuntime>(message => message != null && message.RuntimeInstanceId == stoppingId && message.RunnerId == "runner"));
                await publisher.DidNotReceive().PublishToRunnerNodeAsync(
                    Arg.Is<StopContainerRuntime>(message => message != null && message.RuntimeInstanceId != stoppingId));
            }
        });
    }
}
