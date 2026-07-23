using System.Text.Json;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeChallengeConfigurationCatalog : IChallengeConfigurationCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GetDefaultJson(GameMode mode) => mode switch
    {
        GameMode.Ctf => JsonSerializer.Serialize(
            new CtfChallengeConfiguration(CtfChallengeConfiguration.CurrentSchemaVersion, null, null, null),
            JsonOptions),
        GameMode.Awd => JsonSerializer.Serialize(
            new AwdChallengeConfiguration(AwdChallengeConfiguration.CurrentSchemaVersion, "flag{round_team_service}", null),
            JsonOptions),
        GameMode.Awdp => JsonSerializer.Serialize(
            new AwdpChallengeConfiguration(
                AwdpChallengeConfiguration.CurrentSchemaVersion,
                new(AchievementSettlement.PerRound, 50),
                null,
                true,
                10,
                10),
            JsonOptions),
        GameMode.Koh => JsonSerializer.Serialize(
            new KohChallengeConfiguration(KohChallengeConfiguration.CurrentSchemaVersion, "http://localhost"),
            JsonOptions),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
    };

    public IReadOnlyList<string> Validate(GameMode mode, string json) =>
        Validate(mode, json, GameModeDefaultConfiguration.GetCompetitionJson(mode), 1);

    public IReadOnlyList<string> Validate(
        GameMode mode,
        string json,
        string competitionConfigurationJson,
        int eligibleTeamCount)
    {
        try
        {
            return mode switch
            {
                GameMode.Ctf => CtfConfigurationValidator.Validate(
                    CtfConfigurationUpgrader.ParseChallenge(json),
                    CtfConfigurationUpgrader.ParseCompetition(competitionConfigurationJson),
                    eligibleTeamCount),
                GameMode.Awd => AwdConfigurationValidator.Validate(AwdConfigurationUpgrader.ParseChallenge(json)),
                GameMode.Awdp => AwdpConfigurationValidator.Validate(AwdpConfigurationParser.ParseChallenge(json)),
                GameMode.Koh => KohConfigurationValidator.Validate(KohConfigurationUpgrader.ParseChallenge(json)),
                _ => ["Unsupported game mode."]
            };
        }
        catch (Exception exception) when (exception is GameModeConfigurationException or JsonException)
        {
            return [exception.Message];
        }
    }
}
