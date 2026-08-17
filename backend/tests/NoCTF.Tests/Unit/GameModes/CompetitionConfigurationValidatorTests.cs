using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public class CompetitionConfigurationValidatorTests
{
    [Test]
    public async Task Defaults_AreValidForEveryMode()
    {
        var validator = new GameModeCompetitionConfigurationValidator();
        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var errors = validator.Validate(mode, GameModeDefaultConfiguration.GetCompetitionJson(mode));
            await Assert.That(errors).IsEmpty();
        }
    }

    [Test]
    public async Task InvalidJson_IsReturnedAsValidationFailure()
    {
        var errors = new GameModeCompetitionConfigurationValidator().Validate(GameMode.Ctf, "not-json");
        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task CtfCustomCurve_DivideByZero_IsReturnedAsValidationFailure()
    {
        const string json = """{"schemaVersion":2,"defaultScoreCurve":{"initialPoints":500,"minimumPoints":100,"decayTeamCount":10,"decayMode":5,"customExpression":"1m / (initialPoints - initialPoints)"},"bloodRewards":[]}""";

        var errors = new GameModeCompetitionConfigurationValidator().Validate(GameMode.Ctf, json, 2);

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task CtfChallengeCustomCurve_IsValidatedAgainstEligibleTeams()
    {
        const string competition = """{"schemaVersion":2,"defaultScoreCurve":{"initialPoints":500,"minimumPoints":0,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}""";
        const string challenge = """{"schemaVersion":2,"scoreCurve":{"initialPoints":100,"minimumPoints":0,"decayTeamCount":10,"decayMode":5,"customExpression":"1m / (solveCount - 2)"},"bloodRewards":null}""";

        var errors = new GameModeCompetitionConfigurationValidator().Validate(
            GameMode.Ctf, competition, 2, [challenge]);

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task AwdpLegacyConfiguration_IsRejected()
    {
        const string legacy = """{"schemaVersion":2,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50}}""";

        var parse = () => NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(legacy);

        await Assert.That(parse).Throws<NoCTF.GameModes.Registration.GameModeConfigurationException>();
    }

    [Test]
    public async Task AwdpCurrentConfiguration_UsesIndependentRoundCurves()
    {
        var json = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp);

        var parsed = NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(json);

        await Assert.That(parsed.SchemaVersion).IsEqualTo(3);
        await Assert.That(parsed.Break.DecayMode)
            .IsEqualTo(NoCTF.GameModes.Scoring.ScoreDecayMode.Quadratic);
        await Assert.That(parsed.Fix.DecayMode)
            .IsEqualTo(NoCTF.GameModes.Scoring.ScoreDecayMode.Quadratic);
        await Assert.That(parsed.RequireBreakBeforeFix).IsFalse();
    }

    [Test]
    public async Task AwdpUnsupportedConfiguration_IsRejectedWithoutUpgrade()
    {
        const string obsolete = """{"schemaVersion":0,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50}}""";

        var parse = () => NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(obsolete);

        await Assert.That(parse).Throws<NoCTF.GameModes.Registration.GameModeConfigurationException>();
    }

    [Test]
    public async Task AwdpCompetitionConfiguration_RejectsChallengeDefinitionFields()
    {
        const string invalid =
            """{"schemaVersion":3,"roundDurationSeconds":300,"break":{"initialPoints":500,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"fix":{"initialPoints":500,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"runtime":null}""";

        var errors = new GameModeCompetitionConfigurationValidator().Validate(
            GameMode.Awdp,
            invalid);

        await Assert.That(errors).IsNotEmpty();
    }
}
