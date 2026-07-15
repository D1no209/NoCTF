using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWD;

namespace NoCTF.Tests;

public class AwdRoundEngineRecoveryTests
{
    [Fact]
    public async Task TickCompetitionAsync_RunningRoundWithoutCheckpoint_ResumesPreparationOnce()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var now = DateTime.UtcNow;
        var competition = new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Recoverable AWD",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awd,
            StartTime = now.AddMinutes(-1),
            EndTime = now.AddHours(1),
            Status = CompetitionStatus.Running,
            RoundDurationSeconds = 300,
            TotalRounds = 3
        };
        db.Competitions.Add(competition);
        db.AwdRounds.Add(new AwdRound
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            RoundNumber = 1,
            StartTime = now.AddSeconds(-30),
            Status = AwdRoundStatus.Running
        });
        await db.SaveChangesAsync();

        var containerManager = new NullContainerManager();
        var notifier = new RoundNotifierSpy();
        var flagService = new AwdFlagService(
            db,
            containerManager,
            new AwdChallengeRuntimeConfigProvider(),
            NullLogger<AwdFlagService>.Instance);
        var checkerService = new AwdCheckerService(
            db,
            containerManager,
            NullLogger<AwdCheckerService>.Instance);
        var scoreEngine = new AwdScoreEngine(
            db,
            new EmptyLeaderboard(),
            new NullRedisLeaderboardCache(),
            notifier,
            new EmptySignalEmitter(),
            NullLogger<AwdScoreEngine>.Instance);
        await using var services = new ServiceCollection()
            .AddSingleton(flagService)
            .AddSingleton(checkerService)
            .AddSingleton(scoreEngine)
            .BuildServiceProvider();
        var engine = new AwdRoundEngine(services, NullLogger<AwdRoundEngine>.Instance);

        await engine.TickCompetitionAsync(services, db, notifier, competition, now, CancellationToken.None);
        await engine.TickCompetitionAsync(services, db, notifier, competition, now, CancellationToken.None);

        var checkpoint = await db.CompetitionEngineStates
            .IgnoreQueryFilters()
            .SingleAsync(state => state.CompetitionId == competitionId);
        Assert.Equal("awd-round:1:prepared", checkpoint.EngineKey);
        Assert.Equal(1, notifier.RoundStartedCount);
        Assert.Single(await db.AwdRounds.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task TickCompetitionAsync_EndedDuringOutage_FinalizesRunningRound()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var now = DateTime.UtcNow;
        var competition = new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Ended AWD",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awd,
            StartTime = now.AddHours(-2),
            EndTime = now.AddMinutes(-1),
            Status = CompetitionStatus.Running
        };
        var round = new AwdRound
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            RoundNumber = 1,
            StartTime = now.AddHours(-1),
            Status = AwdRoundStatus.Running
        };
        db.AddRange(competition, round);
        await db.SaveChangesAsync();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var notifier = new RoundNotifierSpy();

        await new AwdRoundEngine(services, NullLogger<AwdRoundEngine>.Instance)
            .TickCompetitionAsync(services, db, notifier, competition, now, CancellationToken.None);

        Assert.Equal(CompetitionStatus.Finished, competition.Status);
        Assert.Equal(AwdRoundStatus.Finished, round.Status);
        Assert.Equal(competition.EndTime, round.EndTime);
        Assert.Equal(0, notifier.RoundStartedCount);
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new AwdRoundTenantContext(competitionId));
    }

    private sealed class EmptyLeaderboard : ILeaderboardService
    {
        public Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(
            Guid competitionId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LeaderboardEntry>>([]);
    }

    private sealed class EmptySignalEmitter : IScoreSignalEmitter
    {
        public Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default)
            => throw new InvalidOperationException("No score signals are expected for an empty round.");
    }

    private sealed class RoundNotifierSpy : IHubNotifierService
    {
        public int RoundStartedCount { get; private set; }

        public Task NotifyRoundStartedAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
        {
            RoundStartedCount++;
            return Task.CompletedTask;
        }

        public Task NotifyScoreUpdateAsync(Guid competitionId, Guid teamId, string teamName, long newScore, int newRank, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyLeaderboardSnapshotAsync(Guid competitionId, IEnumerable<LeaderboardEntryPayload> entries, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyFlagSolvedAsync(Guid competitionId, Guid challengeId, string challengeName, Guid teamId, string teamName, bool isFirstBlood, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyChallengeUpdateAsync(Guid competitionId, Guid challengeId, string challengeName, string action, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyCompetitionStateChangeAsync(Guid competitionId, string state, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifySubmissionEventAsync(Guid competitionId, Guid submissionId, Guid teamId, string teamName, Guid challengeId, string challengeName, bool isCorrect, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyContainerEventAsync(Guid competitionId, Guid containerId, Guid challengeId, Guid teamId, string eventType, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifySystemAlertAsync(Guid competitionId, string level, string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyAttackLogAsync(Guid competitionId, Guid attackerTeamId, string attackerTeamName, Guid victimTeamId, string victimTeamName, Guid challengeId, string challengeName, int roundNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyKohUpdateAsync(Guid competitionId, Guid challengeId, Guid? controllerTeamId, DateTime timestamp, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullContainerManager : IContainerManager
    {
        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => throw new InvalidOperationException("No container should be created for an empty round.");
        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => throw new InvalidOperationException("No checker should run for an empty round.");
        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default) => Task.CompletedTask;
    }
}

file sealed class AwdRoundTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
