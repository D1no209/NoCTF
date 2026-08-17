using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public class GameplayFactAdmissionModePolicyTests
{
    [Test]
    public async Task Koh_DoesNotAcceptManualSubmissions()
    {
        var policy = new GameModeGameplayFactAdmissionPolicy();

        var rules = policy.GetRules(GameMode.Koh, "{}", "{}");

        await Assert.That(rules.AllowsFlag).IsFalse();
        await Assert.That(rules.AllowsFix).IsFalse();
    }

    [Test]
    public async Task Awdp_UsesConfiguredBreakAndFixAttemptLimits()
    {
        const string json = """{"schemaVersion":4,"break":{"initialPoints":10,"minimumPoints":10,"decayTeamCount":10,"decayMode":0},"fix":{"initialPoints":20,"minimumPoints":20,"decayTeamCount":10,"decayMode":0},"requireBreakBeforeFix":true,"maxBreakSubmissions":3,"maxFixSubmissions":2,"runtime":{"allocation":1,"definition":{"kind":"container","image":"target:v1","internalPorts":[8080]},"limits":{"memoryBytes":268435456,"nanoCpus":500000000,"pidsLimit":128}},"patchEntrypoint":"fix.sh","patchCommand":["/bin/sh","{entrypoint}"],"patchTimeoutSeconds":60,"checker":{"image":"checker:v1","timeoutSeconds":60},"readyTimeoutSeconds":30}""";
        var policy = new GameModeGameplayFactAdmissionPolicy();

        var rules = policy.GetRules(
            GameMode.Awdp,
            GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            json);

        await Assert.That(rules.AllowsFlag).IsTrue();
        await Assert.That(rules.AllowsFix).IsTrue();
        await Assert.That(rules.MaxFlagAttempts).IsEqualTo(3);
        await Assert.That(rules.MaxFixAttempts).IsEqualTo(2);
    }

    [Test]
    public async Task AwdpCurrentConfiguration_DoesNotRequireBreakBeforeFix()
    {
        var policy = new GameModeGameplayFactAdmissionPolicy();

        var rules = policy.GetRules(
            GameMode.Awdp,
            GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Awdp));

        await Assert.That(rules.RequireBreakBeforeFix).IsFalse();
    }

    [Test]
    public async Task Awdp_Challenge_null_inherits_competition_break_requirement()
    {
        const string competition =
            """{"schemaVersion":4,"roundDurationSeconds":300,"break":{"initialPoints":50,"minimumPoints":50,"decayTeamCount":10,"decayMode":0},"fix":{"initialPoints":50,"minimumPoints":50,"decayTeamCount":10,"decayMode":0},"exploitSucceededPenalty":100,"serviceAbnormalPenalty":50,"requireBreakBeforeFix":true}""";
        const string challenge =
            """{"schemaVersion":4,"break":{"initialPoints":10,"minimumPoints":10,"decayTeamCount":10,"decayMode":0},"fix":{"initialPoints":20,"minimumPoints":20,"decayTeamCount":10,"decayMode":0},"requireBreakBeforeFix":null,"maxBreakSubmissions":3,"maxFixSubmissions":2}""";
        var policy = new GameModeGameplayFactAdmissionPolicy();

        var rules = policy.GetRules(GameMode.Awdp, competition, challenge);

        await Assert.That(rules.RequireBreakBeforeFix).IsTrue();
    }

    [Test]
    public async Task Awdp_Missing_competition_break_requirement_uses_true_default()
    {
        const string competition =
            """{"schemaVersion":4,"roundDurationSeconds":300,"break":{"initialPoints":50,"minimumPoints":50,"decayTeamCount":10,"decayMode":0},"fix":{"initialPoints":50,"minimumPoints":50,"decayTeamCount":10,"decayMode":0},"exploitSucceededPenalty":100,"serviceAbnormalPenalty":50}""";
        const string challenge =
            """{"schemaVersion":4,"break":{"initialPoints":10,"minimumPoints":10,"decayTeamCount":10,"decayMode":0},"fix":{"initialPoints":20,"minimumPoints":20,"decayTeamCount":10,"decayMode":0},"requireBreakBeforeFix":null,"maxBreakSubmissions":3,"maxFixSubmissions":2}""";
        var policy = new GameModeGameplayFactAdmissionPolicy();

        var rules = policy.GetRules(GameMode.Awdp, competition, challenge);

        await Assert.That(rules.RequireBreakBeforeFix).IsTrue();
    }

    [Test]
    [Arguments(true, false, false)]
    [Arguments(false, true, true)]
    public async Task Awdp_Challenge_break_requirement_overrides_competition(
        bool competitionValue,
        bool challengeValue,
        bool expected)
    {
        var competition =
            $$"""{"schemaVersion":4,"roundDurationSeconds":300,"break":{"initialPoints":50,"minimumPoints":50,"decayTeamCount":10,"decayMode":0},"fix":{"initialPoints":50,"minimumPoints":50,"decayTeamCount":10,"decayMode":0},"exploitSucceededPenalty":100,"serviceAbnormalPenalty":50,"requireBreakBeforeFix":{{competitionValue.ToString().ToLowerInvariant()}}}""";
        var challenge =
            $$"""{"schemaVersion":4,"break":{"initialPoints":10,"minimumPoints":10,"decayTeamCount":10,"decayMode":0},"fix":{"initialPoints":20,"minimumPoints":20,"decayTeamCount":10,"decayMode":0},"requireBreakBeforeFix":{{challengeValue.ToString().ToLowerInvariant()}},"maxBreakSubmissions":3,"maxFixSubmissions":2}""";
        var policy = new GameModeGameplayFactAdmissionPolicy();

        var rules = policy.GetRules(GameMode.Awdp, competition, challenge);

        await Assert.That(rules.RequireBreakBeforeFix).IsEqualTo(expected);
    }
}
