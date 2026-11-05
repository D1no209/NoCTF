using NoCTF.Domain.Challenges;
using NoCTF.Domain.LiveSolo;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.GameModes.Registration;

public static class StaticFlagConfiguration
{
    public static bool SupportsManualFlags(ChallengeDefinition? definition, IChallengeRuntimeTemplateCatalog runtimes) => definition?.Break is not null;
    public static bool SupportsRegularExpression(ChallengeDefinition? definition, IChallengeRuntimeTemplateCatalog runtimes) =>
        definition?.Break is not null;
}
