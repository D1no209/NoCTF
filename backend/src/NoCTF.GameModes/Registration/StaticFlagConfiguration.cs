using NoCTF.Domain.Challenges;
using NoCTF.Domain.LiveSolo;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.GameModes.Registration;

/// <summary>Explicit mode boundary for manual/static Flag authoring capabilities.</summary>
public static class StaticFlagConfiguration
{
    public static bool SupportsManualFlags(ChallengeDefinition? definition, IChallengeRuntimeTemplateCatalog runtimes) => definition is AwdpChallengeDefinition || SupportsRegularExpression(definition, runtimes);
    public static bool SupportsRegularExpression(ChallengeDefinition? definition, IChallengeRuntimeTemplateCatalog runtimes) =>
        definition is CtfChallengeDefinition { InteractionKind: CtfInteractionKind.FlagSubmission } or LiveSoloChallengeDefinition
        && runtimes.Get(definition)?.FlagSource is null or RuntimeFlagSource.Static;
}
