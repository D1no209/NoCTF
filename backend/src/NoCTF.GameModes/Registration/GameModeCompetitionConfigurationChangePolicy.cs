using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeCompetitionConfigurationChangePolicy : ICompetitionConfigurationChangePolicy
{
    public bool IsNonDestructive(GameMode mode, string currentJson, string proposedJson)
    {
        try
        {
            return mode switch
            {
                GameMode.Ctf => Ctf(currentJson, proposedJson),
                GameMode.Awd => Awd(currentJson, proposedJson),
                GameMode.Awdp => Awdp(currentJson, proposedJson),
                GameMode.Koh => Koh(currentJson, proposedJson),
                GameMode.Penetration => Penetration(currentJson, proposedJson),
                _ => false
            };
        }
        catch (Exception exception) when (
            exception is GameModeConfigurationException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool Ctf(string currentJson, string proposedJson)
    {
        _ = NoCTF.GameModes.Ctf.Configuration.CtfConfigurationUpgrader.ParseCompetition(currentJson);
        _ = NoCTF.GameModes.Ctf.Configuration.CtfConfigurationUpgrader.ParseCompetition(proposedJson);
        return true;
    }

    private static bool Awd(string currentJson, string proposedJson)
    {
        var current = NoCTF.GameModes.Awd.Configuration.AwdConfigurationUpgrader.ParseCompetition(currentJson);
        var proposed = NoCTF.GameModes.Awd.Configuration.AwdConfigurationUpgrader.ParseCompetition(proposedJson);
        return current.RoundDurationSeconds == proposed.RoundDurationSeconds
               && current.TotalRounds == proposed.TotalRounds
               && current.FlagValidityRounds == proposed.FlagValidityRounds;
    }

    private static bool Awdp(string currentJson, string proposedJson)
    {
        var current = NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationUpgrader.ParseCompetition(currentJson);
        var proposed = NoCTF.GameModes.Awdp.Configuration.AwdpConfigurationUpgrader.ParseCompetition(proposedJson);
        return current.RoundDurationSeconds == proposed.RoundDurationSeconds
               && current.Break.Settlement == proposed.Break.Settlement
               && current.Fix.Settlement == proposed.Fix.Settlement;
    }

    private static bool Koh(string currentJson, string proposedJson)
    {
        var current = NoCTF.GameModes.Koh.Configuration.KohConfigurationUpgrader.ParseCompetition(currentJson);
        var proposed = NoCTF.GameModes.Koh.Configuration.KohConfigurationUpgrader.ParseCompetition(proposedJson);
        return current.PollIntervalSeconds == proposed.PollIntervalSeconds;
    }

    private static bool Penetration(string currentJson, string proposedJson)
    {
        _ = NoCTF.GameModes.Penetration.Configuration.PenetrationConfigurationUpgrader.ParseCompetition(currentJson);
        _ = NoCTF.GameModes.Penetration.Configuration.PenetrationConfigurationUpgrader.ParseCompetition(proposedJson);
        return true;
    }
}
