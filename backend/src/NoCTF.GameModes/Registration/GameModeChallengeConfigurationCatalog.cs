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
            new AwdChallengeConfiguration(AwdChallengeConfiguration.CurrentSchemaVersion),
            JsonOptions),
        GameMode.Awdp => JsonSerializer.Serialize(
            new AwdpChallengeConfiguration(
                AwdpChallengeConfiguration.CurrentSchemaVersion,
                null,
                null,
                null,
                null,
                null),
            JsonOptions),
        GameMode.Koh => JsonSerializer.Serialize(
            new KohChallengeConfiguration(KohChallengeConfiguration.CurrentSchemaVersion),
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
                GameMode.Awdp => ValidateAwdp(json, competitionConfigurationJson),
                GameMode.Koh => KohConfigurationValidator.Validate(KohConfigurationUpgrader.ParseChallenge(json)),
                _ => ["Unsupported game mode."]
            };
        }
        catch (Exception exception) when (exception is GameModeConfigurationException or JsonException)
        {
            return [exception.Message];
        }
    }

    private static IReadOnlyList<string> ValidateAwdp(
        string json,
        string competitionConfigurationJson)
    {
        var competition = AwdpConfigurationParser.ParseCompetition(
            competitionConfigurationJson);
        var challenge = AwdpConfigurationParser.ParseChallenge(json);
        var competitionErrors = AwdpConfigurationValidator.Validate(competition);
        if (competitionErrors.Count > 0)
            return competitionErrors;
        return
        [
            .. AwdpConfigurationValidator.Validate(challenge),
            .. AwdpConfigurationValidator.Validate(
                AwdpConfigurationResolver.Resolve(competition, challenge))
        ];
    }
}
