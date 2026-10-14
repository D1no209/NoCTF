using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed partial class RuntimeCapacityDomainTests
{
    [Test, Arguments(1), Arguments(2), Timeout(300_000)]
    public async Task Independent_processes_never_oversell_each_domain(int domains, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(domains * 4, ct);
            for (var domain = 0; domain < domains; domain++)
                await fixture.AddOwnerAsync($"runner-{domain}", $"domain-{domain}", new(256, 100, 100), 8, ct);
            var claims = await Task.WhenAll(fixture.RuntimeIds.Select(async (id, index) =>
            {
                // Distinct coordinators model separate processes; correctness comes from EF.
                using var coordinator = new RunnerCapacityLedgerCoordinator();
                return await fixture.ClaimAsync(id, $"runner-{index % domains}", ct, coordinator);
            }));
            await using var db = new NoCtfDbContext(fixture.Options);
            var allocations = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
                .SelectMany(item => item.CapacityAllocationEntries).ToArrayAsync(ct);
            await Assert.That(claims.Count(item => item.State == RunnerCapacityClaimState.Acquired))
                .IsEqualTo(domains * 2);
            foreach (var domain in allocations.GroupBy(item => item.ResourceDomain))
            {
                await Assert.That(domain.Count()).IsEqualTo(2);
                await Assert.That(domain.Sum(item => item.LimitMemoryBytes)).IsEqualTo(256);
            }
            await Assert.That(await db.RuntimeCapacityLedgers.CountAsync(ct)).IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Ownership_handoff_retains_the_previous_owners_pending_reservations(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(2, ct);
            await fixture.AddOwnerAsync("old-runner", "shared-domain", new(128, 100, 100), 8, ct);
            await Assert.That((await fixture.ClaimAsync(fixture.RuntimeIds[0], "old-runner", ct)).State)
                .IsEqualTo(RunnerCapacityClaimState.Acquired);
            await fixture.ReleaseOwnerAsync("old-runner");
            await fixture.AddOwnerAsync("new-runner", "shared-domain", new(128, 100, 100), 8, ct);

            var blocked = await fixture.ClaimAsync(fixture.RuntimeIds[1], "new-runner", ct);
            await Assert.That(blocked.Failure).IsEqualTo(RunnerAdmissionFailure.MemoryActualCapacityInsufficient);
            await Assert.That(await fixture.ReleaseAsync(fixture.RuntimeIds[0], "new-runner", ct))
                .IsEqualTo(RunnerCapacityReleaseOutcome.OwnerMismatch);
            await Assert.That(await fixture.ReleaseAsync(fixture.RuntimeIds[0], "old-runner", ct))
                .IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            await Assert.That(await fixture.ReleaseAsync(fixture.RuntimeIds[0], "old-runner", ct))
                .IsEqualTo(RunnerCapacityReleaseOutcome.AlreadyReleased);
            await Assert.That((await fixture.ClaimAsync(fixture.RuntimeIds[1], "new-runner", ct)).State)
                .IsEqualTo(RunnerCapacityClaimState.Acquired);
            await using var db = new NoCtfDbContext(fixture.Options);
            await Assert.That((await db.RuntimeCapacityLedgers.SingleAsync(ct)).Id)
                .IsEqualTo((short)1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Different_domain_claims_for_the_same_runtime_preserve_one_identity(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(1, ct);
            await fixture.AddOwnerAsync("runner-a", "domain-a", new(512, 100, 100), 4, ct);
            await fixture.AddOwnerAsync("runner-b", "domain-b", new(512, 100, 100), 4, ct);
            using var first = new RunnerCapacityLedgerCoordinator();
            using var second = new RunnerCapacityLedgerCoordinator();
            var claims = await Task.WhenAll(
                fixture.ClaimAsync(fixture.RuntimeIds[0], "runner-a", ct, first),
                fixture.ClaimAsync(fixture.RuntimeIds[0], "runner-b", ct, second));
            await Assert.That(claims.Count(item => item.State == RunnerCapacityClaimState.Acquired)).IsEqualTo(1);
            await using var db = new NoCtfDbContext(fixture.Options);
            await Assert.That(await db.RuntimeInstances.SelectMany(item => item.CapacityAllocationEntries).CountAsync(ct))
                .IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Ambient_rollback_preserves_the_global_fence_and_removes_its_allocation(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(1, ct);
            await fixture.AddOwnerAsync("runner", "domain", new(512, 100, 100), 4, ct);
            Guid originalFence;
            await using (var db = new NoCtfDbContext(fixture.Options))
            {
                originalFence = await db.RuntimeCapacityLedgers.AsNoTracking()
                    .Select(item => item.ConcurrencyStamp).SingleAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var claim = await fixture.Gate(db).TryClaimForRunnerAsync(
                    new(fixture.RuntimeIds[0], RuntimeCapacityDomainFixture.Pool, 128, 10, 1), "runner", ct);
                await Assert.That(claim.State).IsEqualTo(RunnerCapacityClaimState.Acquired);
                await transaction.RollbackAsync(ct);
            }
            await using var verify = new NoCtfDbContext(fixture.Options);
            await Assert.That(await verify.RuntimeCapacityLedgers.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await verify.RuntimeCapacityLedgers.Select(item => item.ConcurrencyStamp).SingleAsync(ct))
                .IsEqualTo(originalFence);
            await Assert.That(await verify.RuntimeInstances.SelectMany(item => item.CapacityAllocationEntries).CountAsync(ct))
                .IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Reusing_a_scoped_context_after_external_release_acquires_a_new_allocation(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(1, ct);
            await fixture.AddOwnerAsync("runner", "domain", new(512, 100, 100), 4, ct);
            await using var db = new NoCtfDbContext(fixture.Options);
            var gate = fixture.Gate(db);
            var request = new RunnerCapacityRequest(fixture.RuntimeIds[0], RuntimeCapacityDomainFixture.Pool, 128, 10, 1);
            await Assert.That((await gate.TryClaimForRunnerAsync(request, "runner", ct)).State)
                .IsEqualTo(RunnerCapacityClaimState.Acquired);
            await Assert.That(await fixture.ReleaseAsync(request.RuntimeInstanceId, "runner", ct))
                .IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            await Assert.That((await gate.TryClaimForRunnerAsync(request, "runner", ct)).State)
                .IsEqualTo(RunnerCapacityClaimState.Acquired);
            await Assert.That(await db.RuntimeInstances.SelectMany(item => item.CapacityAllocationEntries).CountAsync(ct))
                .IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Reinserted_identity_uses_current_owner_and_entry_even_with_a_stale_tracked_collection(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(1, ct);
            await fixture.AddOwnerAsync("runner-a", "domain-a", new(512, 100, 100), 4, ct);
            await fixture.AddOwnerAsync("runner-b", "domain-b", new(512, 100, 100), 4, ct);
            await using var db = new NoCtfDbContext(fixture.Options);
            var gate = fixture.Gate(db);
            var request = new RunnerCapacityRequest(fixture.RuntimeIds[0], RuntimeCapacityDomainFixture.Pool, 128, 10, 1);
            await gate.TryClaimForRunnerAsync(request, "runner-a", ct);
            await fixture.ReleaseAsync(request.RuntimeInstanceId, "runner-a", ct);
            await fixture.ClaimAsync(request.RuntimeInstanceId, "runner-b", ct);

            await Assert.That((await gate.TryClaimForRunnerAsync(request, "runner-b", ct)).State)
                .IsEqualTo(RunnerCapacityClaimState.AlreadyOwned);
            await Assert.That(await gate.ReleaseAsync(request.RuntimeInstanceId, "runner-a", ct))
                .IsEqualTo(RunnerCapacityReleaseOutcome.OwnerMismatch);
            await Assert.That(await gate.ReleaseAsync(request.RuntimeInstanceId, "runner-b", ct))
                .IsEqualTo(RunnerCapacityReleaseOutcome.Released);
            await Assert.That(await db.RuntimeInstances.SelectMany(item => item.CapacityAllocationEntries).CountAsync(ct))
                .IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task A_stale_ambient_graph_fails_without_discarding_the_callers_pending_changes(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(1, ct);
            await fixture.AddOwnerAsync("runner", "domain", new(512, 100, 100), 4, ct);
            await using var db = new NoCtfDbContext(fixture.Options);
            var gate = fixture.Gate(db);
            var request = new RunnerCapacityRequest(fixture.RuntimeIds[0], RuntimeCapacityDomainFixture.Pool, 128, 10, 1);
            await gate.TryClaimForRunnerAsync(request, "runner", ct);
            var tracked = db.RuntimeInstances.Local.Single();
            tracked.TrafficCaptureEnabled = true;
            await fixture.ReleaseAsync(request.RuntimeInstanceId, "runner", ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            {
                await gate.TryClaimForRunnerAsync(request, "runner", ct);
            });
            db.ChangeTracker.DetectChanges();
            await Assert.That(db.Entry(tracked).State).IsEqualTo(EntityState.Modified);
            await Assert.That(tracked.TrafficCaptureEnabled).IsTrue();
            await transaction.RollbackAsync(ct);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Post_commit_release_failure_is_visible_without_replaying_or_discarding_publication(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(1, ct);
            await fixture.AddOwnerAsync("runner", "domain", new(512, 100, 100), 4, ct);
            await fixture.ClaimAsync(fixture.RuntimeIds[0], "runner", ct);
            await using var db = new NoCtfDbContext(fixture.Options);
            var publisher = new FailingReleasePublisher();
            var gate = fixture.Gate(db, publisher: publisher);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            {
                await gate.ReleaseAsync(fixture.RuntimeIds[0], "runner", ct);
            });
            await Assert.That(publisher.Flushes).IsEqualTo(1);
            await Assert.That(publisher.BufferedMessages).IsEqualTo(1);
            await Assert.That(publisher.DiscardedBatches).IsEqualTo(0);
            await using var verify = new NoCtfDbContext(fixture.Options);
            await Assert.That(await verify.RuntimeInstances.SelectMany(item => item.CapacityAllocationEntries).CountAsync(ct))
                .IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task An_ambient_release_leaves_publication_and_rollback_to_its_transaction_owner(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(1, ct);
            await fixture.AddOwnerAsync("runner", "domain", new(512, 100, 100), 4, ct);
            await fixture.ClaimAsync(fixture.RuntimeIds[0], "runner", ct);
            var publisher = new FailingReleasePublisher();
            await using (var db = new NoCtfDbContext(fixture.Options))
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await Assert.That(await fixture.Gate(db, publisher: publisher).ReleaseAsync(fixture.RuntimeIds[0], "runner", ct))
                    .IsEqualTo(RunnerCapacityReleaseOutcome.Released);
                await Assert.That(publisher.Flushes).IsEqualTo(0);
                await Assert.That(publisher.BufferedMessages).IsEqualTo(1);
                await transaction.RollbackAsync(ct);
            }
            await using var verify = new NoCtfDbContext(fixture.Options);
            await Assert.That(await verify.RuntimeInstances.SelectMany(item => item.CapacityAllocationEntries).CountAsync(ct))
                .IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Index_upgrade_and_downgrade_preserve_the_global_fence_and_existing_allocations(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(3, ct, previousSchema: true);
            RuntimeCapacityAllocation preserved;
            await using (var db = new NoCtfDbContext(fixture.Options))
            {
                await Assert.That(await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::integer AS \"Value\" FROM runtime_capacity_ledger WHERE id = 1").SingleAsync(ct))
                    .IsEqualTo(1);
                var runtime = await db.RuntimeInstances.SingleAsync(item => item.Id == fixture.RuntimeIds[0], ct);
                preserved = new(PersistedRunnerCapacityGate.PrimaryIdentity(runtime), null, "domain", "runner",
                    new(128, 10, 1), new(128, 10, 1));
                var entry = RuntimeCapacityAllocationEntry.FromValue(preserved);
                runtime.CapacityAllocationEntries.Add(entry);
                db.Entry(entry).State = EntityState.Added;
                await db.SaveChangesAsync(ct);
                await db.Database.MigrateAsync(ct);
            }
            await using (var verify = new NoCtfDbContext(fixture.Options))
            {
                await Assert.That(await verify.RuntimeCapacityLedgers.CountAsync(ct)).IsEqualTo(1);
                await Assert.That(await verify.RuntimeInstances.CountAsync(ct)).IsEqualTo(3);
                await Assert.That(await verify.Challenges.CountAsync(ct)).IsEqualTo(3);
                var runtime = await verify.RuntimeInstances.Include(item => item.CapacityAllocationEntries)
                    .SingleAsync(item => item.Id == fixture.RuntimeIds[0], ct);
                await Assert.That(runtime.CapacityAllocations.Items.Single()).IsEqualTo(preserved);
            }
            await fixture.AddOwnerAsync("runner", "domain", new(256, 100, 100), 8, ct);
            await Assert.That((await fixture.ClaimAsync(fixture.RuntimeIds[1], "runner", ct)).State)
                .IsEqualTo(RunnerCapacityClaimState.Acquired);
            await Assert.That((await fixture.ClaimAsync(fixture.RuntimeIds[2], "runner", ct)).Failure)
                .IsEqualTo(RunnerAdmissionFailure.MemoryActualCapacityInsufficient);
            await using (var downgrade = new NoCtfDbContext(fixture.Options))
            {
                var originalFence = await downgrade.RuntimeCapacityLedgers.AsNoTracking()
                    .Select(item => item.ConcurrencyStamp).SingleAsync(ct);
                // Index-only upgrades and downgrades leave the authoritative allocation and fence rows intact.
                await downgrade.GetService<IMigrator>().MigrateAsync("20261013124256_MergeAwdAndTheoryModels", ct);
                await Assert.That(await downgrade.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::integer AS \"Value\" FROM runtime_capacity_ledger WHERE id = 1").SingleAsync(ct))
                    .IsEqualTo(1);
                await Assert.That(await downgrade.RuntimeInstances.SelectMany(item => item.CapacityAllocationEntries).CountAsync(ct))
                    .IsEqualTo(2);
                await downgrade.Database.MigrateAsync(ct);
                await Assert.That(await downgrade.RuntimeCapacityLedgers.CountAsync(ct)).IsEqualTo(1);
                await Assert.That(await downgrade.RuntimeCapacityLedgers.AsNoTracking()
                    .Select(item => item.ConcurrencyStamp).SingleAsync(ct)).IsEqualTo(originalFence);
                await Assert.That(await downgrade.RuntimeInstances.CountAsync(ct)).IsEqualTo(3);
            }
            await fixture.RefreshOwnersAsync(ct);
            await Assert.That((await fixture.ClaimAsync(fixture.RuntimeIds[2], "runner", ct)).Failure)
                .IsEqualTo(RunnerAdmissionFailure.MemoryActualCapacityInsufficient);
        });
    }

    private sealed class FailingReleasePublisher : IPostCommitMessagePublisher
    {
        public int BufferedMessages { get; private set; }
        public int Flushes { get; private set; }
        public int DiscardedBatches { get; private set; }
        public ValueTask PublishAsync<T>(T message) { BufferedMessages++; return ValueTask.CompletedTask; }
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => PublishAsync(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
        public void DiscardPendingMessages() => DiscardedBatches++;
        public Task FlushCommittedMessagesAsync()
        {
            Flushes++;
            return Task.FromException(new DbUpdateConcurrencyException("Injected isolated post-commit release publication failure."));
        }
    }
}
