using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionLifecycleUseCaseTests
{
    [Test]
    public async Task Automatic_deadline_finishes_without_starting_an_already_ended_competition()
    {
        var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.CreateVersion7();
        var store = new AdvancingStore(new CompetitionLifecycleSnapshot(
            competitionId,
            CompetitionStatus.Published,
            now.AddMinutes(-2),
            now.AddMinutes(-1)));
        var transitions = await new NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase(store)
            .ExecuteAsync(now);

        await Assert.That(transitions).HasSingleItem();
        await Assert.That(transitions[0].From).IsEqualTo(CompetitionStatus.Published);
        await Assert.That(transitions[0].To).IsEqualTo(CompetitionStatus.Finished);
        await Assert.That(store.Status).IsEqualTo(CompetitionStatus.Finished);
    }

    [Test]
    public async Task Resume_QueuesRuntimeProvision()
    {
        var competitionId = Guid.NewGuid();
        var store = new Store(CompetitionStatus.Paused);
        var useCase = new TransitionCompetitionLifecycle(store, TimeProvider.System);

        var result = await useCase.ExecuteAsync(
            competitionId, CompetitionStatus.Running, Guid.NewGuid(), "resume");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.LastEffects.HasFlag(
            CompetitionLifecycleEffects.ProvisionRuntimes)).IsTrue();
        await Assert.That(store.LastEffects.HasFlag(
            CompetitionLifecycleEffects.CleanupRuntimes)).IsFalse();
    }

    [Test]
    public async Task Finish_QueuesRuntimeCleanup()
    {
        var competitionId = Guid.NewGuid();
        var store = new Store(CompetitionStatus.Running);
        var useCase = new TransitionCompetitionLifecycle(store, TimeProvider.System);

        var result = await useCase.ExecuteAsync(
            competitionId, CompetitionStatus.Finished, Guid.NewGuid(), "finish");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.LastEffects.HasFlag(
            CompetitionLifecycleEffects.CleanupRuntimes)).IsTrue();
        await Assert.That(store.LastEffects.HasFlag(
            CompetitionLifecycleEffects.ProvisionRuntimes)).IsFalse();
    }

    private sealed class Store(CompetitionStatus status) : ICompetitionLifecycleStore
    {
        public CompetitionLifecycleEffects LastEffects { get; private set; }

        public Task<CompetitionStatus?> GetStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStatus?>(status);

        public Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompetitionLifecycleSnapshot>>([]);

        public Task<bool> TryTransitionAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> TryTransitionWithAuditAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            Guid? actorId,
            string? reason,
            bool automatic,
            CompetitionLifecycleEffects effects,
            CancellationToken cancellationToken)
        {
            LastEffects = effects;
            return Task.FromResult(true);
        }
    }

    private sealed class AdvancingStore(CompetitionLifecycleSnapshot snapshot)
        : ICompetitionLifecycleStore
    {
        public CompetitionStatus Status { get; private set; } = snapshot.Status;

        public Task<CompetitionStatus?> GetStatusAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<CompetitionStatus?>(Status);

        public Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompetitionLifecycleSnapshot>>([snapshot]);

        public Task<bool> TryTransitionAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            CancellationToken cancellationToken) => ApplyAsync(from, to);

        public Task<bool> TryTransitionWithAuditAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            Guid? actorId,
            string? reason,
            bool automatic,
            CompetitionLifecycleEffects effects,
            CancellationToken cancellationToken) => ApplyAsync(from, to);

        private Task<bool> ApplyAsync(CompetitionStatus from, CompetitionStatus to)
        {
            if (Status != from)
                return Task.FromResult(false);
            Status = to;
            return Task.FromResult(true);
        }
    }

}
