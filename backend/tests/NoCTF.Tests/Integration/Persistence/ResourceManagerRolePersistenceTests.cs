using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Competitions.Permissions;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ResourceManagerRolePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_administrator_downgrades_preserve_one_active_human_administrator(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_last_administrator",
                cancellationToken);
            var options = Options(postgres);
            var now = DateTimeOffset.UtcNow;
            var firstAdministratorId = Guid.CreateVersion7();
            var secondAdministratorId = Guid.CreateVersion7();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.MigrateAsync(cancellationToken);
                setup.Users.AddRange(
                    User(firstAdministratorId, "first-admin", UserRole.Administrator, now),
                    User(secondAdministratorId, "second-admin", UserRole.Administrator, now));
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using var firstDeadLetters =
                new WolverineProcessDeadLetters(postgres.GetConnectionString());
            await using var secondDeadLetters =
                new WolverineProcessDeadLetters(postgres.GetConnectionString());
            await using var firstDb = new NoCtfDbContext(options);
            await using var secondDb = new NoCtfDbContext(options);
            var firstTask = new PlatformAdministrationStore(
                    firstDb,
                    firstDeadLetters,
                    new PasswordHasher<User>())
                .UpdateRoleAsync(
                    firstAdministratorId,
                    UserRole.User,
                    now.AddMinutes(1),
                    cancellationToken);
            var secondTask = new PlatformAdministrationStore(
                    secondDb,
                    secondDeadLetters,
                    new PasswordHasher<User>())
                .UpdateRoleAsync(
                    secondAdministratorId,
                    UserRole.User,
                    now.AddMinutes(1),
                    cancellationToken);

            var results = await Task.WhenAll(firstTask, secondTask);

            await Assert.That(results.Count(result =>
                    result.State == UpdatePlatformRoleState.Updated))
                .IsEqualTo(1);
            await Assert.That(results.Count(result =>
                    result.State == UpdatePlatformRoleState.LastAdministratorProtected))
                .IsEqualTo(1);
            await using var verification = new NoCtfDbContext(options);
            var users = await verification.Users.AsNoTracking()
                .OrderBy(user => user.Id)
                .ToArrayAsync(cancellationToken);
            await Assert.That(users.Count(user =>
                    user.Kind == UserKind.Human
                    && user.Role == UserRole.Administrator
                    && user.AccountStatus == UserAccountStatus.Active))
                .IsEqualTo(1);
            await Assert.That(users.Count(user =>
                    user.Role == UserRole.User && user.TokenVersion == 1))
                .IsEqualTo(1);
            await Assert.That(users.Count(user =>
                    user.Role == UserRole.Administrator && user.TokenVersion == 0))
                .IsEqualTo(1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Downgrade_reports_active_blockers_and_restore_revalidates_roles(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_role_blockers",
                cancellationToken);
            var options = Options(postgres);
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var administratorId = Guid.CreateVersion7();
            var targetId = Guid.CreateVersion7();
            var otherOrganizerId = Guid.CreateVersion7();
            var ordinaryUserId = Guid.CreateVersion7();
            db.Users.AddRange(
                User(administratorId, "blocker-admin", UserRole.Administrator, now),
                User(targetId, "blocker-target", UserRole.Organizer, now, tokenVersion: 7),
                User(otherOrganizerId, "blocker-organizer", UserRole.Organizer, now),
                User(ordinaryUserId, "blocker-user", UserRole.User, now));

            var ownedCompetitionId = Guid.CreateVersion7();
            var managedCompetitionId = Guid.CreateVersion7();
            var deletedCompetitionId = Guid.CreateVersion7();
            db.Competitions.AddRange(
                Competition(ownedCompetitionId, targetId, now),
                Competition(
                    managedCompetitionId,
                    administratorId,
                    now,
                    managerIds: [targetId]),
                Competition(
                    deletedCompetitionId,
                    targetId,
                    now,
                    managerIds: [otherOrganizerId],
                    judgeIds: [ordinaryUserId],
                    deletedAt: now));

            var ownedChallengeId = Guid.CreateVersion7();
            var managedChallengeId = Guid.CreateVersion7();
            var deletedChallengeId = Guid.CreateVersion7();
            db.Challenges.AddRange(
                Challenge(ownedChallengeId, targetId, now),
                Challenge(
                    managedChallengeId,
                    administratorId,
                    now,
                    managerIds: [targetId]),
                Challenge(
                    deletedChallengeId,
                    targetId,
                    now,
                    managerIds: [otherOrganizerId],
                    deletedAt: now));
            await db.SaveChangesAsync(cancellationToken);

            await using var deadLetters =
                new WolverineProcessDeadLetters(postgres.GetConnectionString());
            var platform = new PlatformAdministrationStore(
                db,
                deadLetters,
                new PasswordHasher<User>());
            var blocked = await platform.UpdateRoleAsync(
                targetId,
                UserRole.User,
                now.AddMinutes(1),
                cancellationToken);

            await Assert.That(blocked.State)
                .IsEqualTo(UpdatePlatformRoleState.ActiveOwnerOrManagerAssignments);
            await Assert.That(blocked.Blockers!.CompetitionIds.SequenceEqual(
                    new[] { ownedCompetitionId, managedCompetitionId }.Order()))
                .IsTrue();
            await Assert.That(blocked.Blockers.ChallengeIds.SequenceEqual(
                    new[] { ownedChallengeId, managedChallengeId }.Order()))
                .IsTrue();
            db.ChangeTracker.Clear();
            var unchangedUser = await db.Users.AsNoTracking()
                .SingleAsync(user => user.Id == targetId, cancellationToken);
            await Assert.That(unchangedUser.Role).IsEqualTo(UserRole.Organizer);
            await Assert.That(unchangedUser.TokenVersion).IsEqualTo(7);

            var activeCompetitionIds = new[] { ownedCompetitionId, managedCompetitionId };
            await db.Competitions
                .Where(competition => activeCompetitionIds.Contains(competition.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        competition => competition.DeletedAt,
                        now.AddMinutes(2)),
                    cancellationToken);
            var activeChallengeIds = new[] { ownedChallengeId, managedChallengeId };
            await db.Challenges
                .Where(challenge => activeChallengeIds.Contains(challenge.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        challenge => challenge.DeletedAt,
                        now.AddMinutes(2)),
                    cancellationToken);

            var downgraded = await platform.UpdateRoleAsync(
                targetId,
                UserRole.User,
                now.AddMinutes(3),
                cancellationToken);
            await Assert.That(downgraded.State).IsEqualTo(UpdatePlatformRoleState.Updated);
            await Assert.That(downgraded.User!.Role).IsEqualTo(UserRole.User);
            await Assert.That(downgraded.User.TokenVersion).IsEqualTo(8);

            var managerRestore = await new AdminCompetitionStore(db).RestoreAsync(
                deletedCompetitionId,
                otherOrganizerId,
                false,
                now.AddMinutes(4),
                cancellationToken);
            await Assert.That(managerRestore.State).IsEqualTo(CompetitionRestoreState.NotFound);
            await Assert.That((await new AdminCompetitionStore(db).HardDeleteAsync(
                    deletedCompetitionId,
                    ordinaryUserId,
                    false,
                    cancellationToken)).State)
                .IsEqualTo(CompetitionHardDeleteState.NotFound);

            var competitionRestore = await new AdminCompetitionStore(db).RestoreAsync(
                deletedCompetitionId,
                administratorId,
                true,
                now.AddMinutes(4),
                cancellationToken);
            var challengeRestore = await new ChallengeBankStore(db).RestoreAsync(
                deletedChallengeId,
                administratorId,
                true,
                now.AddMinutes(4),
                cancellationToken);
            await Assert.That(competitionRestore.State)
                .IsEqualTo(CompetitionRestoreState.RoleNotEligible);
            await Assert.That(competitionRestore.UserIds)
                .IsEquivalentTo([targetId]);
            await Assert.That(challengeRestore.State)
                .IsEqualTo(ChallengeTemplateWriteState.RoleNotEligible);
            await Assert.That(challengeRestore.UserIds)
                .IsEquivalentTo([targetId]);

            var upgraded = await platform.UpdateRoleAsync(
                targetId,
                UserRole.Organizer,
                now.AddMinutes(5),
                cancellationToken);
            await Assert.That(upgraded.State).IsEqualTo(UpdatePlatformRoleState.Updated);
            await Assert.That(upgraded.User!.TokenVersion).IsEqualTo(9);
            await Assert.That((await new AdminCompetitionStore(db).RestoreAsync(
                    deletedCompetitionId,
                    administratorId,
                    true,
                    now.AddMinutes(6),
                    cancellationToken)).State)
                .IsEqualTo(CompetitionRestoreState.Restored);
            await Assert.That((await new ChallengeBankStore(db).RestoreAsync(
                    deletedChallengeId,
                    administratorId,
                    true,
                    now.AddMinutes(6),
                    cancellationToken)).State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Resource_manager_writes_reject_missing_and_ineligible_users(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_role_eligibility",
                cancellationToken);
            var options = Options(postgres);
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var administratorId = Guid.CreateVersion7();
            var organizerId = Guid.CreateVersion7();
            var ordinaryUserId = Guid.CreateVersion7();
            var missingUserId = Guid.CreateVersion7();
            db.Users.AddRange(
                User(administratorId, "eligibility-admin", UserRole.Administrator, now),
                User(organizerId, "eligibility-organizer", UserRole.Organizer, now),
                User(ordinaryUserId, "eligibility-user", UserRole.User, now));

            var competitionId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            db.Competitions.Add(Competition(competitionId, administratorId, now));
            db.Challenges.Add(Challenge(challengeId, administratorId, now));
            await db.SaveChangesAsync(cancellationToken);

            var rejectedCompetition = await new CompetitionManagementStore(db).CreateAsync(
                new(
                    "Rejected owner",
                    null,
                    GameMode.Ctf,
                    now.AddHours(1),
                    now.AddHours(2),
                    true,
                    5,
                    0,
                    ordinaryUserId,
                    now),
                cancellationToken);
            await Assert.That(rejectedCompetition.State)
                .IsEqualTo(CompetitionCreationState.RoleNotEligible);

            var rejectedChallengeId = Guid.CreateVersion7();
            var rejectedChallenge = await new ChallengeBankStore(db).CreateAsync(
                new(
                    rejectedChallengeId,
                    ordinaryUserId,
                    GameMode.Ctf,
                    ChallengeVisibility.Private,
                    "Rejected challenge owner",
                    null,
                    "Web",
                    """{"schemaVersion":1}""",
                    now),
                cancellationToken);
            await Assert.That(rejectedChallenge.State)
                .IsEqualTo(ChallengeTemplateWriteState.RoleNotEligible);
            await Assert.That(await db.Challenges.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(
                        challenge => challenge.Id == rejectedChallengeId,
                        cancellationToken))
                .IsFalse();

            var competitionPermissions = new CompetitionPermissionStore(db);
            var rejectedManager = await competitionPermissions.UpdateAsync(
                new(
                    competitionId,
                    administratorId,
                    [ordinaryUserId],
                    [],
                    [],
                    0),
                cancellationToken);
            await Assert.That(rejectedManager.State)
                .IsEqualTo(CompetitionPermissionUpdateState.RoleNotEligible);
            await Assert.That(rejectedManager.UserIds).IsEquivalentTo([ordinaryUserId]);

            var acceptedJudge = await competitionPermissions.UpdateAsync(
                new(
                    competitionId,
                    administratorId,
                    [organizerId],
                    [ordinaryUserId],
                    [],
                    0),
                cancellationToken);
            await Assert.That(acceptedJudge.State)
                .IsEqualTo(CompetitionPermissionUpdateState.Updated);

            var competitionTransfer = await new AdminCompetitionStore(db).TransferOwnerAsync(
                competitionId,
                administratorId,
                true,
                ordinaryUserId,
                now.AddMinutes(1),
                cancellationToken);
            await Assert.That(competitionTransfer.State)
                .IsEqualTo(CompetitionOwnerTransferState.RoleNotEligible);

            var challengeStore = new ChallengeBankStore(db);
            var challengePermission = await challengeStore.UpdatePermissionsAsync(
                challengeId,
                administratorId,
                true,
                [ordinaryUserId],
                0,
                now.AddMinutes(1),
                cancellationToken);
            await Assert.That(challengePermission.State)
                .IsEqualTo(ChallengeTemplateWriteState.RoleNotEligible);
            var missingManager = await challengeStore.UpdatePermissionsAsync(
                challengeId,
                administratorId,
                true,
                [missingUserId],
                0,
                now.AddMinutes(1),
                cancellationToken);
            await Assert.That(missingManager.State)
                .IsEqualTo(ChallengeTemplateWriteState.UserNotFound);
            await Assert.That(missingManager.UserIds).IsEquivalentTo([missingUserId]);
            var challengeTransfer = await challengeStore.TransferOwnerAsync(
                challengeId,
                administratorId,
                true,
                ordinaryUserId,
                0,
                now.AddMinutes(1),
                cancellationToken);
            await Assert.That(challengeTransfer.State)
                .IsEqualTo(ChallengeTemplateWriteState.RoleNotEligible);
            var missingOwner = await challengeStore.TransferOwnerAsync(
                challengeId,
                administratorId,
                true,
                missingUserId,
                0,
                now.AddMinutes(1),
                cancellationToken);
            await Assert.That(missingOwner.State)
                .IsEqualTo(ChallengeTemplateWriteState.UserNotFound);

            db.ChangeTracker.Clear();
            var persistedCompetition = await db.Competitions.AsNoTracking()
                .SingleAsync(
                    competition => competition.Id == competitionId,
                    cancellationToken);
            var persistedChallenge = await db.Challenges.AsNoTracking()
                .SingleAsync(challenge => challenge.Id == challengeId, cancellationToken);
            await Assert.That(persistedCompetition.OwnerId).IsEqualTo(administratorId);
            await Assert.That(persistedCompetition.ManagerIds)
                .IsEquivalentTo([organizerId]);
            await Assert.That(persistedCompetition.JudgeIds)
                .IsEquivalentTo([ordinaryUserId]);
            await Assert.That(persistedChallenge.OwnerId).IsEqualTo(administratorId);
            await Assert.That(persistedChallenge.ManagerIds).IsEmpty();
            await Assert.That(persistedChallenge.Revision).IsEqualTo(0);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Downgrade_and_manager_assignment_are_serialized(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_role_concurrency",
                cancellationToken);
            var options = Options(postgres);
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var administratorId = Guid.CreateVersion7();
            var targetId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            db.Users.AddRange(
                User(administratorId, "concurrency-admin", UserRole.Administrator, now),
                User(targetId, "concurrency-target", UserRole.Organizer, now));
            db.Competitions.Add(Competition(competitionId, administratorId, now));
            await db.SaveChangesAsync(cancellationToken);

            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION noctf_delay_user_role_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.role IS DISTINCT FROM OLD.role THEN
                        PERFORM pg_sleep(1);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """,
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER noctf_delay_user_role_update
                BEFORE UPDATE ON users
                FOR EACH ROW
                EXECUTE FUNCTION noctf_delay_user_role_update();
                """,
                cancellationToken);

            await using var deadLetters =
                new WolverineProcessDeadLetters(postgres.GetConnectionString());
            var passwordHasher = new PasswordHasher<User>();
            await using var downgradeDb = new NoCtfDbContext(options);
            await using var assignmentDb = new NoCtfDbContext(options);
            var downgradeTask = new PlatformAdministrationStore(
                    downgradeDb,
                    deadLetters,
                    passwordHasher)
                .UpdateRoleAsync(
                    targetId,
                    UserRole.User,
                    now.AddMinutes(1),
                    cancellationToken);
            await WaitForPostgresSleepAsync(db, cancellationToken);
            var assignmentTask = new CompetitionPermissionStore(assignmentDb).UpdateAsync(
                new(
                    competitionId,
                    administratorId,
                    [targetId],
                    [],
                    [],
                    0),
                cancellationToken);
            await Task.WhenAll(downgradeTask, assignmentTask);

            await Assert.That((await downgradeTask).State)
                .IsEqualTo(UpdatePlatformRoleState.Updated);
            await Assert.That((await assignmentTask).State)
                .IsEqualTo(CompetitionPermissionUpdateState.RevisionConflict);
            db.ChangeTracker.Clear();
            await Assert.That((await db.Users.AsNoTracking()
                    .SingleAsync(user => user.Id == targetId, cancellationToken)).Role)
                .IsEqualTo(UserRole.User);
            await Assert.That((await db.Competitions.AsNoTracking()
                    .SingleAsync(
                        competition => competition.Id == competitionId,
                        cancellationToken)).ManagerIds)
                .IsEmpty();

            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER noctf_delay_user_role_update ON users;",
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "DROP FUNCTION noctf_delay_user_role_update();",
                cancellationToken);
            await db.Users
                .Where(user => user.Id == targetId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(user => user.Role, UserRole.Organizer)
                        .SetProperty(user => user.UpdatedAt, now.AddMinutes(2)),
                    cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION noctf_delay_competition_manager_update()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.manager_ids IS DISTINCT FROM OLD.manager_ids THEN
                        PERFORM pg_sleep(1);
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """,
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER noctf_delay_competition_manager_update
                BEFORE UPDATE ON competitions
                FOR EACH ROW
                EXECUTE FUNCTION noctf_delay_competition_manager_update();
                """,
                cancellationToken);

            await using var assignmentFirstDb = new NoCtfDbContext(options);
            await using var downgradeSecondDb = new NoCtfDbContext(options);
            var assignmentFirstTask =
                new CompetitionPermissionStore(assignmentFirstDb).UpdateAsync(
                    new(
                        competitionId,
                        administratorId,
                        [targetId],
                        [],
                        [],
                        0),
                    cancellationToken);
            await WaitForPostgresSleepAsync(db, cancellationToken);
            var downgradeSecondTask = new PlatformAdministrationStore(
                    downgradeSecondDb,
                    deadLetters,
                    passwordHasher)
                .UpdateRoleAsync(
                    targetId,
                    UserRole.User,
                    now.AddMinutes(3),
                    cancellationToken);
            await Task.WhenAll(assignmentFirstTask, downgradeSecondTask);

            var assignmentFirst = await assignmentFirstTask;
            var blockedDowngrade = await downgradeSecondTask;
            db.ChangeTracker.Clear();
            var finalUser = await db.Users.AsNoTracking()
                .SingleAsync(user => user.Id == targetId, cancellationToken);
            var finalCompetition = await db.Competitions.AsNoTracking()
                .SingleAsync(
                    competition => competition.Id == competitionId,
                    cancellationToken);
            if (assignmentFirst.State == CompetitionPermissionUpdateState.Updated)
            {
                await Assert.That(blockedDowngrade.State)
                    .IsEqualTo(UpdatePlatformRoleState.ActiveOwnerOrManagerAssignments);
                await Assert.That(blockedDowngrade.Blockers!.CompetitionIds)
                    .IsEquivalentTo([competitionId]);
                await Assert.That(finalUser.Role).IsEqualTo(UserRole.Organizer);
                await Assert.That(finalCompetition.ManagerIds)
                    .IsEquivalentTo([targetId]);
            }
            else
            {
                await Assert.That(assignmentFirst.State)
                    .IsEqualTo(CompetitionPermissionUpdateState.RevisionConflict);
                await Assert.That(blockedDowngrade.State)
                    .IsEqualTo(UpdatePlatformRoleState.Updated);
                await Assert.That(finalUser.Role).IsEqualTo(UserRole.User);
                await Assert.That(finalCompetition.ManagerIds).IsEmpty();
            }
        });
    }

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        string database,
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private static User User(
        Guid id,
        string userName,
        UserRole role,
        DateTimeOffset now,
        int tokenVersion = 0) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            TokenVersion = tokenVersion,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        DateTimeOffset now,
        Guid[]? managerIds = null,
        Guid[]? judgeIds = null,
        DateTimeOffset? deletedAt = null) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            ManagerIds = managerIds ?? [],
            JudgeIds = judgeIds ?? [],
            Title = $"Competition {id:N}",
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            DeletedAt = deletedAt,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Challenge Challenge(
        Guid id,
        Guid ownerId,
        DateTimeOffset now,
        Guid[]? managerIds = null,
        DateTimeOffset? deletedAt = null) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            ManagerIds = managerIds ?? [],
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = $"Challenge {id:N}",
            Direction = "Web",
            DefinitionJson = """{"schemaVersion":1}""",
            DeletedAt = deletedAt,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static async Task WaitForPostgresSleepAsync(
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var sleepers = await db.Database.SqlQuery<int>(
                    $"""
                     SELECT count(*)::integer AS "Value"
                     FROM pg_stat_activity
                     WHERE datname = current_database() AND wait_event = 'PgSleep'
                     """)
                .SingleAsync(cancellationToken);
            if (sleepers > 0)
                return;
            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException("The PostgreSQL delay trigger did not enter pg_sleep.");
    }
}
