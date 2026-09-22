using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfConfigurationParser
{
    public static CtfConfiguration ParseCompetition(string json) =>
        CurrentConfigurationParser.Parse<CtfConfiguration>(json, CtfConfiguration.CurrentSchemaVersion);

    public static CtfChallengeConfiguration ParseRules(string json) =>
        CurrentConfigurationParser.Parse<CtfChallengeConfiguration>(json, CtfConfiguration.CurrentSchemaVersion);

    public static CtfChallengeConfiguration ParseDefinition(string json) =>
        CurrentConfigurationParser.Parse<CtfChallengeConfiguration>(json, CtfChallengeConfiguration.CurrentSchemaVersion);
}
