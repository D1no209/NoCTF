using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class ChallengeConfigurationTests
{
    [Test]
    [Arguments(CompetitionStatus.Running)]
    [Arguments(CompetitionStatus.Paused)]
    [Arguments(CompetitionStatus.Finished)]
    public async Task UpdateChallengeConfiguration_ActiveOrFinishedCompetition_IsLocked(
        CompetitionStatus status)
    {
        var store = new Store(View(status));
        var useCase = new UpdateChallengeConfiguration(
            store,
            new Catalog(),
            new Cache(),
            new Scheduler());

        var result = await useCase.ExecuteAsync(
            store.Current!.CompetitionId,
            store.Current.ChallengeId,
            0,
            ValidJson,
            DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("configuration_locked");
        await Assert.That(store.UpdateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateChallengeConfiguration_InvalidModeConfiguration_IsRejectedBeforeWrite()
    {
        var store = new Store(View(CompetitionStatus.Draft));
        var useCase = new UpdateChallengeConfiguration(
            store,
            new Catalog(["invalid challenge configuration"]),
            new Cache(),
            new Scheduler());

        var result = await useCase.ExecuteAsync(
            store.Current!.CompetitionId,
            store.Current.ChallengeId,
            0,
            "{}",
            DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("invalid_configuration");
        await Assert.That(store.UpdateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateChallengeConfiguration_RevisionChanged_ReturnsConflict()
    {
        var store = new Store(View(CompetitionStatus.Published)) { Conflict = true };
        var useCase = new UpdateChallengeConfiguration(
            store,
            new Catalog(),
            new Cache(),
            new Scheduler());

        var result = await useCase.ExecuteAsync(
            store.Current!.CompetitionId,
            store.Current.ChallengeId,
            4,
            ValidJson,
            DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("configuration_conflict");
    }

    [Test]
    public async Task UpdateChallengeConfiguration_Success_InvalidatesAndQueuesRebuild()
    {
        var current = View(CompetitionStatus.Draft);
        var store = new Store(current);
        var cache = new Cache();
        var scheduler = new Scheduler();
        var useCase = new UpdateChallengeConfiguration(store, new Catalog(), cache, scheduler);

        var result = await useCase.ExecuteAsync(
            current.CompetitionId,
            current.ChallengeId,
            current.Revision,
            ValidJson,
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(cache.InvalidatedCompetitionId).IsEqualTo(current.CompetitionId);
        await Assert.That(scheduler.RebuildCompetitionId).IsEqualTo(current.CompetitionId);
    }

    private const string ValidJson = """{"schemaVersion":1}""";

    private static ChallengeConfigurationView View(CompetitionStatus status) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        GameMode.Ctf,
        ValidJson,
        4,
        status,
        DateTimeOffset.UtcNow);

    private sealed class Store(ChallengeConfigurationView? current) : IChallengeConfigurationStore
    {
        public ChallengeConfigurationView? Current { get; } = current;
        public bool Conflict { get; init; }
        public int UpdateCalls { get; private set; }

        public Task<ChallengeConfigurationView?> FindAsync(
            Guid competitionId,
            Guid challengeId,
            CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<ChallengeConfigurationView?> TryUpdateAsync(
            Guid competitionId,
            Guid challengeId,
            int expectedRevision,
            string json,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.FromResult(Conflict || Current is null
                ? null
                : Current with { Json = json, Revision = expectedRevision + 1, UpdatedAt = updatedAt });
        }
    }

    private sealed class Catalog(IReadOnlyList<string>? errors = null) : IChallengeConfigurationCatalog
    {
        public string GetDefaultJson(GameMode mode) => ValidJson;
        public IReadOnlyList<string> Validate(GameMode mode, string json) => errors ?? [];
    }

    private sealed class Cache : ILeaderboardCache
    {
        public Guid? InvalidatedCompetitionId { get; private set; }
        public Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(null);
        public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken)
        {
            InvalidatedCompetitionId = competitionId;
            return Task.CompletedTask;
        }
    }

    private sealed class Scheduler : IBackgroundWorkScheduler
    {
        public Guid? RebuildCompetitionId { get; private set; }
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken)
        {
            RebuildCompetitionId = competitionId;
            return ValueTask.CompletedTask;
        }
    }
}
