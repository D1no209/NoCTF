using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.Registration;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.GameplayFact;

namespace NoCTF.Tests.Unit.Gameplay;

public sealed class ChallengeCapabilityTests
{
    [Test]
    public async Task A_template_can_supply_independent_capabilities_to_different_modes()
    {
        var definition = Repairable();
        await Assert.That(ChallengeCapabilityValidation.Validate(definition).Count).IsEqualTo(0);
        await Assert.That(GameModeChallengeConfigurationCatalog.RequiredCapabilities(new CtfCompetitionChallengeRules()).SequenceEqual([ChallengeCapabilityKind.Break])).IsTrue();
        await Assert.That(GameModeChallengeConfigurationCatalog.RequiredCapabilities(new AwdpCompetitionChallengeRules())
            .All(required => definition.Capabilities.Any(capability => capability.Kind == required))).IsTrue();
        await Assert.That(GameModeChallengeConfigurationCatalog.RequiredCapabilities(new LiveSoloCompetitionChallengeRules
            { CompletionKind = LiveSoloCompletionKind.FixAndCheck }).SequenceEqual([ChallengeCapabilityKind.Fix, ChallengeCapabilityKind.Check])).IsTrue();
    }

    [Test]
    public async Task Runtime_allocation_issuance_and_lifetime_are_selected_by_the_consumer()
    {
        var definition = Repairable();
        var ctf = new CtfCompetitionChallengeRules();
        var awdp = new AwdpCompetitionChallengeRules();
        var catalog = new ChallengeRuntimeTemplateCatalog();
        var first = catalog.Get(definition, ctf)!;
        var second = catalog.Get(definition, awdp)!;
        await Assert.That(first.FlagSource).IsEqualTo(NoCTF.Application.Runtime.Provisioning.RuntimeFlagSource.Static);
        await Assert.That(first.TtlSeconds).IsEqualTo(3600);
        await Assert.That(second.FlagSource).IsEqualTo(NoCTF.Application.Runtime.Provisioning.RuntimeFlagSource.PerTeam);
        await Assert.That(second.TtlSeconds).IsEqualTo(900);
        ctf.Runtime.TtlSeconds = 120;
        await Assert.That(catalog.Get(definition, ctf)!.TtlSeconds).IsEqualTo(120);
        await Assert.That(catalog.Get(definition, awdp)!.TtlSeconds).IsEqualTo(900);
        await Assert.That(catalog.Get(definition)!.FlagSource).IsEqualTo(NoCTF.Application.Runtime.Provisioning.RuntimeFlagSource.Static);
    }

    [Test]
    public async Task Fix_uses_its_named_service_in_a_multi_service_runtime()
    {
        var definition = Repairable();
        definition.Fix!.TargetServiceName = "database";
        await Assert.That(ChallengeCapabilityValidation.Validate(definition).Count).IsEqualTo(0);
        definition.Fix.TargetServiceName = "missing";
        await Assert.That(ChallengeCapabilityValidation.Validate(definition)
            .Any(error => error.Code == CapabilityConfigurationFailure.TargetServiceMissing && error.Capability == ChallengeCapabilityKind.Fix)).IsTrue();
    }

    [Test]
    public async Task Duplicate_capabilities_are_rejected()
    {
        var definition = Repairable();
        definition.Capabilities.Add(new BreakCapabilityDefinition());
        await Assert.That(ChallengeCapabilityValidation.Validate(definition).Single().Code).IsEqualTo(CapabilityConfigurationFailure.InvalidCapabilitySet);
    }

