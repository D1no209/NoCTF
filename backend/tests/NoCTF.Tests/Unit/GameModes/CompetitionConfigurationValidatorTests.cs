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
}
