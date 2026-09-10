using NoCTF.Domain.Competitions;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.GameModes.Registration;
using NSubstitute;

namespace NoCTF.Tests.Unit.GameModes;

public class GameplayFactAdmissionModePolicyTests
{
    [Test]
    public async Task Flag_attempt_budget_reports_the_remaining_accepted_attempts()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var store = Substitute.For<IGameplayFactIntakeStore>();
        var policy = Substitute.For<IGameplayFactAdmissionModePolicy>();
        store.LoadAdmissionAsync(competitionId, challengeId, userId, Arg.Any<CancellationToken>())
            .Returns(new GameplayFactAdmissionSnapshot(
                competitionId, Guid.NewGuid(), challengeId, GameMode.Ctf,
                "{}", "{}", 2, 0, CompetitionStatus.Running,
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1),
                false, false, true, false, false, true, true,
                HasCorrectFlag: true));
        policy.GetRules(GameMode.Ctf, "{}", "{}")
            .Returns(new GameplayFactAdmissionRules(true, false, 5, null));

        var budget = await new GetFlagAttemptState(store, policy)
            .ExecuteAsync(competitionId, challengeId, userId);

        await Assert.That(budget).IsEqualTo(new FlagAttemptState(5, 2, 3, true));
    }

    [Test]
    public async Task Flag_attempt_state_reports_a_solve_without_an_attempt_limit()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var store = Substitute.For<IGameplayFactIntakeStore>();
        var policy = Substitute.For<IGameplayFactAdmissionModePolicy>();
        store.LoadAdmissionAsync(competitionId, challengeId, userId, Arg.Any<CancellationToken>())
            .Returns(new GameplayFactAdmissionSnapshot(
                competitionId, Guid.NewGuid(), challengeId, GameMode.Ctf,
                "{}", "{}", 1, 0, CompetitionStatus.Running,
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1),
                false, false, true, false, false, true, true,
                HasCorrectFlag: true));
        policy.GetRules(GameMode.Ctf, "{}", "{}")
            .Returns(new GameplayFactAdmissionRules(true, false, null, null));

        var state = await new GetFlagAttemptState(store, policy)
            .ExecuteAsync(competitionId, challengeId, userId);

        await Assert.That(state).IsEqualTo(new FlagAttemptState(null, 1, null, true));
    }

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
