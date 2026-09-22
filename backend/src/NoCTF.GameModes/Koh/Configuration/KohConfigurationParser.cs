using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Koh.Configuration;

public static class KohConfigurationParser
{
    public static KohConfiguration ParseCompetition(string json) =>
        CurrentConfigurationParser.Parse<KohConfiguration>(json, KohConfiguration.CurrentSchemaVersion);

    public static KohChallengeConfiguration ParseChallenge(string json) =>
        CurrentConfigurationParser.Parse<KohChallengeConfiguration>(json, KohChallengeConfiguration.CurrentSchemaVersion);
}
