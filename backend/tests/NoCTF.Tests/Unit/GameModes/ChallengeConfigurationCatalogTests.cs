using System.Text.Json;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public class ChallengeConfigurationCatalogTests
{
    [Test]
    public async Task Defaults_AreValidForEveryGameMode()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var json = catalog.GetDefaultJson(mode);
            var errors = catalog.Validate(mode, json);

            await Assert.That(errors).IsEmpty();
        }
    }

    [Test]
    public async Task CtfDefault_DoesNotPersistFlagInConfigurationJson()
    {
        var json = new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Ctf);
        using var document = JsonDocument.Parse(json);

        var containsFlag = document.RootElement.TryGetProperty("flag", out _);

        await Assert.That(containsFlag).IsFalse();
    }

    [Test]
    [Arguments(GameMode.Ctf, "{\"schemaVersion\":1,\"points\":{\"initialPoints\":0,\"minimumPoints\":0,\"decayFactor\":1},\"bloodRewards\":[]}")]
    [Arguments(GameMode.Awd, "{\"schemaVersion\":1,\"flagFormat\":\"\"}")]
    [Arguments(GameMode.Awdp, "{\"schemaVersion\":1,\"break\":null,\"fix\":null,\"requireBreakBeforeFix\":false,\"maxBreakAttempts\":0,\"maxFixAttempts\":0}")]
    [Arguments(GameMode.Koh, "{\"schemaVersion\":1,\"agentUrl\":\"file:///secret\"}")]
    [Arguments(GameMode.Penetration, "{\"schemaVersion\":1,\"stages\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"number\":0,\"name\":\"\",\"prerequisiteIds\":[],\"points\":null}]}")]
    public async Task Validate_InvalidModeSpecificConfiguration_ReturnsErrors(GameMode mode, string json)
    {
        var errors = new GameModeChallengeConfigurationCatalog().Validate(mode, json);

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Validate_UnknownSchemaVersion_ReturnsError()
    {
        var errors = new GameModeChallengeConfigurationCatalog().Validate(
            GameMode.Ctf,
            """{"schemaVersion":99,"points":null,"bloodRewards":null}""");

        await Assert.That(errors).IsNotEmpty();
    }
}
