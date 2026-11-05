using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class SubmissionAdmissionModePolicyTests
{
    private readonly GameModeGameplayFactAdmissionPolicy policy = new();

    [Test]
    public async Task Ctf_only_admits_break_even_when_the_template_provides_repair_capabilities()
    {
        var rules = policy.GetRules(GameMode.Ctf, CompetitionModeConfigurationDefaults.Create(GameMode.Ctf, Guid.NewGuid()),
            new CtfCompetitionChallengeRules { MaxFlagAttempts = 3 }, NoCTF.Tests.Unit.Gameplay.ChallengeCapabilityTests.Repairable());
        await Assert.That(rules.AllowsFlag).IsTrue(); await Assert.That(rules.AllowsFix).IsFalse();
        await Assert.That(rules.MaxFlagAttempts).IsEqualTo(3); await Assert.That(rules.MaxFixAttempts).IsNull();
    }

    [Test]
    public async Task Awdp_rules_override_and_inherit_the_competition_limits()
    {
        var competition = (AwdpCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awdp, Guid.NewGuid());
        competition.MaxBreakSubmissions = 10; competition.MaxFixSubmissions = 8; competition.RequireBreakBeforeFix = true;
        var rules = policy.GetRules(GameMode.Awdp, competition,
            new AwdpCompetitionChallengeRules { MaxBreakSubmissions = 3, RequireBreakBeforeFix = false },
            NoCTF.Tests.Unit.Gameplay.ChallengeCapabilityTests.Repairable());
        await Assert.That(rules.AllowsFlag).IsTrue(); await Assert.That(rules.AllowsFix).IsTrue();
        await Assert.That(rules.MaxFlagAttempts).IsEqualTo(3); await Assert.That(rules.MaxFixAttempts).IsEqualTo(8);
        await Assert.That(rules.RequireBreakBeforeFix).IsFalse();
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task Awd_persistent_fix_is_an_explicit_rule_with_no_cumulative_submission_limit(bool enabled)
    {
        var rules = policy.GetRules(GameMode.Awd, CompetitionModeConfigurationDefaults.Create(GameMode.Awd, Guid.NewGuid()),
            new AwdCompetitionChallengeRules { AllowFixSubmissions = enabled }, NoCTF.Tests.Unit.Gameplay.ChallengeCapabilityTests.Repairable());
        await Assert.That(rules.AllowsFlag).IsTrue(); await Assert.That(rules.AllowsFix).IsEqualTo(enabled);
        await Assert.That(rules.MaxFixAttempts).IsNull();
    }

    [Test]
    public async Task Missing_capabilities_cannot_be_advertised_as_awdp_submission_actions()
    {
        var definition = NoCTF.Tests.Unit.Gameplay.ChallengeCapabilityTests.Repairable();
        var competition = CompetitionModeConfigurationDefaults.Create(GameMode.Awdp, Guid.NewGuid());
        var rules = new AwdpCompetitionChallengeRules();
        definition.Capabilities.RemoveAll(value => value.Kind == ChallengeCapabilityKind.Check);
        await Assert.That(policy.GetRules(GameMode.Awdp, competition, rules, definition).AllowsFix).IsFalse();
        definition.Capabilities.RemoveAll(value => value.Kind == ChallengeCapabilityKind.Break);
        await Assert.That(policy.GetRules(GameMode.Awdp, competition, rules, definition).AllowsFlag).IsFalse();
        await Assert.That(policy.GetRules(GameMode.Awdp, competition, rules, null).AllowsFix).IsFalse();
    }

    [Test]
    public async Task Koh_admits_control_observations_through_its_server_orchestration()
    {
        var rules = policy.GetRules(GameMode.Koh, CompetitionModeConfigurationDefaults.Create(GameMode.Koh, Guid.NewGuid()),
            new KohCompetitionChallengeRules(), new() { Capabilities = [new ControlCapabilityDefinition()] });
        await Assert.That(rules.AllowsFlag).IsFalse(); await Assert.That(rules.AllowsFix).IsFalse();
    }
}
