using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeGameplayFactAdmissionPolicy : IGameplayFactAdmissionModePolicy
{
    public GameplayFactAdmissionRules GetRules(
        GameMode mode,
        CompetitionModeConfiguration competitionConfiguration,
        CompetitionChallengeRules challengeRules,
        ChallengeDefinition? challengeDefinition = null) => mode switch
        {
            GameMode.Ctf when challengeRules is CtfCompetitionChallengeRules ctf =>
                challengeDefinition is CtfChallengeDefinition
                    { InteractionKind: CtfInteractionKind.PatchVerification }
                    ? new(false, true, null,
                        ctf.MaxPatchAttempts
                            ?? Ctf.Configuration.CtfPatchVerificationConfigurationResolver
                                .DefaultMaxPatchAttempts)
                    : new(true, false, ctf.MaxFlagAttempts, null),
            GameMode.Awd when challengeRules is AwdCompetitionChallengeRules =>
                new(true, false, null, null),
            GameMode.Awdp when competitionConfiguration is AwdpCompetitionModeConfiguration awdp
                && challengeRules is AwdpCompetitionChallengeRules rules => new(
                    true,
                    true,
                    rules.MaxBreakSubmissions ?? awdp.MaxBreakSubmissions,
                    rules.MaxFixSubmissions ?? awdp.MaxFixSubmissions,
                    rules.RequireBreakBeforeFix ?? awdp.RequireBreakBeforeFix),
            GameMode.Koh when challengeRules is KohCompetitionChallengeRules =>
                new(false, false, null, null),
            GameMode.LiveSolo => new(false, false, null, null),
            _ => throw new InvalidOperationException(
                "GameplayFact admission configuration types do not match the game mode.")
        };
}
