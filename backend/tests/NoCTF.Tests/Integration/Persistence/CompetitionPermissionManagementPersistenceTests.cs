using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Competitions.Permissions;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionPermissionManagementPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Owner_and_administrator_read_complete_snapshot_only(
        CancellationToken cancellationToken)
    {
        await RunAsync(
            "noctf_permission_snapshot",
            async options =>
            {
                var now = DateTimeOffset.UtcNow;
                var ownerId = Guid.CreateVersion7();
                var administratorId = Guid.CreateVersion7();
                var managerId = Guid.CreateVersion7();
                var judgeId = Guid.CreateVersion7();
                var observerId = Guid.CreateVersion7();
                var unrelatedId = Guid.CreateVersion7();
                var competitionId = Guid.CreateVersion7();
                await SeedAsync(
                    options,
                    Competition(
                        competitionId,
                        ownerId,
                        now,
                        managerIds: [managerId],
                        judgeIds: [judgeId],
                        observerIds: [observerId],
                        permissionRevision: 7),
                    [
                        Human(ownerId, "snapshot-owner", UserRole.Organizer, true, now),
                        Human(
                            administratorId,
                            "snapshot-administrator",
                            UserRole.Administrator,
                            true,
                            now),
                        Human(managerId, "snapshot-manager", UserRole.Organizer, true, now),
                        Human(judgeId, "snapshot-judge", UserRole.User, true, now),
                        Human(observerId, "snapshot-observer", UserRole.User, true, now),
                        Human(unrelatedId, "snapshot-unrelated", UserRole.User, true, now)
                    ],
                    cancellationToken);

                await using var db = new NoCtfDbContext(options);
                var store = new CompetitionPermissionStore(db);
                var owner = await store.GetSnapshotAsync(
                    competitionId,
                    ownerId,
                    false,
                    cancellationToken);
                var administrator = await store.GetSnapshotAsync(
                    competitionId,
                    administratorId,
                    true,
                    cancellationToken);

                await Assert.That(owner.State)
                    .IsEqualTo(CompetitionPermissionSnapshotState.Found);
                await Assert.That(administrator.State)
                    .IsEqualTo(CompetitionPermissionSnapshotState.Found);
                await AssertSnapshotAsync(
                    owner.Snapshot!,
                    ownerId,
                    [managerId],
                    [judgeId],
                    [observerId],
                    7);
                await AssertSnapshotAsync(
                    administrator.Snapshot!,
                    ownerId,
                    [managerId],
                    [judgeId],
                    [observerId],
                    7);

                foreach (var actorId in new[]
                         {
                             managerId,
                             judgeId,
                             observerId,
                             unrelatedId
                         })
                {
                    var forbidden = await store.GetSnapshotAsync(
                        competitionId,
                        actorId,
                        false,
                        cancellationToken);
                    await Assert.That(forbidden.State)
                        .IsEqualTo(CompetitionPermissionSnapshotState.Forbidden);
                    await Assert.That(forbidden.Snapshot).IsNull();
                }

                var missing = await store.GetSnapshotAsync(
                    Guid.CreateVersion7(),
                    ownerId,
                    false,
                    cancellationToken);
                await Assert.That(missing.State)
                    .IsEqualTo(CompetitionPermissionSnapshotState.NotFound);
                await Assert.That(missing.Snapshot).IsNull();
            },
            cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Candidate_list_is_minimal_eligible_and_owner_or_administrator_only(
        CancellationToken cancellationToken)
    {
        await RunAsync(
            "noctf_permission_candidates",
            async options =>
            {
                var now = DateTimeOffset.UtcNow;
                var ownerId = Guid.CreateVersion7();
                var administratorId = Guid.CreateVersion7();
                var organizerBotId = Guid.CreateVersion7();
                var verifiedJudgeId = Guid.CreateVersion7();
                var verifiedObserverId = Guid.CreateVersion7();
                var unrelatedVerifiedId = Guid.CreateVersion7();
                var unverifiedUserId = Guid.CreateVersion7();
                var ineligibleBotId = Guid.CreateVersion7();
                var competitionId = Guid.CreateVersion7();
                await SeedAsync(
                    options,
                    Competition(
                        competitionId,
                        ownerId,
                        now,
                        managerIds: [organizerBotId],
                        judgeIds: [verifiedJudgeId],
                        observerIds: [verifiedObserverId]),
                    [
                        Human(ownerId, "candidate-owner", UserRole.Organizer, true, now),
                        Human(
                            administratorId,
                            "candidate-administrator",
                            UserRole.Administrator,
                            true,
                            now),
                        Bot(organizerBotId, "candidate-organizer-bot", UserRole.Organizer, now),
                        Human(
                            verifiedJudgeId,
                            "candidate-verified-judge",
                            UserRole.User,
                            true,
                            now),
                        Human(
                            verifiedObserverId,
                            "candidate-verified-observer",
                            UserRole.User,
                            true,
                            now),
                        Human(
                            unrelatedVerifiedId,
                            "candidate-unrelated-verified",
                            UserRole.User,
                            true,
                            now),
                        Human(
                            unverifiedUserId,
                            "candidate-unverified",
                            UserRole.User,
                            false,
                            now),
                        Bot(ineligibleBotId, "candidate-ineligible-bot", UserRole.User, now)
                    ],
                    cancellationToken);

                await using var db = new NoCtfDbContext(options);
                var store = new CompetitionPermissionStore(db);
                var owner = await store.ListCandidatesAsync(
                    competitionId,
                    ownerId,
                    false,
                    cancellationToken);
                var administrator = await store.ListCandidatesAsync(
                    competitionId,
                    administratorId,
                    true,
                    cancellationToken);

                await Assert.That(owner.State)
                    .IsEqualTo(CompetitionPermissionCandidateListState.Listed);
                await Assert.That(administrator.State)
                    .IsEqualTo(CompetitionPermissionCandidateListState.Listed);
                var expected = new[]
                {
                    new Candidate(
                        administratorId,
                        "candidate-administrator",
                        UserKind.Human,
                        UserRole.Administrator,
                        true),
                    new Candidate(
                        organizerBotId,
                        "candidate-organizer-bot",
                        UserKind.Bot,
                        UserRole.Organizer,
                        false),
                    new Candidate(
                        verifiedJudgeId,
                        "candidate-verified-judge",
                        UserKind.Human,
                        UserRole.User,
                        true),
                    new Candidate(
                        verifiedObserverId,
                        "candidate-verified-observer",
                        UserKind.Human,
                        UserRole.User,
                        true),
                    new Candidate(
                        unrelatedVerifiedId,
                        "candidate-unrelated-verified",
                        UserKind.Human,
                        UserRole.User,
                        true)
                };
                await AssertCandidatesAsync(owner.Candidates!, expected);
                await AssertCandidatesAsync(administrator.Candidates!, expected);

                foreach (var actorId in new[]
                         {
                             organizerBotId,
                             verifiedJudgeId,
                             verifiedObserverId,
                             unrelatedVerifiedId
                         })
                {
                    var forbidden = await store.ListCandidatesAsync(
                        competitionId,
                        actorId,
                        false,
                        cancellationToken);
                    await Assert.That(forbidden.State)
                        .IsEqualTo(CompetitionPermissionCandidateListState.Forbidden);
                    await Assert.That(forbidden.Candidates).IsNull();
                }

                var missing = await store.ListCandidatesAsync(
                    Guid.CreateVersion7(),
                    ownerId,
                    false,
                    cancellationToken);
                await Assert.That(missing.State)
                    .IsEqualTo(CompetitionPermissionCandidateListState.NotFound);
                await Assert.That(missing.Candidates).IsNull();
            },
            cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_full_replacements_accept_exactly_one_expected_revision(
        CancellationToken cancellationToken)
    {
        await RunAsync(
            "noctf_permission_revision",
            async options =>
            {
                var now = DateTimeOffset.UtcNow;
                var ownerId = Guid.CreateVersion7();
                var firstManagerId = Guid.CreateVersion7();
                var secondManagerId = Guid.CreateVersion7();
                var competitionId = Guid.CreateVersion7();
                await SeedAsync(
                    options,
                    Competition(
                        competitionId,
                        ownerId,
                        now,
                        permissionRevision: 5),
                    [
                        Human(ownerId, "revision-owner", UserRole.Organizer, true, now),
                        Human(
                            firstManagerId,
                            "revision-first-manager",
                            UserRole.Organizer,
                            true,
                            now),
                        Human(
                            secondManagerId,
                            "revision-second-manager",
                            UserRole.Organizer,
                            true,
                            now)
                    ],
                    cancellationToken);

                await using var firstDb = new NoCtfDbContext(options);
                await using var secondDb = new NoCtfDbContext(options);
                var first = new CompetitionPermissionStore(firstDb).UpdateAsync(
                    new UpdateCompetitionPermissionsCommand(
                        CompetitionId: competitionId,
                        ActorId: ownerId,
                        ExpectedPermissionRevision: 5,
                        ManagerIds: [firstManagerId],
                        JudgeIds: [],
                        ObserverIds: []),
                    cancellationToken);
                var second = new CompetitionPermissionStore(secondDb).UpdateAsync(
                    new UpdateCompetitionPermissionsCommand(
                        CompetitionId: competitionId,
                        ActorId: ownerId,
                        ExpectedPermissionRevision: 5,
                        ManagerIds: [secondManagerId],
                        JudgeIds: [],
                        ObserverIds: []),
                    cancellationToken);
                var results = await Task.WhenAll(first, second);

                await Assert.That(results.Count(result =>
                        result.State == CompetitionPermissionUpdateState.Updated))
                    .IsEqualTo(1);
                await Assert.That(results.Count(result =>
                        result.State == CompetitionPermissionUpdateState.RevisionConflict))
                    .IsEqualTo(1);
                await using var verifyDb = new NoCtfDbContext(options);
                var persisted = await verifyDb.Competitions.AsNoTracking()
                    .SingleAsync(
                        competition => competition.Id == competitionId,
                        cancellationToken);
                await Assert.That(persisted.PermissionRevision).IsEqualTo(6);
                await Assert.That(
                        persisted.ManagerIds.SequenceEqual([firstManagerId])
                        || persisted.ManagerIds.SequenceEqual([secondManagerId]))
                    .IsTrue();
                await Assert.That(persisted.JudgeIds).IsEmpty();
                await Assert.That(persisted.ObserverIds).IsEmpty();
            },
            cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Unverified_judge_or_observer_is_rejected_atomically(
        CancellationToken cancellationToken)
    {
        await RunAsync(
            "noctf_permission_verification",
            async options =>
            {
                var now = DateTimeOffset.UtcNow;
                var ownerId = Guid.CreateVersion7();
                var existingManagerId = Guid.CreateVersion7();
                var unverifiedUserId = Guid.CreateVersion7();
                var competitionId = Guid.CreateVersion7();
                var originalUpdatedAt = now.AddMinutes(-1);
                originalUpdatedAt = originalUpdatedAt.AddTicks(
                    -(originalUpdatedAt.Ticks % TimeSpan.TicksPerMicrosecond));
                await SeedAsync(
                    options,
                    Competition(
                        competitionId,
                        ownerId,
                        originalUpdatedAt,
                        managerIds: [existingManagerId],
                        permissionRevision: 3),
                    [
                        Human(ownerId, "verification-owner", UserRole.Organizer, true, now),
                        Human(
                            existingManagerId,
                            "verification-manager",
                            UserRole.Organizer,
                            true,
                            now),
                        Human(
                            unverifiedUserId,
                            "verification-unverified",
                            UserRole.User,
                            false,
                            now)
                    ],
                    cancellationToken);

                await using (var judgeDb = new NoCtfDbContext(options))
                {
                    var judge = await new CompetitionPermissionStore(judgeDb).UpdateAsync(
                        new UpdateCompetitionPermissionsCommand(
                            CompetitionId: competitionId,
                            ActorId: ownerId,
                            ExpectedPermissionRevision: 3,
                            ManagerIds: [existingManagerId],
                            JudgeIds: [unverifiedUserId],
                            ObserverIds: []),
                        cancellationToken);
                    await Assert.That(judge.State)
                        .IsEqualTo(CompetitionPermissionUpdateState.EmailNotVerified);
                    await Assert.That(judge.UserIds)
                        .IsEquivalentTo([unverifiedUserId]);
                }

                await using (var observerDb = new NoCtfDbContext(options))
                {
                    var observer = await new CompetitionPermissionStore(observerDb).UpdateAsync(
                        new UpdateCompetitionPermissionsCommand(
                            CompetitionId: competitionId,
                            ActorId: ownerId,
                            ExpectedPermissionRevision: 3,
                            ManagerIds: [existingManagerId],
                            JudgeIds: [],
                            ObserverIds: [unverifiedUserId]),
                        cancellationToken);
                    await Assert.That(observer.State)
                        .IsEqualTo(CompetitionPermissionUpdateState.EmailNotVerified);
                    await Assert.That(observer.UserIds)
                        .IsEquivalentTo([unverifiedUserId]);
                }

                await using var verifyDb = new NoCtfDbContext(options);
                var persisted = await verifyDb.Competitions.AsNoTracking()
                    .SingleAsync(
                        competition => competition.Id == competitionId,
                        cancellationToken);
                await Assert.That(persisted.ManagerIds)
                    .IsEquivalentTo([existingManagerId]);
                await Assert.That(persisted.JudgeIds).IsEmpty();
                await Assert.That(persisted.ObserverIds).IsEmpty();
                await Assert.That(persisted.PermissionRevision).IsEqualTo(3);
                await Assert.That(persisted.UpdatedAt).IsEqualTo(originalUpdatedAt);
            },
            cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Owner_transfer_makes_an_in_flight_stale_permission_update_conflict(
        CancellationToken cancellationToken)
    {
        await RunAsync(
            "noctf_permission_transfer",
            async options =>
            {
                var now = DateTimeOffset.UtcNow;
                var administratorId = Guid.CreateVersion7();
                var previousOwnerId = Guid.CreateVersion7();
                var newOwnerId = Guid.CreateVersion7();
                var existingManagerId = Guid.CreateVersion7();
                var staleManagerId = Guid.CreateVersion7();
                var competitionId = Guid.CreateVersion7();
                await SeedAsync(
                    options,
                    Competition(
                        competitionId,
                        previousOwnerId,
                        now,
                        managerIds: [existingManagerId],
                        judgeIds: [newOwnerId],
                        permissionRevision: 9),
                    [
                        Human(
                            administratorId,
                            "transfer-administrator",
                            UserRole.Administrator,
                            true,
                            now),
                        Human(
                            previousOwnerId,
                            "transfer-previous-owner",
                            UserRole.Organizer,
                            true,
                            now),
                        Human(
                            newOwnerId,
                            "transfer-new-owner",
                            UserRole.Organizer,
                            true,
                            now),
                        Human(
                            existingManagerId,
                            "transfer-existing-manager",
                            UserRole.Organizer,
                            true,
                            now),
                        Human(
                            staleManagerId,
                            "transfer-stale-manager",
                            UserRole.Organizer,
                            true,
                            now)
                    ],
                    cancellationToken);

                await using var setupDb = new NoCtfDbContext(options);
                await setupDb.Database.ExecuteSqlRawAsync(
                    """
                    CREATE FUNCTION noctf_delay_competition_permission_transfer()
                    RETURNS trigger
                    LANGUAGE plpgsql
                    AS $$
                    BEGIN
                        IF NEW.owner_id IS DISTINCT FROM OLD.owner_id THEN
                            PERFORM pg_sleep(1);
                        END IF;
                        RETURN NEW;
                    END;
                    $$;
                    """,
                    cancellationToken);
                await setupDb.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TRIGGER noctf_delay_competition_permission_transfer
                    BEFORE UPDATE ON competitions
                    FOR EACH ROW
                    EXECUTE FUNCTION noctf_delay_competition_permission_transfer();
                    """,
                    cancellationToken);

                await using var transferDb = new NoCtfDbContext(options);
                await using var permissionsDb = new NoCtfDbContext(options);
                var transferTask = new AdminCompetitionStore(transferDb).TransferOwnerAsync(
                    competitionId,
                    administratorId,
                    true,
                    newOwnerId,
                    now.AddMinutes(1),
                    cancellationToken);
                await WaitForPostgresSleepAsync(setupDb, cancellationToken);
                var staleUpdateTask = new CompetitionPermissionStore(permissionsDb).UpdateAsync(
                    new UpdateCompetitionPermissionsCommand(
                        CompetitionId: competitionId,
                        ActorId: administratorId,
                        ExpectedPermissionRevision: 9,
                        ManagerIds: [staleManagerId],
                        JudgeIds: [],
                        ObserverIds: []),
                    cancellationToken);
                await Task.WhenAll(transferTask, staleUpdateTask);

                await Assert.That((await transferTask).State)
                    .IsEqualTo(CompetitionOwnerTransferState.Transferred);
                await Assert.That((await staleUpdateTask).State)
                    .IsEqualTo(CompetitionPermissionUpdateState.RevisionConflict);
                await using var verifyDb = new NoCtfDbContext(options);
                var persisted = await verifyDb.Competitions.AsNoTracking()
                    .SingleAsync(
                        competition => competition.Id == competitionId,
                        cancellationToken);
                await Assert.That(persisted.OwnerId).IsEqualTo(newOwnerId);
                await Assert.That(persisted.ManagerIds)
                    .IsEquivalentTo([previousOwnerId, existingManagerId]);
                await Assert.That(persisted.ManagerIds.Contains(staleManagerId)).IsFalse();
                await Assert.That(persisted.JudgeIds).IsEmpty();
                await Assert.That(persisted.ObserverIds).IsEmpty();
                await Assert.That(persisted.PermissionRevision).IsEqualTo(10);
            },
            cancellationToken);
    }

    private static async Task AssertSnapshotAsync(
        CompetitionPermissionSnapshot snapshot,
        Guid ownerId,
        IReadOnlyList<Guid> managerIds,
        IReadOnlyList<Guid> judgeIds,
        IReadOnlyList<Guid> observerIds,
        int permissionRevision)
    {
        await Assert.That(snapshot.OwnerId).IsEqualTo(ownerId);
        await Assert.That(snapshot.ManagerIds).IsEquivalentTo(managerIds);
        await Assert.That(snapshot.JudgeIds).IsEquivalentTo(judgeIds);
        await Assert.That(snapshot.ObserverIds).IsEquivalentTo(observerIds);
        await Assert.That(snapshot.PermissionRevision).IsEqualTo(permissionRevision);
    }

    private static async Task AssertCandidatesAsync(
        IReadOnlyList<CompetitionPermissionCandidate> candidates,
        IReadOnlyList<Candidate> expected)
    {
        await Assert.That(candidates.Select(candidate => new Candidate(
                candidate.Id,
                candidate.UserName,
                candidate.Kind,
                candidate.Role,
                candidate.EmailVerified)))
            .IsEquivalentTo(expected);
    }

    private static async Task RunAsync(
        string database,
        Func<DbContextOptions<NoCtfDbContext>, Task> test,
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase(database)
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await test(options);
        });
    }

    private static async Task SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        Competition competition,
        IReadOnlyList<User> users,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        db.Users.AddRange(users);
        db.Competitions.Add(competition);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static User Human(
        Guid id,
        string userName,
        UserRole role,
        bool emailVerified,
        DateTimeOffset now) =>
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
            EmailVerifiedAt = emailVerified ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static User Bot(
        Guid id,
        string userName,
        UserRole role,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"bot-{id:N}@bot.invalid",
            NormalizedEmail = $"BOT-{id:N}@BOT.INVALID",
            PasswordHash = "test",
            Kind = UserKind.Bot,
            Role = role,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        DateTimeOffset now,
        Guid[]? managerIds = null,
        Guid[]? judgeIds = null,
        Guid[]? observerIds = null,
        int permissionRevision = 0) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            ManagerIds = managerIds ?? [],
            JudgeIds = judgeIds ?? [],
            ObserverIds = observerIds ?? [],
            PermissionRevision = permissionRevision,
            Title = $"Competition {id:N}",
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
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

    private sealed record Candidate(
        Guid Id,
        string UserName,
        UserKind Kind,
        UserRole Role,
        bool EmailVerified);
}