    [Test]
    public async Task Break_preserves_matches_without_applying_team_or_scoring_rules()
    {
        const string value = "flag{capability}";
        var first = new TemplateChallengeFlag { Id = Guid.NewGuid(), Flag = value, FlagSha256 = ManageChallengeFlags.Hash(value) };
        var second = new TeamChallengeFlag { Id = Guid.NewGuid(), TeamId = Guid.NewGuid(), Flag = value, FlagSha256 = ManageChallengeFlags.Hash(value) };
        var candidates = new[] { first, (ChallengeFlag)second }.Select(flag => new BreakCandidate(flag.Id, flag.Flag, flag.FlagSha256, flag.MatchKind, flag.TeamId)).ToArray();
        var outcome = new EvaluateBreak().Execute(new(Guid.NewGuid(), value, candidates, DateTimeOffset.UtcNow));
        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.Value!.Match).IsEqualTo(BreakMatchResult.Ambiguous);
        await Assert.That(outcome.Value.Flags.Select(flag => flag.FlagId).ToHashSet().SetEquals([first.Id, second.Id])).IsTrue();
        await Assert.That(outcome.Value.Flags.Single(flag => flag.FlagId == second.Id).OwnerTeamId).IsEqualTo(second.TeamId);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Ctf_applies_ownership_after_break_returns_matching_evidence(bool ownFlag)
    {
        var teamId = Guid.NewGuid();
        const string value = "flag{owned}";
        var flag = new TeamChallengeFlag { Id = Guid.NewGuid(), TeamId = ownFlag ? teamId : Guid.NewGuid(), Flag = value,
            FlagSha256 = ManageChallengeFlags.Hash(value), MatchKind = ChallengeFlagMatchKind.Exact };
        var fact = new BreakAttemptGameplayFact { Id = Guid.NewGuid(), TeamId = teamId, Value = value, OccurredAt = DateTimeOffset.UtcNow };
        var decision = new GameModeGameplayFactEvaluatorCatalog().Get(GameMode.Ctf).Evaluate(new(fact, [], [flag], null,
            new CtfCompetitionModeConfiguration(), new CtfCompetitionChallengeRules(), ChallengeDefinition: Repairable()));
        await Assert.That(decision.BreakOutcome!.Match).IsEqualTo(BreakMatchResult.Matched);
        await Assert.That(decision.BreakOutcome.Flags.Single().FlagId).IsEqualTo(flag.Id);
        await Assert.That(decision.Result).IsEqualTo(ownFlag ? GameplayFactResult.Correct : GameplayFactResult.Rejected);
        await Assert.That(decision.FailureCode).IsEqualTo(ownFlag ? null : GameplayFactFailureCode.ForeignTeamFlagDetected);
    }

    [Test]
    public async Task Ctf_rejects_fix_even_when_the_template_provides_it()
    {
        var fact = new FixAttemptGameplayFact { Id = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow };
        var decision = new GameModeGameplayFactEvaluatorCatalog().Get(GameMode.Ctf).Evaluate(new(fact, [], [], null,
            new CtfCompetitionModeConfiguration(), new CtfCompetitionChallengeRules(), ChallengeDefinition: Repairable()));
        await Assert.That(decision.Result).IsEqualTo(GameplayFactResult.Rejected);
        await Assert.That(decision.FailureCode).IsEqualTo(GameplayFactFailureCode.FixNotSupported);
        await Assert.That(decision.BreakOutcome).IsNull();
    }

    [Test]
    public async Task Indeterminate_control_clears_the_previous_controller_before_following_observations()
    {
        var team = Guid.NewGuid(); var internalTeam = Guid.NewGuid(); var challenge = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var input = new NoCTF.Application.Scoring.Leaderboard.LeaderboardProjectionInput(Guid.NewGuid(), GameMode.Koh,
            [new(team, "participant", false, false), new(internalTeam, "internal", false, false, EarnsScore: false, AffectsCompetitiveResults: false)],
            [new(Guid.NewGuid(), team, challenge, GameplayFactKind.ControlObservation, now, GameplayFactState.Completed, GameplayFactResult.Controlled, null),
                new(Guid.NewGuid(), null, challenge, GameplayFactKind.ControlObservation, now.AddSeconds(10), GameplayFactState.PlatformFailed, null, GameplayFactFailureCode.ProducerUnavailable),
                new(Guid.NewGuid(), internalTeam, challenge, GameplayFactKind.ControlObservation, now.AddSeconds(20), GameplayFactState.Completed, GameplayFactResult.Controlled, null)],
            [new(challenge, "Web", "Control", false)], CompetitionConfiguration: new KohCompetitionModeConfiguration { ControlPointsPerInterval = 10 });
        var result = new NoCTF.GameModes.Leaderboard.KohLeaderboardProjector().Project(input);
        await Assert.That(result.Entries.Single(entry => entry.TeamId == team).Score).IsEqualTo(10L);
    }

