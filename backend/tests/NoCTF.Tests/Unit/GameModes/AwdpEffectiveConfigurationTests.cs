using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring;
using NoCTF.GameModes.Scoring;

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

        await Assert.That(effective.Break.InitialPoints).IsEqualTo(500L);
        await Assert.That(effective.Fix.InitialPoints).IsEqualTo(500L);
        await Assert.That(effective.FlagWrongPenalty).IsEqualTo(0L);
        await Assert.That(effective.ExploitSucceededPenalty).IsEqualTo(0L);
        await Assert.That(effective.ServiceAbnormalPenalty).IsEqualTo(0L);
        await Assert.That(effective.RequireBreakBeforeFix).IsFalse();
        await Assert.That(effective.MaxBreakSubmissions).IsEqualTo(10);
        await Assert.That(effective.MaxFixSubmissions).IsEqualTo(10);
        await Assert.That(effective.EvaluationDispatchMode)
            .IsEqualTo(EvaluationDispatchMode.Automatic);
        await Assert.That(effective.PatchEntrypoint).IsEqualTo("fix.sh");
        await Assert.That(effective.PatchTimeoutSeconds).IsEqualTo(60);
        await Assert.That(effective.ReadyTimeoutSeconds).IsEqualTo(30);
        await Assert.That(effective.MaximumPatchUploadBytes)
            .IsEqualTo(PatchUploadRules.DefaultMaximumArchiveBytes);
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
    public async Task Current_awdp_accepts_independent_curves_and_break_gated_fixes()
    {
        var competition = AwdpConfigurationParser.ParseCompetition(
            GameModeDefaultConfiguration.GetCompetitionJson(
                NoCTF.Domain.Competitions.GameMode.Awdp));
        var challenge = new AwdpChallengeConfiguration(
            AwdpChallengeConfiguration.CurrentSchemaVersion,
            new(100, 20, 8, ScoreDecayMode.Exponential),
            new(80, 10, 6, ScoreDecayMode.Logarithmic),
            RequireBreakBeforeFix: true,
            MaxBreakSubmissions: null,
            MaxFixSubmissions: null,
            Runtime: new(
                RuntimeAllocation.PerTeam,
                new ContainerRuntimeDefinition(
                    "awdp-target:latest",
                    PortMappings: new Dictionary<int, int> { [31337] = 0 },
                    FlagEnvironmentVariableName: "FLAG",
                    InternalPorts: [31337]),
                new RuntimeResourceLimits(268_435_456, 500_000_000, 128),
                UrlBindings: [new(
                    "tcp://{HOST}:{PORT}",
                    RuntimeExposure.OwnerOnly,
                    31337)],
                FlagSource: RuntimeFlagSource.PerTeam),
            Checker: new RunnerJobConfiguration("awdp-checker:latest"));
        var effective = AwdpConfigurationResolver.Resolve(competition, challenge);

        await Assert.That(effective.RequireBreakBeforeFix).IsTrue();
        await Assert.That(effective.Break.DecayMode)
            .IsEqualTo(ScoreDecayMode.Exponential);
        await Assert.That(effective.Fix.DecayMode)
            .IsEqualTo(ScoreDecayMode.Logarithmic);
        await Assert.That(AwdpConfigurationValidator.Validate(challenge)).IsEmpty();
        await Assert.That(AwdpConfigurationValidator.ValidateForStart(effective)).IsEmpty();
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
                [new(
                    challengeId,
                    NoCTF.Domain.Competitions.GameMode.Awdp,
                    challenge,
                    challenge,
                    true,
                    100,
                    [])],
                1,
                0)),
            new GameModeCompetitionConfigurationValidator(),
            new GameModeChallengeConfigurationCatalog());

        var errors = await gate.ValidateAsync(competitionId);

        await Assert.That(errors).IsNotNull();
        await Assert.That(errors!.Select(error => error.Message))
            .Contains("Runtime is required before an AWDP competition can start.");
    }

    [Test]
    public async Task Start_gate_rejects_legacy_awdp_competition_configuration()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var currentChallenge = new GameModeChallengeConfigurationCatalog()
            .GetDefaultJson(NoCTF.Domain.Competitions.GameMode.Awdp);
        var gate = new CompetitionStartGate(
            new StartGateStore(new(
                competitionId,
                NoCTF.Domain.Competitions.GameMode.Awdp,
                NoCTF.Domain.Competitions.CompetitionStatus.Published,
                """{"schemaVersion":1}""",
                [new(
                    challengeId,
                    NoCTF.Domain.Competitions.GameMode.Awdp,
                    currentChallenge,
                    currentChallenge,
                    true,
                    100,
                    [])],
                1,
                0)),
            new GameModeCompetitionConfigurationValidator(),
            new GameModeChallengeConfigurationCatalog());

        var errors = await gate.ValidateAsync(competitionId);

        await Assert.That(errors).IsNotNull();
        await Assert.That(errors!.Any(error =>
            error.Code == StartGateFailureCode.CompetitionConfigurationInvalid
            && error.Message.Contains("schemaVersion 1 is unsupported", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task Start_gate_rejects_legacy_awdp_template_definition()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var currentRules = new GameModeChallengeConfigurationCatalog()
            .GetDefaultJson(NoCTF.Domain.Competitions.GameMode.Awdp);
        var gate = new CompetitionStartGate(
            new StartGateStore(new(
                competitionId,
                NoCTF.Domain.Competitions.GameMode.Awdp,
                NoCTF.Domain.Competitions.CompetitionStatus.Published,
                GameModeDefaultConfiguration.GetCompetitionJson(
                    NoCTF.Domain.Competitions.GameMode.Awdp),
                [new(
                    challengeId,
                    NoCTF.Domain.Competitions.GameMode.Awdp,
                    currentRules,
                    """{"schemaVersion":1}""",
                    true,
                    100,
                    [])],
                1,
                0)),
            new GameModeCompetitionConfigurationValidator(),
            new GameModeChallengeConfigurationCatalog());

        var errors = await gate.ValidateAsync(competitionId);

        await Assert.That(errors).IsNotNull();
        await Assert.That(errors!.Any(error =>
            error.Code == StartGateFailureCode.RuntimeDefinitionInvalid
            && error.CompetitionChallengeId == challengeId
            && error.Message.Contains("schemaVersion 1 is unsupported", StringComparison.Ordinal)))
            .IsTrue();
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
