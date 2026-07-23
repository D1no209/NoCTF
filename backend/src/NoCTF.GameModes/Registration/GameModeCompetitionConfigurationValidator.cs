using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeCompetitionConfigurationValidator : ICompetitionConfigurationValidator
{
    public IReadOnlyList<string> Validate(GameMode mode, string json) => Validate(mode, json, 1, []);

    public IReadOnlyList<string> Validate(GameMode mode, string json, int eligibleTeamCount) =>
        Validate(mode, json, eligibleTeamCount, []);

    public IReadOnlyList<string> Validate(
        GameMode mode,
        string json,
        int eligibleTeamCount,
        IReadOnlyList<string> challengeConfigurationJsons)
    {
        try
        {
            return mode switch
            {
                GameMode.Ctf => ValidateCtf(json, eligibleTeamCount, challengeConfigurationJsons),
                GameMode.Awd => Awd.Configuration.AwdConfigurationValidator.Validate(Awd.Configuration.AwdConfigurationUpgrader.ParseCompetition(json)),
                GameMode.Awdp => Awdp.Configuration.AwdpConfigurationValidator.Validate(Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(json)),
                GameMode.Koh => Koh.Configuration.KohConfigurationValidator.Validate(Koh.Configuration.KohConfigurationUpgrader.ParseCompetition(json)),
                _ => ["Unsupported game mode."]
            };
        }
        catch (Exception exception) when (exception is GameModeConfigurationException or System.Text.Json.JsonException)
        {
            return [exception.Message];
        }
    }

    private static IReadOnlyList<string> ValidateCtf(
        string json,
        int eligibleTeamCount,
        IReadOnlyList<string> challengeConfigurationJsons)
    {
        var competition = Ctf.Configuration.CtfConfigurationUpgrader.ParseCompetition(json);
        var errors = Ctf.Configuration.CtfConfigurationValidator.Validate(competition, eligibleTeamCount).ToList();
        foreach (var challengeJson in challengeConfigurationJsons)
        {
            var challenge = Ctf.Configuration.CtfConfigurationUpgrader.ParseChallenge(challengeJson);
            errors.AddRange(Ctf.Configuration.CtfConfigurationValidator.Validate(
                challenge, competition, eligibleTeamCount));
        }
        return errors;
    }
}
