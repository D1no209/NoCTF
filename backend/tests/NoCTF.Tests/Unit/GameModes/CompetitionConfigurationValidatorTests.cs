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
    public async Task AwdpCurrentConfiguration_ParsesFinalPenaltyFieldsWithoutUpgrade()
    {
        const string current = """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50},"violationPenalty":5,"serviceDownPenalty":7}""";

        var parsed = NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(current);

        await Assert.That(parsed.SchemaVersion).IsEqualTo(1);
        await Assert.That(parsed.ViolationPenalty).IsEqualTo(5L);
        await Assert.That(parsed.ServiceDownPenalty).IsEqualTo(7L);
    }

    [Test]
    public async Task AwdpNonCurrentConfiguration_IsRejectedWithoutUpgrade()
    {
        const string obsolete = """{"schemaVersion":0,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50}}""";

        var parse = () => NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(obsolete);

        await Assert.That(parse).Throws<NoCTF.GameModes.Registration.GameModeConfigurationException>();
    }
}
