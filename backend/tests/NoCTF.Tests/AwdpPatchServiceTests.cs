using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWDP;

namespace NoCTF.Tests;

/// <summary>
/// Tests for AwdpPatchService: patch submission, validation workflow, and rollback.
/// </summary>
public class AwdpPatchServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextAwdp(competitionId));
    }

    private static AwdpPatchService CreateService(
        ApplicationDbContext db,
        IContainerManager containerManager,
        IStorageProvider? storageProvider = null)
        => new(db, containerManager, storageProvider ?? new NullStorageProvider(),
               NullLogger<AwdpPatchService>.Instance);

    private static Guid SeedCompetition(ApplicationDbContext db, Guid competitionId, int defensePoints = 150)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "AWDP Test",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awdp,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = CompetitionStatus.Running,
            DefensePoints = defensePoints
        });
        return competitionId;
    }

    private static Guid SeedChallenge(
        ApplicationDbContext db,
        Guid competitionId,
        string? checkerImage = "checker:latest",
        string? expImage = "exp:latest")
    {
        var id = Guid.NewGuid();
        db.Challenges.Add(new Challenge
        {
            Id = id,
            CompetitionId = competitionId,
            Title = "Vuln Service",
            TypeId = "awd",
            PointsConfig = new PointsConfig(),
            ContainerImage = "vuln-service:latest",
            CheckerConfig = new CheckerConfig
            {
                Image = checkerImage,
                Command = "sh -c 'exit 0'",
                TimeoutSeconds = 5,
                ExpImage = expImage,
                ExpCommand = "sh -c 'exit 0'"
            },
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static Guid SeedGameBox(ApplicationDbContext db, Guid competitionId, Guid teamId, Guid challengeId,
        string containerId = "container-123")
    {
        var id = Guid.NewGuid();
        db.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = id,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ContainerInstanceId = containerId,
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitPatchAsync_CreatesSubmissionWithPendingStatus()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        await db.SaveChangesAsync();

        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var storage = new RecordingStorageProvider();
        var service = CreateService(db, new NullContainerManager(), storage);

        var patchContent = new MemoryStream(new byte[] { 1, 2, 3 });
        var submissionId = await service.SubmitPatchAsync(
            competitionId, teamId, challengeId, patchContent, "fix.tar.gz");

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == submissionId);

        Assert.Equal(AwdpPatchStatus.Pending, submission.Status);
        Assert.Equal(teamId, submission.TeamId);
        Assert.Equal(challengeId, submission.ChallengeId);
        Assert.Equal(competitionId, submission.CompetitionId);
        Assert.NotEmpty(submission.PatchArchiveUrl);
        Assert.Equal(1, storage.UploadCount);
    }

    [Fact]
    public async Task ValidatePatchAsync_SandboxFails_RejectsSubmission()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallenge(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        // Sandbox returns exit code 1 (patch.sh failed)
        var containerManager = new SequencedContainerManager([1]);
        var service = CreateService(db, containerManager);

        var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

        await service.ValidatePatchAsync(submissionId);

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == submissionId);

        Assert.Equal(AwdpPatchStatus.Rejected, submission.Status);
        Assert.NotNull(submission.ValidationDetail);
        Assert.Contains("patch.sh exited", submission.ValidationDetail);
        // Only sandbox ran — no container create/destroy
        Assert.Equal(0, containerManager.CreateCount);
    }

    [Fact]
    public async Task ValidatePatchAsync_CheckerPassExpFail_VerifiesAndAwardsPoints()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, defensePoints: 200);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallenge(db, competitionId, checkerImage: "checker:latest", expImage: "exp:latest");
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        // Sequence: sandbox=0 (pass), checker=0 (pass), exp=1 (fail → patch effective)
        var containerManager = new SequencedContainerManager([0, 0, 1]);
        var service = CreateService(db, containerManager);

        var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

        await service.ValidatePatchAsync(submissionId);

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == submissionId);

        Assert.Equal(AwdpPatchStatus.Verified, submission.Status);
        Assert.NotNull(submission.ValidatedAt);

        var scoreEvent = await db.ScoreEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(se => se.TeamId == teamId && se.EventType == "awdp_defense");

        Assert.NotNull(scoreEvent);
        Assert.Equal(200, scoreEvent.PointsDelta);
        Assert.Equal(challengeId, scoreEvent.ChallengeId);
    }

    [Fact]
    public async Task ValidatePatchAsync_CheckerPassExpPass_RejectsAndRollsBack()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallenge(db, competitionId, checkerImage: "checker:latest", expImage: "exp:latest");
        SeedGameBox(db, competitionId, teamId, challengeId, "original-container");
        await db.SaveChangesAsync();

        // Sequence: sandbox=0 (pass), checker=0 (pass), exp=0 (pass → patch NOT effective)
        // After rejection: destroy patched + create original
        var containerManager = new SequencedContainerManager([0, 0, 0]);
        var service = CreateService(db, containerManager);

        var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

        await service.ValidatePatchAsync(submissionId);

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == submissionId);

        Assert.Equal(AwdpPatchStatus.Rejected, submission.Status);
        Assert.Contains("EXP succeeded", submission.ValidationDetail);

        // No defense points awarded
        var scoreEvent = await db.ScoreEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(se => se.TeamId == teamId && se.EventType == "awdp_defense");
        Assert.Null(scoreEvent);

        // Container was recreated (create called twice: patched + rollback)
        Assert.Equal(2, containerManager.CreateCount);
    }

    [Fact]
    public async Task ValidatePatchAsync_CheckerFails_RejectsWithServiceDownMessage()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallenge(db, competitionId, checkerImage: "checker:latest", expImage: "exp:latest");
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        // Sequence: sandbox=0 (pass), checker=1 (fail → service down)
        var containerManager = new SequencedContainerManager([0, 1]);
        var service = CreateService(db, containerManager);

        var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

        await service.ValidatePatchAsync(submissionId);

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == submissionId);

        Assert.Equal(AwdpPatchStatus.Rejected, submission.Status);
        Assert.Contains("Checker failed", submission.ValidationDetail);
    }

    [Fact]
    public async Task ValidatePatchAsync_NoCheckerConfig_AssumesPassed()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, defensePoints: 100);
        var teamId = Guid.NewGuid();

        // Challenge with no checker or exp
        var challengeId = Guid.NewGuid();
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "No Checker",
            TypeId = "awd",
            PointsConfig = new PointsConfig(),
            ContainerImage = "vuln:latest",
            CreatedAt = DateTime.UtcNow
        });
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        // Only sandbox runs (exit 0)
        var containerManager = new SequencedContainerManager([0]);
        var service = CreateService(db, containerManager);

        var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

        await service.ValidatePatchAsync(submissionId);

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == submissionId);

        // No checker + no exp → both assumed passed/failed → Verified
        Assert.Equal(AwdpPatchStatus.Verified, submission.Status);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<Guid> CreatePendingSubmission(
        ApplicationDbContext db, Guid competitionId, Guid teamId, Guid challengeId)
    {
        var submission = new AwdpPatchSubmission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            PatchArchiveUrl = "http://storage/patch.tar.gz",
            Status = AwdpPatchStatus.Pending,
            SubmittedAt = DateTime.UtcNow
        };
        db.AwdpPatchSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return submission.Id;
    }

    // ── Mock implementations ──────────────────────────────────────────────────

    private sealed class NullContainerManager : IContainerManager
    {
        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => Task.FromResult(new ContainerInstance(
                Guid.NewGuid(), Guid.Empty, null, null, "null", "null-id",
                new Dictionary<int, int>(), "running", DateTime.UtcNow));

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => Task.FromResult(new ContainerRunResult("null-id", 0, null, null, DateTime.UtcNow, DateTime.UtcNow));

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => Task.FromResult(new ComposeDeployment(
                Guid.NewGuid(), Guid.Empty, null, null, "null", config.ProjectName,
                config.ComposeYaml, "running", DateTime.UtcNow));

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Returns exit codes from a pre-defined sequence; defaults to 0 when exhausted.</summary>
    private sealed class SequencedContainerManager(int[] exitCodes) : IContainerManager
    {
        private int _runIndex;
        public int CreateCount { get; private set; }
        public int DestroyCount { get; private set; }

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
        {
            CreateCount++;
            return Task.FromResult(new ContainerInstance(
                Guid.NewGuid(), Guid.Empty, null, null, "null", $"container-{CreateCount}",
                new Dictionary<int, int>(), "running", DateTime.UtcNow));
        }

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
        {
            DestroyCount++;
            return Task.CompletedTask;
        }

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
        {
            var idx = _runIndex++;
            var exitCode = idx < exitCodes.Length ? exitCodes[idx] : 0;
            return Task.FromResult(new ContainerRunResult(
                "run-id", exitCode, null, null, DateTime.UtcNow, DateTime.UtcNow));
        }

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => Task.FromResult(new ComposeDeployment(
                Guid.NewGuid(), Guid.Empty, null, null, "null", config.ProjectName,
                config.ComposeYaml, "running", DateTime.UtcNow));

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NullStorageProvider : IStorageProvider
    {
        public Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken ct = default)
            => Task.FromResult($"/api/files/{fileName}");

        public Task<Stream> DownloadAsync(string fileName, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string fileName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string> GetUrlAsync(string fileName, CancellationToken ct = default)
            => Task.FromResult($"/api/files/{fileName}");
    }

    private sealed class RecordingStorageProvider : IStorageProvider
    {
        public int UploadCount { get; private set; }

        public Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken ct = default)
        {
            UploadCount++;
            return Task.FromResult($"/api/files/{fileName}");
        }

        public Task<Stream> DownloadAsync(string fileName, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string fileName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string> GetUrlAsync(string fileName, CancellationToken ct = default)
            => Task.FromResult($"/api/files/{fileName}");
    }
}

/// <summary>Fixed tenant context for AwdpPatchService tests.</summary>
file class FixedTenantContextAwdp(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { /* no-op */ }
}
