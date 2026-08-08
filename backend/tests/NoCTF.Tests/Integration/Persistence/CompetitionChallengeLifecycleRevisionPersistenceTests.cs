using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Hints;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Persistence;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionChallengeLifecycleRevision")]
public sealed class CompetitionChallengeLifecycleRevisionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Lifecycle_changes_are_revision_fenced_and_atomic(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_competition_challenge_lifecycle_revision")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var now = fixture.Now.AddMinutes(1);
            var lifecycleOutbox = Substitute.For<ITransactionalMessageOutbox>();

            await using (var deleteDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(deleteDb, lifecycleOutbox).SoftDeleteAsync(
                    fixture.RevisionCompetitionId,
                    fixture.RevisionCompetitionChallengeId,
                    0,
                    now,
                    cancellationToken);
                await Assert.That(failure).IsNull();
            }
            await AssertStateAsync(
                options,
                fixture.RevisionCompetitionId,
                fixture.RevisionCompetitionChallengeId,
                expectedRevision: 1,
                expectedDeleted: true,
                expectedLeaderboardRevision: 11,
                expectedBaseScore: 500,
                expectedOrder: 1,
                cancellationToken);

            await using (var staleRestoreDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(staleRestoreDb).RestoreAsync(
                    fixture.RevisionCompetitionId,
                    fixture.RevisionCompetitionChallengeId,
                    0,
                    now.AddSeconds(1),
                    cancellationToken);
                await Assert.That(failure)
                    .IsEqualTo(ChallengeMutationFailure.RevisionConflict);
            }
            await AssertStateAsync(
                options,
                fixture.RevisionCompetitionId,
                fixture.RevisionCompetitionChallengeId,
                expectedRevision: 1,
                expectedDeleted: true,
                expectedLeaderboardRevision: 11,
                expectedBaseScore: 500,
                expectedOrder: 1,
                cancellationToken);

            await using (var wrongStateDeleteDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(wrongStateDeleteDb).SoftDeleteAsync(
                    fixture.RevisionCompetitionId,
                    fixture.RevisionCompetitionChallengeId,
                    1,
                    now.AddSeconds(2),
                    cancellationToken);
                await Assert.That(failure)
                    .IsEqualTo(ChallengeMutationFailure.LifecycleStateConflict);
            }
            await AssertStateAsync(
                options,
                fixture.RevisionCompetitionId,
                fixture.RevisionCompetitionChallengeId,
                expectedRevision: 1,
                expectedDeleted: true,
                expectedLeaderboardRevision: 11,
                expectedBaseScore: 500,
                expectedOrder: 1,
                cancellationToken);

            await using (var restoreDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(restoreDb, lifecycleOutbox).RestoreAsync(
                    fixture.RevisionCompetitionId,
                    fixture.RevisionCompetitionChallengeId,
                    1,
                    now.AddSeconds(3),
                    cancellationToken);
                await Assert.That(failure).IsNull();
            }
            await AssertStateAsync(
                options,
                fixture.RevisionCompetitionId,
                fixture.RevisionCompetitionChallengeId,
                expectedRevision: 2,
                expectedDeleted: false,
                expectedLeaderboardRevision: 12,
                expectedBaseScore: 500,
                expectedOrder: 1,
                cancellationToken);

            await using (var wrongStateRestoreDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(wrongStateRestoreDb).RestoreAsync(
                    fixture.RevisionCompetitionId,
                    fixture.RevisionCompetitionChallengeId,
                    2,
                    now.AddSeconds(4),
                    cancellationToken);
                await Assert.That(failure)
                    .IsEqualTo(ChallengeMutationFailure.LifecycleStateConflict);
            }
            await AssertStateAsync(
                options,
                fixture.RevisionCompetitionId,
                fixture.RevisionCompetitionChallengeId,
                expectedRevision: 2,
                expectedDeleted: false,
                expectedLeaderboardRevision: 12,
                expectedBaseScore: 500,
                expectedOrder: 1,
                cancellationToken);

            await using (var updateDb = new NoCtfDbContext(options))
            {
                var result = await CreateManagementStore(updateDb, lifecycleOutbox).UpdateAsync(
                    new UpdateCompetitionChallengeCommand(
                        fixture.RevisionCompetitionId,
                        fixture.RevisionCompetitionChallengeId,
                        750,
                        2,
                        true,
                        2,
                        now.AddSeconds(5)),
                    cancellationToken);
                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Challenge).IsNotNull();
            }
            await AssertStateAsync(
                options,
                fixture.RevisionCompetitionId,
                fixture.RevisionCompetitionChallengeId,
                expectedRevision: 3,
                expectedDeleted: false,
                expectedLeaderboardRevision: 13,
                expectedBaseScore: 750,
                expectedOrder: 2,
                cancellationToken);

            await using (var staleDeleteDb = new NoCtfDbContext(options))
            {
                var failure = await CreateManagementStore(staleDeleteDb).SoftDeleteAsync(
                    fixture.RevisionCompetitionId,
                    fixture.RevisionCompetitionChallengeId,
                    2,
                    now.AddSeconds(6),
                    cancellationToken);
                await Assert.That(failure)
                    .IsEqualTo(ChallengeMutationFailure.RevisionConflict);
            }
            await AssertStateAsync(
                options,
                fixture.RevisionCompetitionId,
                fixture.RevisionCompetitionChallengeId,
                expectedRevision: 3,
                expectedDeleted: false,
                expectedLeaderboardRevision: 13,
                expectedBaseScore: 750,
                expectedOrder: 2,
                cancellationToken);

            await using (var publishedEditDb = new NoCtfDbContext(options))
            {
                var result = await CreateManagementStore(
                        publishedEditDb,
                        lifecycleOutbox)
                    .UpdateAsync(
                        new UpdateCompetitionChallengeCommand(
                            fixture.RevisionCompetitionId,
                            fixture.RevisionCompetitionChallengeId,
                            800,
                            2,
                            true,
                            3,
                            now.AddSeconds(7)),
                        cancellationToken);
                await Assert.That(result.Failure).IsNull();
                await Assert.That(result.Challenge).IsNotNull();
            }
            await AssertStateAsync(
                options,
                fixture.RevisionCompetitionId,
                fixture.RevisionCompetitionChallengeId,
                expectedRevision: 4,
                expectedDeleted: false,
                expectedLeaderboardRevision: 14,
                expectedBaseScore: 800,
                expectedOrder: 2,
                cancellationToken);

            await lifecycleOutbox.Received(4).PublishAsync(
                Arg.Is<InvalidateLeaderboard>(message =>
                    message!.CompetitionId == fixture.RevisionCompetitionId));
            await lifecycleOutbox.Received(1).PublishAsync(
                Arg.Is<ChallengePublished>(message =>
                    message!.CompetitionId == fixture.RevisionCompetitionId
                    && message.CompetitionChallengeId
                        == fixture.RevisionCompetitionChallengeId
                    && message.Revision == 3));
            await lifecycleOutbox.Received(4).FlushOutgoingMessagesAsync();

            await AssertRestoreFailureIsAtomicAsync(
                options,
                fixture.TemplateMissingCompetitionId,
                fixture.TemplateMissingCompetitionChallengeId,
                expectedRevision: 4,
                expectedFailure: ChallengeMutationFailure.TemplateNotFound,
                expectedLeaderboardRevision: 20,
                now.AddSeconds(7),
                cancellationToken);
            await AssertRestoreFailureIsAtomicAsync(
                options,
                fixture.ModeMismatchCompetitionId,
                fixture.ModeMismatchCompetitionChallengeId,
                expectedRevision: 5,
                expectedFailure: ChallengeMutationFailure.TemplateModeMismatch,
                expectedLeaderboardRevision: 30,
                now.AddSeconds(8),
                cancellationToken);
            await AssertRestoreFailureIsAtomicAsync(
                options,
                fixture.OrderConflictCompetitionId,
                fixture.OrderConflictCompetitionChallengeId,
                expectedRevision: 6,
                expectedFailure: ChallengeMutationFailure.ChallengeOrderConflict,
                expectedLeaderboardRevision: 40,
                now.AddSeconds(9),
                cancellationToken);
            await AssertRestoreFailureIsAtomicAsync(
                options,
                fixture.TemplateConflictCompetitionId,
                fixture.TemplateConflictCompetitionChallengeId,
                expectedRevision: 7,
                expectedFailure: ChallengeMutationFailure.ChallengeTemplateConflict,
                expectedLeaderboardRevision: 45,
                now.AddSeconds(10),
                cancellationToken);

            await AssertConcurrentDeleteAsync(
                options,
                fixture.ConcurrentDeleteCompetitionId,
                fixture.ConcurrentDeleteCompetitionChallengeId,
                now.AddSeconds(11),
                cancellationToken);
            await AssertConcurrentRestoreAsync(
                options,
                fixture.ConcurrentRestoreCompetitionId,
                fixture.ConcurrentRestoreCompetitionChallengeId,
                now.AddSeconds(12),
                cancellationToken);
            await AssertHintMutationsShareRevisionFenceAsync(
                options,
                fixture.HintMutationCompetitionId,
                fixture.HintMutationCompetitionChallengeId,
                now.AddSeconds(13),
                cancellationToken);
        });
    }

    private static async Task AssertRestoreFailureIsAtomicAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
        ChallengeMutationFailure expectedFailure,
        long expectedLeaderboardRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var mutationDb = new NoCtfDbContext(options))
        {
            var failure = await CreateManagementStore(mutationDb).RestoreAsync(
                competitionId,
                competitionChallengeId,
                expectedRevision,
                now,
                cancellationToken);
            await Assert.That(failure).IsEqualTo(expectedFailure);
        }

        await AssertStateAsync(
            options,
            competitionId,
            competitionChallengeId,
            expectedRevision,
            expectedDeleted: true,
            expectedLeaderboardRevision,
            expectedBaseScore: 500,
            expectedOrder: 1,
            cancellationToken);
    }

    private static async Task AssertConcurrentDeleteAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var firstDb = new NoCtfDbContext(options);
        await using var secondDb = new NoCtfDbContext(options);
        var first = CreateManagementStore(firstDb).SoftDeleteAsync(
            competitionId,
            competitionChallengeId,
            0,
            now,
            cancellationToken);
        var second = CreateManagementStore(secondDb).SoftDeleteAsync(
            competitionId,
            competitionChallengeId,
            0,
            now.AddMilliseconds(1),
            cancellationToken);

        var results = await Task.WhenAll(first, second);
        await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
        await Assert.That(results.Count(
                result => result == ChallengeMutationFailure.RevisionConflict))
            .IsEqualTo(1);
        await AssertStateAsync(
            options,
            competitionId,
            competitionChallengeId,
            expectedRevision: 1,
            expectedDeleted: true,
            expectedLeaderboardRevision: 51,
            expectedBaseScore: 500,
            expectedOrder: 1,
            cancellationToken);
    }

    private static async Task AssertConcurrentRestoreAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var firstDb = new NoCtfDbContext(options);
        await using var secondDb = new NoCtfDbContext(options);
        var first = CreateManagementStore(firstDb).RestoreAsync(
            competitionId,
            competitionChallengeId,
            0,
            now,
            cancellationToken);
        var second = CreateManagementStore(secondDb).RestoreAsync(
            competitionId,
            competitionChallengeId,
            0,
            now.AddMilliseconds(1),
            cancellationToken);

        var results = await Task.WhenAll(first, second);
        await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
        await Assert.That(results.Count(
                result => result == ChallengeMutationFailure.RevisionConflict))
            .IsEqualTo(1);
        await AssertStateAsync(
            options,
            competitionId,
            competitionChallengeId,
            expectedRevision: 1,
            expectedDeleted: false,
            expectedLeaderboardRevision: 61,
            expectedBaseScore: 500,
            expectedOrder: 1,
            cancellationToken);
    }

    private static async Task AssertHintMutationsShareRevisionFenceAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var hintId = Guid.CreateVersion7(now);
        var hintOutbox = Substitute.For<ITransactionalMessageOutbox>();
        await using (var saveDb = new NoCtfDbContext(options))
        {
            var store = CreateHintStore(saveDb, hintOutbox);
            var result = await RunWhileCompetitionLockHeldAsync(
                options,
                competitionId,
                () => store.SaveAsync(
                    new(
                        competitionId,
                        competitionChallengeId,
                        hintId,
                        true,
                        "Serialized hint",
                        25,
                        null,
                        now),
                    cancellationToken),
                cancellationToken);
            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.Hint).IsNotNull();
        }
        await AssertStateAsync(
            options,
            competitionId,
            competitionChallengeId,
            expectedRevision: 1,
            expectedDeleted: false,
            expectedLeaderboardRevision: 71,
            expectedBaseScore: 500,
            expectedOrder: 1,
            cancellationToken);

        await using (var deleteDb = new NoCtfDbContext(options))
        {
            var store = CreateHintStore(deleteDb, hintOutbox);
            var deleted = await RunWhileCompetitionLockHeldAsync(
                options,
                competitionId,
                () => store.DeleteAsync(
                    competitionId,
                    competitionChallengeId,
                    hintId,
                    now.AddSeconds(1),
                    cancellationToken),
                cancellationToken);
            await Assert.That(deleted).IsTrue();
        }
        await AssertStateAsync(
            options,
            competitionId,
            competitionChallengeId,
            expectedRevision: 2,
            expectedDeleted: false,
            expectedLeaderboardRevision: 72,
            expectedBaseScore: 500,
            expectedOrder: 1,
            cancellationToken);

        await using (var restoreDb = new NoCtfDbContext(options))
        {
            var store = CreateHintStore(restoreDb, hintOutbox);
            var restored = await RunWhileCompetitionLockHeldAsync(
                options,
                competitionId,
                () => store.RestoreAsync(
                    competitionId,
                    competitionChallengeId,
                    hintId,
                    now.AddSeconds(2),
                    cancellationToken),
                cancellationToken);
            await Assert.That(restored).IsTrue();
        }

        await using (var staleManagementDb = new NoCtfDbContext(options))
        {
            var result = await CreateManagementStore(staleManagementDb).UpdateAsync(
                new(
                    competitionId,
                    competitionChallengeId,
                    750,
                    2,
                    true,
                    0,
                    now.AddSeconds(3)),
                cancellationToken);
            await Assert.That(result.Failure)
                .IsEqualTo(ChallengeMutationFailure.RevisionConflict);
        }
        await AssertStateAsync(
            options,
            competitionId,
            competitionChallengeId,
            expectedRevision: 3,
            expectedDeleted: false,
            expectedLeaderboardRevision: 73,
            expectedBaseScore: 500,
            expectedOrder: 1,
            cancellationToken);

        await using var assertionDb = new NoCtfDbContext(options);
        var challenge = await assertionDb.CompetitionChallenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == competitionChallengeId, cancellationToken);
        await Assert.That(challenge.Hints.Single(hint => hint.Id == hintId).HiddenAt)
            .IsNull();
        await hintOutbox.Received(3).PublishAsync(
            Arg.Is<InvalidateLeaderboard>(message =>
                message!.CompetitionId == competitionId));
        await hintOutbox.Received(3).FlushOutgoingMessagesAsync();
    }

    private static ChallengeHintStore CreateHintStore(
        NoCtfDbContext db,
        ITransactionalMessageOutbox? outbox = null) =>
        new(
            db,
            Substitute.For<ILeaderboardProjectionEngine>(),
            outbox ?? Substitute.For<ITransactionalMessageOutbox>());

    private static ChallengeManagementStore CreateManagementStore(
        NoCtfDbContext db,
        ITransactionalMessageOutbox? outbox = null) =>
        new(db, outbox ?? Substitute.For<ITransactionalMessageOutbox>());

    private static async Task<T> RunWhileCompetitionLockHeldAsync<T>(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        Func<Task<T>> mutation,
        CancellationToken cancellationToken)
    {
        _ = options;
        _ = competitionId;
        var startedAt = TimeProvider.System.GetTimestamp();
        var result = await mutation().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await Assert.That(TimeProvider.System.GetElapsedTime(startedAt))
            .IsLessThan(TimeSpan.FromSeconds(2));
        return result;
    }

    private static async Task AssertStateAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
        bool expectedDeleted,
        long expectedLeaderboardRevision,
        long expectedBaseScore,
        int expectedOrder,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var challenge = await db.CompetitionChallenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(
                item => item.Id == competitionChallengeId
                    && item.CompetitionId == competitionId,
                cancellationToken);
        var leaderboardRevision = await db.Competitions
            .AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.LeaderboardRevision)
            .SingleAsync(cancellationToken);

        await Assert.That(challenge.Revision).IsEqualTo(expectedRevision);
        await Assert.That(challenge.DeletedAt is not null).IsEqualTo(expectedDeleted);
        await Assert.That(challenge.BaseScore).IsEqualTo(expectedBaseScore);
        await Assert.That(challenge.Order).IsEqualTo(expectedOrder);
        await Assert.That(leaderboardRevision).IsEqualTo(expectedLeaderboardRevision);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7(now);
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "lifecycle-owner",
            NormalizedUserName = "LIFECYCLE-OWNER",
            Email = "lifecycle-owner@example.test",
            NormalizedEmail = "LIFECYCLE-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.Organizer,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        var revision = Scenario.Create(now, ownerId, 10);
        var templateMissing = Scenario.Create(now.AddMilliseconds(10), ownerId, 20);
        templateMissing.Template.DeletedAt = now;
        templateMissing.Link.DeletedAt = now;
        templateMissing.Link.Revision = 4;

        var modeMismatch = Scenario.Create(now.AddMilliseconds(20), ownerId, 30);
        modeMismatch.Template.Mode = GameMode.Awd;
        modeMismatch.Link.DeletedAt = now;
        modeMismatch.Link.Revision = 5;

        var orderConflict = Scenario.Create(now.AddMilliseconds(30), ownerId, 40);
        orderConflict.Link.DeletedAt = now;
        orderConflict.Link.Revision = 6;
        var occupiedTemplate = CreateChallengeTemplate(
            Guid.CreateVersion7(now.AddMilliseconds(33)),
            ownerId,
            GameMode.Ctf,
            now);
        var occupiedLink = CreateCompetitionChallenge(
            Guid.CreateVersion7(now.AddMilliseconds(34)),
            orderConflict.Competition.Id,
            occupiedTemplate.Id,
            now,
            revision: 0,
            deleted: false);

        var templateConflict = Scenario.Create(now.AddMilliseconds(35), ownerId, 45);
        templateConflict.Link.DeletedAt = now;
        templateConflict.Link.Revision = 7;
        var duplicateTemplateLink = CreateCompetitionChallenge(
            Guid.CreateVersion7(now.AddMilliseconds(39)),
            templateConflict.Competition.Id,
            templateConflict.Template.Id,
            now,
            revision: 0,
            deleted: false,
            order: 2);

        var concurrentDelete = Scenario.Create(now.AddMilliseconds(40), ownerId, 50);
        var concurrentRestore = Scenario.Create(now.AddMilliseconds(50), ownerId, 60);
        concurrentRestore.Link.DeletedAt = now;
        var hintMutation = Scenario.Create(now.AddMilliseconds(60), ownerId, 70);

        db.Competitions.AddRange(
            revision.Competition,
            templateMissing.Competition,
            modeMismatch.Competition,
            orderConflict.Competition,
            templateConflict.Competition,
            concurrentDelete.Competition,
            concurrentRestore.Competition,
            hintMutation.Competition);
        db.Challenges.AddRange(
            revision.Template,
            templateMissing.Template,
            modeMismatch.Template,
            orderConflict.Template,
            occupiedTemplate,
            templateConflict.Template,
            concurrentDelete.Template,
            concurrentRestore.Template,
            hintMutation.Template);
        db.CompetitionChallenges.AddRange(
            revision.Link,
            templateMissing.Link,
            modeMismatch.Link,
            orderConflict.Link,
            occupiedLink,
            templateConflict.Link,
            duplicateTemplateLink,
            concurrentDelete.Link,
            concurrentRestore.Link,
            hintMutation.Link);
        await db.SaveChangesAsync(cancellationToken);

        return new(
            now,
            revision.Competition.Id,
            revision.Link.Id,
            templateMissing.Competition.Id,
            templateMissing.Link.Id,
            modeMismatch.Competition.Id,
            modeMismatch.Link.Id,
            orderConflict.Competition.Id,
            orderConflict.Link.Id,
            templateConflict.Competition.Id,
            templateConflict.Link.Id,
            concurrentDelete.Competition.Id,
            concurrentDelete.Link.Id,
            concurrentRestore.Competition.Id,
            concurrentRestore.Link.Id,
            hintMutation.Competition.Id,
            hintMutation.Link.Id);
    }

    private static Competition CreateCompetition(
        Guid id,
        Guid ownerId,
        long leaderboardRevision,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Title = $"Lifecycle {leaderboardRevision}",
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            ConfigurationUpdatedAt = now,
            LeaderboardRevision = leaderboardRevision,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Challenge CreateChallengeTemplate(
        Guid id,
        Guid ownerId,
        GameMode mode,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Mode = mode,
            Visibility = ChallengeVisibility.Private,
            Title = $"Lifecycle {id:N}",
            Direction = "Web",
            DefinitionJson = """{"schemaVersion":1}""",
            CreatedAt = now,
            UpdatedAt = now
        };

    private static CompetitionChallenge CreateCompetitionChallenge(
        Guid id,
        Guid competitionId,
        Guid challengeId,
        DateTimeOffset now,
        int revision,
        bool deleted,
        int order = 1) =>
        new()
        {
            Id = id,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 500,
            Order = order,
            RulesJson = """{"schemaVersion":1}""",
            Revision = revision,
            UpdatedAt = now,
            DeletedAt = deleted ? now : null
        };

    private sealed record Scenario(
        Competition Competition,
        Challenge Template,
        CompetitionChallenge Link)
    {
        public static Scenario Create(
            DateTimeOffset now,
            Guid ownerId,
            long leaderboardRevision)
        {
            var competitionId = Guid.CreateVersion7(now.AddMilliseconds(1));
            var challengeId = Guid.CreateVersion7(now.AddMilliseconds(2));
            return new(
                CreateCompetition(
                    competitionId,
                    ownerId,
                    leaderboardRevision,
                    now),
                CreateChallengeTemplate(challengeId, ownerId, GameMode.Ctf, now),
                CreateCompetitionChallenge(
                    Guid.CreateVersion7(now.AddMilliseconds(3)),
                    competitionId,
                    challengeId,
                    now,
                    revision: 0,
                    deleted: false));
        }
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid RevisionCompetitionId,
        Guid RevisionCompetitionChallengeId,
        Guid TemplateMissingCompetitionId,
        Guid TemplateMissingCompetitionChallengeId,
        Guid ModeMismatchCompetitionId,
        Guid ModeMismatchCompetitionChallengeId,
        Guid OrderConflictCompetitionId,
        Guid OrderConflictCompetitionChallengeId,
        Guid TemplateConflictCompetitionId,
        Guid TemplateConflictCompetitionChallengeId,
        Guid ConcurrentDeleteCompetitionId,
        Guid ConcurrentDeleteCompetitionChallengeId,
        Guid ConcurrentRestoreCompetitionId,
        Guid ConcurrentRestoreCompetitionChallengeId,
        Guid HintMutationCompetitionId,
        Guid HintMutationCompetitionChallengeId);
}
