using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class ChallengeManagementTests
{
    [Test]
    [Arguments(CompetitionStatus.Running)]
    [Arguments(CompetitionStatus.Paused)]
    [Arguments(CompetitionStatus.Finished)]
    public async Task CreateChallenge_LockedCompetition_RejectsMutation(CompetitionStatus status)
    {
        var store = new Store { Status = status };

        var result = await new CreateChallenge(store, new Catalog()).ExecuteAsync(CreateCommand());

        await Assert.That(result.ErrorCode).IsEqualTo("challenge_locked");
        await Assert.That(store.CreateCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("", "web", 0, "invalid_title")]
    [Arguments("challenge", "", 0, "invalid_direction")]
    [Arguments("challenge", "web", -1, "invalid_order")]
    public async Task CreateChallenge_InvalidDefinition_ReturnsSpecificError(
        string title,
        string direction,
        int order,
        string expectedError)
    {
        var store = new Store();

        var result = await new CreateChallenge(store, new Catalog()).ExecuteAsync(
            CreateCommand() with { Title = title, Direction = direction, Order = order });

        await Assert.That(result.ErrorCode).IsEqualTo(expectedError);
        await Assert.That(store.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task CreateChallenge_OrderConflict_PropagatesTypedErrorCode()
    {
        var store = new Store
        {
            CreateResult = new ChallengeMutationResult(null, ChallengeMutationFailure.ChallengeOrderConflict)
        };

        var result = await new CreateChallenge(store, new Catalog()).ExecuteAsync(CreateCommand());

        await Assert.That(result.ErrorCode).IsEqualTo("challenge_order_conflict");
    }

    [Test]
    public async Task ListChallenges_PublicQuery_ExcludesUnpublishedAtStoreBoundary()
    {
        var store = new Store();

        await new ListChallenges(store).ExecuteAsync(Guid.NewGuid(), includeUnpublished: false);

        await Assert.That(store.LastIncludeUnpublished).IsFalse();
    }

    [Test]
    public async Task GetChallenge_AdminQuery_IncludesUnpublishedAtStoreBoundary()
    {
        var store = new Store();

        await new GetChallenge(store).ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), includeUnpublished: true);

        await Assert.That(store.LastIncludeUnpublished).IsTrue();
    }

    [Test]
    [Arguments(ChallengeMutation.Update)]
    [Arguments(ChallengeMutation.Publish)]
    [Arguments(ChallengeMutation.Delete)]
    public async Task Mutation_ActiveCompetition_IsRejectedBeforeStoreWrite(ChallengeMutation mutation)
    {
        var store = new Store { Status = CompetitionStatus.Running };
        var cache = new Cache();
        var scheduler = new Scheduler();

        var error = mutation switch
        {
            ChallengeMutation.Update => (await new UpdateChallenge(store, cache, scheduler)
                .ExecuteAsync(UpdateCommand())).ErrorCode,
            ChallengeMutation.Publish => (await new SetChallengePublished(store, cache, scheduler)
                .ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), true, DateTimeOffset.UtcNow)).ErrorCode,
            ChallengeMutation.Delete => (await new DeleteChallenge(store, cache, scheduler)
                .ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow)).ErrorCode,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };

        await Assert.That(error).IsEqualTo("challenge_locked");
        await Assert.That(store.MutationCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(ChallengeMutation.Update)]
    [Arguments(ChallengeMutation.Publish)]
    [Arguments(ChallengeMutation.Delete)]
    public async Task Mutation_Succeeds_InvalidatesAndQueuesCompetitionRebuild(ChallengeMutation mutation)
    {
        var competitionId = Guid.NewGuid();
        var store = new Store();
        var cache = new Cache();
        var scheduler = new Scheduler();

        _ = mutation switch
        {
            ChallengeMutation.Update => (object)await new UpdateChallenge(store, cache, scheduler)
                .ExecuteAsync(UpdateCommand(competitionId)),
            ChallengeMutation.Publish => await new SetChallengePublished(store, cache, scheduler)
                .ExecuteAsync(competitionId, Guid.NewGuid(), true, DateTimeOffset.UtcNow),
            ChallengeMutation.Delete => await new DeleteChallenge(store, cache, scheduler)
                .ExecuteAsync(competitionId, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };

        await Assert.That(cache.InvalidatedCompetitionId).IsEqualTo(competitionId);
        await Assert.That(scheduler.RebuildCompetitionId).IsEqualTo(competitionId);
    }

    public enum ChallengeMutation
    {
        Update,
        Publish,
        Delete
    }

    private static CreateChallengeCommand CreateCommand(Guid? competitionId = null) => new(
        competitionId ?? Guid.NewGuid(),
        "Web 100",
        "Description",
        "Web",
        1,
        DateTimeOffset.UtcNow);

    private static UpdateChallengeCommand UpdateCommand(Guid? competitionId = null) => new(
        competitionId ?? Guid.NewGuid(),
        Guid.NewGuid(),
        "Web 100",
        "Description",
        "Web",
        2,
        DateTimeOffset.UtcNow);

    private sealed class Store : IChallengeManagementStore
    {
        private readonly ChallengeView challenge = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Web 100",
            "Description",
            "Web",
            1,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        public CompetitionStatus? Status { get; init; } = CompetitionStatus.Draft;
        public ChallengeMutationResult? CreateResult { get; init; }
        public int CreateCalls { get; private set; }
        public int MutationCalls { get; private set; }
        public bool? LastIncludeUnpublished { get; private set; }

        public Task<ChallengeCompetitionContext?> GetCompetitionAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(
                Status is null ? null : new ChallengeCompetitionContext(GameMode.Ctf, Status.Value));

        public Task<ChallengeMutationResult> CreateAsync(
            CreateChallengeCommand command,
            string configurationJson,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(CreateResult ?? new ChallengeMutationResult(challenge, null));
        }

        public Task<ChallengeView?> FindAsync(
            Guid competitionId,
            Guid challengeId,
            bool includeUnpublished,
            CancellationToken cancellationToken)
        {
            LastIncludeUnpublished = includeUnpublished;
            return Task.FromResult<ChallengeView?>(challenge);
        }

        public Task<IReadOnlyList<ChallengeView>> ListAsync(
            Guid competitionId,
            bool includeUnpublished,
            CancellationToken cancellationToken)
        {
            LastIncludeUnpublished = includeUnpublished;
            return Task.FromResult<IReadOnlyList<ChallengeView>>([challenge]);
        }

        public Task<ChallengeMutationResult> UpdateAsync(
            UpdateChallengeCommand command,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            return Task.FromResult(new ChallengeMutationResult(challenge, null));
        }

        public Task<ChallengeMutationFailure?> SetPublishedAsync(
            Guid competitionId,
            Guid challengeId,
            bool published,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            return Task.FromResult<ChallengeMutationFailure?>(null);
        }

        public Task<ChallengeMutationFailure?> SoftDeleteAsync(
            Guid competitionId,
            Guid challengeId,
            Guid actorId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            return Task.FromResult<ChallengeMutationFailure?>(null);
        }
    }

    private sealed class Catalog : IChallengeConfigurationCatalog
    {
        public string GetDefaultJson(GameMode mode) => """{"schemaVersion":1}""";

        public IReadOnlyList<string> Validate(GameMode mode, string json) => [];
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
