using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class AdministratorIssuedTokenPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Issuance_registration_single_revoke_and_global_revoke_are_durable(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_administrator_issued_tokens")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(cancellationToken);
                var now = DateTimeOffset.Parse("2026-09-12T02:00:00Z");
                setup.Users.AddRange(
                    User(Guid.NewGuid(), "administrator", UserRole.Administrator, now),
                    User(Guid.NewGuid(), "other-administrator", UserRole.Administrator, now),
                    User(Guid.NewGuid(), "target", UserRole.User, now));
                await setup.SaveChangesAsync(cancellationToken);
            }

            Guid administratorId;
            Guid targetId;
            Guid otherAdministratorId;
            Guid jwtId;
            await using (var db = new NoCtfDbContext(options))
            {
                administratorId = await db.Users.Where(user => user.UserName == "administrator")
                    .Select(user => user.Id).SingleAsync(cancellationToken);
                targetId = await db.Users.Where(user => user.UserName == "target")
                    .Select(user => user.Id).SingleAsync(cancellationToken);
                otherAdministratorId = await db.Users
                    .Where(user => user.UserName == "other-administrator")
                    .Select(user => user.Id).SingleAsync(cancellationToken);
                jwtId = Guid.NewGuid();
                var now = DateTimeOffset.Parse("2026-09-12T03:00:00Z");
                var store = new PlatformAdministrationStore(db, new PasswordHasher<User>());
                await store.RecordTokenIssuedAsync(
                    administratorId,
                    new(
                        1,
                        targetId,
                        "target",
                        PlatformUserTokenAdministrationAction.AccessTokenIssued,
                        jwtId,
                        now.AddHours(1),
                        "support case",
                        0),
                    now,
                    cancellationToken);

                var reader = new AccessTokenVersionReader(db);
                await Assert.That(await reader.IsCurrentAsync(
                    targetId,
                    0,
                    cancellationToken,
                    new(jwtId, administratorId))).IsTrue();
                await Assert.That(await reader.IsCurrentAsync(
                    targetId,
                    0,
                    cancellationToken,
                    new(jwtId, otherAdministratorId))).IsFalse();
                var listed = await store.ListIssuedTokensAsync(
                    administratorId, targetId, now, cancellationToken);
                await Assert.That(listed.Select(token => token.JwtId)).IsEquivalentTo([jwtId]);
                var foreignList = await store.ListIssuedTokensAsync(
                    otherAdministratorId, targetId, now, cancellationToken);
                await Assert.That(foreignList).IsEmpty();

                var foreignRevoke = await store.RevokeIssuedTokenAsync(
                    otherAdministratorId,
                    targetId,
                    jwtId,
                    now.AddSeconds(30),
                    cancellationToken);
                await Assert.That(foreignRevoke)
                    .IsEqualTo(RevokeAdminIssuedAccessTokenState.NotFound);
                await Assert.That(await reader.IsCurrentAsync(
                    targetId,
                    0,
                    cancellationToken,
                    new(jwtId, administratorId))).IsTrue();

                async Task<RevokeAdminIssuedAccessTokenState> RevokeAsync()
                {
                    await using var revokeDb = new NoCtfDbContext(options);
                    return await new PlatformAdministrationStore(
                            revokeDb,
                            new PasswordHasher<User>())
                        .RevokeIssuedTokenAsync(
                            administratorId,
                            targetId,
                            jwtId,
                            now.AddMinutes(1),
                            cancellationToken);
                }

                var concurrentRevocations = await Task.WhenAll(
                    RevokeAsync(),
                    RevokeAsync());
                await Assert.That(concurrentRevocations)
                    .IsEquivalentTo([
                        RevokeAdminIssuedAccessTokenState.Revoked,
                        RevokeAdminIssuedAccessTokenState.Revoked
                    ]);
                await Assert.That(await reader.IsCurrentAsync(
                    targetId,
                    0,
                    cancellationToken,
                    new(jwtId, administratorId))).IsFalse();

                var globallyRevoked = await store.InvalidateTokensAsync(
                    targetId, administratorId, now.AddMinutes(2), cancellationToken);
                await Assert.That(globallyRevoked!.TokenVersion).IsEqualTo(1);
                await Assert.That(await new AccessTokenVersionReader(db).IsCurrentAsync(
                    targetId, 0, cancellationToken)).IsFalse();
            }

            await using (var verification = new NoCtfDbContext(options))
            {
                var audits = await verification.Notifications.AsNoTracking()
                    .Where(notification =>
                        notification.Kind == NotificationKind.PlatformUserAccessTokenIssued
                        || notification.Kind == NotificationKind.PlatformUserAccessTokenRevoked
                        || notification.Kind == NotificationKind.PlatformUserTokensInvalidated)
                    .OrderBy(notification => notification.SentAt)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(audits).Count().IsEqualTo(3);
                await Assert.That(audits[0].Id).IsEqualTo(jwtId);
                await Assert.That(audits[1].RelatedId).IsEqualTo(jwtId);
                await Assert.That(audits.Any(notification =>
                    notification.ContentJson.Contains("support case")
                    && !notification.ContentJson.Contains("accessToken"))).IsTrue();
                var projected = await new PlatformAuditLogStore(verification).QueryAsync(
                    new(
                        PlatformAuditKind.PlatformAdministration,
                        null,
                        null,
                        null,
                        administratorId,
                        null,
                        null,
                        20),
                    cancellationToken);
                await Assert.That(projected.Select(item =>
                        item.PlatformAdministrationAction!.Value))
                    .IsEquivalentTo([
                        PlatformAdministrationAction.UserTokensInvalidated,
                        PlatformAdministrationAction.UserAccessTokenRevoked,
                        PlatformAdministrationAction.UserAccessTokenIssued
                    ]);
                await Assert.That(projected.Single(item => item.PlatformAdministrationAction
                    == PlatformAdministrationAction.UserAccessTokenIssued).JwtId)
                    .IsEqualTo(jwtId);
            }

            await using (var deletion = new NoCtfDbContext(options))
            {
                var deleted = await new UserAccountAdministrationStore(deletion).DeleteAsync(
                    targetId,
                    administratorId,
                    UserDeletionMode.HardDelete,
                    "privacy request",
                    DateTimeOffset.Parse("2026-09-12T04:00:00Z"),
                    cancellationToken);
                await Assert.That(deleted.State)
                    .IsEqualTo(UserDeletionState.PhysicallyDeleted);
            }

            await using (var preservation = new NoCtfDbContext(options))
            {
                var preservedTokenAuditCount = await preservation.Notifications.AsNoTracking()
                    .CountAsync(notification =>
                        notification.Kind == NotificationKind.PlatformUserAccessTokenIssued
                        || notification.Kind == NotificationKind.PlatformUserAccessTokenRevoked
                        || notification.Kind == NotificationKind.PlatformUserTokensInvalidated,
                        cancellationToken);
                await Assert.That(preservedTokenAuditCount).IsEqualTo(3);
                var preservedAudits = await new PlatformAuditLogStore(preservation).QueryAsync(
                    new(
                        PlatformAuditKind.PlatformAdministration,
                        null,
                        null,
                        null,
                        administratorId,
                        null,
                        null,
                        20),
                    cancellationToken);
                await Assert.That(preservedAudits).Count().IsEqualTo(3);
                await Assert.That(preservedAudits.Select(audit =>
                        audit.SubjectDisplayName ?? string.Empty))
                    .IsEquivalentTo(["target", "target", "target"]);
            }
        });
    }

    private static User User(
        Guid id,
        string name,
        UserRole role,
        DateTimeOffset now) => new()
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
}
