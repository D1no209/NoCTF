using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Scoring;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionStartGateScoreLimitTests
{
    [Test]
    public async Task Start_rejects_a_persisted_base_score_above_the_configured_maximum()
    {
        var errors = await ValidateAsync(
            ScoreValueLimits.MaximumConfiguredValue + 1,
            [ScoreValueLimits.MaximumConfiguredValue]);

        await Assert.That(errors.Any(error =>
                error.Code == StartGateFailureCode.ChallengeRulesInvalid
                && error.Message.Contains("BaseScore", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task Start_rejects_a_persisted_hint_cost_above_the_configured_maximum()
    {
        var errors = await ValidateAsync(
            ScoreValueLimits.MaximumConfiguredValue,
            [ScoreValueLimits.MaximumConfiguredValue + 1]);

        await Assert.That(errors.Any(error =>
                error.Code == StartGateFailureCode.ChallengeRulesInvalid
                && error.Message.Contains("Hint cost", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task Start_accepts_base_score_and_hint_cost_at_the_configured_maximum()
    {
        var errors = await ValidateAsync(
            ScoreValueLimits.MaximumConfiguredValue,
            [ScoreValueLimits.MaximumConfiguredValue]);

        await Assert.That(errors.Any(error =>
                error.Code == StartGateFailureCode.ChallengeRulesInvalid))
            .IsFalse();
    }

    private static async Task<IReadOnlyList<StartGateError>> ValidateAsync(
        long baseScore,
        IReadOnlyList<long> hintCosts)
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var configurations = new GameModeChallengeConfigurationCatalog();
        var gate = new CompetitionStartGate(
            new Store(new(
                competitionId,
                GameMode.Ctf,
                CompetitionStatus.Published,
                GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf),
                [new(
                    challengeId,
                    GameMode.Ctf,
                    configurations.GetDefaultJson(GameMode.Ctf),
                    configurations.GetDefaultDefinitionJson(GameMode.Ctf),
                    true,
                    baseScore,
                    hintCosts)],
                ApprovedTeamCount: 1,
                MaxConcurrentRuntimeInstancesPerTeam: 0)),
            new GameModeCompetitionConfigurationValidator(),
            configurations);

        return (await gate.ValidateAsync(competitionId))!;
    }

    private sealed class Store(CompetitionStartGateSnapshot snapshot)
        : ICompetitionStartGateStore
    {
        public Task<CompetitionStartGateSnapshot?> LoadAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStartGateSnapshot?>(
                competitionId == snapshot.CompetitionId ? snapshot : null);
    }
}
