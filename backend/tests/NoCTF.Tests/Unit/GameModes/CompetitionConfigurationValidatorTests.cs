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
    public async Task AwdpV1Configuration_UpgradesWithZeroPenalties()
    {
        const string versionOne = """{"schemaVersion":1,"roundDurationSeconds":300,"break":{"settlement":1,"points":50},"fix":{"settlement":1,"points":50}}""";

        var upgraded = NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationUpgrader.ParseCompetition(versionOne);

        await Assert.That(upgraded.SchemaVersion).IsEqualTo(2);
        await Assert.That(upgraded.ViolationPenalty).IsEqualTo(0L);
        await Assert.That(upgraded.ServiceDownPenalty).IsEqualTo(0L);
    }
}
