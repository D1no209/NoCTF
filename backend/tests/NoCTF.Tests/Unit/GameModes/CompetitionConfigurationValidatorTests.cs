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
    public async Task CtfExpression_DivideByZero_IsReturnedAsValidationFailure()
    {
        const string json = """{"schemaVersion":1,"defaultPoints":{"initialPoints":500,"minimumPoints":100,"decayFactor":10},"bloodRewards":[],"scoreExpression":"1m / (initialPoints - initialPoints)"}""";

        var errors = new GameModeCompetitionConfigurationValidator().Validate(GameMode.Ctf, json, 2);

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task CtfCompetitionExpression_IsValidatedAgainstChallengePointOverrides()
    {
        const string competition = """{"schemaVersion":1,"defaultPoints":{"initialPoints":500,"minimumPoints":0,"decayFactor":10},"bloodRewards":[],"scoreExpression":"1m / (initialPoints - 100m)"}""";
        const string challenge = """{"schemaVersion":1,"points":{"initialPoints":100,"minimumPoints":0,"decayFactor":10},"bloodRewards":null}""";

        var errors = new GameModeCompetitionConfigurationValidator().Validate(
            GameMode.Ctf, competition, 2, [challenge]);

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task AwdpLegacyConfiguration_RemainsReadableWithoutChangingItsSemantics()
    {
        const string current = """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50},"violationPenalty":5,"serviceDownPenalty":7}""";

        var parsed = NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(current);

        await Assert.That(parsed.SchemaVersion).IsEqualTo(1);
        await Assert.That(parsed.UsesContinuousRoundScoring).IsFalse();
        await Assert.That(parsed.ViolationPenalty).IsEqualTo(5L);
        await Assert.That(parsed.ServiceDownPenalty).IsEqualTo(7L);
    }

    [Test]
    public async Task AwdpCurrentConfiguration_UsesContinuousRoundScoringAndIndependentFix()
    {
        var json = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp);

        var parsed = NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(json);

        await Assert.That(parsed.SchemaVersion).IsEqualTo(2);
        await Assert.That(parsed.UsesContinuousRoundScoring).IsTrue();
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
            """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50},"runtime":null}""";

        var errors = new GameModeCompetitionConfigurationValidator().Validate(
            GameMode.Awdp,
            invalid);

        await Assert.That(errors).IsNotEmpty();
    }
}
