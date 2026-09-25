using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("ChallengeAccess")]
public sealed class CompetitionChallengeAudienceAccessPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Only_staff_and_eligible_participants_can_read_competition_challenges(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_challenge_audience")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var ids = await SeedAsync(options, ct);

            await using var db = new NoCtfDbContext(options);
            var access = new CompetitionChallengeReadAccess(db);
            async Task<bool> CanReadAsync(Guid userId) =>
                await access.ResolveAsync(userId, ids.CompetitionId, DateTimeOffset.UtcNow, ct) is not null;

            await Assert.That(await CanReadAsync(Guid.Empty)).IsFalse();
            await Assert.That(await CanReadAsync(ids.UnregisteredId)).IsFalse();
            await Assert.That(await CanReadAsync(ids.PendingId)).IsFalse();
            await Assert.That(await CanReadAsync(ids.RejectedId)).IsFalse();
            await Assert.That(await CanReadAsync(ids.BannedId)).IsFalse();
            await Assert.That(await CanReadAsync(ids.DeletedTeamId)).IsFalse();
            await Assert.That(await CanReadAsync(ids.InactiveAdministratorId)).IsFalse();

            await Assert.That(await CanReadAsync(ids.ApprovedId)).IsTrue();
            await Assert.That(await CanReadAsync(ids.OwnerId)).IsTrue();
            await Assert.That(await CanReadAsync(ids.ManagerId)).IsTrue();
            await Assert.That(await CanReadAsync(ids.JudgeId)).IsTrue();
            await Assert.That(await CanReadAsync(ids.ObserverId)).IsTrue();
            await Assert.That(await CanReadAsync(ids.AdministratorId)).IsTrue();

            var counter = new QueryCounter();
            var measuredOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(counter)
                .Options;
            await using var measuredDb = new NoCtfDbContext(measuredOptions);
            var participantDecision = await new CompetitionChallengeReadAccess(measuredDb)
                .ResolveAsync(ids.ApprovedId, ids.CompetitionId, DateTimeOffset.UtcNow, ct);
            await Assert.That(participantDecision?.TeamId).IsNotNull();
            await Assert.That(counter.ReaderCount).IsLessThanOrEqualTo(3);
        });
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            ReaderCount++;
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<Ids> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var ids = new Ids(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7());
        db.Users.AddRange(
            User(ids.OwnerId, "challenge-owner", now),
            User(ids.ManagerId, "challenge-manager", now),
            User(ids.JudgeId, "challenge-judge", now),
            User(ids.ObserverId, "challenge-observer", now),
            User(ids.AdministratorId, "challenge-admin", now, UserRole.Administrator),
            User(ids.InactiveAdministratorId, "challenge-inactive-admin", now, UserRole.Administrator, UserAccountStatus.Disabled),
            User(ids.ApprovedId, "challenge-approved", now),
            User(ids.PendingId, "challenge-pending", now),
            User(ids.RejectedId, "challenge-rejected", now),
            User(ids.BannedId, "challenge-banned", now),
            User(ids.DeletedTeamId, "challenge-deleted-team", now),
            User(ids.UnregisteredId, "challenge-unregistered", now));
        db.Competitions.Add(new CtfCompetition
        {
            Id = ids.CompetitionId,
            Title = "Challenge audience",
            OwnerId = ids.OwnerId,
            ManagerIds = [ids.ManagerId],
            JudgeIds = [ids.JudgeId],
            ObserverIds = [ids.ObserverId],
            Status = CompetitionStatus.Running,
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.AddRange(
            Team(ids.CompetitionId, ids.ApprovedId, TeamRegistrationStatus.Approved, now),
            Team(ids.CompetitionId, ids.PendingId, TeamRegistrationStatus.Pending, now),
            Team(ids.CompetitionId, ids.RejectedId, TeamRegistrationStatus.Rejected, now),
            Team(ids.CompetitionId, ids.BannedId, TeamRegistrationStatus.Approved, now, banned: true),
            Team(ids.CompetitionId, ids.DeletedTeamId, TeamRegistrationStatus.Approved, now, deleted: true));
        await db.SaveChangesAsync(ct);
        return ids;
    }

    private static User User(
        Guid id,
        string name,
        DateTimeOffset now,
        UserRole role = UserRole.User,
        UserAccountStatus status = UserAccountStatus.Active) => new()
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = status,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Team Team(
        Guid competitionId,
        Guid userId,
        TeamRegistrationStatus status,
        DateTimeOffset now,
        bool banned = false,
        bool deleted = false) => new()
        {
            Id = Guid.CreateVersion7(),
            CompetitionId = competitionId,
            Name = $"team-{userId:N}",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = status,
            RegisteredAt = now,
            IsBanned = banned,
            BannedAt = banned ? now : null,
            DeletedAt = deleted ? now : null
        };

    private sealed record Ids(
        Guid CompetitionId,
        Guid OwnerId,
        Guid ManagerId,
        Guid JudgeId,
        Guid ObserverId,
        Guid AdministratorId,
        Guid InactiveAdministratorId,
        Guid ApprovedId,
        Guid PendingId,
        Guid RejectedId,
        Guid BannedId,
        Guid DeletedTeamId,
        Guid UnregisteredId);
}
