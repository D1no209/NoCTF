using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Profiles;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Profiles;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class GlobalTeamPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Global_team_is_reusable_and_each_registration_keeps_its_roster_snapshot(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_global_teams")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var captainId = Guid.CreateVersion7();
            var memberId = Guid.CreateVersion7();
            var firstCompetitionId = Guid.CreateVersion7();
            var secondCompetitionId = Guid.CreateVersion7();

            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.MigrateAsync(cancellationToken);
                db.Users.AddRange(
                    User(captainId, "captain", now),
                    User(memberId, "member", now));
                db.Competitions.AddRange(
                    Competition(firstCompetitionId, captainId, "First", now),
                    Competition(secondCompetitionId, captainId, "Second", now));
                await db.SaveChangesAsync(cancellationToken);
            }

            Guid teamProfileId;
            await using (var db = new NoCtfDbContext(options))
            {
                var profiles = new TeamProfileStore(db);
                var created = await new CreateTeamProfile(profiles).ExecuteAsync(new(
                    captainId,
                    "  Alpha  ",
                    null,
                    now), cancellationToken);
                await Assert.That(created.Succeeded).IsTrue();
                await Assert.That(created.Value!.Name).IsEqualTo("Alpha");
                teamProfileId = created.Value.Id;

                var joined = await new JoinTeamProfile(profiles).ExecuteAsync(
                    created.Value.InvitationToken,
                    memberId,
                    cancellationToken);
                await Assert.That(joined.Succeeded).IsTrue();

                var registrations = new TeamRegistrationStore(db, new NoopOutbox());
                var registered = await new RegisterTeamProfile(
                    profiles,
                    new CreateTeam(registrations)).ExecuteAsync(
                    firstCompetitionId,
                    teamProfileId,
                    captainId,
                    now,
                    cancellationToken);
                await Assert.That(registered.Succeeded).IsTrue();
                await Assert.That(registered.Value!.MemberIds)
                    .IsEquivalentTo([captainId, memberId]);

                var removed = await profiles.RemoveMemberAsync(
                    teamProfileId,
                    memberId,
                    cancellationToken);
                await Assert.That(removed).IsNull();

                var registeredAgain = await new RegisterTeamProfile(
                    profiles,
                    new CreateTeam(registrations)).ExecuteAsync(
                    secondCompetitionId,
                    teamProfileId,
                    captainId,
                    now.AddMinutes(1),
                    cancellationToken);
                await Assert.That(registeredAgain.Succeeded).IsTrue();
                await Assert.That(registeredAgain.Value!.MemberIds)
                    .IsEquivalentTo([captainId]);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var snapshots = await db.Teams.AsNoTracking()
                    .Where(team => team.TeamProfileId == teamProfileId)
                    .OrderBy(team => team.CompetitionId)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(snapshots).Count().IsEqualTo(2);
                var first = snapshots.Single(team =>
                    team.CompetitionId == firstCompetitionId);
                var second = snapshots.Single(team =>
                    team.CompetitionId == secondCompetitionId);
                await Assert.That(first.MemberIds)
                    .IsEquivalentTo([captainId, memberId]);
                await Assert.That(second.MemberIds).IsEquivalentTo([captainId]);

                var registrations = new TeamRegistrationStore(db, new NoopOutbox());
                var projectedFirst = await registrations.FindForUserAsync(
                    firstCompetitionId,
                    captainId,
                    true,
                    cancellationToken);
                var projectedSecond = await registrations.FindAsync(
                    secondCompetitionId,
                    second.Id,
                    true,
                    cancellationToken);
                await Assert.That(projectedFirst!.TeamProfileId)
                    .IsEqualTo(teamProfileId);
                await Assert.That(projectedSecond!.TeamProfileId)
                    .IsEqualTo(teamProfileId);

                var duplicate = await new CreateTeamProfile(
                    new TeamProfileStore(db)).ExecuteAsync(new(
                    memberId,
                    "alpha",
                    null,
                    now.AddMinutes(2)), cancellationToken);
                await Assert.That(duplicate.ErrorCode).IsEqualTo("team_name_conflict");
            }
        });
    }

    private static User User(Guid id, string name, DateTimeOffset now) => new()
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

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        string title,
        DateTimeOffset now) => new()
    {
        Id = id,
        OwnerId = ownerId,
        Title = title,
        Mode = GameMode.Ctf,
        Status = CompetitionStatus.Published,
        TeamRegistrationAutoApprove = true,
        MaxTeamMembers = 3,
        FlagDerivationSecret = new byte[32],
        StartAt = now.AddHours(1),
        EndAt = now.AddHours(2),
        CreatedAt = now,
        UpdatedAt = now,
        ConfigurationUpdatedAt = now
    };

    private sealed class NoopOutbox : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
