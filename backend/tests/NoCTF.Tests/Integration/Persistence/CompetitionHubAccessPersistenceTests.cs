using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Competitions.Permissions;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionHubAccess")]
public sealed class CompetitionHubAccessPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Draft_access_is_limited_to_active_staff_while_visible_access_is_preserved(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_hub_access")
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
            var access = new CompetitionHubAccess(db);

            await Assert.That(await access.CanJoinAsync(ids.OwnerId, ids.DraftId, ct))
                .IsTrue();
            await Assert.That(await access.CanJoinAsync(ids.ManagerId, ids.DraftId, ct))
                .IsTrue();
            await Assert.That(await access.CanJoinAsync(ids.JudgeId, ids.DraftId, ct))
                .IsTrue();
            await Assert.That(await access.CanJoinAsync(ids.ObserverId, ids.DraftId, ct))
                .IsTrue();
            await Assert.That(await access.CanJoinAsync(ids.AdministratorId, ids.DraftId, ct))
                .IsTrue();

            await Assert.That(await access.CanJoinAsync(ids.UserId, ids.DraftId, ct))
                .IsFalse();
            await Assert.That(await access.CanJoinAsync(ids.InactiveAdministratorId, ids.DraftId, ct))
                .IsFalse();
            await Assert.That(await access.CanJoinAsync(Guid.NewGuid(), ids.DraftId, ct))
                .IsFalse();

            await Assert.That(await access.CanJoinAsync(ids.UserId, ids.VisibleId, ct))
                .IsTrue();
            await Assert.That(await access.CanJoinAsync(ids.UserId, ids.DeletedId, ct))
                .IsFalse();
            await Assert.That(await access.CanJoinAsync(ids.UserId, Guid.NewGuid(), ct))
                .IsFalse();
        });
    }

    private static async Task<Ids> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var ids = new Ids(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        db.Users.AddRange(
            User(ids.OwnerId, "hub-owner", now),
            User(ids.ManagerId, "hub-manager", now),
            User(ids.JudgeId, "hub-judge", now),
            User(ids.ObserverId, "hub-observer", now),
            User(ids.AdministratorId, "hub-administrator", now, UserRole.Administrator),
            User(
                ids.InactiveAdministratorId,
                "hub-inactive-administrator",
                now,
                UserRole.Administrator,
                UserAccountStatus.Disabled),
            User(ids.UserId, "hub-user", now));
        db.Competitions.AddRange(
            Competition(
                ids.DraftId,
                ids.OwnerId,
                CompetitionStatus.Draft,
                now,
                managerIds: [ids.ManagerId],
                judgeIds: [ids.JudgeId],
                observerIds: [ids.ObserverId]),
            Competition(ids.VisibleId, ids.OwnerId, CompetitionStatus.Visible, now),
            Competition(ids.DeletedId, ids.OwnerId, CompetitionStatus.Visible, now, now));
        await db.SaveChangesAsync(ct);
        return ids;
    }

    private static User User(
        Guid id,
        string name,
        DateTimeOffset now,
        UserRole role = UserRole.User,
        UserAccountStatus accountStatus = UserAccountStatus.Active) => new()
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = accountStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        CompetitionStatus status,
        DateTimeOffset now,
        DateTimeOffset? deletedAt = null,
        Guid[]? managerIds = null,
        Guid[]? judgeIds = null,
        Guid[]? observerIds = null) => new()
        {
            Id = id,
            Title = $"Hub access {status}",
            OwnerId = ownerId,
            ManagerIds = managerIds ?? [],
            JudgeIds = judgeIds ?? [],
            ObserverIds = observerIds ?? [],
            Mode = GameMode.Ctf,
            Status = status,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = deletedAt
        };

    private sealed record Ids(
        Guid DraftId,
        Guid VisibleId,
        Guid DeletedId,
        Guid OwnerId,
        Guid ManagerId,
        Guid JudgeId,
        Guid ObserverId,
        Guid AdministratorId,
        Guid InactiveAdministratorId,
        Guid UserId);
}
