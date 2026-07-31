using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TeamLeaderboardInvalidationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Team_revision_changes_publish_one_transactional_invalidation_each(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_team_leaderboard_invalidation")
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

            await using (var db = new NoCtfDbContext(options))
            {
                var registration = new TeamRegistrationStore(db, outbox);
                var created = await registration.TryCreateAsync(
                    new CreateTeamCommand(
                        fixture.RegistrationCompetitionId,
                        fixture.RegistrationCaptainId,
                        "Registration Team",
                        null,
                        fixture.Now),
                    TeamRegistrationStatus.Pending,
                    cancellationToken);
                await Assert.That(created.Team).IsNotNull();
                var teamId = created.Team!.Id;

                var rejected = await registration.SetStatusAsync(
                    fixture.RegistrationCompetitionId,
                    teamId,
                    TeamRegistrationStatus.Rejected,
                    cancellationToken);
                await Assert.That(rejected.Changed).IsTrue();

                var resubmitted = await registration.ResubmitAsync(
                    fixture.RegistrationCompetitionId,
                    teamId,
                    fixture.RegistrationCaptainId,
                    cancellationToken);
                await Assert.That(resubmitted.Changed).IsTrue();

                var approved = await registration.SetStatusAsync(
                    fixture.RegistrationCompetitionId,
                    teamId,
                    TeamRegistrationStatus.Approved,
                    cancellationToken);
                await Assert.That(approved.Changed).IsTrue();

                var updated = await registration.UpdateAsync(
                    new UpdateTeamCommand(
                        fixture.RegistrationCompetitionId,
                        teamId,
                        "Updated Registration Team",
                        null),
                    cancellationToken);
                await Assert.That(updated.Team).IsNotNull();

                var deleteFailure = await registration.SoftDeleteAsync(
                    fixture.RegistrationCompetitionId,
                    teamId,
                    fixture.RegistrationCaptainId,
                    fixture.Now.AddMinutes(1),
                    cancellationToken);
                await Assert.That(deleteFailure).IsNull();
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var membership = new TeamMembershipStore(db, outbox);
                var joinSecondFailure = await membership.JoinByInvitationAsync(
                    fixture.MembershipCompetitionId,
                    fixture.InvitationToken,
                    fixture.SecondMemberId,
                    fixture.Now,
                    cancellationToken);
                await Assert.That(joinSecondFailure).IsNull();

                var transferFailure = await membership.TransferCaptainAsync(
                    fixture.MembershipCompetitionId,
                    fixture.MembershipTeamId,
                    fixture.MembershipCaptainId,
                    fixture.SecondMemberId,
                    cancellationToken);
                await Assert.That(transferFailure).IsNull();

                var joinThirdFailure = await membership.JoinByInvitationAsync(
                    fixture.MembershipCompetitionId,
                    fixture.InvitationToken,
                    fixture.ThirdMemberId,
                    fixture.Now,
                    cancellationToken);
                await Assert.That(joinThirdFailure).IsNull();

                var removeFailure = await membership.RemoveMemberAsync(
                    fixture.MembershipCompetitionId,
                    fixture.MembershipTeamId,
                    fixture.ThirdMemberId,
                    fixture.SecondMemberId,
                    cancellationToken);
                await Assert.That(removeFailure).IsNull();

                var leaveFailure = await membership.LeaveAsync(
                    fixture.MembershipCompetitionId,
                    fixture.MembershipCaptainId,
                    cancellationToken);
                await Assert.That(leaveFailure).IsNull();
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var moderation = new TeamModerationStore(db, outbox);
                var command = new TeamModerationCommand(
                    fixture.MembershipCompetitionId,
                    fixture.MembershipTeamId,
                    fixture.SecondMemberId,
                    true,
                    "integration test",
                    fixture.Now.AddMinutes(2));
                var applied = await moderation.ApplyAsync(command, cancellationToken);
                await Assert.That(applied.Succeeded).IsTrue();

                var idempotent = await moderation.ApplyAsync(command, cancellationToken);
                await Assert.That(idempotent.Succeeded).IsTrue();
            }

            var invalidations = outbox.Published.OfType<InvalidateLeaderboard>().ToArray();
            await Assert.That(invalidations).Count().IsEqualTo(12);
            await Assert.That(invalidations.Count(
                message => message.CompetitionId == fixture.RegistrationCompetitionId))
                .IsEqualTo(6);
            await Assert.That(invalidations.Count(
                message => message.CompetitionId == fixture.MembershipCompetitionId))
                .IsEqualTo(6);
            await Assert.That(outbox.Published.All(
                message => message is InvalidateLeaderboard)).IsTrue();
            await Assert.That(outbox.FlushCount).IsEqualTo(12);

            await using var verify = new NoCtfDbContext(options);
            var revisions = await verify.Competitions.AsNoTracking()
                .Where(competition =>
                    competition.Id == fixture.RegistrationCompetitionId
                    || competition.Id == fixture.MembershipCompetitionId)
                .ToDictionaryAsync(
                    competition => competition.Id,
                    competition => competition.LeaderboardRevision,
                    cancellationToken);
            await Assert.That(revisions[fixture.RegistrationCompetitionId]).IsEqualTo(6);
            await Assert.That(revisions[fixture.MembershipCompetitionId]).IsEqualTo(6);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var registrationCaptainId = Guid.CreateVersion7();
        var membershipCaptainId = Guid.CreateVersion7();
        var secondMemberId = Guid.CreateVersion7();
        var thirdMemberId = Guid.CreateVersion7();
        db.Users.AddRange(
            User(registrationCaptainId, "registration-captain", now),
            User(membershipCaptainId, "membership-captain", now),
            User(secondMemberId, "second-member", now),
            User(thirdMemberId, "third-member", now));

        var registrationCompetitionId = Guid.CreateVersion7();
        var membershipCompetitionId = Guid.CreateVersion7();
        db.Competitions.AddRange(
            Competition(
                registrationCompetitionId,
                registrationCaptainId,
                "Registration invalidation",
                now),
            Competition(
                membershipCompetitionId,
                membershipCaptainId,
                "Membership invalidation",
                now));

        var invitationToken = new string('i', 32);
        var membershipTeamId = Guid.CreateVersion7();
        db.Teams.Add(new Team
        {
            Id = membershipTeamId,
            CompetitionId = membershipCompetitionId,
            Name = "Membership Team",
            NormalizedName = "MEMBERSHIP TEAM",
            CaptainId = membershipCaptainId,
            MemberIds = [membershipCaptainId],
            InvitationToken = invitationToken,
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            registrationCompetitionId,
            registrationCaptainId,
            membershipCompetitionId,
            membershipTeamId,
            membershipCaptainId,
            secondMemberId,
            thirdMemberId,
            invitationToken);
    }

    private static User User(Guid id, string userName, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        string title,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Title = title,
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Draft,
            MaxTeamMembers = 5,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        };

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        private int flushCount;

        public ConcurrentQueue<object> Published { get; } = new();
        public int FlushCount => Volatile.Read(ref flushCount);

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

        public Task FlushOutgoingMessagesAsync()
        {
            Interlocked.Increment(ref flushCount);
            return Task.CompletedTask;
        }
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid RegistrationCompetitionId,
        Guid RegistrationCaptainId,
        Guid MembershipCompetitionId,
        Guid MembershipTeamId,
        Guid MembershipCaptainId,
        Guid SecondMemberId,
        Guid ThirdMemberId,
        string InvitationToken);
}
