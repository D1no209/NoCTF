using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CompetitionConfigurationChangePolicyTests
{
    private readonly GameModeCompetitionConfigurationChangePolicy _policy = new();

    [Test]
    public async Task Ctf_scoring_change_is_non_destructive()
    {
        var current = """{"schemaVersion":1,"defaultPoints":{"initialPoints":500,"minimumPoints":100,"decayFactor":0.2},"bloodRewards":[]}""";
        var proposed = """{"schemaVersion":1,"defaultPoints":{"initialPoints":600,"minimumPoints":100,"decayFactor":0.2},"bloodRewards":[]}""";

        await Assert.That(_policy.IsNonDestructive(GameMode.Ctf, current, proposed)).IsTrue();
    }

    [Test]
    public async Task Awd_points_change_is_non_destructive_but_round_change_is_destructive()
    {
        const string current = """{"schemaVersion":1,"roundDurationSeconds":300,"totalRounds":10,"flagValidityRounds":2,"attackPoints":100,"serviceOnlinePoints":10,"serviceDownPenalty":10,"victimPenalty":50}""";
        const string points = """{"schemaVersion":1,"roundDurationSeconds":300,"totalRounds":10,"flagValidityRounds":2,"attackPoints":200,"serviceOnlinePoints":20,"serviceDownPenalty":20,"victimPenalty":100}""";
        const string rounds = """{"schemaVersion":1,"roundDurationSeconds":60,"totalRounds":10,"flagValidityRounds":2,"attackPoints":100,"serviceOnlinePoints":10,"serviceDownPenalty":10,"victimPenalty":50}""";

        await Assert.That(_policy.IsNonDestructive(GameMode.Awd, current, points)).IsTrue();
        await Assert.That(_policy.IsNonDestructive(GameMode.Awd, current, rounds)).IsFalse();
    }

    [Test]
    public async Task Awdp_points_change_is_non_destructive_but_settlement_change_is_destructive()
    {
        const string current = """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50}}""";
        const string points = """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":75},"fix":{"settlement":1,"points":80}}""";
        const string settlement = """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":0,"points":50},"fix":{"settlement":1,"points":50}}""";

        await Assert.That(_policy.IsNonDestructive(GameMode.Awdp, current, points)).IsTrue();
        await Assert.That(_policy.IsNonDestructive(GameMode.Awdp, current, settlement)).IsFalse();
    }

    [Test]
    public async Task Koh_points_change_is_non_destructive_but_poll_interval_change_is_destructive()
    {
        const string current = """{"schemaVersion":1,"pollIntervalSeconds":10,"controlPointsPerInterval":5}""";
        const string points = """{"schemaVersion":1,"pollIntervalSeconds":10,"controlPointsPerInterval":10}""";
        const string interval = """{"schemaVersion":1,"pollIntervalSeconds":5,"controlPointsPerInterval":5}""";

        await Assert.That(_policy.IsNonDestructive(GameMode.Koh, current, points)).IsTrue();
        await Assert.That(_policy.IsNonDestructive(GameMode.Koh, current, interval)).IsFalse();
    }

    [Test]
    public async Task Penetration_scoring_change_is_non_destructive()
    {
        var current = """{"schemaVersion":1,"defaultPoints":{"initialPoints":500,"minimumPoints":100,"decayFactor":0.2},"bloodRewards":[]}""";
        var proposed = """{"schemaVersion":1,"defaultPoints":{"initialPoints":700,"minimumPoints":100,"decayFactor":0.2},"bloodRewards":[]}""";

        await Assert.That(_policy.IsNonDestructive(GameMode.Penetration, current, proposed)).IsTrue();
    }

    [Test]
    public async Task Invalid_current_configuration_is_not_accepted_as_non_destructive()
    {
        await Assert.That(_policy.IsNonDestructive(GameMode.Ctf, "not-json", "{}" )).IsFalse();
    }
}
