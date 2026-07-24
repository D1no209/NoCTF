using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpEffectiveConfigurationTests
{
    [Test]
    public async Task Challenge_nulls_inherit_complete_competition_defaults()
    {
        var effective = AwdpConfigurationResolver.Resolve(
            GameModeDefaultConfiguration.GetCompetitionJson(
                NoCTF.Domain.Competitions.GameMode.Awdp),
            new GameModeChallengeConfigurationCatalog().GetDefaultJson(
                NoCTF.Domain.Competitions.GameMode.Awdp));

        await Assert.That(effective.Break.Points).IsEqualTo(50L);
        await Assert.That(effective.Fix.Points).IsEqualTo(50L);
        await Assert.That(effective.BreakWrongPenalty).IsEqualTo(0L);
        await Assert.That(effective.FixFailurePenalty).IsEqualTo(0L);
        await Assert.That(effective.ViolationPenalty).IsEqualTo(100L);
        await Assert.That(effective.ServiceDownPenalty).IsEqualTo(50L);
        await Assert.That(effective.RequireBreakBeforeFix).IsTrue();
        await Assert.That(effective.MaxBreakSubmissions).IsEqualTo(10);
        await Assert.That(effective.MaxFixSubmissions).IsEqualTo(10);
        await Assert.That(effective.EvaluationDispatchMode)
            .IsEqualTo(EvaluationDispatchMode.Automatic);
        await Assert.That(effective.PatchEntrypoint).IsEqualTo("fix.sh");
        await Assert.That(effective.PatchTimeoutSeconds).IsEqualTo(60);
        await Assert.That(effective.ReadyTimeoutSeconds).IsEqualTo(30);
    }

    [Test]
    public async Task Draft_defaults_are_save_valid_but_not_start_valid()
    {
        var competition = AwdpConfigurationParser.ParseCompetition(
            GameModeDefaultConfiguration.GetCompetitionJson(
                NoCTF.Domain.Competitions.GameMode.Awdp));
        var challenge = AwdpConfigurationParser.ParseChallenge(
            new GameModeChallengeConfigurationCatalog().GetDefaultJson(
                NoCTF.Domain.Competitions.GameMode.Awdp));
        var effective = AwdpConfigurationResolver.Resolve(competition, challenge);

        await Assert.That(AwdpConfigurationValidator.Validate(competition)).IsEmpty();
        await Assert.That(AwdpConfigurationValidator.Validate(challenge)).IsEmpty();
        await Assert.That(AwdpConfigurationValidator.ValidateForStart(effective))
            .Contains("Runtime is required before an AWDP competition can start.");
        await Assert.That(AwdpConfigurationValidator.ValidateForStart(effective))
            .Contains("Checker is required before an AWDP competition can start.");
        await Assert.That(AwdpConfigurationValidator.ValidateForStart(effective))
            .Contains("TargetPort must be a valid TCP port before an AWDP competition can start.");
    }

    [Test]
    public async Task Competition_validator_applies_completeness_only_at_start()
    {
        var competition = GameModeDefaultConfiguration.GetCompetitionJson(
            NoCTF.Domain.Competitions.GameMode.Awdp);
        var challenge = new GameModeChallengeConfigurationCatalog().GetDefaultJson(
            NoCTF.Domain.Competitions.GameMode.Awdp);
        var validator = new GameModeCompetitionConfigurationValidator();

        await Assert.That(validator.Validate(
                NoCTF.Domain.Competitions.GameMode.Awdp,
                competition,
                1,
                [challenge]))
            .IsEmpty();
        await Assert.That(validator.ValidateForStart(
                NoCTF.Domain.Competitions.GameMode.Awdp,
                competition,
                1,
                [challenge]))
            .Contains("Runtime is required before an AWDP competition can start.");
    }

    [Test]
    public async Task Start_gate_rejects_incomplete_published_awdp_challenge()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competition = GameModeDefaultConfiguration.GetCompetitionJson(
            NoCTF.Domain.Competitions.GameMode.Awdp);
        var challenge = new GameModeChallengeConfigurationCatalog().GetDefaultJson(
            NoCTF.Domain.Competitions.GameMode.Awdp);
        var gate = new CompetitionStartGate(
            new StartGateStore(new(
                competitionId,
                NoCTF.Domain.Competitions.GameMode.Awdp,
                NoCTF.Domain.Competitions.CompetitionStatus.Published,
                competition,
                [new(challengeId, challenge, true)],
                1)),
            new GameModeCompetitionConfigurationValidator(),
            new GameModeChallengeConfigurationCatalog());

        var errors = await gate.ValidateAsync(competitionId);

        await Assert.That(errors).IsNotNull();
        await Assert.That(errors!.Select(error => error.Message))
            .Contains("Runtime is required before an AWDP competition can start.");
    }

    private sealed class StartGateStore(CompetitionStartGateSnapshot snapshot)
        : ICompetitionStartGateStore
    {
        public Task<CompetitionStartGateSnapshot?> LoadAsync(
            Guid competitionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStartGateSnapshot?>(
                competitionId == snapshot.CompetitionId ? snapshot : null);
    }
}
