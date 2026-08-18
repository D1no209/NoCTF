using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Appeals;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Appeals;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("TeamBanAppeals")]
public sealed class TeamBanAppealPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Captain_can_appeal_and_staff_can_correct_finished_competition(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_team_ban_appeals")
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
            var store = new TeamBanAppealStore(db, outbox, eventStore);

            var memberView = await store.GetForMemberAsync(
                fixture.CompetitionId,
                fixture.MemberId,
                cancellationToken);
            await Assert.That(memberView).IsNotNull();
            await Assert.That(memberView!.Source).IsEqualTo(TeamBanSource.ManualModeration);
            await Assert.That(memberView.CanAppeal).IsFalse();
            await Assert.That(memberView.Appeal).IsNull();

            var memberAttempt = await store.SubmitAsync(new(
                fixture.CompetitionId,
                fixture.MemberId,
                "The member cannot submit this appeal.",
                fixture.Now.AddMinutes(1)), cancellationToken);
            await Assert.That(memberAttempt.Failure)
                .IsEqualTo(TeamBanAppealFailure.CaptainRequired);

            var submitted = await store.SubmitAsync(new(
                fixture.CompetitionId,
                fixture.CaptainId,
                "We request a private review of the moderation decision.",
                fixture.Now.AddMinutes(2)), cancellationToken);
            await Assert.That(submitted.Succeeded).IsTrue();
            var duplicate = await store.SubmitAsync(new(
                fixture.CompetitionId,
                fixture.CaptainId,
                "This second appeal must not be accepted.",
                fixture.Now.AddMinutes(3)), cancellationToken);
            await Assert.That(duplicate.Failure)
                .IsEqualTo(TeamBanAppealFailure.AppealAlreadySubmitted);

            var queue = await store.ListForStaffAsync(
                fixture.CompetitionId,
                cancellationToken);
            await Assert.That(queue).IsNotNull();
            await Assert.That(queue!).Count().IsEqualTo(1);
            await Assert.That(queue[0].Appeal!.Status)
                .IsEqualTo(TeamBanAppealStatus.Submitted);
            await Assert.That(queue[0].Appeal!.Statement)
                .IsEqualTo("We request a private review of the moderation decision.");

            var resolved = await store.ResolveAsync(new(
                fixture.CompetitionId,
                queue[0].Appeal!.Id,
                fixture.OwnerId,
                TeamBanAppealResolution.Accept,
                "Independent review found the original decision incorrect.",
                fixture.Now.AddMinutes(4)), cancellationToken);
            await Assert.That(resolved.Succeeded).IsTrue();

            db.ChangeTracker.Clear();
            var team = await db.Teams.AsNoTracking().SingleAsync(
                candidate => candidate.Id == fixture.TeamId,
                cancellationToken);
            await Assert.That(team.IsBanned).IsFalse();
            await Assert.That(team.BannedAt).IsNull();
            await Assert.That(team.BanReason).IsNull();
            var competition = await db.Competitions.AsNoTracking().SingleAsync(
                candidate => candidate.Id == fixture.CompetitionId,
                cancellationToken);
            await Assert.That(competition.Status).IsEqualTo(CompetitionStatus.Finished);
            await Assert.That(competition.LeaderboardDirty).IsTrue();

            var publicCorrection = await db.CompetitionEvents.AsNoTracking().SingleAsync(
                @event =>
                    @event.CompetitionId == fixture.CompetitionId
                    && @event.Kind == CompetitionEventKind.TeamBanCorrectionPublished,
                cancellationToken);
            await Assert.That(publicCorrection.Visibility)
                .IsEqualTo(CompetitionEventVisibility.Public);
            await Assert.That(publicCorrection.ParentEventId).IsEqualTo(fixture.BanEventId);
            await Assert.That(publicCorrection.ActorUserId).IsNull();
            await Assert.That(publicCorrection.Reason).IsNull();

            var captainView = await store.GetForMemberAsync(
                fixture.CompetitionId,
                fixture.CaptainId,
                cancellationToken);
            await Assert.That(captainView).IsNotNull();
            await Assert.That(captainView!.IsCurrentlyBanned).IsFalse();
            await Assert.That(captainView.CanAppeal).IsFalse();
            await Assert.That(captainView.Appeal!.Status)
                .IsEqualTo(TeamBanAppealStatus.Accepted);
            await Assert.That(captainView.Appeal!.ResolutionReason)
                .IsEqualTo("Independent review found the original decision incorrect.");

            var correctionMessage = outbox.Published.OfType<TeamBanCorrected>().Single();
            await Assert.That(correctionMessage.BanEventId).IsEqualTo(fixture.BanEventId);
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
        var captainId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var banEventId = Guid.CreateVersion7(now);
        db.Users.AddRange(
            User(ownerId, "appeal-owner", UserRole.Organizer, now),
            User(captainId, "appeal-captain", UserRole.User, now),
            User(memberId, "appeal-member", UserRole.User, now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Finished appeal competition",
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Finished,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-2),
            EndAt = now.AddHours(-1),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Appealing Team",
            NormalizedName = "APPEALING TEAM",
            CaptainId = captainId,
            MemberIds = [captainId, memberId],
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now.AddDays(-1),
            IsBanned = true,
            BannedAt = now,
            BannedById = ownerId,
            BanReason = "Private staff evidence"
        });
        db.CompetitionEvents.Add(new CompetitionEvent
        {
            Id = banEventId,
            CompetitionId = competitionId,
            Kind = CompetitionEventKind.TeamBanned,
            Level = CompetitionEventLevel.Warning,
            Visibility = CompetitionEventVisibility.Staff,
            ActorUserId = ownerId,
            SubjectType = EntityReferenceKind.Team,
            SubjectId = teamId,
            PayloadJson = """{"schemaVersion":1,"reason":"Private staff evidence"}""",
            OccurredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, ownerId, captainId, memberId, teamId, banEventId);
    }

    private static User User(
        Guid id,
        string userName,
        UserRole role,
        DateTimeOffset now) =>
        new()
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

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ConcurrentQueue<object> Published { get; } = new();

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid OwnerId,
        Guid CaptainId,
        Guid MemberId,
        Guid TeamId,
        Guid BanEventId);
}
