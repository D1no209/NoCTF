using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json.Nodes;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.GameModes.Registration;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTemplateModeInvariantPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Summary_lists_do_not_load_the_definition_graph(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_challenge_summary_projection")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var patchChallengeId = Guid.CreateVersion7();
            await using (var additions = new NoCtfDbContext(options))
            {
                foreach (var mode in new[] { GameMode.Awd, GameMode.Awdp, GameMode.Koh })
                {
                    var challenge = ChallengeGeneratedCatalog.Create(mode);
                    challenge.Id = Guid.CreateVersion7();
                    challenge.OwnerId = fixture.OwnerId;
                    challenge.Title = $"{mode} summary";
                    challenge.Direction = "Web";
                    challenge.Visibility = ChallengeVisibility.Shared;
                    challenge.Definition = TestConfigurations.Definition(mode);
                    challenge.CreatedAt = fixture.Now;
                    challenge.UpdatedAt = fixture.Now;
                    additions.Challenges.Add(challenge);
                }
                var patchDefinition = (CtfChallengeDefinition)TestConfigurations.Definition(GameMode.Ctf);
                patchDefinition.InteractionKind = CtfInteractionKind.PatchVerification;
                additions.Challenges.Add(new CtfChallenge
                {
                    Id = patchChallengeId,
                    OwnerId = fixture.OwnerId,
                    Title = "Patch summary",
                    Direction = "Pwn",
                    Visibility = ChallengeVisibility.Shared,
                    Definition = patchDefinition,
                    CreatedAt = fixture.Now,
                    UpdatedAt = fixture.Now
                });
                await additions.SaveChangesAsync(cancellationToken);
            }
            var strictOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
                .Options;
            await using var db = new NoCtfDbContext(strictOptions);

            var bankPage = await new ChallengeBankStore(db).ListPageAsync(
                new(fixture.OwnerId, true, false, null, null, 0, 10, false),
                cancellationToken);
            var bankItem = bankPage.Items.Single(item => item.Id == fixture.ChallengeId);
            await Assert.That(bankItem.Title).IsEqualTo("Original title");
            await Assert.That(bankItem.ActiveCompetitionReferenceCount).IsEqualTo(1);
            await Assert.That(bankPage.Items.Select(item => item.Mode).Distinct().Count()).IsEqualTo(4);
            await Assert.That(bankPage.Items.Single(item => item.Id == patchChallengeId).InteractionKind)
                .IsEqualTo(CtfInteractionKind.PatchVerification);
            var visiblePage = await new ChallengeBankStore(db).ListPageAsync(
                new(Guid.CreateVersion7(), false, false, null, "Pwn", 0, 1, false),
                cancellationToken);
            await Assert.That(visiblePage.Total).IsEqualTo(1);
            await Assert.That(visiblePage.Items.Single().Id).IsEqualTo(patchChallengeId);
            var privateItem = await new ChallengeBankStore(db).ListPageAsync(
                new(Guid.CreateVersion7(), false, false, "Original", null, 0, 10, false),
                cancellationToken);
            await Assert.That(privateItem.Total).IsEqualTo(0);
            var detail = await new ChallengeBankStore(db).FindAsync(
                fixture.ChallengeId, fixture.OwnerId, true, false, cancellationToken);
            await Assert.That(detail?.Definition).IsTypeOf<CtfChallengeDefinition>();

            var competitionItems = await CreateManagementStore(db).ListAsync(
                fixture.CompetitionId, true, false, cancellationToken);
            var competitionItem = competitionItems.Single(item => item.Id == fixture.CompetitionChallengeId);
            await Assert.That(competitionItem.Title).IsEqualTo("Original title");
            await Assert.That(competitionItem.ChallengeId).IsEqualTo(fixture.ChallengeId);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Mode_updates_are_fenced_by_active_competition_references(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
                    "wEb",
                    Definition(GameMode.Ctf),
                    changedAt),
                cancellationToken);
            await Assert.That(unchanged.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(unchanged.Template!.UpdatedAt).IsEqualTo(fixture.Now);
            await AssertTemplateAsync(
                options,
                fixture.ChallengeId,
                GameMode.Ctf,
                ChallengeVisibility.Private,
                "Original title",
                "Original description",
                "Web",
                Definition(GameMode.Ctf),
                fixture.Now,
                cancellationToken);

            var blocked = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Awd,
                    "Blocked title",
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
                Definition(GameMode.Ctf),
                fixture.Now,
                cancellationToken);

            var metadata = await UpdateWithEventsAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Ctf,
                    "Updated title",
                    changedAt),
                cancellationToken);
            await Assert.That(metadata.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(metadata.Template!.Mode).IsEqualTo(GameMode.Ctf);
            await AssertTemplateAsync(
                options,
                fixture.ChallengeId,
                GameMode.Ctf,
                ChallengeVisibility.Shared,
                "Updated title",
                "Updated description",
                "Pwn",
                Definition(GameMode.Ctf),
                changedAt,
                cancellationToken);
            await using (var eventDb = new NoCtfDbContext(options))
            {
                var descriptionEvents = await eventDb.CompetitionEvents.AsNoTracking()
                    .Where(item =>
                        item.CompetitionId == fixture.CompetitionId
                        && item.Kind == CompetitionEventKind.ChallengeDescriptionUpdated
                        && item.Visibility == CompetitionEventVisibility.Public
                        && item.SubjectId == fixture.CompetitionChallengeId)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(descriptionEvents).Count().IsEqualTo(1);
            }

            var activeRuntimeId = Guid.CreateVersion7(changedAt.AddMilliseconds(1));
            await using (var runtimeDb = new NoCtfDbContext(options))
            {
                runtimeDb.RuntimeInstances.Add(new PlayerRuntimeInstance
                {
                    Id = activeRuntimeId,
                    CompetitionId = fixture.CompetitionId,
                    CompetitionChallengeId = fixture.CompetitionChallengeId,
                    RuntimeKind = RuntimeKind.Container,
                    RuntimeProvider = RuntimeProvider.Docker,
                    State = RuntimeState.Running,
                    CreatedAt = changedAt.AddMilliseconds(1),
                    RunningAt = changedAt.AddMilliseconds(1)
                });
                await runtimeDb.SaveChangesAsync(cancellationToken);
            }

            var activeRuntimeBlocked = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Ctf,
                    "Blocked by active Runtime",
                    changedAt.AddMilliseconds(2)) with
                {
                    Definition = new CtfChallengeDefinition
                    {
                        InteractionKind = CtfInteractionKind.FlagSubmission,
                        PatchEntrypoint = "changed"
                    }
                },
                cancellationToken);
            await Assert.That(activeRuntimeBlocked.State)
                .IsEqualTo(ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict);

            var metadataWhileActive = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Ctf,
                    "Metadata while active",
                    changedAt.AddMilliseconds(3)),
                cancellationToken);
            await Assert.That(metadataWhileActive.State)
                .IsEqualTo(ChallengeTemplateWriteState.Succeeded);

            await using (var runtimeDb = new NoCtfDbContext(options))
            {
                var runtime = await runtimeDb.RuntimeInstances.SingleAsync(
                    item => item.Id == activeRuntimeId,
                    cancellationToken);
                runtime.State = RuntimeState.Stopped;
                runtime.StoppedAt = changedAt.AddMilliseconds(4);
                await runtimeDb.SaveChangesAsync(cancellationToken);
            }

            await using (var deleteDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(deleteDb).SoftDeleteAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
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
                    changedAt.AddSeconds(2)),
                cancellationToken);
            await Assert.That(changedMode.State)
                .IsEqualTo(ChallengeTemplateWriteState.ActiveCompetitionModeConflict);
            await Assert.That(changedMode.Template).IsNull();

            await using (var mismatchDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(mismatchDb).RestoreAsync(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    changedAt.AddSeconds(3),
                    cancellationToken);
                await Assert.That(failure).IsNull();
            }
            await AssertCompetitionChallengeAsync(
                options,
                fixture.CompetitionChallengeId,
                isDeleted: false,
                cancellationToken);

            var restoredMode = await UpdateAsync(
                options,
                Command(
                    fixture.ChallengeId,
                    fixture.OwnerId,
                    GameMode.Ctf,
                    "Restored CTF title",
                    changedAt.AddSeconds(4)),
                cancellationToken);
            await Assert.That(restoredMode.State).IsEqualTo(ChallengeTemplateWriteState.Succeeded);
            await Assert.That(restoredMode.Template!.Mode).IsEqualTo(GameMode.Ctf);

            var deletedParentBlocked = await UpdateAsync(
                options,
                Command(
                    fixture.DeletedParentChallengeId,
                    fixture.OwnerId,
                    GameMode.Awd,
                    "Blocked by deleted parent",
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
                Definition(GameMode.Ctf),
                fixture.Now,
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
        var createOutbox = Substitute.For<IPostCommitMessagePublisher>();
        var createTask = CreateManagementStore(createDb, createOutbox).CreateAsync(
            new(
                fixture.ConcurrentCompetitionChallengeId,
                fixture.ConcurrentCompetitionId,
                fixture.ConcurrentChallengeId,
                1,
                now),
            new CtfCompetitionChallengeRules(),
            cancellationToken);
        await WaitForPostgresSleepAsync(observerDb, cancellationToken);
        var updateTask = new ChallengeBankStore(updateDb).UpdateAsync(
            Command(
                fixture.ConcurrentChallengeId,
                fixture.OwnerId,
                GameMode.Awd,
                "Concurrent AWD title",
                now),
            cancellationToken);

        await Task.WhenAll(createTask, updateTask);

        await Assert.That((await createTask).Failure).IsNull();
        await Assert.That((await updateTask).State)
            .IsEqualTo(ChallengeTemplateWriteState.ActiveCompetitionModeConflict);
        await AssertTemplateAsync(
            options,
            fixture.ConcurrentChallengeId,
            GameMode.Ctf,
            ChallengeVisibility.Private,
            "Concurrent template",
            "Original description",
            "Web",
            Definition(GameMode.Ctf),
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
                now),
            cancellationToken);
        await WaitForPostgresSleepAsync(observerDb, cancellationToken);
        var createTask = CreateManagementStore(createDb).CreateAsync(
            new(
                fixture.UpdateFirstCompetitionChallengeId,
                fixture.UpdateFirstCompetitionId,
                fixture.UpdateFirstChallengeId,
                1,
                now),
            new CtfCompetitionChallengeRules(),
            cancellationToken);

        await Task.WhenAll(updateTask, createTask);

        await Assert.That((await updateTask).State)
            .IsEqualTo(ChallengeTemplateWriteState.Succeeded);
        await Assert.That((await createTask).Failure)
            .IsEqualTo(ChallengeMutationFailure.TemplateModeMismatch);
        await AssertTemplateAsync(
            options,
            fixture.UpdateFirstChallengeId,
            GameMode.Awd,
            ChallengeVisibility.Shared,
            "Update-first AWD title",
            "Updated description",
            "Pwn",
            Definition(GameMode.Awd),
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
        await db.Database.EnsureCreatedAsync(cancellationToken);
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
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Order = 1,
            IsPublished = true,
            Rules = TestConfigurations.Rules(GameMode.Ctf),
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
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge
        {
            Id = Guid.CreateVersion7(),
            CompetitionId = deletedParentCompetitionId,
            ChallengeId = deletedParentChallengeId,
            Order = 1,
            Rules = TestConfigurations.Rules(GameMode.Ctf),
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

    private static async Task<ChallengeTemplateWriteResult> UpdateWithEventsAsync(
        DbContextOptions<NoCtfDbContext> options,
        UpdateChallengeTemplateCommand command,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var outbox = Substitute.For<IPostCommitMessagePublisher>();
        var events = new CompetitionEventStore(db, outbox);
        return await new ChallengeBankStore(db, outbox, events)
            .UpdateAsync(command, cancellationToken);
    }

    private static UpdateChallengeTemplateCommand Command(
        Guid challengeId,
        Guid ownerId,
        GameMode mode,
        string title,
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
            Definition(mode),
            updatedAt);

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        string title,
        DateTimeOffset now,
        DateTimeOffset? deletedAt = null) =>
        new CtfCompetition
        {
            Id = id,
            OwnerId = ownerId,
            Title = title,
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            DeletedAt = deletedAt,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static ChallengeDefinition Definition(GameMode mode)
    {
        var challengeId = Guid.Empty;
        ChallengeDefinition definition = mode switch
        {
            GameMode.Ctf => new CtfChallengeDefinition(),
            GameMode.Awd => new AwdChallengeDefinition(),
            GameMode.Awdp => new AwdpChallengeDefinition(),
            GameMode.Koh => new KohChallengeDefinition(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        definition.ChallengeId = challengeId;
        return definition;
    }

    private static Challenge Challenge(
        Guid id,
        Guid ownerId,
        string title,
        DateTimeOffset now) =>
        new CtfChallenge
        {
            Id = id,
            OwnerId = ownerId,
            Visibility = ChallengeVisibility.Private,
            Title = title,
            Description = "Original description",
            Direction = "Web",
            Definition = TestConfigurations.Definition(GameMode.Ctf),
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
        ChallengeDefinition expectedDefinition,
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
        await Assert.That(ChallengeDefinitionStructuralComparer.Equals(
            challenge.Definition,
            expectedDefinition)).IsTrue();
        await Assert.That(challenge.UpdatedAt).IsEqualTo(expectedUpdatedAt);
    }

    private static async Task AssertCompetitionChallengeAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionChallengeId,
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
        await Assert.That(competitionChallenge.DeletedAt is not null)
            .IsEqualTo(isDeleted);
    }

    private static ChallengeManagementStore CreateManagementStore(
        NoCtfDbContext db,
        IPostCommitMessagePublisher? outbox = null) =>
        new(
            db,
            outbox ?? Substitute.For<IPostCommitMessagePublisher>(),
            new ChallengeRuntimeTemplateCatalog());

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
