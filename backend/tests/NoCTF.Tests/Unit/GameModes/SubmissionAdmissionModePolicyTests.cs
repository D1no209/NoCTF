using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public class SubmissionAdmissionModePolicyTests
{
    [Test]
    public async Task Koh_DoesNotAcceptManualSubmissions()
    {
        var policy = new GameModeSubmissionAdmissionPolicy();

        var rules = policy.GetRules(GameMode.Koh, "{}", "{}");

        await Assert.That(rules.AllowsFlag).IsFalse();
        await Assert.That(rules.AllowsFix).IsFalse();
    }

    [Test]
    public async Task Awdp_UsesConfiguredBreakAndFixAttemptLimits()
    {
        const string json = """{"schemaVersion":1,"break":{"settlement":1,"points":10},"fix":{"settlement":1,"points":20},"requireBreakBeforeFix":true,"maxBreakAttempts":3,"maxFixAttempts":2,"runtime":{"provider":0,"allocation":1,"image":"target:v1","portMappings":{"8080":0}},"patchEntrypoint":"fix.sh","patchCommand":["/bin/sh","{entrypoint}"],"patchTimeoutSeconds":60,"checker":{"provider":0,"image":"checker:v1","timeoutSeconds":60},"targetPort":8080,"readyTimeoutSeconds":30}""";
        var policy = new GameModeSubmissionAdmissionPolicy();

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
    public async Task Awdp_Challenge_null_inherits_competition_break_requirement()
    {
        const string competition =
            """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50},"violationPenalty":100,"serviceDownPenalty":50,"requireBreakBeforeFix":true}""";
        const string challenge =
            """{"schemaVersion":1,"break":{"settlement":1,"points":10},"fix":{"settlement":1,"points":20},"requireBreakBeforeFix":null,"maxBreakAttempts":3,"maxFixAttempts":2}""";
        var policy = new GameModeSubmissionAdmissionPolicy();

        var rules = policy.GetRules(GameMode.Awdp, competition, challenge);

        await Assert.That(rules.RequireBreakBeforeFix).IsTrue();
    }

    [Test]
    public async Task Awdp_Missing_competition_break_requirement_uses_true_default()
    {
        const string competition =
            """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50},"violationPenalty":100,"serviceDownPenalty":50}""";
        const string challenge =
            """{"schemaVersion":1,"break":{"settlement":1,"points":10},"fix":{"settlement":1,"points":20},"requireBreakBeforeFix":null,"maxBreakAttempts":3,"maxFixAttempts":2}""";
        var policy = new GameModeSubmissionAdmissionPolicy();

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
            $$"""{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50},"violationPenalty":100,"serviceDownPenalty":50,"requireBreakBeforeFix":{{competitionValue.ToString().ToLowerInvariant()}}}""";
        var challenge =
            $$"""{"schemaVersion":1,"break":{"settlement":1,"points":10},"fix":{"settlement":1,"points":20},"requireBreakBeforeFix":{{challengeValue.ToString().ToLowerInvariant()}},"maxBreakAttempts":3,"maxFixAttempts":2}""";
        var policy = new GameModeSubmissionAdmissionPolicy();

        var rules = policy.GetRules(GameMode.Awdp, competition, challenge);

        await Assert.That(rules.RequireBreakBeforeFix).IsEqualTo(expected);
    }
}
