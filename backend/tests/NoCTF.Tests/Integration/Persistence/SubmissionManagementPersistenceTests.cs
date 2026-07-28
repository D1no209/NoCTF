using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Messaging;
using NoCTF.Application.Submissions.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Submissions.Management;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class SubmissionManagementPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Team_member_lists_shared_submission_and_admin_filters_current_result(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_submission_management")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var store = new SubmissionManagementStore(
                db,
                Substitute.For<ITransactionalMessageOutbox>());
            var playerItems = await store.ListPlayerAsync(
                fixture.CompetitionId,
                fixture.TeammateId,
                null,
                null,
                50,
                cancellationToken);
            var adminItems = await store.ListAdminAsync(
                new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.TeamId,
                    null,
                    SubmissionKind.Flag,
                    SubmissionEvaluationState.Completed,
                    ScoringResult.Correct,
                    null,
                    null,
                    null,
                    null,
                    true),
                null,
                null,
                50,
                cancellationToken);

            await Assert.That(playerItems).IsNotNull();
            await Assert.That(playerItems!).HasSingleItem();
            await Assert.That(playerItems[0].Id).IsEqualTo(fixture.SubmissionId);
            await Assert.That(playerItems[0].SubmittedByUserId).IsEqualTo(fixture.CaptainId);
            await Assert.That(playerItems[0].Result).IsEqualTo(ScoringResult.Correct);
            await Assert.That(adminItems).HasSingleItem();
            await Assert.That(adminItems[0].Id).IsEqualTo(fixture.SubmissionId);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var captainId = Guid.CreateVersion7();
        var teammateId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var submissionId = Guid.CreateVersion7();
        var scoringEventId = Guid.CreateVersion7();
        db.Users.AddRange(
            User(captainId, "captain", now),
            User(teammateId, "teammate", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = captainId,
            Title = "Submission management",
            Mode = GameMode.Ctf,
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            MaxTeamMembers = 5,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Shared team",
            NormalizedName = "SHARED TEAM",
            CaptainId = captainId,
            MemberIds = [captainId, teammateId],
            InvitationToken = "0123456789abcdef0123456789abcdef",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = captainId,
            Title = "Challenge",
            Direction = "Web",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 500,
            IsPublished = true,
            UpdatedAt = now
        });
        var submission = new Submission
        {
            Id = submissionId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            SubmittedByUserId = captainId,
            Kind = SubmissionKind.Flag,
            ReceivedAt = now,
            SubmittedFlag = "flag{shared}",
            SubmittedFlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{shared}")),
            EvaluationState = SubmissionEvaluationState.Completed,
            EvaluationUpdatedAt = now,
            ProcessingVersion = 1
        };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);
        db.ScoringEvents.Add(new ScoringEvent
        {
            Id = scoringEventId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            SubmissionId = submissionId,
            Kind = ScoringEventKind.SubmissionEvaluation,
            Result = ScoringResult.Correct,
            ProcessingVersion = 1,
            OccurredAt = now,
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        submission.CurrentScoringEventId = scoringEventId;
        await db.SaveChangesAsync(cancellationToken);
        return new(
            competitionId,
            competitionChallengeId,
            teamId,
            captainId,
            teammateId,
            submissionId);
    }

    private static User User(Guid id, string name, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "test",
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now
        };

    private sealed record Fixture(
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        Guid CaptainId,
        Guid TeammateId,
        Guid SubmissionId);

}
