using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class SubmissionAdmissionModePolicyTests
{
    private readonly GameModeGameplayFactAdmissionPolicy policy = new();

    [Test]
    public async Task Ctf_flag_and_patch_interactions_have_distinct_attempt_budgets()
    {
        var competition = CompetitionModeConfigurationDefaults.Create(GameMode.Ctf, Guid.NewGuid());
        var flag = policy.GetRules(
            GameMode.Ctf,
            competition,
            new CtfCompetitionChallengeRules { MaxFlagAttempts = 3 },
            new CtfChallengeDefinition { InteractionKind = CtfInteractionKind.FlagSubmission });
        var patch = policy.GetRules(
            GameMode.Ctf,
            competition,
            new CtfCompetitionChallengeRules { MaxPatchAttempts = 2 },
            new CtfChallengeDefinition { InteractionKind = CtfInteractionKind.PatchVerification });

        await Assert.That(flag.AllowsFlag).IsTrue();
        await Assert.That(flag.AllowsFix).IsFalse();
        await Assert.That(flag.MaxFlagAttempts).IsEqualTo(3);
        await Assert.That(patch.AllowsFlag).IsFalse();
        await Assert.That(patch.AllowsFix).IsTrue();
        await Assert.That(patch.MaxFixAttempts).IsEqualTo(2);
    }

    [Test]
    public async Task Awdp_rules_override_and_inherit_competition_limits()
    {
        var competition = (AwdpCompetitionModeConfiguration)
            CompetitionModeConfigurationDefaults.Create(GameMode.Awdp, Guid.NewGuid());
        competition.MaxBreakSubmissions = 10;
        competition.MaxFixSubmissions = 8;
        competition.RequireBreakBeforeFix = true;
        var rules = policy.GetRules(
            GameMode.Awdp,
            competition,
            new AwdpCompetitionChallengeRules
            {
                MaxBreakSubmissions = 3,
                RequireBreakBeforeFix = false
            },
            new AwdpChallengeDefinition());

        await Assert.That(rules.MaxFlagAttempts).IsEqualTo(3);
        await Assert.That(rules.MaxFixAttempts).IsEqualTo(8);
        await Assert.That(rules.RequireBreakBeforeFix).IsFalse();
    }

    [Test]
    public async Task Awd_and_koh_expose_only_their_supported_actions()
    {
        var awd = policy.GetRules(
            GameMode.Awd,
            CompetitionModeConfigurationDefaults.Create(GameMode.Awd, Guid.NewGuid()),
            new AwdCompetitionChallengeRules(),
            new AwdChallengeDefinition());
        var koh = policy.GetRules(
            GameMode.Koh,
            CompetitionModeConfigurationDefaults.Create(GameMode.Koh, Guid.NewGuid()),
            new KohCompetitionChallengeRules(),
            new KohChallengeDefinition());

        await Assert.That(awd.AllowsFlag).IsTrue();
        await Assert.That(awd.AllowsFix).IsFalse();
        await Assert.That(koh.AllowsFlag).IsFalse();
        await Assert.That(koh.AllowsFix).IsFalse();
    }
}
