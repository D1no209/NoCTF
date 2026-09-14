using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeGameplayFactAdmissionPolicy : IGameplayFactAdmissionModePolicy
{
    public GameplayFactAdmissionRules GetRules(
        GameMode mode,
        string competitionConfigurationJson,
        string challengeConfigurationJson) => mode switch
        {
            GameMode.Ctf => CtfRules(challengeConfigurationJson),
            GameMode.Awd => AwdRules(challengeConfigurationJson),
            GameMode.Awdp => AwdpRules(
                competitionConfigurationJson,
                challengeConfigurationJson),
            GameMode.Koh => new(false, false, null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
        };

    public GameplayFactAdmissionRules GetRules(
        GameMode mode,
        string competitionConfigurationJson,
        string challengeConfigurationJson,
        string? challengeDefinitionJson) =>
        mode == GameMode.Ctf
            ? CtfRules(challengeConfigurationJson, challengeDefinitionJson)
            : GetRules(mode, competitionConfigurationJson, challengeConfigurationJson);

    private static GameplayFactAdmissionRules CtfRules(string json)
    {
        var configuration = Ctf.Configuration.CtfConfigurationUpgrader.ParseChallenge(json);
        return configuration.InteractionKind == CtfInteractionKind.PatchVerification
            ? new(false, true, null,
                configuration.MaxPatchAttempts
                    ?? Ctf.Configuration.CtfPatchVerificationConfigurationResolver.DefaultMaxPatchAttempts)
            : new(true, false, configuration.MaxFlagAttempts, null);
    }

    private static GameplayFactAdmissionRules CtfRules(
        string rulesJson,
        string? definitionJson)
    {
        if (!string.IsNullOrWhiteSpace(definitionJson)
            && Ctf.Configuration.CtfConfigurationUpgrader.ParseChallenge(definitionJson)
                .InteractionKind == CtfInteractionKind.PatchVerification)
        {
            var rules = Ctf.Configuration.CtfConfigurationUpgrader.ParseChallenge(rulesJson);
            return new(false, true, null,
                rules.MaxPatchAttempts
                    ?? Ctf.Configuration.CtfPatchVerificationConfigurationResolver.DefaultMaxPatchAttempts);
        }
        return CtfRules(rulesJson);
    }

    private static GameplayFactAdmissionRules AwdRules(string json)
    {
        _ = Awd.Configuration.AwdConfigurationUpgrader.ParseChallenge(json);
        return new(true, false, null, null);
    }

    private static GameplayFactAdmissionRules AwdpRules(
        string competitionJson,
        string challengeJson)
    {
        var configuration = Awdp.Configuration.AwdpConfigurationResolver.Resolve(
            competitionJson,
            challengeJson);
        return new(
            true,
            true,
            configuration.MaxBreakSubmissions,
            configuration.MaxFixSubmissions,
            configuration.RequireBreakBeforeFix);
    }
}
