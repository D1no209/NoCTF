using NoCTF.Application.Messaging;
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
            store, new Validator(), dependencies, dependencies);

        var result = await useCase.ExecuteAsync(store.CompetitionId, 1, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.AllowWhileRunning).IsTrue();
        await Assert.That(dependencies.Invalidated).IsEqualTo(1);
        await Assert.That(dependencies.Rebuilt).IsEqualTo(1);
    }

    [Test]
    public async Task Running_destructive_change_is_allowed()
    {
        var store = new Store(CompetitionStatus.Running);
        var dependencies = new CacheAndScheduler();
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator(), dependencies, dependencies);

        var result = await useCase.ExecuteAsync(store.CompetitionId, 1, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Paused_change_is_allowed()
    {
        var store = new Store(CompetitionStatus.Paused);
        var dependencies = new CacheAndScheduler();
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator(), dependencies, dependencies);

        var result = await useCase.ExecuteAsync(store.CompetitionId, 1, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    private sealed class Store(CompetitionStatus status) : ICompetitionConfigurationStore
    {
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public int UpdateCalls { get; private set; }
        public bool AllowWhileRunning { get; private set; }

        public Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionConfigurationView?>(new(
                CompetitionId, GameMode.Ctf, "{\"value\":1}", 1, status, 2, [], DateTimeOffset.UtcNow));

        public Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
            Guid competitionId,
            int expectedRevision,
            string json,
            bool allowWhileRunning,
            IReadOnlyDictionary<Guid, int> expectedChallengeRevisions,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            AllowWhileRunning = allowWhileRunning;
            return Task.FromResult(new CompetitionConfigurationUpdateResult(
                new(competitionId, GameMode.Ctf, json, expectedRevision + 1, status, 2, [], now)));
        }
    }

    private sealed class Validator : ICompetitionConfigurationValidator
    {
        public IReadOnlyList<string> Validate(
            GameMode mode,
            string json,
            int eligibleTeamCount,
            IReadOnlyList<string> challengeConfigurationJsons) => [];
    }

    private sealed class CacheAndScheduler : ILeaderboardCache, IBackendMessagePublisher
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
        public ValueTask RebuildCompetitionAsync(Guid competitionId, CancellationToken cancellationToken)
        {
            Rebuilt++;
            return ValueTask.CompletedTask;
        }
        public ValueTask ProjectLeaderboardAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
