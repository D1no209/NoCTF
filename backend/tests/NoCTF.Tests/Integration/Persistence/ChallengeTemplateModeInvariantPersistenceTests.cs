using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Persistence;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTemplateModeInvariantPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Mode_updates_are_fenced_by_active_competition_references(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_challenge_template_mode_invariant")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var changedAt = fixture.Now.AddMinutes(1);

            var unchanged = await UpdateAsync(
                options,
                new(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    false,
                    GameMode.Ctf,
                    ChallengeVisibility.Private,
                    "Original title",
                    "Original description",
                    "Web",
                    """{ "schemaVersion": 1 }""",
                    0,
                    changedAt),
                cancellationToken);
            await Assert.That(unchanged.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(unchanged.Template!.Revision).IsEqualTo(0);
            await Assert.That(unchanged.Template.UpdatedAt).IsEqualTo(fixture.Now);
            await AssertTemplateAsync(
                options,
                fixture.ChallengeId,
                GameMode.Ctf,
                ChallengeVisibility.Private,
                "Original title",
                "Original description",
                "Web",
                """{"schemaVersion":1}""",
                0,
                fixture.Now,
                cancellationToken);

            var blocked = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Awd,
                    "Blocked title",
                    0,
                    changedAt),
                cancellationToken);
            await Assert.That(blocked.State)
                .IsEqualTo(ChallengeTemplateWriteState.ActiveCompetitionModeConflict);
            await Assert.That(blocked.Template).IsNull();
            await AssertTemplateAsync(
                options,
                fixture.ChallengeId,
                GameMode.Ctf,
                ChallengeVisibility.Private,
                "Original title",
                "Original description",
                "Web",
                """{"schemaVersion":1}""",
                0,
                fixture.Now,
                cancellationToken);

            var stale = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Awd,
                    "Stale title",
                    1,
                    changedAt),
                cancellationToken);
            await Assert.That(stale.State)
                .IsEqualTo(ChallengeTemplateWriteState.RevisionConflict);
            await Assert.That(stale.Template).IsNull();

            var metadata = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Ctf,
                    "Updated title",
                    0,
                    changedAt),
                cancellationToken);
            await Assert.That(metadata.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(metadata.Template!.Revision).IsEqualTo(1);
            await Assert.That(metadata.Template.Mode).IsEqualTo(GameMode.Ctf);
            await AssertTemplateAsync(
                options,
                fixture.ChallengeId,
                GameMode.Ctf,
                ChallengeVisibility.Shared,
                "Updated title",
                "Updated description",
                "Pwn",
                """{"schemaVersion":1,"updated":true}""",
                1,
                changedAt,
                cancellationToken);

            await using (var deleteDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(deleteDb).SoftDeleteAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    0,
                    changedAt.AddSeconds(1),
                    cancellationToken);
                await Assert.That(failure).IsNull();
            }

            var changedMode = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Awd,
                    "AWD title",
                    1,
                    changedAt.AddSeconds(2)),
                cancellationToken);
            await Assert.That(changedMode.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(changedMode.Template!.Revision).IsEqualTo(2);
            await Assert.That(changedMode.Template.Mode).IsEqualTo(GameMode.Awd);

            await using (var mismatchDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(mismatchDb).RestoreAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    1,
                    changedAt.AddSeconds(3),
                    cancellationToken);
                await Assert.That(failure)
                    .IsEqualTo(ChallengeMutationFailure.TemplateModeMismatch);
            }
            await AssertCompetitionChallengeAsync(
                options,
                fixture.CompetitionChallengeId,
                revision: 1,
                isDeleted: true,
                cancellationToken);

            var restoredMode = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Ctf,
                    "Restored CTF title",
                    2,
                    changedAt.AddSeconds(4)),
                cancellationToken);
            await Assert.That(restoredMode.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(restoredMode.Template!.Revision).IsEqualTo(3);

            await using (var restoreDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(restoreDb).RestoreAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    1,
                    changedAt.AddSeconds(5),
                    cancellationToken);
                await Assert.That(failure).IsNull();
            }
            await AssertCompetitionChallengeAsync(
                options,
                fixture.CompetitionChallengeId,
                revision: 2,
                isDeleted: false,
                cancellationToken);

            var deletedParentBlocked = await UpdateAsync(
                options,
                Command(
                    fixture.DeletedParentChallengeId,
                    fixture.OwnerId,
                    GameMode.Awd,
                    "Blocked by deleted parent",
                    0,
                    changedAt.AddSeconds(6)),
                cancellationToken);
            await Assert.That(deletedParentBlocked.State)
                .IsEqualTo(ChallengeTemplateWriteState.ActiveCompetitionModeConflict);
            await AssertTemplateAsync(
                options,
                fixture.DeletedParentChallengeId,
                GameMode.Ctf,
                ChallengeVisibility.Private,
                "Deleted parent reference",
                "Original description",
                "Web",
                """{"schemaVersion":1}""",
                0,
                fixture.Now,
                cancellationToken);

            await AssertCreateFirstRaceAsync(
                options,
                fixture,
                changedAt.AddSeconds(7),
                cancellationToken);
            await AssertUpdateFirstRaceAsync(
                options,
                fixture,
                changedAt.AddSeconds(8),
                cancellationToken);
        });
    }

    private static async Task AssertCreateFirstRaceAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var observerDb = new NoCtfDbContext(options);
        await observerDb.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION noctf_delay_competition_challenge_insert()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                PERFORM pg_sleep(1);
                RETURN NEW;
            END;
            $$;
            """,
            cancellationToken);
        await observerDb.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER noctf_delay_competition_challenge_insert
            BEFORE INSERT ON competition_challenges
            FOR EACH ROW
            EXECUTE FUNCTION noctf_delay_competition_challenge_insert();
            """,
            cancellationToken);

        await using var createDb = new NoCtfDbContext(options);
        await using var updateDb = new NoCtfDbContext(options);
        var createOutbox = Substitute.For<ITransactionalMessageOutbox>();
        var createTask = CreateManagementStore(createDb, createOutbox).CreateAsync(
            new(
                fixture.ConcurrentCompetitionChallengeId,
                fixture.ConcurrentCompetitionId,
                fixture.ConcurrentChallengeId,
                500,
                1,
                now),
            """{"schemaVersion":1}""",
            cancellationToken);
        await WaitForPostgresSleepAsync(observerDb, cancellationToken);
        var updateTask = new ChallengeBankStore(updateDb).UpdateAsync(
            Command(
                fixture.ConcurrentChallengeId,
                fixture.OwnerId,
                GameMode.Awd,
                "Concurrent AWD title",
                0,
                now),
            cancellationToken);

        await Task.WhenAll(createTask, updateTask);

        await Assert.That((await createTask).Failure).IsNull();
        await Assert.That((await updateTask).State)
            .IsEqualTo(ChallengeTemplateWriteState.RevisionConflict);
        await AssertTemplateAsync(
            options,
            fixture.ConcurrentChallengeId,
            GameMode.Ctf,
            ChallengeVisibility.Private,
            "Concurrent template",
            "Original description",
            "Web",
            """{"schemaVersion":1}""",
            1,
            fixture.Now,
            cancellationToken);
        await using var verifyDb = new NoCtfDbContext(options);
        await Assert.That(await verifyDb.CompetitionChallenges.AsNoTracking()
            .CountAsync(
                item => item.ChallengeId == fixture.ConcurrentChallengeId,
                cancellationToken)).IsEqualTo(1);
    }

    private static async Task AssertUpdateFirstRaceAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var observerDb = new NoCtfDbContext(options);
        await observerDb.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION noctf_delay_challenge_mode_update()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW.mode IS DISTINCT FROM OLD.mode THEN
                    PERFORM pg_sleep(1);
                END IF;
                RETURN NEW;
            END;
            $$;
            """,
            cancellationToken);
        await observerDb.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER noctf_delay_challenge_mode_update
            BEFORE UPDATE ON challenges
            FOR EACH ROW
            EXECUTE FUNCTION noctf_delay_challenge_mode_update();
            """,
            cancellationToken);

        await using var updateDb = new NoCtfDbContext(options);
        await using var createDb = new NoCtfDbContext(options);
        var updateTask = new ChallengeBankStore(updateDb).UpdateAsync(
            Command(
                fixture.UpdateFirstChallengeId,
                fixture.OwnerId,
                GameMode.Awd,
                "Update-first AWD title",
                0,
                now),
            cancellationToken);
        await WaitForPostgresSleepAsync(observerDb, cancellationToken);
        var createTask = CreateManagementStore(createDb).CreateAsync(
            new(
                fixture.UpdateFirstCompetitionChallengeId,
                fixture.UpdateFirstCompetitionId,
                fixture.UpdateFirstChallengeId,
                500,
                1,
                now),
            """{"schemaVersion":1}""",
            cancellationToken);

        await Task.WhenAll(updateTask, createTask);

        await Assert.That((await updateTask).State)
            .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
        await Assert.That((await createTask).Failure)
            .IsEqualTo(ChallengeMutationFailure.RevisionConflict);
        await AssertTemplateAsync(
            options,
            fixture.UpdateFirstChallengeId,
            GameMode.Awd,
            ChallengeVisibility.Shared,
            "Update-first AWD title",
            "Updated description",
            "Pwn",
            """{"schemaVersion":1,"updated":true}""",
            1,
            now,
            cancellationToken);
        await using var verifyDb = new NoCtfDbContext(options);
        await Assert.That(await verifyDb.CompetitionChallenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                item => item.ChallengeId == fixture.UpdateFirstChallengeId,
                cancellationToken)).IsFalse();
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var current = DateTimeOffset.UtcNow;
        var now = current.AddTicks(-(current.Ticks % 10));
        var ownerId = Guid.CreateVersion7(now);
        db.Users.Add(Organizer(ownerId, now));

        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        db.Competitions.Add(Competition(
            competitionId,
            ownerId,
            "Active competition",
            now));
        db.Challenges.Add(Challenge(
            challengeId,
            ownerId,
            "Original title",
            now));
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 500,
            Order = 1,
            UpdatedAt = now
        });

        var deletedParentCompetitionId = Guid.CreateVersion7();
        var deletedParentChallengeId = Guid.CreateVersion7();
        db.Competitions.Add(Competition(
            deletedParentCompetitionId,
            ownerId,
            "Deleted parent competition",
            now,
            deletedAt: now));
        db.Challenges.Add(Challenge(
            deletedParentChallengeId,
            ownerId,
            "Deleted parent reference",
            now));
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = Guid.CreateVersion7(),
            CompetitionId = deletedParentCompetitionId,
            ChallengeId = deletedParentChallengeId,
            BaseScore = 500,
            Order = 1,
            UpdatedAt = now
        });

        var concurrentCompetitionId = Guid.CreateVersion7();
        var concurrentChallengeId = Guid.CreateVersion7();
        var concurrentCompetitionChallengeId = Guid.CreateVersion7();
        db.Competitions.Add(Competition(
            concurrentCompetitionId,
            ownerId,
            "Concurrent competition",
            now));
        db.Challenges.Add(Challenge(
            concurrentChallengeId,
            ownerId,
            "Concurrent template",
            now));

        var updateFirstCompetitionId = Guid.CreateVersion7();
        var updateFirstChallengeId = Guid.CreateVersion7();
        var updateFirstCompetitionChallengeId = Guid.CreateVersion7();
        db.Competitions.Add(Competition(
            updateFirstCompetitionId,
            ownerId,
            "Update-first competition",
            now));
        db.Challenges.Add(Challenge(
            updateFirstChallengeId,
            ownerId,
            "Update-first template",
            now));
        await db.SaveChangesAsync(cancellationToken);
        return new(
            ownerId,
            competitionId,
            challengeId,
            competitionChallengeId,
            deletedParentChallengeId,
            concurrentCompetitionId,
            concurrentChallengeId,
            concurrentCompetitionChallengeId,
            updateFirstCompetitionId,
            updateFirstChallengeId,
            updateFirstCompetitionChallengeId,
            now);
    }

    private static Task<ChallengeTemplateWriteResult> UpdateAsync(
        DbContextOptions<NoCtfDbContext> options,
        UpdateChallengeTemplateCommand command,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync();

        async Task<ChallengeTemplateWriteResult> ExecuteAsync()
        {
            await using var db = new NoCtfDbContext(options);
            return await new ChallengeBankStore(db).UpdateAsync(command, cancellationToken);
        }
    }

    private static UpdateChallengeTemplateCommand Command(
        Guid challengeId,
        Guid ownerId,
        GameMode mode,
        string title,
        int expectedRevision,
        DateTimeOffset updatedAt) =>
        new(
            challengeId,
            ownerId,
            false,
            mode,
            ChallengeVisibility.Shared,
            title,
            "Updated description",
            "Pwn",
            """{"schemaVersion":1,"updated":true}""",
            expectedRevision,
            updatedAt);

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        string title,
        DateTimeOffset now,
        DateTimeOffset? deletedAt = null) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Title = title,
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
        string title,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = title,
            Description = "Original description",
            Direction = "Web",
            DefinitionJson = """{"schemaVersion":1}""",
            CreatedAt = now,
            UpdatedAt = now
        };

    private static User Organizer(Guid id, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = "mode-invariant-owner",
            NormalizedUserName = "MODE-INVARIANT-OWNER",
            Email = "mode-invariant-owner@example.test",
            NormalizedEmail = "MODE-INVARIANT-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.Organizer,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static async Task AssertTemplateAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid challengeId,
        GameMode expectedMode,
        ChallengeVisibility expectedVisibility,
        string expectedTitle,
        string expectedDescription,
        string expectedDirection,
        string expectedDefinitionJson,
        int expectedRevision,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var challenge = await db.Challenges.AsNoTracking()
            .SingleAsync(item => item.Id == challengeId, cancellationToken);
        await Assert.That(challenge.Mode).IsEqualTo(expectedMode);
        await Assert.That(challenge.Visibility).IsEqualTo(expectedVisibility);
        await Assert.That(challenge.Title).IsEqualTo(expectedTitle);
        await Assert.That(challenge.Description).IsEqualTo(expectedDescription);
        await Assert.That(challenge.Direction).IsEqualTo(expectedDirection);
        await Assert.That(JsonNode.DeepEquals(
            JsonNode.Parse(challenge.DefinitionJson),
            JsonNode.Parse(expectedDefinitionJson))).IsTrue();
        await Assert.That(challenge.Revision).IsEqualTo(expectedRevision);
        await Assert.That(challenge.UpdatedAt).IsEqualTo(expectedUpdatedAt);
    }

    private static async Task AssertCompetitionChallengeAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionChallengeId,
        int revision,
        bool isDeleted,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var competitionChallenge = await db.CompetitionChallenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == competitionChallengeId,
                cancellationToken);
        await Assert.That(competitionChallenge.Revision).IsEqualTo(revision);
        await Assert.That(competitionChallenge.DeletedAt is not null)
            .IsEqualTo(isDeleted);
    }

    private static ChallengeManagementStore CreateManagementStore(
        NoCtfDbContext db,
        ITransactionalMessageOutbox? outbox = null) =>
        new(db, outbox ?? Substitute.For<ITransactionalMessageOutbox>());

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

    private sealed record Fixture(
        Guid OwnerId,
        Guid CompetitionId,
        Guid ChallengeId,
        Guid CompetitionChallengeId,
        Guid DeletedParentChallengeId,
        Guid ConcurrentCompetitionId,
        Guid ConcurrentChallengeId,
        Guid ConcurrentCompetitionChallengeId,
        Guid UpdateFirstCompetitionId,
        Guid UpdateFirstChallengeId,
        Guid UpdateFirstCompetitionChallengeId,
        DateTimeOffset Now);
}
