using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
public sealed class LastAdministratorConcurrencyPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_role_and_lifecycle_mutations_preserve_an_active_human_administrator(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_last_admin_lifecycle")
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

            await DowngradeAndHardDeleteAsync(
                options,
                postgres.GetConnectionString(),
                cancellationToken);
            await DowngradeAndAnonymizeAsync(
                options,
                postgres.GetConnectionString(),
                cancellationToken);
            await DeleteBothAsync(
                options,
                postgres.GetConnectionString(),
                cancellationToken);
            await DowngradeBothAsync(
                options,
                postgres.GetConnectionString(),
                cancellationToken);
            await BotDoesNotSatisfyInvariantAsync(
                options,
                postgres.GetConnectionString(),
                cancellationToken);
        });
    }

    private static async Task DowngradeAndHardDeleteAsync(
        DbContextOptions<NoCtfDbContext> options,
        string connectionString,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var firstId = Guid.CreateVersion7(now);
        var secondId = Guid.CreateVersion7(now.AddTicks(1));
        await ResetAndSeedAsync(
            options,
            [
                User(firstId, "hard-delete-first", UserKind.Human, UserRole.Administrator, 11, now),
                User(secondId, "hard-delete-second", UserKind.Human, UserRole.Administrator, 13, now)
            ],
            ct);

        await using var mutationGate = await AcquireMutationGateAsync(connectionString, ct);
        var downgradeTask = UpdateRoleAsync(
            options,
            connectionString,
            firstId,
            now.AddMinutes(1),
            ct);
        await WaitForBlockedMutationsAsync(options, 1, ct);
        var deletionTask = DeleteAsync(
            options,
            secondId,
            firstId,
            UserDeletionMode.HardDelete,
            now.AddMinutes(1),
            ct);
        await WaitForBlockedMutationsAsync(options, 2, ct);
        await ReleaseMutationGateAsync(mutationGate, ct);
        await Task.WhenAll(deletionTask, downgradeTask);

        await Assert.That((await deletionTask).State)
            .IsEqualTo(UserDeletionState.LastAdministratorProtected);
        await Assert.That((await downgradeTask).State)
            .IsEqualTo(UpdatePlatformRoleState.Updated);
        await using var verification = new NoCtfDbContext(options);
        await AssertActiveHumanAdministratorCountAsync(verification, 1, ct);
        var users = await verification.Users.AsNoTracking().ToArrayAsync(ct);
        await Assert.That(users.Single(user => user.Id == firstId).TokenVersion).IsEqualTo(12);
        await Assert.That(users.Single(user => user.Id == secondId).TokenVersion).IsEqualTo(13);
        await Assert.That(await verification.Notifications.CountAsync(
            notification => notification.Kind == NotificationKind.UserAccountLifecycleChanged,
            ct)).IsEqualTo(0);
    }

    private static async Task DowngradeAndAnonymizeAsync(
        DbContextOptions<NoCtfDbContext> options,
        string connectionString,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var firstId = Guid.CreateVersion7(now);
        var secondId = Guid.CreateVersion7(now.AddTicks(1));
        await ResetAndSeedAsync(
            options,
            [
                User(firstId, "anonymize-first", UserKind.Human, UserRole.Administrator, 17, now),
                User(secondId, "anonymize-second", UserKind.Human, UserRole.Administrator, 19, now)
            ],
            ct);

        await using var mutationGate = await AcquireMutationGateAsync(connectionString, ct);
        var anonymizeTask = DeleteAsync(
            options,
            secondId,
            firstId,
            UserDeletionMode.Anonymize,
            now.AddMinutes(1),
            ct);
        await WaitForBlockedMutationsAsync(options, 1, ct);
        var downgradeTask = UpdateRoleAsync(
            options,
            connectionString,
            firstId,
            now.AddMinutes(1),
            ct);
        await WaitForBlockedMutationsAsync(options, 2, ct);
        await ReleaseMutationGateAsync(mutationGate, ct);
        await Task.WhenAll(anonymizeTask, downgradeTask);

        await Assert.That((await anonymizeTask).State)
            .IsEqualTo(UserDeletionState.Anonymized);
        await Assert.That((await downgradeTask).State)
            .IsEqualTo(UpdatePlatformRoleState.LastAdministratorProtected);
        await using var verification = new NoCtfDbContext(options);
        await AssertActiveHumanAdministratorCountAsync(verification, 1, ct);
        var users = await verification.Users.AsNoTracking()
            .OrderBy(user => user.Id)
            .ToArrayAsync(ct);
        var survivor = users.Single(user => user.Id == firstId);
        var anonymized = users.Single(user => user.Id == secondId);
        await Assert.That(survivor.TokenVersion).IsEqualTo(17);
        await Assert.That(anonymized.TokenVersion).IsEqualTo(20);
        await Assert.That(anonymized.AccountStatus).IsEqualTo(UserAccountStatus.Anonymized);
        await Assert.That(anonymized.Role).IsEqualTo(UserRole.User);
        await AssertLifecycleFactAsync(
            verification,
            secondId,
            UserAccountLifecycleAction.Anonymized,
            ct);
    }

    private static async Task DeleteBothAsync(
        DbContextOptions<NoCtfDbContext> options,
        string connectionString,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var firstId = Guid.CreateVersion7(now);
        var secondId = Guid.CreateVersion7(now.AddTicks(1));
        await ResetAndSeedAsync(
            options,
            [
                User(firstId, "delete-first", UserKind.Human, UserRole.Administrator, 23, now),
                User(secondId, "delete-second", UserKind.Human, UserRole.Administrator, 29, now)
            ],
            ct);

        await using var mutationGate = await AcquireMutationGateAsync(connectionString, ct);
        var firstTask = DeleteAsync(
            options,
            secondId,
            firstId,
            UserDeletionMode.HardDelete,
            now.AddMinutes(1),
            ct);
        await WaitForBlockedMutationsAsync(options, 1, ct);
        var secondTask = DeleteAsync(
            options,
            firstId,
            secondId,
            UserDeletionMode.HardDelete,
            now.AddMinutes(1),
            ct);
        await WaitForBlockedMutationsAsync(options, 2, ct);
        await ReleaseMutationGateAsync(mutationGate, ct);
        await Task.WhenAll(firstTask, secondTask);

        await Assert.That((await firstTask).State)
            .IsEqualTo(UserDeletionState.PhysicallyDeleted);
        await Assert.That((await secondTask).State)
            .IsEqualTo(UserDeletionState.LastAdministratorProtected);
        await using var verification = new NoCtfDbContext(options);
        await AssertActiveHumanAdministratorCountAsync(verification, 1, ct);
        var survivor = await verification.Users.AsNoTracking()
            .SingleAsync(user => user.Id == firstId, ct);
        await Assert.That(survivor.TokenVersion).IsEqualTo(23);
        await AssertLifecycleFactAsync(
            verification,
            secondId,
            UserAccountLifecycleAction.PhysicallyDeleted,
            ct);
    }

    private static async Task DowngradeBothAsync(
        DbContextOptions<NoCtfDbContext> options,
        string connectionString,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var firstId = Guid.CreateVersion7(now);
        var secondId = Guid.CreateVersion7(now.AddTicks(1));
        await ResetAndSeedAsync(
            options,
            [
                User(firstId, "downgrade-first", UserKind.Human, UserRole.Administrator, 31, now),
                User(secondId, "downgrade-second", UserKind.Human, UserRole.Administrator, 37, now)
            ],
            ct);

        await using var mutationGate = await AcquireMutationGateAsync(connectionString, ct);
        var firstTask = UpdateRoleAsync(
            options,
            connectionString,
            firstId,
            now.AddMinutes(1),
            ct);
        await WaitForBlockedMutationsAsync(options, 1, ct);
        var secondTask = UpdateRoleAsync(
            options,
            connectionString,
            secondId,
            now.AddMinutes(1),
            ct);
        await WaitForBlockedMutationsAsync(options, 2, ct);
        await ReleaseMutationGateAsync(mutationGate, ct);
        await Task.WhenAll(firstTask, secondTask);

        await Assert.That((await firstTask).State).IsEqualTo(UpdatePlatformRoleState.Updated);
        await Assert.That((await secondTask).State)
            .IsEqualTo(UpdatePlatformRoleState.LastAdministratorProtected);
        await using var verification = new NoCtfDbContext(options);
        await AssertActiveHumanAdministratorCountAsync(verification, 1, ct);
        var users = await verification.Users.AsNoTracking().ToArrayAsync(ct);
        await Assert.That(users.Single(user => user.Id == firstId).TokenVersion).IsEqualTo(32);
        await Assert.That(users.Single(user => user.Id == secondId).TokenVersion).IsEqualTo(37);
        await Assert.That(await verification.Notifications.CountAsync(
            notification => notification.Kind == NotificationKind.UserAccountLifecycleChanged,
            ct)).IsEqualTo(0);
    }

    private static async Task BotDoesNotSatisfyInvariantAsync(
        DbContextOptions<NoCtfDbContext> options,
        string connectionString,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var administratorId = Guid.CreateVersion7(now);
        var botId = Guid.CreateVersion7(now.AddTicks(1));
        var actorId = Guid.CreateVersion7(now.AddTicks(2));
        await ResetAndSeedAsync(
            options,
            [
                User(administratorId, "only-human-admin", UserKind.Human, UserRole.Administrator, 41, now),
                User(botId, "organizer-bot", UserKind.Bot, UserRole.Organizer, 43, now),
                User(actorId, "ordinary-actor", UserKind.Human, UserRole.User, 47, now)
            ],
            ct,
            createDelayTrigger: false);

        await using (var promoteDb = new NoCtfDbContext(options))
        {
            var promoted = await new PlatformAdministrationStore(promoteDb, new PasswordHasher<User>())
                .UpdateRoleAsync(botId, UserRole.Administrator, now.AddSeconds(30), ct);
            await Assert.That(promoted.State).IsEqualTo(UpdatePlatformRoleState.Updated);
        }

        var downgrade = await UpdateRoleAsync(
            options,
            connectionString,
            administratorId,
            now.AddMinutes(1),
            ct);
        var deletion = await DeleteAsync(
            options,
            administratorId,
            actorId,
            UserDeletionMode.HardDelete,
            now.AddMinutes(2),
            ct);

        await Assert.That(downgrade.State)
            .IsEqualTo(UpdatePlatformRoleState.LastAdministratorProtected);
        await Assert.That(deletion.State).IsEqualTo(UserDeletionState.LastAdministratorProtected);
        await using var verification = new NoCtfDbContext(options);
        await AssertActiveHumanAdministratorCountAsync(verification, 1, ct);
        var administrator = await verification.Users.AsNoTracking()
            .SingleAsync(user => user.Id == administratorId, ct);
        await Assert.That(administrator.TokenVersion).IsEqualTo(41);
        await Assert.That(await verification.Notifications.CountAsync(
            notification => notification.Kind == NotificationKind.UserAccountLifecycleChanged,
            ct)).IsEqualTo(0);
    }

    private static async Task ResetAndSeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        IReadOnlyList<User> users,
        CancellationToken ct,
        bool createDelayTrigger = true)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS noctf_delay_admin_mutation ON users;",
            ct);
        await db.Database.ExecuteSqlRawAsync(
            "DROP FUNCTION IF EXISTS noctf_delay_admin_mutation();",
            ct);
        await db.Notifications.ExecuteDeleteAsync(ct);
        await db.AccountTokens.ExecuteDeleteAsync(ct);
        await db.Users.ExecuteDeleteAsync(ct);
        db.Users.AddRange(users);
        await db.SaveChangesAsync(ct);
        if (!createDelayTrigger)
            return;

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION noctf_delay_admin_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('noctf-last-administrator-concurrency-gate', 0));
                    RETURN OLD;
                END IF;
                IF NEW.role IS DISTINCT FROM OLD.role
                    OR NEW.account_status IS DISTINCT FROM OLD.account_status THEN
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('noctf-last-administrator-concurrency-gate', 0));
                END IF;
                RETURN NEW;
            END;
            $$;
            """,
            ct);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER noctf_delay_admin_mutation
            BEFORE UPDATE OR DELETE ON users
            FOR EACH ROW
            EXECUTE FUNCTION noctf_delay_admin_mutation();
            """,
            ct);
    }

    private static async Task<UpdatePlatformRoleResult> UpdateRoleAsync(
        DbContextOptions<NoCtfDbContext> options,
        string connectionString,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        return await new PlatformAdministrationStore(
                db,
                new PasswordHasher<User>())
            .UpdateRoleAsync(userId, UserRole.User, now, ct);
    }

    private static async Task<UserDeletionStoreResult> DeleteAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid userId,
        Guid actorUserId,
        UserDeletionMode mode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        return await new UserAccountAdministrationStore(db).DeleteAsync(
            userId,
            actorUserId,
            mode,
            "Concurrent administrator mutation",
            now,
            ct);
    }

    private static async Task<NpgsqlConnection> AcquireMutationGateAsync(
        string connectionString,
        CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT pg_advisory_lock(hashtextextended('noctf-last-administrator-concurrency-gate', 0))";
        await command.ExecuteNonQueryAsync(ct);
        return connection;
    }

    private static async Task ReleaseMutationGateAsync(
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT pg_advisory_unlock(hashtextextended('noctf-last-administrator-concurrency-gate', 0))";
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new InvalidOperationException("The administrator mutation gate was not held.");
    }

    private static async Task WaitForBlockedMutationsAsync(
        DbContextOptions<NoCtfDbContext> options,
        int expectedCount,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var blockedMutations = await db.Database.SqlQuery<int>(
                    $"""
                     SELECT count(*)::integer AS "Value"
                     FROM pg_stat_activity
                     WHERE datname = current_database() AND wait_event_type = 'Lock'
                     """)
                .SingleAsync(ct);
            if (blockedMutations >= expectedCount)
                return;
            await Task.Delay(50, ct);
        }

        throw new TimeoutException(
            $"Expected {expectedCount} administrator mutation(s) to wait on PostgreSQL locks.");
    }

    private static async Task AssertActiveHumanAdministratorCountAsync(
        NoCtfDbContext db,
        int expected,
        CancellationToken ct)
    {
        var count = await db.Users.AsNoTracking().CountAsync(user =>
            user.Kind == UserKind.Human
            && user.Role == UserRole.Administrator
            && user.AccountStatus == UserAccountStatus.Active,
            ct);
        await Assert.That(count).IsEqualTo(expected);
    }

    private static async Task AssertLifecycleFactAsync(
        NoCtfDbContext db,
        Guid targetUserId,
        UserAccountLifecycleAction expectedAction,
        CancellationToken ct)
    {
        var facts = await db.Notifications.AsNoTracking()
            .Where(notification => notification.Kind == NotificationKind.UserAccountLifecycleChanged)
            .Select(notification => new
            {
                notification.UserId,
                notification.UserLifecycleAction
            })
            .ToArrayAsync(ct);
        await Assert.That(facts).Count().IsEqualTo(1);
        await Assert.That(facts[0].UserId).IsEqualTo(targetUserId);
        await Assert.That(facts[0].UserLifecycleAction).IsEqualTo(expectedAction);
    }

    private static User User(
        Guid id,
        string userName,
        UserKind kind,
        UserRole role,
        int tokenVersion,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            PasswordHash = "test",
            Kind = kind,
            Role = role,
            AccountStatus = UserAccountStatus.Active,
            TokenVersion = tokenVersion,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
}
