using NoCTF.Application.Challenges.Images;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Tests.Unit.Application;

public sealed class ChallengeImagePinningTests
{
    private static readonly ChallengeImageLocation Runtime =
        new(ChallengeImageLocationKind.RuntimeContainer);
    private static readonly ChallengeImageLocation Checker =
        new(ChallengeImageLocationKind.Checker);
    private const string Digest =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task Any_resolution_failure_keeps_every_definition_unchanged()
    {
        var first = Snapshot("first-tag");
        var second = Snapshot("second-tag");
        var store = new Store([first, second]);
        var resolver = new Resolver(new Dictionary<string, RegistryManifestResolution>
        {
            ["first-tag"] = new($"first@{Digest}"),
            ["second-tag"] = new(
                null,
                RegistryManifestFailureCode.ManifestNotFound,
                "Registry manifest was not found.")
        });

        var result = await new PinChallengeImages(store, new Definitions(), resolver)
            .PinCompetitionAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Errors[0].Code)
            .IsEqualTo(ChallengeImagePinFailureCode.RegistryManifestNotFound);
        await Assert.That(store.ApplyCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Duplicate_image_slots_are_resolved_once_and_committed_together()
    {
        var snapshot = Snapshot("shared-tag");
        var store = new Store([snapshot]);
        var definitions = new Definitions(includeChecker: true);
        var resolver = new Resolver(new Dictionary<string, RegistryManifestResolution>
        {
            ["shared-tag"] = new($"shared@{Digest}")
        });

        var result = await new PinChallengeImages(store, definitions, resolver)
            .PinChallengeAsync(snapshot.ChallengeId, DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(resolver.Calls).IsEqualTo(1);
        await Assert.That(store.ApplyCalls).IsEqualTo(1);
        await Assert.That(store.LastPlans[0].PinnedDefinitionJson)
            .IsEqualTo($"pinned:shared@{Digest}|shared@{Digest}");
    }

    [Test]
    public async Task Concurrent_definition_change_returns_revision_conflict()
    {
        var snapshot = Snapshot("moving-tag");
        var store = new Store([snapshot])
        {
            CommitState = ChallengeImagePinCommitState.RevisionConflict
        };
        var resolver = new Resolver(new Dictionary<string, RegistryManifestResolution>
        {
            ["moving-tag"] = new($"moving@{Digest}")
        });

        var result = await new PinChallengeImages(store, new Definitions(), resolver)
            .PinChallengeAsync(snapshot.ChallengeId, DateTimeOffset.UtcNow);

        await Assert.That(result.Errors[0].Code)
            .IsEqualTo(ChallengeImagePinFailureCode.RevisionConflict);
    }

    [Test]
    public async Task Runtime_guard_is_a_total_predicate_for_malformed_compose()
    {
        var template = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                "services: [not-a-mapping]",
                new Dictionary<string, RuntimeResourceLimits>()));

        var pinned = ChallengeImagePinningPolicy.AreRuntimeImagesPinned(template);

        await Assert.That(pinned).IsFalse();
    }

    [Test]
    [Timeout(10_000)]
    public async Task Large_compose_resolution_is_parallel_but_bounded(
        CancellationToken cancellationToken)
    {
        var snapshots = Enumerable.Range(0, 64)
            .Select(index => Snapshot($"registry.example/image-{index}:v1"))
            .ToArray();
        var store = new Store(snapshots);
        var resolver = new GatedResolver(requiredConcurrency: 8);
        var execution = new PinChallengeImages(store, new Definitions(), resolver)
            .PinCompetitionAsync(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                cancellationToken);

        await resolver.RequiredConcurrencyReached.Task.WaitAsync(cancellationToken);
        resolver.Release.TrySetResult();
        var result = await execution;

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(resolver.CallCount).IsEqualTo(64);
        await Assert.That(resolver.MaximumConcurrency).IsEqualTo(8);
    }

