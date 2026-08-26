using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("AwdpFixFence")]
public sealed class AwdpFixExecutionFencePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Authoritative_target_state_controls_execute_recover_and_superseded(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);
            var request = Request(fixture);

            var execute = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(execute.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Execute);
            await Assert.That(execute.RuntimeInstanceId).IsEqualTo(fixture.RuntimeInstanceId);
            await Assert.That(execute.RunnerId).IsEqualTo(fixture.RunnerId);
            await Assert.That(execute.ProviderReceiptJson).IsEqualTo("{}");

            await SetRuntimeStateAsync(
                options,
                fixture.RuntimeInstanceId,
                RuntimeState.Stopping,
                cancellationToken);
            var recover = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(recover.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Recover);

            await SetRuntimeStateAsync(
                options,
                fixture.RuntimeInstanceId,
                RuntimeState.Stopped,
                cancellationToken);
            var stopped = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(stopped.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Superseded);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Mismatched_or_expired_operation_is_superseded(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);
            var request = Request(fixture);

            var wrongFact = await AcquireAsync(
                options,
                request with { GameplayFactId = Guid.NewGuid() },
                cancellationToken);
            var wrongPatch = await AcquireAsync(
                options,
                request with { PatchUploadId = Guid.NewGuid() },
                cancellationToken);
            var wrongRunner = await AcquireAsync(
                options,
                request with { RunnerId = "runner-2" },
                cancellationToken);
            var expired = await AcquireAsync(
                options,
                request with { Deadline = fixture.Now.AddMinutes(-1) },
                cancellationToken);

            await Assert.That(new[]
                {
                    wrongFact.Disposition,
                    wrongPatch.Disposition,
                    wrongRunner.Disposition,
                    expired.Disposition
                })
                .IsEquivalentTo(Enumerable.Repeat(
                    AwdpFixExecutionFenceDisposition.Superseded,
                    4));

            await using var mutation = new NoCtfDbContext(options);
            var fact = await mutation.GameplayFacts.SingleAsync(
                item => item.Id == fixture.GameplayFactId,
                cancellationToken);
            fact.State = GameplayFactState.Completed;
            fact.Result = GameplayFactResult.Correct;
            await mutation.SaveChangesAsync(cancellationToken);

            var completed = await AcquireAsync(options, request, cancellationToken);
            await Assert.That(completed.Disposition)
                .IsEqualTo(AwdpFixExecutionFenceDisposition.Superseded);
        });
    }

    private static async Task<AwdpFixExecutionFenceResult> AcquireAsync(
        DbContextOptions<NoCtfDbContext> options,
        AwdpFixExecutionFenceRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new PostgresAwdpFixExecutionFence(db, TimeProvider.System)
            .AcquireAsync(request, cancellationToken);
    }

    private static AwdpFixExecutionFenceRequest Request(Fixture fixture) =>
        new(
            fixture.GameplayFactId,
            fixture.CompetitionChallengeId,
            fixture.PatchUploadId,
            fixture.RuntimeInstanceId,
            fixture.Now.AddMinutes(15),
            fixture.RunnerId);

    private static async Task SetRuntimeStateAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid runtimeInstanceId,
        RuntimeState state,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var runtime = await db.RuntimeInstances.SingleAsync(
            item => item.Id == runtimeInstanceId,
            cancellationToken);
        runtime.State = state;
        if (state == RuntimeState.Stopped)
            runtime.StoppedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competitionChallengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var factId = Guid.NewGuid();
        var patchUploadId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid();
        const string runnerId = "runner-1";

        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWDP fence",
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Title = "Fence target",
            Visibility = ChallengeVisibility.Private,
            DefinitionJson = new GameModeChallengeConfigurationCatalog()
                .GetDefaultJson(GameMode.Awdp),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = new GameModeChallengeConfigurationCatalog()
                .GetDefaultJson(GameMode.Awdp),
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "team",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        var file = new StoredFile
        {
            Id = fileId,
            ObjectKey = $"fix/{fileId:N}",
            FileName = "fix.tar.gz",
            ContentType = "application/gzip",
            ByteLength = 1,
            Sha256 = RandomNumberGenerator.GetBytes(32),
            CreatedAt = now
        };
        db.Files.Add(file);
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            UploadedByUserId = ownerId,
            RuntimeInstanceId = runtimeId,
            FileId = fileId,
            File = file,
            UploadedAt = now
        });
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = factId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = ownerId,
            Kind = GameplayFactKind.FixAttempt,
            ReferenceKind = GameplayFactReferenceKind.PatchUpload,
            ReferenceId = patchUploadId,
            State = GameplayFactState.Processing,
            OccurredAt = now,
            UpdatedAt = now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Purpose = RuntimePurpose.AwdpTarget,
            GameplayFactId = factId,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerId = runnerId,
            State = RuntimeState.Running,
            ProviderReceiptJson = "{}",
            CreatedAt = now,
            RunningAt = now,
            ExpiresAt = now.AddMinutes(15)
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            competitionChallengeId,
            factId,
            patchUploadId,
            runtimeId,
            runnerId);
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase($"noctf_awdp_fence_{Guid.NewGuid():N}")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionChallengeId,
        Guid GameplayFactId,
        Guid PatchUploadId,
        Guid RuntimeInstanceId,
        string RunnerId);
}
