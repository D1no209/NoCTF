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
            0,
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
            0,
            "{}",
            DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("invalid_configuration");
        await Assert.That(store.UpdateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateChallengeConfiguration_RevisionChanged_ReturnsConflict()
    {
        var store = new Store(View(CompetitionStatus.Published)) { Conflict = true };
        var useCase = new UpdateChallengeConfiguration(
            store,
            new Catalog());

        var result = await useCase.ExecuteAsync(
            store.Current!.CompetitionId,
            store.Current.CompetitionChallengeId,
            4,
            ValidJson,
            DateTimeOffset.UtcNow);

        await Assert.That(result.ErrorCode).IsEqualTo("configuration_conflict");
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
            current.Revision,
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
        0,
        4,
        status,
        2,
        DateTimeOffset.UtcNow);

    private sealed class Store(ChallengeConfigurationView? current) : IChallengeConfigurationStore
    {
        public ChallengeConfigurationView? Current { get; } = current;
        public bool Conflict { get; init; }
        public int UpdateCalls { get; private set; }

        public Task<ChallengeConfigurationView?> FindAsync(
            Guid competitionId,
            Guid challengeId,
            CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<ChallengeConfigurationUpdateResult> TryUpdateAsync(
            Guid competitionId,
            Guid challengeId,
            int expectedRevision,
            int expectedCompetitionConfigurationRevision,
            string json,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.FromResult(Conflict || Current is null
                ? new ChallengeConfigurationUpdateResult(null, ChallengeConfigurationUpdateFailure.RevisionConflict)
                : new ChallengeConfigurationUpdateResult(Current with { Json = json, Revision = expectedRevision + 1, UpdatedAt = updatedAt }));
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
