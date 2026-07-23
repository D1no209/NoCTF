using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeCompetitionConfigurationValidator : ICompetitionConfigurationValidator
{
    public IReadOnlyList<string> Validate(GameMode mode, string json)
    {
        try
        {
            return mode switch
            {
                GameMode.Ctf => Ctf.Configuration.CtfConfigurationValidator.Validate(Ctf.Configuration.CtfConfigurationUpgrader.ParseCompetition(json)),
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
}
