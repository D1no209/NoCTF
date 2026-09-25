using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
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

            await Assert.That((await access.ResolveAsync(ids.OwnerId, ids.DraftId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That((await access.ResolveAsync(ids.ManagerId, ids.DraftId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That((await access.ResolveAsync(ids.JudgeId, ids.DraftId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That((await access.ResolveAsync(ids.ObserverId, ids.DraftId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That((await access.ResolveAsync(ids.AdministratorId, ids.DraftId, ct))?.IsStaff)
                .IsTrue();

            await Assert.That(await access.ResolveAsync(ids.UserId, ids.DraftId, ct))
                .IsNull();
            await Assert.That(await access.ResolveAsync(ids.InactiveAdministratorId, ids.DraftId, ct))
                .IsNull();
            await Assert.That(await access.ResolveAsync(Guid.NewGuid(), ids.DraftId, ct))
                .IsNull();

            await Assert.That((await access.ResolveAsync(ids.UserId, ids.VisibleId, ct))?.IsStaff)
                .IsFalse();
            await Assert.That(await access.ResolveAsync(ids.UserId, ids.HiddenId, ct))
                .IsNull();
            await Assert.That(await access.ResolveAsync(ids.TeamMemberId, ids.HiddenId, ct))
                .IsNull();
            await Assert.That(await access.ResolveAsync(Guid.Empty, ids.HiddenId, ct))
                .IsNull();
            await Assert.That((await access.ResolveAsync(ids.OwnerId, ids.HiddenId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That((await access.ResolveAsync(ids.ManagerId, ids.HiddenId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That((await access.ResolveAsync(ids.JudgeId, ids.HiddenId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That((await access.ResolveAsync(ids.ObserverId, ids.HiddenId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That((await access.ResolveAsync(ids.AdministratorId, ids.HiddenId, ct))?.IsStaff)
                .IsTrue();
            await Assert.That(await access.ResolveAsync(ids.UserId, ids.DeletedId, ct))
                .IsNull();
            await Assert.That(await access.ResolveAsync(ids.UserId, Guid.NewGuid(), ct))
                .IsNull();
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
            User(ids.UserId, "hub-user", now),
            User(ids.TeamMemberId, "hub-team-member", now));
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
            Competition(
                ids.HiddenId,
                ids.OwnerId,
                CompetitionStatus.Visible,
                now,
                managerIds: [ids.ManagerId],
                judgeIds: [ids.JudgeId],
                observerIds: [ids.ObserverId],
                accessMode: CompetitionAccessMode.StaffOnly),
            Competition(ids.DeletedId, ids.OwnerId, CompetitionStatus.Visible, now, now));
        db.Teams.Add(new Team
        {
            Id = Guid.CreateVersion7(),
            CompetitionId = ids.HiddenId,
            Name = "Existing hidden team",
            CaptainId = ids.TeamMemberId,
            MemberIds = [ids.TeamMemberId],
            RegistrationStatus = TeamRegistrationStatus.Approved,
            InvitationToken = "0123456789abcdefghijklmnopqrstuv",
            RegisteredAt = now
        });
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
        Guid[]? observerIds = null,
        CompetitionAccessMode accessMode = CompetitionAccessMode.Public) => new CtfCompetition
        {
            Id = id,
            Title = $"Hub access {status}",
            OwnerId = ownerId,
            ManagerIds = managerIds ?? [],
            JudgeIds = judgeIds ?? [],
            ObserverIds = observerIds ?? [],
            AccessMode = accessMode,
            Status = status,
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
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
        Guid HiddenId,
        Guid DeletedId,
        Guid OwnerId,
        Guid ManagerId,
        Guid JudgeId,
        Guid ObserverId,
        Guid AdministratorId,
        Guid InactiveAdministratorId,
        Guid UserId,
        Guid TeamMemberId);
}
