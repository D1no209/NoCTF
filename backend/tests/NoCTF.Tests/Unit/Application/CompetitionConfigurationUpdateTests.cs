using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionConfigurationUpdateTests
{
    [Test]
    public async Task Running_non_destructive_change_is_persisted_and_rebuilt()
    {
        var store = new Store(CompetitionStatus.Running);
        var dependencies = new CacheAndScheduler();
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator(), new ChangePolicy(true), dependencies, dependencies);

        var result = await useCase.ExecuteAsync(store.CompetitionId, 1, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.AllowWhileRunning).IsTrue();
        await Assert.That(dependencies.Invalidated).IsEqualTo(1);
        await Assert.That(dependencies.Rebuilt).IsEqualTo(1);
    }

    [Test]
    public async Task Running_destructive_change_is_rejected_before_store()
    {
        var store = new Store(CompetitionStatus.Running);
        var dependencies = new CacheAndScheduler();
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator(), new ChangePolicy(false), dependencies, dependencies);

        var result = await useCase.ExecuteAsync(store.CompetitionId, 1, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("configuration_locked");
        await Assert.That(store.UpdateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Paused_change_remains_locked_even_when_non_destructive()
    {
        var store = new Store(CompetitionStatus.Paused);
        var dependencies = new CacheAndScheduler();
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator(), new ChangePolicy(true), dependencies, dependencies);

        var result = await useCase.ExecuteAsync(store.CompetitionId, 1, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("configuration_locked");
        await Assert.That(store.UpdateCalls).IsEqualTo(0);
    }

    private sealed class Store(CompetitionStatus status) : ICompetitionConfigurationStore
    {
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public int UpdateCalls { get; private set; }
        public bool AllowWhileRunning { get; private set; }

        public Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionConfigurationView?>(new(
                CompetitionId, GameMode.Ctf, "{\"value\":1}", 1, status, DateTimeOffset.UtcNow));

        public Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
            Guid competitionId,
            int expectedRevision,
            string json,
            bool allowWhileRunning,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            AllowWhileRunning = allowWhileRunning;
            return Task.FromResult(new CompetitionConfigurationUpdateResult(
                new(competitionId, GameMode.Ctf, json, expectedRevision + 1, status, now)));
        }
    }

    private sealed class Validator : ICompetitionConfigurationValidator
    {
        public IReadOnlyList<string> Validate(GameMode mode, string json) => [];
    }

    private sealed class ChangePolicy(bool allowed) : ICompetitionConfigurationChangePolicy
    {
        public bool IsNonDestructive(GameMode mode, string currentJson, string proposedJson) => allowed;
    }

    private sealed class CacheAndScheduler : ILeaderboardCache, IBackgroundWorkScheduler
    {
        public int Invalidated { get; private set; }
        public int Rebuilt { get; private set; }
        public Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(null);
        public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken)
        {
            Invalidated++;
            return Task.CompletedTask;
        }
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken)
        {
            Rebuilt++;
            return ValueTask.CompletedTask;
        }
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
