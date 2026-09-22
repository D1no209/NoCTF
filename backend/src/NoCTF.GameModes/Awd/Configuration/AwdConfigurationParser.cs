using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Awd.Configuration;

public static class AwdConfigurationParser
{
    public static AwdConfiguration ParseCompetition(string json) =>
        CurrentConfigurationParser.Parse<AwdConfiguration>(json, AwdConfiguration.CurrentSchemaVersion);

    public static AwdChallengeConfiguration ParseChallenge(string json) =>
        CurrentConfigurationParser.Parse<AwdChallengeConfiguration>(json, AwdChallengeConfiguration.CurrentSchemaVersion);
}
