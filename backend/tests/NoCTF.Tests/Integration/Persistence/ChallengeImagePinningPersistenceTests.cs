using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Images;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Images;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeImagePinningPersistenceTests
{
    private const string DigestA = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(300_000)]
    public async Task Pinning_is_idempotent_atomic_and_rejects_concurrent_definition_edits(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_challenge_image_pinning")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var resolver = new MutableResolver
            {
                ["registry.example/one:latest"] =
                    new("registry.example/one@" + DigestA),
                ["registry.example/two:v1"] =
                    new("registry.example/two@" + DigestA),
                ["registry.example/three:v1"] =
                    new("registry.example/three@" + DigestA)
            };

            var firstPinAt = fixture.Now.AddMinutes(1);
            var first = await PinChallengeAsync(
                options,
                resolver,
                fixture.FirstChallengeId,
                firstPinAt,
                cancellationToken);
            await Assert.That(first.Succeeded).IsTrue();
            await AssertChallengeAsync(
                options,
                fixture.FirstChallengeId,
                "registry.example/one@" + DigestA,
                revision: 1,
                firstPinAt,
                cancellationToken);

            var repeated = await PinChallengeAsync(
                options,
                resolver,
                fixture.FirstChallengeId,
                firstPinAt.AddMinutes(1),
                cancellationToken);
            await Assert.That(repeated.Succeeded).IsTrue();
            await AssertChallengeAsync(
                options,
                fixture.FirstChallengeId,
                "registry.example/one@" + DigestA,
                revision: 1,
                firstPinAt,
                cancellationToken);

            var editedAt = firstPinAt.AddMinutes(2);
            await using (var editDb = new NoCtfDbContext(options))
            {
                await editDb.Challenges
                    .Where(challenge => challenge.Id == fixture.FirstChallengeId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            challenge => challenge.DefinitionJson,
                            Definition("registry.example/one:latest"))
                        .SetProperty(challenge => challenge.Revision, challenge => challenge.Revision + 1)
                        .SetProperty(challenge => challenge.UpdatedAt, editedAt), cancellationToken);
            }
            resolver["registry.example/one:latest"] =
                new("registry.example/one@" + DigestB);
            var drifted = await PinChallengeAsync(
                options,
                resolver,
                fixture.FirstChallengeId,
                editedAt.AddMinutes(1),
                cancellationToken);
            await Assert.That(drifted.Succeeded).IsTrue();
            await AssertChallengeAsync(
                options,
                fixture.FirstChallengeId,
                "registry.example/one@" + DigestB,
                revision: 3,
                editedAt.AddMinutes(1),
                cancellationToken);

            var blocking = new BlockingResolver(
                "registry.example/two:v1",
                new("registry.example/two@" + DigestA));
            var concurrentPin = PinChallengeAsync(
                options,
                blocking,
                fixture.SecondChallengeId,
                editedAt.AddMinutes(2),
                cancellationToken);
            await blocking.Entered.Task.WaitAsync(cancellationToken);
            var concurrentAt = editedAt.AddMinutes(3);
            await using (var concurrentDb = new NoCtfDbContext(options))
            {
                await concurrentDb.Challenges
                    .Where(challenge => challenge.Id == fixture.SecondChallengeId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            challenge => challenge.DefinitionJson,
                            Definition("registry.example/two:v2"))
                        .SetProperty(challenge => challenge.Revision, challenge => challenge.Revision + 1)
                        .SetProperty(challenge => challenge.UpdatedAt, concurrentAt), cancellationToken);
            }
            blocking.Release.TrySetResult();
            var concurrent = await concurrentPin;
            await Assert.That(concurrent.Errors[0].Code)
                .IsEqualTo(ChallengeImagePinFailureCode.RevisionConflict);
            await AssertChallengeAsync(
                options,
                fixture.SecondChallengeId,
                "registry.example/two:v2",
                revision: 1,
                concurrentAt,
                cancellationToken);

            resolver["registry.example/two:v2"] =
                new("registry.example/two@" + DigestB);
            resolver["registry.example/three:v1"] = new(
                null,
                RegistryManifestFailureCode.ManifestNotFound,
                "Registry manifest was not found.");
            var atomic = await PinCompetitionAsync(
                options,
                resolver,
                fixture.CompetitionId,
                concurrentAt.AddMinutes(1),
                cancellationToken);
            await Assert.That(atomic.Succeeded).IsFalse();
            await AssertChallengeAsync(
                options,
                fixture.SecondChallengeId,
                "registry.example/two:v2",
                revision: 1,
                concurrentAt,
                cancellationToken);
            await AssertChallengeAsync(
                options,
                fixture.ThirdChallengeId,
                "registry.example/three:v1",
                revision: 0,
                fixture.Now,
                cancellationToken);

            await using (var lifecycleDb = new NoCtfDbContext(options))
            {
                await lifecycleDb.Competitions
                    .Where(competition => competition.Id == fixture.CompetitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        competition => competition.Status,
                        CompetitionStatus.Running), cancellationToken);
            }
            await using (var updateDb = new NoCtfDbContext(options))
            {
                var pinningStore = new ChallengeImagePinningStore(updateDb);
                var catalog = new ChallengeImageDefinitionCatalog();
                var result = await new ChallengeBankStore(
                        updateDb,
                        imagePinning: pinningStore,
                        imageDefinitions: catalog)
                    .UpdateAsync(new(
                        fixture.FirstChallengeId,
                        fixture.OwnerId,
                        IsAdministrator: false,
                        GameMode.Ctf,
                        ChallengeVisibility.Private,
                        "One",
                        null,
                        "Web",
                        Definition("registry.example/one:latest"),
                        ExpectedRevision: 3,
                        UpdatedAt: concurrentAt.AddMinutes(2)), cancellationToken);
                await Assert.That(result.State)
                    .IsEqualTo(ChallengeTemplateWriteState.RuntimeImageNotPinned);
            }
            await AssertChallengeAsync(
                options,
                fixture.FirstChallengeId,
                "registry.example/one@" + DigestB,
                revision: 3,
                editedAt.AddMinutes(1),
                cancellationToken);

            var deletedAt = concurrentAt.AddMinutes(3);
            await using (var deleteChallengeDb = new NoCtfDbContext(options))
            {
                await deleteChallengeDb.CompetitionChallenges
                    .Where(item => item.Id == fixture.FirstCompetitionChallengeId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        item => item.DeletedAt,
                        deletedAt), cancellationToken);
            }
            await using (var restoreChallengeDb = new NoCtfDbContext(options))
            {
                var pinningStore = new ChallengeImagePinningStore(restoreChallengeDb);
                var catalog = new ChallengeImageDefinitionCatalog();
                var realStore = new ChallengeManagementStore(
                    restoreChallengeDb,
                    new OpenApiTransactionalMessageOutbox(),
                    imagePinning: pinningStore,
                    imageDefinitions: catalog);
                var blockingStore = new BlockingChallengeManagementStore(realStore);
                var restoreTask = new DeleteChallenge(
                        blockingStore,
                        new PinChallengeImages(pinningStore, catalog, resolver))
                    .RestoreAsync(
                        fixture.CompetitionId,
                        fixture.FirstCompetitionChallengeId,
                        expectedRevision: 0,
                        deletedAt.AddMinutes(1),
                        cancellationToken);
                await blockingStore.Entered.Task.WaitAsync(cancellationToken);
                await using (var concurrentDb = new NoCtfDbContext(options))
                {
                    await concurrentDb.Challenges
                        .Where(challenge => challenge.Id == fixture.FirstChallengeId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(
                                challenge => challenge.DefinitionJson,
                                Definition("registry.example/one@" + DigestA))
                            .SetProperty(
                                challenge => challenge.Revision,
                                challenge => challenge.Revision + 1),
                            cancellationToken);
                }
                blockingStore.Release.TrySetResult();
                await Assert.That(await restoreTask)
                    .IsEqualTo(ChallengeMutationFailure.RevisionConflict);
            }
            await using (var verifyChallengeDb = new NoCtfDbContext(options))
            {
                var challenge = await verifyChallengeDb.CompetitionChallenges
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(
                        item => item.Id == fixture.FirstCompetitionChallengeId,
                        cancellationToken);
                await Assert.That(challenge.DeletedAt).IsNotNull();
                await verifyChallengeDb.CompetitionChallenges
                    .IgnoreQueryFilters()
                    .Where(item => item.Id == fixture.FirstCompetitionChallengeId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        item => item.DeletedAt,
                        (DateTimeOffset?)null), cancellationToken);
            }

            resolver["registry.example/two:v2"] =
                new("registry.example/two@" + DigestB);
            resolver["registry.example/three:v1"] =
                new("registry.example/three@" + DigestA);
            await using (var deleteCompetitionDb = new NoCtfDbContext(options))
            {
                await deleteCompetitionDb.Competitions
                    .Where(item => item.Id == fixture.CompetitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        item => item.DeletedAt,
                        deletedAt), cancellationToken);
            }
            await using (var restoreCompetitionDb = new NoCtfDbContext(options))
            {
                var pinningStore = new ChallengeImagePinningStore(restoreCompetitionDb);
                var catalog = new ChallengeImageDefinitionCatalog();
                var realStore = new AdminCompetitionStore(
                    restoreCompetitionDb,
                    imagePinning: pinningStore,
                    imageDefinitions: catalog);
                var blockingStore = new BlockingAdminCompetitionStore(realStore);
                var restoreTask = new RestoreCompetition(
                        blockingStore,
                        new PinChallengeImages(pinningStore, catalog, resolver))
                    .ExecuteAsync(
                        fixture.CompetitionId,
                        fixture.OwnerId,
                        isAdministrator: false,
                        deletedAt.AddMinutes(2),
                        cancellationToken);
                await blockingStore.Entered.Task.WaitAsync(cancellationToken);
                await using (var concurrentDb = new NoCtfDbContext(options))
                {
                    await concurrentDb.Challenges
                        .Where(challenge => challenge.Id == fixture.FirstChallengeId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(
                                challenge => challenge.DefinitionJson,
                                Definition("registry.example/one@" + DigestB))
                            .SetProperty(
                                challenge => challenge.Revision,
                                challenge => challenge.Revision + 1),
                            cancellationToken);
                }
                blockingStore.Release.TrySetResult();
                var result = await restoreTask;
                await Assert.That(result.State)
                    .IsEqualTo(CompetitionRestoreState.ChallengeDefinitionRevisionConflict);
            }
            await using (var verifyCompetitionDb = new NoCtfDbContext(options))
            {
                var competition = await verifyCompetitionDb.Competitions
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(
                        item => item.Id == fixture.CompetitionId,
                        cancellationToken);
                await Assert.That(competition.DeletedAt).IsNotNull();
            }
        });
    }

    private static async Task<ChallengeImagePinResult> PinChallengeAsync(
        DbContextOptions<NoCtfDbContext> options,
        IContainerRegistryManifestResolver resolver,
        Guid challengeId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new PinChallengeImages(
                new ChallengeImagePinningStore(db),
                new ChallengeImageDefinitionCatalog(),
                resolver)
            .PinChallengeAsync(challengeId, now, cancellationToken);
    }

    private static async Task<ChallengeImagePinResult> PinCompetitionAsync(
        DbContextOptions<NoCtfDbContext> options,
        IContainerRegistryManifestResolver resolver,
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await new PinChallengeImages(
                new ChallengeImagePinningStore(db),
                new ChallengeImageDefinitionCatalog(),
                resolver)
            .PinCompetitionAsync(competitionId, now, cancellationToken);
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
        var competitionId = Guid.CreateVersion7();
        var firstChallengeId = Guid.CreateVersion7();
        var secondChallengeId = Guid.CreateVersion7();
        var thirdChallengeId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "pinning-owner",
            NormalizedUserName = "PINNING-OWNER",
            Email = "pinning-owner@example.test",
            NormalizedEmail = "PINNING-OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.Organizer,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = "Image pinning competition",
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(1),
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.AddRange(
            Challenge(firstChallengeId, ownerId, "One", "registry.example/one:latest", now),
            Challenge(secondChallengeId, ownerId, "Two", "registry.example/two:v1", now),
            Challenge(thirdChallengeId, ownerId, "Three", "registry.example/three:v1", now));
        var order = 0;
        Guid? firstCompetitionChallengeId = null;
        foreach (var challengeId in new[] { firstChallengeId, secondChallengeId, thirdChallengeId })
        {
            var competitionChallengeId = Guid.CreateVersion7();
            firstCompetitionChallengeId ??= competitionChallengeId;
            db.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                BaseScore = 500,
                Order = order++,
                IsPublished = true,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(
            competitionId,
            ownerId,
            firstChallengeId,
            secondChallengeId,
            thirdChallengeId,
            firstCompetitionChallengeId!.Value,
            now);
    }

    private static Challenge Challenge(
        Guid id,
        Guid ownerId,
        string title,
        string image,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = title,
            Direction = "Web",
            DefinitionJson = Definition(image),
            CreatedAt = now,
            UpdatedAt = now
        };

    private static string Definition(string image) =>
        JsonSerializer.Serialize(new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            Points: null,
            BloodRewards: null,
            Runtime: new(
                RuntimeAllocation.PerTeam,
                new ContainerRuntimeDefinition(image))), JsonOptions);

    private static async Task AssertChallengeAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid challengeId,
        string expectedImage,
        int revision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var challenge = await db.Challenges.AsNoTracking()
            .SingleAsync(item => item.Id == challengeId, cancellationToken);
        var definition = new ChallengeImageDefinitionCatalog().Read(
            challenge.Mode,
            challenge.DefinitionJson);
        await Assert.That(definition.Images![0].Image).IsEqualTo(expectedImage);
        await Assert.That(challenge.Revision).IsEqualTo(revision);
        await Assert.That(challenge.UpdatedAt).IsEqualTo(updatedAt);
    }

    private sealed class MutableResolver : IContainerRegistryManifestResolver
    {
        private readonly Dictionary<string, RegistryManifestResolution> resolutions =
            new(StringComparer.Ordinal);

        public RegistryManifestResolution this[string image]
        {
            set => resolutions[image] = value;
        }

        public Task<RegistryManifestResolution> ResolveAsync(
            string image,
            CancellationToken cancellationToken)
        {
            if (ContainerImageReference.TryParse(image, out var parsed) && parsed.IsDigest)
                return Task.FromResult(new RegistryManifestResolution(image));
            return Task.FromResult(resolutions[image]);
        }
    }

    private sealed class BlockingResolver(
        string expectedImage,
        RegistryManifestResolution resolution)
        : IContainerRegistryManifestResolver
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RegistryManifestResolution> ResolveAsync(
            string image,
            CancellationToken cancellationToken)
        {
            await Assert.That(image).IsEqualTo(expectedImage);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return resolution;
        }
    }

    private sealed class BlockingChallengeManagementStore(
        IChallengeManagementStore inner) : IChallengeManagementStore
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChallengeView?> FindAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            bool includeUnpublished,
            bool includeDeleted,
            CancellationToken cancellationToken) =>
            inner.FindAsync(
                competitionId,
                competitionChallengeId,
                includeUnpublished,
                includeDeleted,
                cancellationToken);

        public async Task<ChallengeMutationFailure?> RestoreWithTemplateFenceAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            int expectedRevision,
            int expectedTemplateRevision,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await inner.RestoreWithTemplateFenceAsync(
                competitionId,
                competitionChallengeId,
                expectedRevision,
                expectedTemplateRevision,
                now,
                cancellationToken);
        }

        public Task<ChallengeCompetitionContext?> GetCompetitionAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationResult> CreateAsync(
            CreateCompetitionChallengeCommand command,
            string configurationJson,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChallengeView>> ListAsync(
            Guid competitionId,
            bool includeUnpublished,
            bool includeDeleted,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationResult> UpdateAsync(
            UpdateCompetitionChallengeCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationFailure?> SoftDeleteAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            int expectedRevision,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ChallengeMutationFailure?> RestoreAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            int expectedRevision,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            inner.RestoreAsync(
                competitionId,
                competitionChallengeId,
                expectedRevision,
                now,
                cancellationToken);
    }

    private sealed class BlockingAdminCompetitionStore(IAdminCompetitionStore inner)
        : IAdminCompetitionStore
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CompetitionView?> FindAsync(
            Guid competitionId,
            Guid actorId,
            bool isAdministrator,
            bool includeDeleted,
            CancellationToken cancellationToken) =>
            inner.FindAsync(
                competitionId,
                actorId,
                isAdministrator,
                includeDeleted,
                cancellationToken);

        public async Task<CompetitionRestoreResult> RestoreWithChallengeFenceAsync(
            Guid competitionId,
            Guid actorId,
            bool isAdministrator,
            DateTimeOffset now,
            IReadOnlyDictionary<Guid, int> expectedChallengeRevisions,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await inner.RestoreWithChallengeFenceAsync(
                competitionId,
                actorId,
                isAdministrator,
                now,
                expectedChallengeRevisions,
                cancellationToken);
        }

        public Task<IReadOnlyList<CompetitionView>> ListAsync(
            Guid actorId,
            bool isAdministrator,
            bool includeDeleted,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompetitionRestoreResult> RestoreAsync(
            Guid competitionId,
            Guid actorId,
            bool isAdministrator,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            inner.RestoreAsync(
                competitionId,
                actorId,
                isAdministrator,
                now,
                cancellationToken);
        public Task<CompetitionHardDeletePreview?> PreviewHardDeleteAsync(
            Guid competitionId,
            Guid actorId,
            bool isAdministrator,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompetitionHardDeleteResult> HardDeleteAsync(
            Guid competitionId,
            Guid actorId,
            bool isAdministrator,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CompetitionOwnerTransferResult> TransferOwnerAsync(
            Guid competitionId,
            Guid actorId,
            bool isAdministrator,
            Guid ownerId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record Fixture(
        Guid CompetitionId,
        Guid OwnerId,
        Guid FirstChallengeId,
        Guid SecondChallengeId,
        Guid ThirdChallengeId,
        Guid FirstCompetitionChallengeId,
        DateTimeOffset Now);
}
