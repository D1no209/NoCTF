using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.RefreshSession;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class AdministratorIssuedTokenPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test, Timeout(300_000)]
    public async Task Ordinary_issuance_creates_no_server_record_and_global_invalidation_remains_durable(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = BuildPostgres("noctf_ordinary_issued_tokens");
            await postgres.StartAsync(ct);
            var options = Options(postgres);
            var now = DateTimeOffset.Parse("2026-09-20T04:00:00Z");
            var administratorId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            db.Users.AddRange(
                User(administratorId, "administrator", UserRole.Administrator, now),
                User(targetId, "target", UserRole.User, now));
            await db.SaveChangesAsync(ct);
            var platform = new ManagePlatform(
                new PlatformAdministrationStore(db, new PasswordHasher<User>()),
                new StubIssuer(now));

            var issued = await platform.IssueUserTokenAsync(targetId, 3600, now, ct);

            await Assert.That(issued.Failure).IsEqualTo(IssuePlatformUserTokenFailure.None);
            await Assert.That(issued.Token!.Token).IsEqualTo("ordinary-access-token");
            await Assert.That(await db.Notifications.CountAsync(notification =>
                notification.Kind == NotificationKind.PlatformUserAccessTokenIssued
                || notification.Kind == NotificationKind.PlatformUserAccessTokenRevoked, ct)).IsEqualTo(0);
            await Assert.That(await new AccessTokenVersionReader(db).IsCurrentAsync(targetId, 0, ct)).IsTrue();

            var invalidated = await platform.InvalidateTokensAsync(targetId, administratorId, now.AddMinutes(1), ct);

            await Assert.That(invalidated!.TokenVersion).IsEqualTo(1);
            await Assert.That(await new AccessTokenVersionReader(db).IsCurrentAsync(targetId, 0, ct)).IsFalse();
            await Assert.That(await db.Notifications.CountAsync(notification =>
                notification.Kind == NotificationKind.PlatformUserTokensInvalidated, ct)).IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Legacy_claims_keep_historical_issuance_and_revocation_validation(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = BuildPostgres("noctf_legacy_issued_tokens");
            await postgres.StartAsync(ct);
            var options = Options(postgres);
            var now = DateTimeOffset.Parse("2026-09-20T05:00:00Z");
            var administratorId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var jwtId = Guid.NewGuid();
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            db.Users.AddRange(
                User(administratorId, "administrator", UserRole.Administrator, now),
                User(targetId, "target", UserRole.User, now));
            var fact = new PlatformUserTokenAuditFact(1, targetId, "target",
                PlatformUserTokenAdministrationAction.AccessTokenIssued, jwtId,
                now.AddHours(1), "legacy support", 0);
            db.Notifications.Add(TokenAudit(jwtId, administratorId, fact,
                NotificationKind.PlatformUserAccessTokenIssued,
                EntityReferenceKind.User, targetId, now));
            await db.SaveChangesAsync(ct);
            var reader = new AccessTokenVersionReader(db);

            await Assert.That(await reader.IsCurrentAsync(targetId, 0, ct,
                new LegacyAdministratorIssuedAccessToken(jwtId, administratorId))).IsTrue();
            await Assert.That(await reader.IsCurrentAsync(targetId, 0, ct,
                new LegacyAdministratorIssuedAccessToken(jwtId, Guid.NewGuid()))).IsFalse();

            db.Notifications.Add(TokenAudit(Guid.NewGuid(), administratorId,
                fact with { Action = PlatformUserTokenAdministrationAction.AccessTokenRevoked, Reason = null },
                NotificationKind.PlatformUserAccessTokenRevoked,
                EntityReferenceKind.Notification, jwtId, now.AddMinutes(1)));
            await db.SaveChangesAsync(ct);

            await Assert.That(await reader.IsCurrentAsync(targetId, 0, ct,
                new LegacyAdministratorIssuedAccessToken(jwtId, administratorId))).IsFalse();
        });
    }

    private static PostgreSqlContainer BuildPostgres(string database) =>
        new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private static Notification TokenAudit(Guid id, Guid actorId, PlatformUserTokenAuditFact fact,
        NotificationKind kind, EntityReferenceKind relatedType, Guid relatedId, DateTimeOffset at) => new()
    {
        Id = id,
        SourceType = NotificationSourceType.User,
        SourceId = actorId,
        TargetType = NotificationTargetType.PlatformAdministrators,
        TargetId = Notification.PlatformAdministratorsTargetId,
        Kind = kind,
        ContentJson = JsonSerializer.Serialize(fact, JsonOptions),
        RelatedType = relatedType,
        RelatedId = relatedId,
        SentAt = at
    };

    private static User User(Guid id, string name, UserRole role, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "hash",
        Kind = UserKind.Human,
        Role = role,
        AccountStatus = UserAccountStatus.Active,
        CreatedAt = now,
        UpdatedAt = now
    };

    private sealed class StubIssuer(DateTimeOffset now) : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(AuthenticatedUser user, DateTimeOffset issuedAt, TimeSpan? lifetime = null) =>
            new("ordinary-access-token", issuedAt.Add(lifetime ?? TimeSpan.FromMinutes(15)), Guid.NewGuid());
        public IssuedRefreshToken IssueRefresh(AuthenticatedUser user) =>
            new("unused-refresh", now.AddDays(30));
        public RefreshTokenPrincipal? ValidateRefresh(string token) => null;
    }
}
