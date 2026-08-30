using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
public sealed class PlatformUserAccountStatusPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Email_verification_changes_invalidate_tokens_and_are_audited(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_platform_email_verification")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var migrationDb = new NoCtfDbContext(options))
                await migrationDb.Database.EnsureCreatedAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var actorId = Guid.CreateVersion7(now);
            var targetId = Guid.CreateVersion7(now.AddTicks(1));
            await using (var seedDb = new NoCtfDbContext(options))
            {
                var target = User(
                    targetId,
                    "email-target",
                    UserRole.User,
                    UserAccountStatus.Active,
                    7,
                    now);
                target.EmailVerifiedAt = null;
                seedDb.Users.AddRange(
                    User(actorId, "email-actor", UserRole.Administrator,
                        UserAccountStatus.Active, 3, now),
                    target);
                await seedDb.SaveChangesAsync(cancellationToken);
            }

            await using var operationDb = new NoCtfDbContext(options);
            var store = new PlatformAdministrationStore(
                operationDb,
                new PasswordHasher<User>());
            var verified = await store.UpdateEmailVerificationAsync(
                targetId,
                actorId,
                true,
                now.AddMinutes(1),
                cancellationToken);
            var duplicate = await store.UpdateEmailVerificationAsync(
                targetId,
                actorId,
                true,
                now.AddMinutes(2),
                cancellationToken);
            var unverified = await store.UpdateEmailVerificationAsync(
                targetId,
                actorId,
                false,
                now.AddMinutes(3),
                cancellationToken);

            await Assert.That(verified.State)
                .IsEqualTo(UpdatePlatformUserEmailVerificationState.Updated);
            await Assert.That(verified.User!.EmailVerified).IsTrue();
            await Assert.That(verified.User.TokenVersion).IsEqualTo(8);
            await Assert.That(duplicate.User!.TokenVersion).IsEqualTo(8);
            await Assert.That(unverified.User!.EmailVerified).IsFalse();
            await Assert.That(unverified.User.TokenVersion).IsEqualTo(9);

            await using var verification = new NoCtfDbContext(options);
            var facts = await verification.Notifications.AsNoTracking()
                .Where(notification =>
                    notification.Kind == NotificationKind.UserAccountLifecycleChanged)
                .OrderBy(notification => notification.SentAt)
                .Select(notification => notification.ContentJson)
                .ToArrayAsync(cancellationToken);
            var actions = facts.Select(content =>
                    JsonSerializer.Deserialize<UserAccountLifecycleFact>(
                        content,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Action)
                .ToArray();
            await Assert.That(actions).IsEquivalentTo([
                UserAccountLifecycleAction.EmailVerified,
                UserAccountLifecycleAction.EmailUnverified
            ]);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Account_status_changes_invalidate_tokens_audit_and_preserve_an_administrator(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_platform_user_status")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var migrationDb = new NoCtfDbContext(options))
                await migrationDb.Database.EnsureCreatedAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var actorId = Guid.CreateVersion7(now);
            var secondAdministratorId = Guid.CreateVersion7(now.AddTicks(1));
            var targetId = Guid.CreateVersion7(now.AddTicks(2));
            var anonymizedId = Guid.CreateVersion7(now.AddTicks(3));
            await using (var seedDb = new NoCtfDbContext(options))
            {
                seedDb.Users.AddRange(
                    User(actorId, "status-actor", UserRole.Administrator, UserAccountStatus.Active, 3, now),
                    User(secondAdministratorId, "status-admin", UserRole.Administrator, UserAccountStatus.Active, 5, now),
                    User(targetId, "status-target", UserRole.User, UserAccountStatus.Active, 7, now),
                    User(anonymizedId, "status-anonymized", UserRole.User, UserAccountStatus.Anonymized, 11, now));
                await seedDb.SaveChangesAsync(cancellationToken);
            }

            await Assert.That((await UpdateAsync(
                options, postgres.GetConnectionString(), targetId, actorId,
                UserAccountStatus.Disabled, now.AddMinutes(1), cancellationToken)).State)
                .IsEqualTo(UpdatePlatformUserStatusState.Updated);
            var duplicate = await UpdateAsync(
                options, postgres.GetConnectionString(), targetId, actorId,
                UserAccountStatus.Disabled, now.AddMinutes(2), cancellationToken);
            await Assert.That(duplicate.User!.TokenVersion).IsEqualTo(8);
            await Assert.That((await UpdateAsync(
                options, postgres.GetConnectionString(), targetId, actorId,
                UserAccountStatus.Active, now.AddMinutes(3), cancellationToken)).State)
                .IsEqualTo(UpdatePlatformUserStatusState.Updated);
            await Assert.That((await UpdateAsync(
                options, postgres.GetConnectionString(), targetId, actorId,
                UserAccountStatus.Banned, now.AddMinutes(4), cancellationToken)).State)
                .IsEqualTo(UpdatePlatformUserStatusState.Updated);

            var immutable = await UpdateAsync(
                options, postgres.GetConnectionString(), anonymizedId, actorId,
                UserAccountStatus.Active, now.AddMinutes(5), cancellationToken);
            await Assert.That(immutable.State)
                .IsEqualTo(UpdatePlatformUserStatusState.AnonymizedAccountImmutable);

            await Assert.That((await UpdateAsync(
                options, postgres.GetConnectionString(), secondAdministratorId, actorId,
                UserAccountStatus.Disabled, now.AddMinutes(6), cancellationToken)).State)
                .IsEqualTo(UpdatePlatformUserStatusState.Updated);
            await Assert.That((await UpdateAsync(
                options, postgres.GetConnectionString(), actorId, actorId,
                UserAccountStatus.Disabled, now.AddMinutes(7), cancellationToken)).State)
                .IsEqualTo(UpdatePlatformUserStatusState.LastAdministratorProtected);

            await using var verification = new NoCtfDbContext(options);
            var target = await verification.Users.AsNoTracking()
                .SingleAsync(user => user.Id == targetId, cancellationToken);
            await Assert.That(target.AccountStatus).IsEqualTo(UserAccountStatus.Banned);
            await Assert.That(target.TokenVersion).IsEqualTo(10);
            var activeAdministrators = await verification.Users.AsNoTracking().CountAsync(user =>
                user.Kind == UserKind.Human
                && user.Role == UserRole.Administrator
                && user.AccountStatus == UserAccountStatus.Active,
                cancellationToken);
            await Assert.That(activeAdministrators).IsEqualTo(1);

            var facts = (await verification.Notifications.AsNoTracking()
                    .Where(notification => notification.Kind == NotificationKind.UserAccountLifecycleChanged)
                    .OrderBy(notification => notification.SentAt)
                    .Select(notification => new { notification.SourceId, notification.ContentJson })
                    .ToArrayAsync(cancellationToken))
                .Select(item => new
                {
                    item.SourceId,
                    Fact = JsonSerializer.Deserialize<UserAccountLifecycleFact>(
                        item.ContentJson,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))!
                })
                .ToArray();
            await Assert.That(facts).Count().IsEqualTo(4);
            await Assert.That(facts.Select(item => item.Fact.Action).ToArray())
                .IsEquivalentTo([
                    UserAccountLifecycleAction.Disabled,
                    UserAccountLifecycleAction.Activated,
                    UserAccountLifecycleAction.Banned,
                    UserAccountLifecycleAction.Disabled
                ]);
            await Assert.That(facts.All(item => item.SourceId == actorId)).IsTrue();
        });
    }

    private static async Task<UpdatePlatformUserStatusResult> UpdateAsync(
        DbContextOptions<NoCtfDbContext> options,
        string connectionString,
        Guid userId,
        Guid actorUserId,
        UserAccountStatus accountStatus,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        return await new PlatformAdministrationStore(
                db,
                new PasswordHasher<User>())
            .UpdateAccountStatusAsync(userId, actorUserId, accountStatus, now, ct);
    }

    private static User User(
        Guid id,
        string userName,
        UserRole role,
        UserAccountStatus accountStatus,
        int tokenVersion,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            AccountStatus = accountStatus,
            TokenVersion = tokenVersion,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
}
