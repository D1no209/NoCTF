using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Submissions.CheatIncidents;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Submissions.CheatIncidents;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CheatIncidentPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Incident_event_stream_supports_audited_detail_dismissal_ban_and_correction(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_cheat_incident_adjudication")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();
            await using var db = new NoCtfDbContext(options);
            var eventStore = new CompetitionEventStore(db, outbox);
            var store = new CheatIncidentStore(db, outbox, eventStore);
            var from = fixture.Now.AddHours(-1);
            var to = fixture.Now.AddHours(1);

            var initial = await store.ListAsync(new(
                fixture.CompetitionId,
                null,
                null,
                null,
                null,
                null,
                from,
                to,
                null,
                null,
                50), cancellationToken);
            await Assert.That(initial).IsNotNull();
            await Assert.That(initial!.Items).Count().IsEqualTo(2);
            await Assert.That(initial.PendingCount).IsEqualTo(2);
            await Assert.That(initial.Items.All(item =>
                item.Status == CheatIncidentStatus.Pending)).IsTrue();

            var detail = await store.GetDetailAsync(
                fixture.CompetitionId,
                fixture.ScoringEventIds[0],
                fixture.JudgeId,
                fixture.Now.AddSeconds(1),
                cancellationToken);
            await Assert.That(detail).IsNotNull();
            await Assert.That(detail!.SubmittedFlag).IsEqualTo(fixture.Flags[0]);
            var accessAudit = await db.CompetitionEvents.AsNoTracking()
                .SingleAsync(item =>
                    item.ScoringEventId == fixture.ScoringEventIds[0]
                    && item.Kind == CompetitionEventKind.ProtectedSubmissionFlagAccessed,
                    cancellationToken);
            await Assert.That(accessAudit.Reason).DoesNotContain(fixture.Flags[0]);

            var dismissed = await store.DismissAsync(new(
                fixture.CompetitionId,
                fixture.ScoringEventIds[0],
                fixture.JudgeId,
                $"Reviewed evidence {fixture.Flags[0]} and dismissed it.",
                fixture.Now.AddSeconds(2)), cancellationToken);
            await Assert.That(dismissed.Succeeded).IsTrue();
            var dismissal = await db.CompetitionEvents.AsNoTracking()
                .SingleAsync(item =>
                    item.ScoringEventId == fixture.ScoringEventIds[0]
                    && item.Kind == CompetitionEventKind.CheatIncidentDismissed,
                    cancellationToken);
            await Assert.That(dismissal.Reason).DoesNotContain(fixture.Flags[0]);

            var confirmed = await store.ConfirmAndBanAsync(new(
                fixture.CompetitionId,
                fixture.ScoringEventIds[1],
                fixture.OwnerId,
                "Confirmed after independent evidence review.",
                fixture.Now.AddSeconds(3)), cancellationToken);
            await Assert.That(confirmed.Succeeded).IsTrue();
            var bannedTeam = await db.Teams.SingleAsync(
                item => item.Id == fixture.SourceTeamId,
                cancellationToken);
            await Assert.That(bannedTeam.IsBanned).IsTrue();
            await Assert.That(outbox.Published.OfType<TeamBanned>()).Count().IsEqualTo(1);

            var corrected = await store.CorrectAndUnbanAsync(new(
                fixture.CompetitionId,
                fixture.ScoringEventIds[1],
                fixture.OwnerId,
                "Manual review proved the prior ruling incorrect.",
                fixture.Now.AddSeconds(4)), cancellationToken);
            await Assert.That(corrected.Succeeded).IsTrue();
            db.ChangeTracker.Clear();
            var correctedTeam = await db.Teams.AsNoTracking().SingleAsync(
                item => item.Id == fixture.SourceTeamId,
                cancellationToken);
            await Assert.That(correctedTeam.IsBanned).IsFalse();
            await Assert.That(correctedTeam.BanReason).IsNull();
            var correction = outbox.Published.OfType<TeamBanCorrected>().Single();
            var correctedBan = await db.CompetitionEvents.AsNoTracking().SingleAsync(
                item => item.Id == correction.BanEventId,
                cancellationToken);
            await Assert.That(correctedBan.ScoringEventId)
                .IsEqualTo(fixture.ScoringEventIds[1]);

            var final = await store.ListAsync(new(
                fixture.CompetitionId,
                null,
                null,
                null,
                null,
                null,
                from,
                to,
                null,
                null,
                50), cancellationToken);
            await Assert.That(final).IsNotNull();
            await Assert.That(final!.PendingCount).IsEqualTo(0);
            await Assert.That(final.Items.Select(item => item.Status)).IsEquivalentTo([
                CheatIncidentStatus.Dismissed,
                CheatIncidentStatus.Corrected
            ]);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var judgeId = Guid.CreateVersion7();
        var sourceUserId = Guid.CreateVersion7();
        var ownerUserId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var sourceTeamId = Guid.CreateVersion7();
        var ownerTeamId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var submissionIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var scoringEventIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var flags = new[] { "flag{incident-one}", "flag{incident-two}" };
        db.Users.AddRange(
            User(ownerId, "incident-owner", UserRole.Organizer, now),
            User(judgeId, "incident-judge", UserRole.Organizer, now),
            User(sourceUserId, "incident-source", UserRole.User, now),
            User(ownerUserId, "incident-target", UserRole.User, now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Cheat incident adjudication",
            OwnerId = ownerId,
            JudgeIds = [judgeId],
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-30),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.AddRange(
            Team(sourceTeamId, competitionId, "Source", sourceUserId, now),
            Team(ownerTeamId, competitionId, "Owner", ownerUserId, now));
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = "Foreign Flag challenge",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            UpdatedAt = now
        });
        for (var index = 0; index < submissionIds.Length; index++)
        {
            var occurredAt = now.AddMilliseconds(index);
            db.Submissions.Add(new Submission
            {
                Id = submissionIds[index],
                CompetitionId = competitionId,
                TeamId = sourceTeamId,
                CompetitionChallengeId = competitionChallengeId,
                SubmittedByUserId = sourceUserId,
                Kind = SubmissionKind.Flag,
                SubmittedFlag = flags[index],
                SubmittedFlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flags[index])),
                ReceivedAt = occurredAt,
                EvaluationState = SubmissionEvaluationState.Completed,
                EvaluationUpdatedAt = occurredAt,
                ProcessingVersion = 1
            });
            db.ScoringEvents.Add(new ScoringEvent
            {
                Id = scoringEventIds[index],
                CompetitionId = competitionId,
                TeamId = sourceTeamId,
                VictimTeamId = ownerTeamId,
                CompetitionChallengeId = competitionChallengeId,
                SubmissionId = submissionIds[index],
                Kind = ScoringEventKind.SubmissionEvaluation,
                Result = ScoringResult.Rejected,
                FailureCode = ScoringFailureCode.ForeignTeamFlagDetected,
                ProcessingVersion = 1,
                OccurredAt = occurredAt,
                CreatedAt = occurredAt
            });
            db.CompetitionEvents.Add(new CompetitionEvent
            {
                Id = Guid.CreateVersion7(occurredAt),
                CompetitionId = competitionId,
                Kind = CompetitionEventKind.CheatIncidentDetected,
                Level = CompetitionEventLevel.Warning,
                Visibility = CompetitionEventVisibility.Staff,
                ActorUserId = sourceUserId,
                TeamId = sourceTeamId,
                CompetitionChallengeId = competitionChallengeId,
                SubmissionId = submissionIds[index],
                ScoringEventId = scoringEventIds[index],
                OccurredAt = occurredAt
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        for (var index = 0; index < submissionIds.Length; index++)
        {
            db.Submissions.Local.Single(item => item.Id == submissionIds[index])
                .CurrentScoringEventId = scoringEventIds[index];
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            competitionId,
            ownerId,
            judgeId,
            sourceTeamId,
            scoringEventIds,
            flags);
    }

    private static User User(
        Guid id,
        string userName,
        UserRole role,
        DateTimeOffset now) => new()
    {
        Id = id,
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        Email = $"{userName}@example.test",
        NormalizedEmail = $"{userName.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        Kind = UserKind.Human,
        Role = role,
        AccountStatus = UserAccountStatus.Active,
        EmailVerifiedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Team Team(
        Guid id,
        Guid competitionId,
        string name,
        Guid captainId,
        DateTimeOffset now) => new()
    {
        Id = id,
        CompetitionId = competitionId,
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        CaptainId = captainId,
        MemberIds = [captainId],
        InvitationToken = Guid.NewGuid().ToString("N"),
        RegistrationStatus = TeamRegistrationStatus.Approved,
        RegisteredAt = now
    };

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ConcurrentQueue<object> Published { get; } = new();

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            PublishAsync(message);

        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid OwnerId,
        Guid JudgeId,
        Guid SourceTeamId,
        IReadOnlyList<Guid> ScoringEventIds,
        IReadOnlyList<string> Flags);
}
