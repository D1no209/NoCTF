using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Tests.Unit.Application;

public class TeamModerationTests
{
    [Test]
    public async Task ExecuteAsync_FinishedCompetition_DoesNotMutateOrRebuild()
    {
        var store = new Store { Status = CompetitionStatus.Finished };
        var scheduler = new Scheduler();
        var useCase = new ModerateTeam(store, new Cache(), scheduler);

        var result = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, "reason", DateTimeOffset.UtcNow));

        await Assert.That(result.ErrorCode).IsEqualTo("competition_finished");
        await Assert.That(store.ApplyCalls).IsEqualTo(0);
        await Assert.That(scheduler.RebuildCompetitionId).IsNull();
    }

    private sealed class Store : ITeamModerationStore
    {
        public CompetitionStatus? Status { get; init; }
        public int ApplyCalls { get; private set; }
        public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(Status);
        public Task<TeamModerationStoreResult> ApplyAsync(TeamModerationCommand command, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return Task.FromResult(new TeamModerationStoreResult());
        }
    }

    private sealed class Cache : ILeaderboardCache
    {
        public Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(null);
        public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Scheduler : NoCTF.Application.BackgroundWork.IBackgroundWorkScheduler
    {
        public Guid? RebuildCompetitionId { get; private set; }
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken)
        {
            RebuildCompetitionId = competitionId;
            return ValueTask.CompletedTask;
        }
    }
}
