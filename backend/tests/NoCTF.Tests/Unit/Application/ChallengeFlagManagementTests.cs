using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class ChallengeFlagManagementTests
{
    [Test]
    public async Task CreateChallengeFlag_InvalidWindow_IsRejectedBeforeStoreWrite()
    {
        var store = new Store();
        var now = DateTimeOffset.UtcNow;

        var result = await CreateUseCase(store).ExecuteAsync(
            CreateCommand() with { ValidStart = now, ValidEnd = now });

        await Assert.That(result.ErrorCode).IsEqualTo("invalid_flag_window");
        await Assert.That(store.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task CreateChallengeFlag_MissingTeamScope_IsRejected()
    {
        var store = new Store { Scope = new(CompetitionStatus.Running, TeamExists: false) };

        var result = await CreateUseCase(store).ExecuteAsync(
            CreateCommand() with { TeamId = Guid.NewGuid() });

        await Assert.That(result.ErrorCode).IsEqualTo("team_not_found");
        await Assert.That(store.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task CreateChallengeFlag_FinishedCompetition_IsReadOnly()
    {
        var store = new Store { Scope = new(CompetitionStatus.Finished, TeamExists: true) };

        var result = await CreateUseCase(store).ExecuteAsync(CreateCommand());

        await Assert.That(result.ErrorCode).IsEqualTo("flag_locked");
    }

    [Test]
    public async Task CreateChallengeFlag_OverlappingWindow_PropagatesConflict()
    {
        var store = new Store { MutationError = "flag_window_conflict" };

        var result = await CreateUseCase(store).ExecuteAsync(CreateCommand());

        await Assert.That(result.ErrorCode).IsEqualTo("flag_window_conflict");
    }

    [Test]
    public async Task UpdateChallengeFlag_StaleRowVersion_PropagatesConflict()
    {
        var store = new Store { MutationError = "flag_conflict" };
        var useCase = new UpdateChallengeFlag(store, new Cache(), new Scheduler());

        var result = await useCase.ExecuteAsync(UpdateCommand());

        await Assert.That(result.ErrorCode).IsEqualTo("flag_conflict");
    }

    [Test]
    [Arguments(FlagMutation.Create)]
    [Arguments(FlagMutation.Update)]
    [Arguments(FlagMutation.Delete)]
    public async Task FlagMutation_Success_InvalidatesAndQueuesRebuild(FlagMutation mutation)
    {
        var competitionId = Guid.NewGuid();
        var store = new Store();
        var cache = new Cache();
        var scheduler = new Scheduler();

        _ = mutation switch
        {
            FlagMutation.Create => (object)await new CreateChallengeFlag(store, cache, scheduler)
                .ExecuteAsync(CreateCommand(competitionId)),
            FlagMutation.Update => await new UpdateChallengeFlag(store, cache, scheduler)
                .ExecuteAsync(UpdateCommand(competitionId)),
            FlagMutation.Delete => await new DeleteChallengeFlag(store, cache, scheduler)
                .ExecuteAsync(competitionId, Guid.NewGuid(), Guid.NewGuid(), 0),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };

        await Assert.That(cache.InvalidatedCompetitionId).IsEqualTo(competitionId);
        await Assert.That(scheduler.RebuildCompetitionId).IsEqualTo(competitionId);
    }

    [Test]
    public async Task FlagBearingContracts_ToString_RedactsRawValue()
    {
        const string secret = "flag{must-not-appear}";
        var command = CreateCommand() with { Flag = secret };
        var view = View(command.CompetitionId, command.ChallengeId, command.TeamId, secret);

        await Assert.That(command.ToString()).DoesNotContain(secret);
        await Assert.That(view.ToString()).DoesNotContain(secret);
    }

    public enum FlagMutation
    {
        Create,
        Update,
        Delete
    }

    private static CreateChallengeFlag CreateUseCase(Store store) =>
        new(store, new Cache(), new Scheduler());

    private static CreateChallengeFlagCommand CreateCommand(Guid? competitionId = null) => new(
        competitionId ?? Guid.NewGuid(),
        Guid.NewGuid(),
        null,
        "flag{secret}",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow);

    private static UpdateChallengeFlagCommand UpdateCommand(Guid? competitionId = null) => new(
        competitionId ?? Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        null,
        "flag{secret}",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddHours(1),
        0,
        DateTimeOffset.UtcNow);

    private static ChallengeFlagView View(Guid competitionId, Guid challengeId, Guid? teamId, string flag) => new(
        Guid.NewGuid(),
        competitionId,
        challengeId,
        teamId,
        flag,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        0);

    private sealed class Store : IChallengeFlagStore
    {
        public ChallengeFlagScope? Scope { get; init; } = new(CompetitionStatus.Running, TeamExists: true);
        public string? MutationError { get; init; }
        public int CreateCalls { get; private set; }

        public Task<ChallengeFlagScope?> LoadScopeAsync(
            Guid competitionId,
            Guid challengeId,
            Guid? teamId,
            CancellationToken cancellationToken) => Task.FromResult(Scope);

        public Task<IReadOnlyList<ChallengeFlagView>> ListAsync(
            Guid competitionId,
            Guid challengeId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChallengeFlagView>>([]);

        public Task<ChallengeFlagView?> FindAsync(
            Guid competitionId,
            Guid challengeId,
            Guid flagId,
            CancellationToken cancellationToken) => Task.FromResult<ChallengeFlagView?>(null);

        public Task<ChallengeFlagMutationResult> CreateAsync(
            CreateChallengeFlagCommand command,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(MutationError is null
                ? new ChallengeFlagMutationResult(
                    View(command.CompetitionId, command.ChallengeId, command.TeamId, command.Flag),
                    null)
                : new ChallengeFlagMutationResult(null, MutationError));
        }

        public Task<ChallengeFlagMutationResult> UpdateAsync(
            UpdateChallengeFlagCommand command,
            CancellationToken cancellationToken) => Task.FromResult(MutationError is null
                ? new ChallengeFlagMutationResult(
                    View(command.CompetitionId, command.ChallengeId, command.TeamId, command.Flag),
                    null)
                : new ChallengeFlagMutationResult(null, MutationError));

        public Task<string?> DeleteAsync(
            Guid competitionId,
            Guid challengeId,
            Guid flagId,
            long expectedRowVersion,
            CancellationToken cancellationToken) => Task.FromResult(MutationError);
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
