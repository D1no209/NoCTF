using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class ChallengeConfigurationTests
{
    [Test]
    [Arguments(CompetitionStatus.Running)]
    [Arguments(CompetitionStatus.Paused)]
    [Arguments(CompetitionStatus.Finished)]
    public async Task UpdateChallengeConfiguration_IsAllowedInEveryLifecycleState(
        CompetitionStatus status)
    {
        var store = new Store(View(status));
        var useCase = new UpdateChallengeConfiguration(
            store,
            new Catalog());

        var result = await useCase.ExecuteAsync(
            store.Current!.CompetitionId,
            store.Current.CompetitionChallengeId,
            ValidJson,
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    [Test]
    public async Task UpdateChallengeConfiguration_InvalidModeConfiguration_IsRejectedBeforeWrite()
    {
        var store = new Store(View(CompetitionStatus.Draft));
        var useCase = new UpdateChallengeConfiguration(
            store,
            new Catalog(["invalid challenge configuration"]));

        var result = await useCase.ExecuteAsync(
            store.Current!.CompetitionId,
            store.Current.CompetitionChallengeId,
            "{}",
            DateTimeOffset.UtcNow);

        await Assert.That(result.FailureCode).IsEqualTo(ChallengeConfigurationFailureCode.InvalidConfiguration);
        await Assert.That(store.UpdateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateChallengeConfiguration_RepeatedWritesUseLastValue()
    {
        var store = new Store(View(CompetitionStatus.Published));
        var useCase = new UpdateChallengeConfiguration(
            store,
            new Catalog());

        var first = await useCase.ExecuteAsync(
            store.Current!.CompetitionId,
            store.Current.CompetitionChallengeId,
            ValidJson,
            DateTimeOffset.UtcNow);
        var second = await useCase.ExecuteAsync(
            store.Current!.CompetitionId,
            store.Current.CompetitionChallengeId,
            "{\"schemaVersion\":1,\"flagPrefix\":\"latest\"}",
            DateTimeOffset.UtcNow.AddSeconds(1));

        await Assert.That(first.Succeeded).IsTrue();
        await Assert.That(second.Succeeded).IsTrue();
        await Assert.That(store.Current!.Json).Contains("latest");
    }

    [Test]
    public async Task UpdateChallengeConfiguration_Success_PersistsConfiguration()
    {
        var current = View(CompetitionStatus.Draft);
        var store = new Store(current);
        var useCase = new UpdateChallengeConfiguration(store, new Catalog());

        var result = await useCase.ExecuteAsync(
            current.CompetitionId,
            current.CompetitionChallengeId,
            ValidJson,
            DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    private const string ValidJson = """{"schemaVersion":1}""";

    private static ChallengeConfigurationView View(CompetitionStatus status) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        GameMode.Ctf,
        ValidJson,
        ValidJson,
        status,
        2,
        DateTimeOffset.UtcNow);

    private sealed class Store(ChallengeConfigurationView? current) : IChallengeConfigurationStore
    {
        public ChallengeConfigurationView? Current { get; private set; } = current;
        public int UpdateCalls { get; private set; }

        public Task<ChallengeConfigurationView?> FindAsync(
            Guid competitionId,
            Guid challengeId,
            CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<ChallengeConfigurationUpdateResult> TryUpdateAsync(
            Guid competitionId,
            Guid challengeId,
            string json,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            Current = Current is null
                ? null
                : Current with { Json = json, UpdatedAt = updatedAt };
            return Task.FromResult(Current is null
                ? new ChallengeConfigurationUpdateResult(null, ChallengeConfigurationUpdateFailure.ChallengeNotFound)
                : new ChallengeConfigurationUpdateResult(Current));
        }
    }

    private sealed class Catalog(IReadOnlyList<string>? errors = null) : IChallengeConfigurationCatalog
    {
        public string GetDefaultJson(GameMode mode) => ValidJson;
        public IReadOnlyList<string> Validate(
            GameMode mode,
            string json,
            string competitionConfigurationJson,
            int eligibleTeamCount) => errors ?? [];
    }

}
