using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Events;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.Penetration;

namespace NoCTF.Tests;

public class PenetrationSubmissionRaceTests
{
    [Fact]
    public async Task ProcessSubmissionAsync_CompetitionFinishedAfterPreRead_DoesNotRecreateSubmissionRows()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options, new PenetrationRaceTenantContext(competitionId));
        var challenge = new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Penetration",
            TypeId = PenetrationConstants.TypeId,
            PointsConfig = new PointsConfig(),
            CreatedAt = DateTime.UtcNow
        };
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "CTF",
            GameModeType = GameModeType.Ctf,
            ModeKey = "ctf",
            OwnerId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddHours(1),
            Status = CompetitionStatus.Running
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Team",
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });
        db.Challenges.Add(challenge);
        db.TeamChallengeInstances.Add(new TeamChallengeInstance
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            Status = PenetrationInstanceStatus.Running,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var lease = new MutatingExecutionLease(async (leaseDb, ct) =>
        {
            var competition = await leaseDb.Competitions.IgnoreQueryFilters().SingleAsync(c => c.Id == competitionId, ct);
            competition.Status = CompetitionStatus.Finished;
            competition.EndTime = DateTime.UtcNow;
            await leaseDb.SaveChangesAsync(ct);
        });
        var handler = new PenetrationSubmissionHandler(
            db,
            new PenetrationFlagService(db),
            new NoopScoreSignalEmitter(),
            new NoopSubmissionEventHandler(),
            new ServiceCollection().BuildServiceProvider(),
            executionLease: lease);

        var result = await handler.ProcessSubmissionAsync(
            new SubmissionContext(
                competitionId,
                teamId,
                challengeId,
                Guid.NewGuid(),
                "flag{stale}",
                "127.0.0.1"),
            challenge);

        Assert.Equal(SubmissionResult.CompetitionEnded, result.Result);
        Assert.Empty(await db.Submissions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CompetitionLogs.IgnoreQueryFilters().ToListAsync());
    }

    private sealed class MutatingExecutionLease(
        Func<ApplicationDbContext, CancellationToken, Task> mutation) : ICompetitionExecutionLease
    {
        public async Task<IExecutionLease?> TryAcquireAsync(
            ApplicationDbContext db,
            string engineKey,
            Guid competitionId,
            CancellationToken ct = default)
        {
            Assert.Equal(CompetitionExecutionLeaseKeys.RuntimePreparation, engineKey);
            await mutation(db, ct);
            return new NoopExecutionLease();
        }
    }

    private sealed class NoopExecutionLease : IExecutionLease
    {
        public CancellationToken LostToken => CancellationToken.None;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopScoreSignalEmitter : IScoreSignalEmitter
    {
        public Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default)
            => throw new InvalidOperationException("A fenced submission must not emit score facts.");
    }

    private sealed class NoopSubmissionEventHandler : ISubmissionEventHandler
    {
        public Task HandleAsync(SubmissionSolvedEvent solvedEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("A fenced submission must not emit notifications.");
    }
}

file class PenetrationRaceTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
