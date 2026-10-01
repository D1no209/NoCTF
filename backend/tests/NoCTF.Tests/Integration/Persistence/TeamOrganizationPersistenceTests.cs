using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class TeamOrganizationPersistenceTests
{
    public enum OrganizationChange { TransferCaptain, Dissolve }

    [Test]
    [Arguments(OrganizationChange.TransferCaptain)]
    [Arguments(OrganizationChange.Dissolve)]
    [Timeout(120_000)]
    public async Task Rejoined_member_does_not_break_captain_transfer_or_dissolution(
        OrganizationChange change,
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var now = DateTimeOffset.UtcNow;
            var owner = User("organization-owner", now);
            var captain = User("organization-captain", now);
            var returningMember = User("organization-returning", now);
            var competition = new CtfCompetition
            {
                Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "Team organization regression",
                Status = CompetitionStatus.Visible,
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                MaxTeamMembers = 5, FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(1), EndAt = now.AddHours(2),
                CreatedAt = now, UpdatedAt = now,
            };
            var oldTeam = Team(competition.Id, returningMember.Id, "Original", 'A', now);
            var currentTeam = Team(competition.Id, captain.Id, "Current", 'B', now);
            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.EnsureCreatedAsync(ct);
                db.Users.AddRange(owner, captain, returningMember);
                db.Competitions.Add(competition);
                db.Teams.AddRange(oldTeam, currentTeam);
                await db.SaveChangesAsync(ct);
            }
            await using (var db = new NoCtfDbContext(options))
            {
                var publisher = new NoopPublisher();
                var events = new CompetitionEventStore(db, publisher);
                var registration = new TeamRegistrationStore(db, publisher, eventRecorder: events);
                await Assert.That(await registration.SoftDeleteAsync(
                    competition.Id, oldTeam.Id, returningMember.Id, now.AddSeconds(1), ct)).IsNull();
            }
            await using (var db = new NoCtfDbContext(options))
            {
                var publisher = new NoopPublisher();
                var membership = new TeamMembershipStore(
                    db, publisher, eventRecorder: new CompetitionEventStore(db, publisher));
                await Assert.That(await membership.JoinByInvitationAsync(
                    competition.Id, currentTeam.InvitationToken, returningMember.Id,
                    now.AddSeconds(2), ct)).IsNull();
            }
            await using (var db = new NoCtfDbContext(options))
            {
                var publisher = new NoopPublisher();
                var events = new CompetitionEventStore(db, publisher);
                if (change == OrganizationChange.TransferCaptain)
                {
                    var membership = new TeamMembershipStore(db, publisher, eventRecorder: events);
                    await Assert.That(await membership.TransferCaptainAsync(
                        competition.Id, currentTeam.Id, captain.Id, returningMember.Id, ct)).IsNull();
                }
                else
                {
                    var registration = new TeamRegistrationStore(db, publisher, eventRecorder: events);
                    await Assert.That(await registration.SoftDeleteAsync(
                        competition.Id, currentTeam.Id, captain.Id, now.AddSeconds(3), ct)).IsNull();
                }
            }
            await using var verification = new NoCtfDbContext(options);
            var saved = await verification.Teams.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(team => team.Id == currentTeam.Id, ct);
            await Assert.That(saved.MemberIds.Order()).IsEquivalentTo(new[] { captain.Id, returningMember.Id }.Order());
            await Assert.That(saved.Members.All(member => member.CompetitionId == competition.Id)).IsTrue();
            await Assert.That(saved.CaptainMembership!.UserId).IsEqualTo(saved.CaptainId);
            var eventKind = change == OrganizationChange.TransferCaptain
                ? CompetitionEventKind.TeamCaptainTransferred : CompetitionEventKind.TeamDeleted;
            await Assert.That(await verification.CompetitionEvents.CountAsync(
                item => item.TeamId == currentTeam.Id && item.Kind == eventKind, ct)).IsEqualTo(1);
            if (change == OrganizationChange.TransferCaptain)
            {
                await Assert.That(saved.CaptainId).IsEqualTo(returningMember.Id);
                await Assert.That(saved.DeletedAt).IsNull();
            }
            else
            {
                await Assert.That(saved.DeletedAt).IsNotNull();
                await Assert.That(await verification.Teams.AnyAsync(
                    team => team.CompetitionId == competition.Id
                        && team.Members.Any(member => member.UserId == returningMember.Id), ct)).IsFalse();
            }
        });
    }

    private static User User(string name, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test", PasswordHash = "unused", Kind = UserKind.Human,
        Role = UserRole.User, AccountStatus = UserAccountStatus.Active,
        EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now,
    };

    private static Team Team(Guid competitionId, Guid captainId, string name, char token, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), CompetitionId = competitionId, Name = name,
        CaptainId = captainId, MemberIds = [captainId], InvitationToken = new string(token, 32),
        RegistrationStatus = TeamRegistrationStatus.Unregistered, RegisteredAt = now,
    };

    private sealed class NoopPublisher : IPostCommitMessagePublisher
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
