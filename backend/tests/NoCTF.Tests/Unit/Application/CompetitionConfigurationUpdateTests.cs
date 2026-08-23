using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionConfigurationUpdateTests
{
    [Test]
    public async Task Running_non_destructive_change_is_persisted()
    {
        var store = new Store(CompetitionStatus.Running);
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator());

        var result = await useCase.ExecuteAsync(store.CompetitionId, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.AllowWhileRunning).IsTrue();
    }

    [Test]
    public async Task Running_destructive_change_is_allowed()
    {
        var store = new Store(CompetitionStatus.Running);
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator());

        var result = await useCase.ExecuteAsync(store.CompetitionId, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Paused_change_is_allowed()
    {
        var store = new Store(CompetitionStatus.Paused);
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator());

        var result = await useCase.ExecuteAsync(store.CompetitionId, "{\"value\":2}", DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    private sealed class Store(CompetitionStatus status) : ICompetitionConfigurationStore
    {
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public int UpdateCalls { get; private set; }
        public bool AllowWhileRunning { get; private set; }

        public Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionConfigurationView?>(new(
                CompetitionId, GameMode.Ctf, "{\"value\":1}", status, 2, [], DateTimeOffset.UtcNow));

        public Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
            Guid competitionId,
            string json,
            bool allowWhileRunning,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            AllowWhileRunning = allowWhileRunning;
            return Task.FromResult(new CompetitionConfigurationUpdateResult(
                new(competitionId, GameMode.Ctf, json, status, 2, [], now)));
        }
    }

    private sealed class Validator : ICompetitionConfigurationValidator
    {
        public IReadOnlyList<string> Validate(
            GameMode mode,
            string json,
            int eligibleTeamCount,
            IReadOnlyList<string> challengeConfigurationJsons) => [];
    }

}