    [Test]
    public async Task Questionnaire_grades_assigned_questions_by_option_identity()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var correct = Guid.NewGuid(); var wrong = Guid.NewGuid();
        var revision = new QuestionnaireRevision { Id = Guid.NewGuid(), Questions =
        [
            new() { Id = first, Kind = QuestionnaireQuestionKind.SingleChoice, Options = [new() { Id = correct, IsCorrect = true }, new() { Id = wrong }] },
            new() { Id = second, Kind = QuestionnaireQuestionKind.MultipleChoice, Options = [new() { Id = Guid.NewGuid(), IsCorrect = true }] }
        ] };
        var result = new EvaluateQuestionnaire().Execute(new(Guid.NewGuid(), revision, [second, first], [new(first, [correct])], DateTimeOffset.UtcNow));
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Questions.Single(question => question.QuestionId == first).Result).IsEqualTo(QuestionnaireAnswerResult.Correct);
        await Assert.That(result.Value.Questions.Single(question => question.QuestionId == second).Result).IsEqualTo(QuestionnaireAnswerResult.Unanswered);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Questionnaire_rejects_duplicate_or_unassigned_options(bool duplicate)
    {
        var question = Guid.NewGuid(); var option = Guid.NewGuid();
        var revision = new QuestionnaireRevision { Id = Guid.NewGuid(), Questions = [new() { Id = question, Options = [new() { Id = option, IsCorrect = true }] }] };
        var result = new EvaluateQuestionnaire().Execute(new(Guid.NewGuid(), revision, [question],
            [new(question, duplicate ? [option, option] : [Guid.NewGuid()])], DateTimeOffset.UtcNow));
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(CapabilityExecutionFailure.InvalidInput);
    }

    [Test]
    public async Task Capability_copy_does_not_share_mutable_children()
    {
        var original = Repairable().Check!;
        var copied = (CheckCapabilityDefinition)ChallengeCapabilityCopy.Copy(original);
        copied.Requirements[0].Kind = CheckItemKind.Connection;
        await Assert.That(original.Requirements[0].Kind).IsEqualTo(CheckItemKind.Functionality);
    }

    [Test]
    public async Task Check_requires_each_server_selected_probe_once()
    {
        var now = DateTimeOffset.UtcNow;
        var input = new CheckEvidenceInput(Guid.NewGuid(), Guid.NewGuid(),
            [new(CheckItemKind.Functionality), new(CheckItemKind.Vulnerability)],
            [new(CheckItemKind.Functionality, null, CheckProbeResult.Passed, CheckFailureReason.None)], now.AddSeconds(-1), now.AddSeconds(30), now, now);
        await Assert.That(CheckEvidenceValidation.Evaluate(input).FailureCode).IsEqualTo(CapabilityExecutionFailure.MissingEvidence);
        var complete = input with { Probes = [.. input.Probes, new(CheckItemKind.Vulnerability, null, CheckProbeResult.TargetFailure, CheckFailureReason.VulnerabilityPresent)] };
        var result = CheckEvidenceValidation.Evaluate(complete);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.ExecutionState).IsEqualTo(CapabilityExecutionState.Completed);
        await Assert.That(result.Value.Probes.Single(probe => probe.Kind == CheckItemKind.Vulnerability).Result).IsEqualTo(CheckProbeResult.TargetFailure);
        await Assert.That(CheckEvidenceValidation.Evaluate(complete with { Probes = [.. complete.Probes, complete.Probes[0]] }).FailureCode)
            .IsEqualTo(CapabilityExecutionFailure.InvalidEvidence);
        await Assert.That(CheckEvidenceValidation.Evaluate(complete with { ReceivedAt = input.Deadline.AddSeconds(1) }).FailureCode)
            .IsEqualTo(CapabilityExecutionFailure.DeadlineExceeded);
    }

    [Test]
    public async Task Fix_command_preserves_argument_boundaries_and_rejects_unsafe_entrypoints()
    {
        var definition = new FixCapabilityDefinition { Entrypoint = "patches/fix.sh", Command =
            [new() { Value = "python3", Position = 0 }, new() { Value = "{entrypoint}", Position = 1 }, new() { Value = "literal argument", Position = 2 }] };
        await Assert.That(FixCommand.Create(definition).SequenceEqual(["python3", "/noctf/fix/patches/fix.sh", "literal argument"])).IsTrue();
        foreach (var unsafePath in new[] { "../fix.sh", "/fix.sh", "C:/fix.sh", "patch/../fix.sh", "patch\\..\\fix.sh", "fix.sh\0" })
        {
            definition.Entrypoint = unsafePath;
            await Assert.That(FixCommand.IsValid(definition)).IsFalse();
        }
    }

    [Test]
    public async Task Break_environment_targets_supply_effective_runtime_and_preserve_copy_independence()
    {
        var definition = Repairable();
        definition.Break!.EnvironmentTargets.Add(new() { ServiceName = "web", VariableName = "FLAG" });
        var runtime = new NoCTF.GameModes.Registration.ChallengeRuntimeTemplateCatalog().Get(definition)!;
        var container = (NoCTF.Application.Runtime.Provisioning.ContainerRuntimeDefinition)runtime.Definition;
        await Assert.That(container.Services.Single(service => service.Name == "web").FlagEnvironmentVariableName).IsEqualTo("FLAG");
        await Assert.That(container.Services.Single(service => service.Name == "database").FlagEnvironmentVariableName).IsNull();
        var copied = (BreakCapabilityDefinition)ChallengeCapabilityCopy.Copy(definition.Break);
        copied.EnvironmentTargets[0].VariableName = "COPIED_FLAG";
        await Assert.That(definition.Break.EnvironmentTargets[0].VariableName).IsEqualTo("FLAG");
        definition.Break.EnvironmentTargets.Add(new() { Position = 1, ServiceName = "web", VariableName = "OTHER" });
        await Assert.That(ChallengeCapabilityValidation.Validate(definition).Any(error => error.Code == CapabilityConfigurationFailure.DuplicateIdentity)).IsTrue();
        await Assert.That(new NoCTF.GameModes.Registration.GameModeChallengeConfigurationCatalog().ValidateDefinition(definition).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Break_environment_targets_reject_reserved_variables_and_missing_services()
    {
        var definition = Repairable();
        definition.Break!.EnvironmentTargets.Add(new() { ServiceName = "absent", VariableName = "NOCTF_CALLBACK_TOKEN" });
        var errors = ChallengeCapabilityValidation.Validate(definition);
        await Assert.That(errors.Any(error => error.Code == CapabilityConfigurationFailure.TargetServiceMissing)).IsTrue();
        await Assert.That(errors.Any(error => error.Code == CapabilityConfigurationFailure.InvalidEnvironmentTarget)).IsTrue();
    }

    public static ChallengeDefinition Repairable() => new()
    {
        Runtime = new ContainerChallengeRuntimeTemplate { Services =
        [
            new() { Name = "web", Image = "nginx:alpine", Position = 0 },
            new() { Name = "database", Image = "postgres:17-alpine", Position = 1 }
        ] },
        Capabilities =
        [
            new BreakCapabilityDefinition(), new FixCapabilityDefinition { TargetServiceName = "web" },
            new CheckCapabilityDefinition { Image = "checker", TargetServiceName = "web", Requirements =
                [new() { Kind = CheckItemKind.Functionality }, new() { Kind = CheckItemKind.Vulnerability }] }
        ]
    };
}
