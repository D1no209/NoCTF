using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Challenges.Images;
using NoCTF.Domain.Competitions;
using NSubstitute;
using Microsoft.Extensions.Logging;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionLifecycleUseCaseTests
{
    [Test]
    public async Task Automatic_deadline_finishes_without_starting_an_already_ended_competition()
    {
        var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.CreateVersion7();
        var store = new AdvancingStore(new CompetitionLifecycleSnapshot(
            competitionId,
            CompetitionStatus.Published,
            now.AddMinutes(-2),
            now.AddMinutes(-1)));
        var transitions = await new NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase(store)
            .ExecuteAsync(now);

        await Assert.That(transitions).HasSingleItem();
        await Assert.That(transitions[0].From).IsEqualTo(CompetitionStatus.Published);
        await Assert.That(transitions[0].To).IsEqualTo(CompetitionStatus.Finished);
        await Assert.That(store.Status).IsEqualTo(CompetitionStatus.Finished);
    }

    [Test]
    public async Task Automatic_finishes_are_processed_before_registry_resolution()
    {
        var now = DateTimeOffset.UtcNow;
        var startingCompetitionId = Guid.CreateVersion7();
        var endingCompetitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var order = new List<string>();
        var lifecycle = Substitute.For<ICompetitionLifecycleStore>();
        lifecycle.GetDueAsync(now, Arg.Any<CancellationToken>()).Returns([
            new(
                startingCompetitionId,
                CompetitionStatus.Published,
                now.AddMinutes(-1),
                now.AddMinutes(10)),
            new(
                endingCompetitionId,
                CompetitionStatus.Running,
                now.AddHours(-1),
                now.AddMinutes(-1))
        ]);
        lifecycle.TryTransitionWithAuditAsync(
                endingCompetitionId,
                CompetitionStatus.Running,
                CompetitionStatus.Finished,
                null,
                "end_time_reached",
                true,
                CompetitionLifecycleEffects.CleanupRuntimes,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("finish");
                return true;
            });
        var pinStore = Substitute.For<IChallengeImagePinningStore>();
        pinStore.LoadCompetitionAsync(
                startingCompetitionId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(new CompetitionImagePinSnapshot(
                true,
                [new(challengeId, GameMode.Ctf, "{}", 0)]));
        var definitions = Substitute.For<IChallengeImageDefinitionCatalog>();
        definitions.Read(GameMode.Ctf, "{}").Returns(new ChallengeImageDefinitionReadResult(
            [new(new(ChallengeImageLocationKind.RuntimeContainer), "registry.example/app:v1")]));
        var resolver = Substitute.For<IContainerRegistryManifestResolver>();
        resolver.ResolveAsync("registry.example/app:v1", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("pin");
                return new RegistryManifestResolution(
                    null,
                    RegistryManifestFailureCode.RegistryUnavailable,
                    "unavailable");
            });
        var imagePinning = new PinChallengeImages(pinStore, definitions, resolver);

        _ = await new AdvanceCompetitionLifecycleUseCase(
                lifecycle,
                imagePinning: imagePinning)
            .ExecuteAsync(now);

        await Assert.That(order).Count().IsEqualTo(2);
        await Assert.That(order[0]).IsEqualTo("finish");
        await Assert.That(order[1]).IsEqualTo("pin");
    }

    [Test]
    public async Task Automatic_image_pinning_uses_one_bounded_budget_and_logs_only_identifiers()
    {
        var now = DateTimeOffset.UtcNow;
        var firstCompetitionId = Guid.CreateVersion7();
        var secondCompetitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var lifecycle = Substitute.For<ICompetitionLifecycleStore>();
        lifecycle.GetDueAsync(now, Arg.Any<CancellationToken>()).Returns([
            new(
                firstCompetitionId,
                CompetitionStatus.Published,
                now.AddMinutes(-1),
                now.AddMinutes(10)),
            new(
                secondCompetitionId,
                CompetitionStatus.Published,
                now.AddMinutes(-1),
                now.AddMinutes(10))
        ]);
        var pinStore = Substitute.For<IChallengeImagePinningStore>();
        pinStore.LoadCompetitionAsync(
                firstCompetitionId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(new CompetitionImagePinSnapshot(
                true,
                [new(challengeId, GameMode.Ctf, "{}", 0)]));
        var definitions = Substitute.For<IChallengeImageDefinitionCatalog>();
        const string sensitiveImage = "registry.internal/private/secret:v1";
        definitions.Read(GameMode.Ctf, "{}").Returns(new ChallengeImageDefinitionReadResult(
            [new(new(ChallengeImageLocationKind.RuntimeContainer), sensitiveImage)]));
        var resolver = Substitute.For<IContainerRegistryManifestResolver>();
        resolver.ResolveAsync(sensitiveImage, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return new RegistryManifestResolution(null);
            });
        var logger = new CollectingLogger<AdvanceCompetitionLifecycleUseCase>();

        _ = await new AdvanceCompetitionLifecycleUseCase(
                lifecycle,
                imagePinning: new PinChallengeImages(pinStore, definitions, resolver),
                logger: logger,
                automaticImagePinningBudget: TimeSpan.FromMilliseconds(50))
            .ExecuteAsync(now);

        await pinStore.DidNotReceive().LoadCompetitionAsync(
            secondCompetitionId,
            false,
            Arg.Any<CancellationToken>());
        await Assert.That(logger.Records).HasSingleItem();
        var record = logger.Records[0];
        await Assert.That(record.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(record.Properties["FailureCode"])
            .IsEqualTo(ChallengeImagePinFailureCode.RegistryUnavailable);
        await Assert.That(record.Properties["CompetitionId"])
            .IsEqualTo(firstCompetitionId);
        await Assert.That(record.Properties).ContainsKey("ChallengeId");
        await Assert.That(record.Message.Contains(sensitiveImage, StringComparison.Ordinal))
            .IsFalse();
        await Assert.That(record.Message.Contains("secret", StringComparison.Ordinal))
            .IsFalse();
    }

    [Test]
    public async Task Resume_QueuesRuntimeProvision()
    {
        var competitionId = Guid.NewGuid();
        var store = new Store(CompetitionStatus.Paused);
        var useCase = new TransitionCompetitionLifecycle(store);

        var result = await useCase.ExecuteAsync(
            competitionId, CompetitionStatus.Running, Guid.NewGuid(), "resume");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.LastEffects.HasFlag(
            CompetitionLifecycleEffects.ProvisionRuntimes)).IsTrue();
        await Assert.That(store.LastEffects.HasFlag(
            CompetitionLifecycleEffects.CleanupRuntimes)).IsFalse();
    }

    [Test]
    public async Task Finish_QueuesRuntimeCleanup()
    {
        var competitionId = Guid.NewGuid();
        var store = new Store(CompetitionStatus.Running);
        var useCase = new TransitionCompetitionLifecycle(store);

        var result = await useCase.ExecuteAsync(
            competitionId, CompetitionStatus.Finished, Guid.NewGuid(), "finish");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.LastEffects.HasFlag(
            CompetitionLifecycleEffects.CleanupRuntimes)).IsTrue();
        await Assert.That(store.LastEffects.HasFlag(
            CompetitionLifecycleEffects.ProvisionRuntimes)).IsFalse();
    }

    private sealed class Store(CompetitionStatus status) : ICompetitionLifecycleStore
    {
        public CompetitionLifecycleEffects LastEffects { get; private set; }

        public Task<CompetitionStatus?> GetStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStatus?>(status);

        public Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompetitionLifecycleSnapshot>>([]);

        public Task<bool> TryTransitionAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> TryTransitionWithAuditAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            Guid? actorId,
            string? reason,
            bool automatic,
            CompetitionLifecycleEffects effects,
            CancellationToken cancellationToken)
        {
            LastEffects = effects;
            return Task.FromResult(true);
        }
    }

    private sealed class AdvancingStore(CompetitionLifecycleSnapshot snapshot)
        : ICompetitionLifecycleStore
    {
        public CompetitionStatus Status { get; private set; } = snapshot.Status;

        public Task<CompetitionStatus?> GetStatusAsync(
            Guid competitionId,
            CancellationToken cancellationToken) => Task.FromResult<CompetitionStatus?>(Status);

        public Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompetitionLifecycleSnapshot>>([snapshot]);

        public Task<bool> TryTransitionAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            CancellationToken cancellationToken) => ApplyAsync(from, to);

        public Task<bool> TryTransitionWithAuditAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            Guid? actorId,
            string? reason,
            bool automatic,
            CompetitionLifecycleEffects effects,
            CancellationToken cancellationToken) => ApplyAsync(from, to);

        private Task<bool> ApplyAsync(CompetitionStatus from, CompetitionStatus to)
        {
            if (Status != from)
                return Task.FromResult(false);
            Status = to;
            return Task.FromResult(true);
        }
    }

    private sealed record CapturedLog(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values
                    .Where(pair => pair.Key != "{OriginalFormat}")
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Records.Add(new(logLevel, formatter(state, exception), properties));
        }
    }

}
