using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Submissions.Intake;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdpFixAdmissionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Break_requirement_is_enforced_before_patch_consumption(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);

            await using (var intakeDb = new NoCtfDbContext(options))
            {
                var useCase = new SubmitFix(
                    new SubmissionIntakeStore(
                        intakeDb,
                        new OpenApiTransactionalMessageOutbox()),
                    new GameModeSubmissionAdmissionPolicy());
                var result = await useCase.ExecuteAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.MemberId,
                    fixture.PatchUploadId,
                    fixture.Now), cancellationToken);

                await Assert.That(result.FailureCode).IsEqualTo(SubmissionFailureCode.BreakRequired);
            }

            await using (var verifyRejectedDb = new NoCtfDbContext(options))
            {
                var patch = await verifyRejectedDb.PatchUploads.AsNoTracking()
                    .SingleAsync(upload => upload.Id == fixture.PatchUploadId, cancellationToken);
                await Assert.That(patch.ConsumedAt).IsNull();
                await Assert.That(patch.SubmissionId).IsNull();
                await Assert.That(await verifyRejectedDb.Submissions.CountAsync(cancellationToken))
                    .IsEqualTo(0);
            }

            await AddCorrectBreakAsync(options, fixture, cancellationToken);

            await using (var intakeDb = new NoCtfDbContext(options))
            {
                var useCase = new SubmitFix(
                    new SubmissionIntakeStore(
                        intakeDb,
                        new OpenApiTransactionalMessageOutbox()),
                    new GameModeSubmissionAdmissionPolicy());
                var result = await useCase.ExecuteAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.MemberId,
                    fixture.PatchUploadId,
                    fixture.Now), cancellationToken);

                await Assert.That(result.Succeeded).IsTrue();
            }

            await using var verifyAcceptedDb = new NoCtfDbContext(options);
            var consumedPatch = await verifyAcceptedDb.PatchUploads.AsNoTracking()
                .SingleAsync(upload => upload.Id == fixture.PatchUploadId, cancellationToken);
            await Assert.That(consumedPatch.ConsumedAt).IsNotNull();
            await Assert.That(consumedPatch.SubmissionId).IsNotNull();
            await Assert.That(await verifyAcceptedDb.Submissions.CountAsync(
                    submission => submission.Kind == SubmissionKind.Fix,
                    cancellationToken))
                .IsEqualTo(1);
        });
    }

    private static async Task AddCorrectBreakAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var submissionId = Guid.CreateVersion7();
        db.Submissions.Add(new Submission
        {
            Id = submissionId,
            CompetitionId = fixture.CompetitionId,
            TeamId = fixture.TeamId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            SubmittedByUserId = fixture.MemberId,
            Kind = SubmissionKind.Break,
            SubmittedFlag = "break-proof",
            SubmittedFlagSha256 = new byte[32],
            ReceivedAt = fixture.Now,
            EvaluationState = SubmissionEvaluationState.Completed,
            EvaluationUpdatedAt = fixture.Now,
            ProcessingVersion = 1
        });
        db.ScoringEvents.Add(new ScoringEvent
        {
            Id = Guid.CreateVersion7(),
            CompetitionId = fixture.CompetitionId,
            TeamId = fixture.TeamId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            SubmissionId = submissionId,
            Kind = ScoringEventKind.SubmissionEvaluation,
            Result = ScoringResult.Correct,
            ProcessingVersion = 1,
            OccurredAt = fixture.Now,
            CreatedAt = fixture.Now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var patchUploadId = Guid.CreateVersion7();
        db.Users.AddRange(
            NewUser(ownerId, "owner", now),
            NewUser(memberId, "member", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWDP Fix admission",
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Status = CompetitionStatus.Running,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-1),
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "team",
            NormalizedName = "TEAM",
            CaptainId = memberId,
            MemberIds = [memberId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "service",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson =
                """{"schemaVersion":1,"break":{"settlement":0,"points":10},"fix":{"settlement":0,"points":20},"requireBreakBeforeFix":true,"maxBreakSubmissions":10,"maxFixSubmissions":10}""",
            UpdatedAt = now
        });
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            UploadedByUserId = memberId,
            ObjectKey = $"patches/{patchUploadId:N}",
            OriginalFileName = "fix.tar.gz",
            ContentType = "application/gzip",
            ByteLength = 1,
            Sha256 = new byte[32],
            UploadedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            competitionId,
            competitionChallengeId,
            teamId,
            memberId,
            patchUploadId);
    }

    private static User NewUser(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        Guid MemberId,
        Guid PatchUploadId);
}
