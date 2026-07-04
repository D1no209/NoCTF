using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Admin;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class CompetitionChallengeDeletionTests
{
    [Fact]
    public async Task DeleteChallengeArtifactsAsync_CleansScoresFlagsAndRunningInstances()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        var challenge = new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "baby",
            TypeId = "PWN",
            CreatedAt = DateTime.UtcNow
        };
        db.Challenges.Add(challenge);
        db.ChallengeHints.Add(new ChallengeHint
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Content = "hint",
            CreatedAt = DateTime.UtcNow
        });
        db.Submissions.Add(new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            UserId = Guid.NewGuid(),
            FlagContent = "sha256:test",
            IsCorrect = true,
            SubmittedAt = DateTime.UtcNow,
            IpAddress = "127.0.0.1"
        });
        db.ScoreEvents.Add(new ScoreEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ScoringKey = "decay-solve",
            EventType = "ctf.solve",
            PointsDelta = 500,
            Timestamp = DateTime.UtcNow
        });
        db.ScoreSignals.Add(new ScoreSignal
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            SubjectType = "challenge",
            SubjectId = challengeId,
            SignalType = "solve.accepted",
            OccurredAt = DateTime.UtcNow,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        });
        db.CtfDynamicFlags.Add(new CtfDynamicFlag
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            FlagUuid = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTime.UtcNow
        });
        db.DynamicFlagInstances.Add(new DynamicFlagInstance
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            FlagId = Guid.NewGuid(),
            InstanceId = Guid.NewGuid(),
            ValueSecret = "secret",
            ValueHash = "hash",
            GeneratedAt = DateTime.UtcNow
        });
        db.CheatIncidents.Add(new CheatIncident
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            SuspectTeamId = teamId,
            ChallengeId = challengeId,
            UserId = Guid.NewGuid(),
            SubmittedFlag = "sha256:test",
            Reason = "test",
            CreatedAt = DateTime.UtcNow
        });
        db.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ContainerInstanceId = "container-1",
            ProviderType = "docker",
            CreatedAt = DateTime.UtcNow
        });
        db.TeamChallengeInstances.Add(new TeamChallengeInstance
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            Status = PenetrationInstanceStatus.Running,
            ComposeProjectName = "noctf-test",
            RenderedComposeYaml = "services:\n  app:\n    image: nginx\n",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var manager = new RecordingContainerManager();

        await DeleteCompetitionChallengeEndpoint.DeleteChallengeArtifactsAsync(
            db,
            manager,
            challenge,
            new DefaultHttpContext(),
            Guid.NewGuid(),
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(["container-1"], manager.DestroyedContainers);
        Assert.Equal(["noctf-test"], manager.DestroyedComposeProjects);
        Assert.Empty(await db.ChallengeHints.IgnoreQueryFilters().Where(h => h.ChallengeId == challengeId).ToListAsync());
        Assert.Empty(await db.Submissions.IgnoreQueryFilters().Where(s => s.ChallengeId == challengeId).ToListAsync());
        Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().Where(s => s.ChallengeId == challengeId).ToListAsync());
        Assert.Empty(await db.ScoreSignals.IgnoreQueryFilters().Where(s => s.SubjectId == challengeId).ToListAsync());
        Assert.Empty(await db.CtfDynamicFlags.IgnoreQueryFilters().Where(f => f.ChallengeId == challengeId).ToListAsync());
        Assert.Empty(await db.DynamicFlagInstances.IgnoreQueryFilters().Where(f => f.ChallengeId == challengeId).ToListAsync());
        Assert.Empty(await db.CheatIncidents.IgnoreQueryFilters().Where(i => i.ChallengeId == challengeId).ToListAsync());
        Assert.Empty(await db.AwdGameBoxes.IgnoreQueryFilters().Where(g => g.ChallengeId == challengeId).ToListAsync());
        Assert.Empty(await db.TeamChallengeInstances.IgnoreQueryFilters().Where(i => i.ChallengeId == challengeId).ToListAsync());
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext(competitionId));
    }

    private sealed class RecordingContainerManager : IContainerManager
    {
        public List<string> DestroyedContainers { get; } = [];
        public List<string> DestroyedComposeProjects { get; } = [];

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
        {
            DestroyedContainers.Add(container.ContainerId);
            return Task.CompletedTask;
        }

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default)
        {
            DestroyedComposeProjects.Add(deployment.ProjectName);
            return Task.CompletedTask;
        }

        public Task<ComposeStatus> GetComposeStatusAsync(
            string projectName,
            Dictionary<string, string>? labels = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

file class FixedTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