    private static ChallengeImagePinSnapshot Snapshot(string definition) =>
        new(Guid.NewGuid(), GameMode.Ctf, definition, 7);

    private sealed class Definitions(bool includeChecker = false)
        : IChallengeImageDefinitionCatalog
    {
        public ChallengeImageDefinitionReadResult Read(GameMode mode, string definitionJson) =>
            new(includeChecker
                ? [new(Runtime, definitionJson), new(Checker, definitionJson)]
                : [new(Runtime, definitionJson)]);

        public string Replace(
            GameMode mode,
            string definitionJson,
            IReadOnlyDictionary<ChallengeImageLocation, string> replacements) =>
            includeChecker
                ? $"pinned:{replacements[Runtime]}|{replacements[Checker]}"
                : $"pinned:{replacements[Runtime]}";
    }

    private sealed class Resolver(
        IReadOnlyDictionary<string, RegistryManifestResolution> resolutions)
        : IContainerRegistryManifestResolver
    {
        public int Calls { get; private set; }

        public Task<RegistryManifestResolution> ResolveAsync(
            string image,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(resolutions[image]);
        }
    }

    private sealed class GatedResolver(int requiredConcurrency)
        : IContainerRegistryManifestResolver
    {
        private int active;
        private int callCount;
        private int maximumConcurrency;

        public TaskCompletionSource RequiredConcurrencyReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount => Volatile.Read(ref callCount);
        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        public async Task<RegistryManifestResolution> ResolveAsync(
            string image,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            var current = Interlocked.Increment(ref active);
            while (true)
            {
                var observed = Volatile.Read(ref maximumConcurrency);
                if (current <= observed
                    || Interlocked.CompareExchange(
                        ref maximumConcurrency,
                        current,
                        observed) == observed)
                    break;
            }
            if (current >= requiredConcurrency)
                RequiredConcurrencyReached.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return new($"{image.Split(':')[0]}@{Digest}");
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }
    }

    private sealed class Store(IReadOnlyList<ChallengeImagePinSnapshot> snapshots)
        : IChallengeImagePinningStore
    {
        public int ApplyCalls { get; private set; }
        public IReadOnlyList<ChallengeImagePinPlan> LastPlans { get; private set; } = [];
        public ChallengeImagePinCommitState CommitState { get; init; } =
            ChallengeImagePinCommitState.Succeeded;

        public Task<ChallengeImagePinSnapshot?> LoadChallengeAsync(
            Guid challengeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(snapshots.SingleOrDefault(item => item.ChallengeId == challengeId));

        public Task<ChallengeImagePinSnapshot?> LoadCompetitionChallengeAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ChallengeImagePinSnapshot?>(snapshots.FirstOrDefault());

        public Task<CompetitionImagePinSnapshot> LoadCompetitionAsync(
            Guid competitionId,
            bool includeDeleted,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CompetitionImagePinSnapshot(true, snapshots));

        public Task<ChallengeImagePinCommitState> ApplyAsync(
            IReadOnlyList<ChallengeImagePinPlan> plans,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            ApplyCalls++;
            LastPlans = plans;
            return Task.FromResult(CommitState);
        }

        public Task<bool> RequiresPinnedDefinitionAsync(
            Guid challengeId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<ChallengeTemplateWriteBoundary> LockTemplateWriteBoundaryAsync(
            Guid challengeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ChallengeTemplateWriteBoundary(true, false));

        public Task<CompetitionImagePublicationBoundary> LockCompetitionPublicationBoundaryAsync(
            Guid competitionId,
            bool includeDeleted,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CompetitionImagePublicationBoundary(
                true,
                CompetitionStatus.Draft,
                snapshots));

        public Task<IReadOnlyList<ChallengeImagePinSnapshot>> LockCompetitionAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult(snapshots);

        public Task<ChallengeImagePinSnapshot?> LockCompetitionChallengeAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ChallengeImagePinSnapshot?>(snapshots.FirstOrDefault());
    }
}
