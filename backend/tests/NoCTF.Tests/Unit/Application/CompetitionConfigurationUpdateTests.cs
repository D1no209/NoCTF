using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionConfigurationUpdateTests
{
    [Test]
    public async Task Running_non_destructive_change_is_persisted()
    {
        var store = new Store(CompetitionStatus.Running);
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator());

        var result = await useCase.ExecuteAsync(store.CompetitionId, Configuration(2), DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.AllowWhileRunning).IsTrue();
    }

    [Test]
    public async Task Running_destructive_change_is_allowed()
    {
        var store = new Store(CompetitionStatus.Running);
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator());

        var result = await useCase.ExecuteAsync(store.CompetitionId, Configuration(2), DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Paused_change_is_allowed()
    {
        var store = new Store(CompetitionStatus.Paused);
        var useCase = new UpdateCompetitionConfiguration(
            store, new Validator());

        var result = await useCase.ExecuteAsync(store.CompetitionId, Configuration(2), DateTimeOffset.UtcNow);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Active_awd_defaults_require_an_operator_reason()
    {
        var store = new Store(CompetitionStatus.Running, GameMode.Awd);
        var useCase = new UpdateCompetitionConfiguration(store, new Validator());
        var config = CompetitionModeConfigurationDefaults.Create(GameMode.Awd, store.CompetitionId);
        await Assert.That((await useCase.ExecuteAsync(store.CompetitionId, config, DateTimeOffset.UtcNow)).FailureCode)
            .IsEqualTo(CompetitionConfigurationFailureCode.RuleChangeReasonRequired);
        await Assert.That(store.UpdateCalls).IsEqualTo(0);
        var audit = new NoCTF.Application.Competitions.Awd.AwdConfigurationChangeAudit(Guid.NewGuid(), "Adjust next-round budget");
        await Assert.That((await useCase.ExecuteAsync(store.CompetitionId, config, DateTimeOffset.UtcNow, audit: audit)).Succeeded).IsTrue();
        await Assert.That(store.UpdateCalls).IsEqualTo(1);
    }

    private sealed class Store(CompetitionStatus status, GameMode mode = GameMode.Ctf) : ICompetitionConfigurationStore
    {
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public int UpdateCalls { get; private set; }
        public bool AllowWhileRunning { get; private set; }

        public Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionConfigurationView?>(new(
                CompetitionId, mode, CompetitionModeConfigurationDefaults.Create(mode, CompetitionId), status, 2, [], DateTimeOffset.UtcNow));

        public Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
            Guid competitionId,
            CompetitionModeConfiguration configuration,
            bool allowWhileRunning,
            DateTimeOffset now,
            CancellationToken cancellationToken, NoCTF.Application.Competitions.Awd.AwdConfigurationChangeAudit? audit = null)
        {
            UpdateCalls++;
            AllowWhileRunning = allowWhileRunning;
            return Task.FromResult(new CompetitionConfigurationUpdateResult(
                new(competitionId, mode, configuration, status, 2, [], now)));
        }
    }

    private sealed class Validator : ICompetitionConfigurationValidator
    {
        public IReadOnlyList<string> Validate(
            GameMode mode,
            CompetitionModeConfiguration configuration,
            int eligibleTeamCount,
            IReadOnlyList<CompetitionChallengeRules> challengeRules) => [];
    }

    private static CtfCompetitionModeConfiguration Configuration(long penalty) => new()
    {
        WrongSubmissionPenalty = penalty
    };

}
