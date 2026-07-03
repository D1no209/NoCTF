using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Security;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWDP;

namespace NoCTF.Tests;

public class AwdpPatchServiceTests
{
    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextAwdp(competitionId));
    }

    private static AwdpPatchService CreateService(
        ApplicationDbContext db,
        IContainerManager containerManager,
        IStorageProvider? storageProvider = null)
        => new(
            db,
            containerManager,
            storageProvider ?? new NullStorageProvider(),
            new PatchArchiveValidator(),
            new AwdpConfigResolver(db),
            new AwdpStateService(db),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["StorageProvider:PublicBaseUrl"] = "http://api.local"
                })
                .Build(),
            NullLogger<AwdpPatchService>.Instance);

    [Fact]
    public async Task SubmitPatchAsync_CreatesPendingSubmissionAndCountsDefenseAttempt()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallenge(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var storage = new RecordingStorageProvider();
        var service = CreateService(db, new NullContainerManager(), storage);

        await using var patchContent = CreateFixArchive();
        var result = await service.SubmitPatchAsync(
            competitionId, teamId, challengeId, patchContent, "fix.tar.gz");

        Assert.True(result.Success);
        Assert.Equal("pending", result.Code);
        Assert.NotNull(result.SubmissionId);
        Assert.Equal(1, result.DefenseAttempts);
        Assert.Equal(3, result.MaxDefenseAttempts);

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == result.SubmissionId);
        var state = await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .FirstAsync(s => s.TeamId == teamId && s.ChallengeId == challengeId);

        Assert.Equal(AwdpPatchStatus.Pending, submission.Status);
        Assert.Equal(AwdpFixStatus.FixUploading, submission.FixStatus);
        Assert.Equal("fix.sh", submission.FixEntry);
        Assert.Equal(1, submission.AttemptNumber);
        Assert.Equal(1, state.DefenseAttempts);
        Assert.Equal(AwdpFixStatus.FixUploading, state.FixStatus);
        Assert.Equal(1, storage.UploadCount);
    }

    [Fact]
    public async Task SubmitPatchAsync_RejectsWhenDefenseAttemptsAreExhausted()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, maxDefenseAttempts: 1);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallenge(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        db.AwdpTeamChallengeStates.Add(new AwdpTeamChallengeState
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            DefenseAttempts = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, new NullContainerManager());
        await using var patchContent = CreateFixArchive();
        var result = await service.SubmitPatchAsync(
            competitionId, teamId, challengeId, patchContent, "fix.tar.gz");

        Assert.False(result.Success);
        Assert.Equal("defense_attempts_exhausted", result.Code);
        Assert.Equal(1, result.DefenseAttempts);
        Assert.Equal(1, result.MaxDefenseAttempts);
    }

    [Fact]
    public async Task SubmitPatchAsync_AuditFailureCountsAttemptButDoesNotQueueValidation()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallenge(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var service = CreateService(db, new NullContainerManager());
        await using var patchContent = CreateFixArchive("wrong.sh");
        var result = await service.SubmitPatchAsync(
            competitionId, teamId, challengeId, patchContent, "fix.tar.gz");

        Assert.False(result.Success);
        Assert.NotNull(result.SubmissionId);
        Assert.Contains("missing fix entry", result.Code, StringComparison.OrdinalIgnoreCase);

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == result.SubmissionId);
        Assert.Equal(AwdpPatchStatus.Rejected, submission.Status);
        Assert.Equal(AwdpFixStatus.AuditFailed, submission.FixStatus);
    }

    [Fact]
    public async Task ValidatePatchAsync_SandboxFails_RejectsAsScriptError()
    {
        var (db, service, competitionId, teamId, challengeId, containerManager) =
            await CreateValidationScenarioAsync([1]);
        await using (db)
        {
            var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

            await service.ValidatePatchAsync(submissionId);

            var submission = await db.AwdpPatchSubmissions.IgnoreQueryFilters().FirstAsync(s => s.Id == submissionId);
            var state = await db.AwdpTeamChallengeStates.IgnoreQueryFilters().FirstAsync(s => s.TeamId == teamId);

            Assert.Equal(AwdpPatchStatus.Rejected, submission.Status);
            Assert.Equal(AwdpFixStatus.FixScriptError, submission.FixStatus);
            Assert.Equal(AwdpFixStatus.FixScriptError, state.FixStatus);
            Assert.Contains("FixScript exited", submission.ValidationDetail);
            Assert.Equal(0, containerManager.CreateCount);
        }
    }

    [Fact]
    public async Task ValidatePatchAsync_CheckerPassExpFail_VerifiesWithoutImmediateScore()
    {
        var (db, service, competitionId, teamId, challengeId, _) =
            await CreateValidationScenarioAsync([0, 0, 1]);
        await using (db)
        {
            var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

            await service.ValidatePatchAsync(submissionId);

            var submission = await db.AwdpPatchSubmissions.IgnoreQueryFilters().FirstAsync(s => s.Id == submissionId);
            var state = await db.AwdpTeamChallengeStates.IgnoreQueryFilters().FirstAsync(s => s.TeamId == teamId);
            var scoreEvents = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();

            Assert.Equal(AwdpPatchStatus.Verified, submission.Status);
            Assert.Equal(AwdpFixStatus.FixSuccess, submission.FixStatus);
            Assert.Equal(AwdpServiceStatus.ServiceOk, state.ServiceStatus);
            Assert.Empty(scoreEvents);
        }
    }

    [Fact]
    public async Task ValidatePatchAsync_CheckerPassExpPass_RecordsFixFailedWithoutPenaltyEvent()
    {
        var (db, service, competitionId, teamId, challengeId, containerManager) =
            await CreateValidationScenarioAsync([0, 0, 0]);
        await using (db)
        {
            var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

            await service.ValidatePatchAsync(submissionId);

            var submission = await db.AwdpPatchSubmissions.IgnoreQueryFilters().FirstAsync(s => s.Id == submissionId);
            var state = await db.AwdpTeamChallengeStates.IgnoreQueryFilters().FirstAsync(s => s.TeamId == teamId);

            Assert.Equal(AwdpPatchStatus.Rejected, submission.Status);
            Assert.Equal(AwdpFixStatus.FixFailed, submission.FixStatus);
            Assert.Equal(AwdpServiceStatus.ServiceOk, state.ServiceStatus);
            Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());
            Assert.Equal(2, containerManager.CreateCount);
        }
    }

    [Fact]
    public async Task ValidatePatchAsync_CheckerFails_RecordsServiceErrorSeparately()
    {
        var (db, service, competitionId, teamId, challengeId, _) =
            await CreateValidationScenarioAsync([0, 1]);
        await using (db)
        {
            var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

            await service.ValidatePatchAsync(submissionId);

            var submission = await db.AwdpPatchSubmissions.IgnoreQueryFilters().FirstAsync(s => s.Id == submissionId);
            var state = await db.AwdpTeamChallengeStates.IgnoreQueryFilters().FirstAsync(s => s.TeamId == teamId);

            Assert.Equal(AwdpPatchStatus.Rejected, submission.Status);
            Assert.Equal(AwdpFixStatus.FixServiceError, submission.FixStatus);
            Assert.Equal(AwdpServiceStatus.ServiceError, state.ServiceStatus);
        }
    }

    [Fact]
    public async Task ValidatePatchAsync_NoCheckerOrExp_TreatsExploitAsFailed()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var challengeId = SeedChallenge(db, competitionId, checkerImage: null, expImage: null);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var service = CreateService(db, new SequencedContainerManager([0]));
        var submissionId = await CreatePendingSubmission(db, competitionId, teamId, challengeId);

        await service.ValidatePatchAsync(submissionId);

        var submission = await db.AwdpPatchSubmissions.IgnoreQueryFilters().FirstAsync(s => s.Id == submissionId);
        Assert.Equal(AwdpPatchStatus.Verified, submission.Status);
        Assert.Equal(AwdpFixStatus.FixSuccess, submission.FixStatus);
    }

    private static async Task<(ApplicationDbContext Db, AwdpPatchService Service, Guid CompetitionId, Guid TeamId, Guid ChallengeId, SequencedContainerManager ContainerManager)>
        CreateValidationScenarioAsync(int[] exitCodes)
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var challengeId = SeedChallenge(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();
        var containerManager = new SequencedContainerManager(exitCodes);
        var service = CreateService(db, containerManager);
        return (db, service, competitionId, teamId, challengeId, containerManager);
    }

    private static void SeedCompetition(
        ApplicationDbContext db,
        Guid competitionId,
        int defensePoints = 150,
        int maxDefenseAttempts = 3)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "AWDP Test",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awdp,
            ModeKey = "awdp",
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = CompetitionStatus.Running,
            DefensePoints = defensePoints,
            AwdpMaxDefenseAttempts = maxDefenseAttempts,
            AwdpFixEntry = "fix.sh"
        });
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
            TypeId = "awdp",
            PointsConfig = new PointsConfig(),
            ContainerImage = "vuln-service:latest",
            ExposedPort = 80,
            CheckerConfig = checkerImage is null && expImage is null
                ? null
                : new CheckerConfig
                {
                    Image = checkerImage,
                    Command = "exit 0",
                    TimeoutSeconds = 5,
                    ExpImage = expImage,
                    ExpCommand = "exit 0"
                },
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static void SeedGameBox(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        string containerId = "container-123")
    {
        db.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ContainerInstanceId = containerId,
            PortMappingsJson = "{}",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        });
    }

    private static async Task<Guid> CreatePendingSubmission(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId)
    {
        var submission = new AwdpPatchSubmission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            PatchArchiveUrl = "http://storage/fix.tar.gz",
            Status = AwdpPatchStatus.Pending,
            FixStatus = AwdpFixStatus.FixUploading,
            AttemptNumber = 1,
            FileName = "fix.tar.gz",
            FixEntry = "fix.sh",
            SubmittedAt = DateTime.UtcNow
        };
        db.AwdpPatchSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return submission.Id;
    }

    private static MemoryStream CreateFixArchive(string entryName = "fix.sh")
    {
        var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            using var writer = new TarWriter(gzip, leaveOpen: true);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"))
            };
            writer.WriteEntry(entry);
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class NullContainerManager : IContainerManager
    {
        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => Task.FromResult(new ContainerInstance(
                Guid.NewGuid(), Guid.Empty, null, null, "null", "null-id", [], "running", DateTime.UtcNow));

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => Task.FromResult(new ContainerRunResult("null-id", 0, null, null, DateTime.UtcNow, DateTime.UtcNow));

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => Task.FromResult(new ComposeDeployment(Guid.NewGuid(), Guid.Empty, null, null, "null", config.ProjectName, config.ComposeYaml, "running", DateTime.UtcNow));

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class SequencedContainerManager(int[] exitCodes) : IContainerManager
    {
        private int _runIndex;
        public int CreateCount { get; private set; }
        public int DestroyCount { get; private set; }

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
        {
            CreateCount++;
            return Task.FromResult(new ContainerInstance(
                Guid.NewGuid(), Guid.Empty, null, null, "null", $"container-{CreateCount}", [], "running", DateTime.UtcNow));
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
            return Task.FromResult(new ContainerRunResult("run-id", exitCode, null, null, DateTime.UtcNow, DateTime.UtcNow));
        }

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => Task.FromResult(new ComposeDeployment(Guid.NewGuid(), Guid.Empty, null, null, "null", config.ProjectName, config.ComposeYaml, "running", DateTime.UtcNow));

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NullStorageProvider : IStorageProvider
    {
        public Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken ct = default)
            => Task.FromResult(fileName);

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
            return Task.FromResult(fileName);
        }

        public Task<Stream> DownloadAsync(string fileName, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string fileName, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string> GetUrlAsync(string fileName, CancellationToken ct = default)
            => Task.FromResult($"/api/files/{fileName}");
    }
}

file class FixedTenantContextAwdp(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
