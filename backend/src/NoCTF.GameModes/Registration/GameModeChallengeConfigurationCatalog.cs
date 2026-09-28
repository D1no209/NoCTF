using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeChallengeConfigurationCatalog : IChallengeConfigurationCatalog
{
    public CompetitionChallengeRules CreateDefaultRules(
        GameMode mode,
        Guid competitionChallengeId)
    {
        CompetitionChallengeRules rules = mode switch
        {
            GameMode.Ctf => new CtfCompetitionChallengeRules(),
            GameMode.Awd => new AwdCompetitionChallengeRules(),
            GameMode.Awdp => new AwdpCompetitionChallengeRules(),
            GameMode.Koh => new KohCompetitionChallengeRules(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        rules.CompetitionChallengeId = competitionChallengeId;
        return rules;
    }

    public ChallengeDefinition CreateDefaultDefinition(GameMode mode, Guid challengeId)
    {
        ChallengeDefinition definition = mode switch
        {
            GameMode.Ctf => new CtfChallengeDefinition
            {
                InteractionKind = CtfInteractionKind.FlagSubmission
            },
            GameMode.Awd => new AwdChallengeDefinition(),
            GameMode.Awdp => new AwdpChallengeDefinition
            {
                MaximumPatchUploadBytes = PatchUploadRules.DefaultMaximumArchiveBytes
            },
            GameMode.Koh => new KohChallengeDefinition(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        definition.ChallengeId = challengeId;
        return definition;
    }

    public IReadOnlyList<string> Validate(
        GameMode mode,
        CompetitionChallengeRules rules,
        CompetitionModeConfiguration competitionConfiguration,
        int eligibleTeamCount) => (rules, competitionConfiguration) switch
        {
            (CtfCompetitionChallengeRules ctfRules,
                CtfCompetitionModeConfiguration ctfCompetition) =>
                CtfConfigurationValidator.Validate(
                    TypedGameModeConfiguration.Ctf(ctfRules),
                    TypedGameModeConfiguration.Ctf(ctfCompetition),
                    eligibleTeamCount),
            (AwdCompetitionChallengeRules awdRules,
                AwdCompetitionModeConfiguration) =>
                AwdConfigurationValidator.Validate(TypedGameModeConfiguration.Awd(awdRules)),
            (AwdpCompetitionChallengeRules awdpRules,
                AwdpCompetitionModeConfiguration awdpCompetition) =>
                ValidateAwdpRules(awdpRules, awdpCompetition, eligibleTeamCount),
            (KohCompetitionChallengeRules kohRules,
                KohCompetitionModeConfiguration) =>
                KohConfigurationValidator.Validate(TypedGameModeConfiguration.Koh(kohRules)),
            _ => ["Challenge rules type does not match the competition mode."]
        };

    public IReadOnlyList<string> ValidateDefinition(
        GameMode mode,
        ChallengeDefinition definition)
    {
        if (definition.Mode != mode)
            return ["Challenge definition type does not match the game mode."];
        var runtime = TypedGameModeConfiguration.Runtime(definition.Runtime);
        var errors = ChallengeRuntimeTemplateValidator.Validate(
            runtime,
            allowControlCheckUrlBinding: mode == GameMode.Koh).ToList();
        errors.AddRange(definition switch
        {
            CtfChallengeDefinition ctf => CtfConfigurationValidator.Validate(
                TypedGameModeConfiguration.Ctf(ctf)),
            AwdChallengeDefinition awd => AwdConfigurationValidator.Validate(
                TypedGameModeConfiguration.Awd(awd)),
            AwdpChallengeDefinition awdp => AwdpConfigurationValidator.Validate(
                TypedGameModeConfiguration.Awdp(awdp)),
            KohChallengeDefinition koh => KohConfigurationValidator.Validate(
                TypedGameModeConfiguration.Koh(koh)),
            _ => ["Unsupported challenge definition type."]
        });
        return errors;
    }

    public IReadOnlyList<string> ValidateDefinitionForStart(
        GameMode mode,
        ChallengeDefinition definition)
    {
        var errors = ValidateDefinition(mode, definition).ToList();
        if (errors.Count > 0) return errors;
        if (definition is KohChallengeDefinition koh)
            errors.AddRange(KohConfigurationValidator.ValidateForStart(
                TypedGameModeConfiguration.Koh(koh)));
        return errors;
    }

    public IReadOnlyList<string> ValidateRulesForDefinition(
        GameMode mode,
        CompetitionChallengeRules rules,
        ChallengeDefinition definition,
        CompetitionModeConfiguration competitionConfiguration,
        int eligibleTeamCount)
    {
        var errors = Validate(
            mode, rules, competitionConfiguration, eligibleTeamCount).ToList();
        errors.AddRange(ValidateDefinition(mode, definition));
        if (errors.Count > 0) return errors;
        if (mode == GameMode.Awdp
            && rules is AwdpCompetitionChallengeRules awdpRules
            && definition is AwdpChallengeDefinition awdpDefinition
            && competitionConfiguration is AwdpCompetitionModeConfiguration awdpCompetition)
        {
            errors.AddRange(AwdpConfigurationValidator.Validate(
                AwdpConfigurationResolver.Resolve(
                    awdpCompetition,
                    awdpRules,
                    awdpDefinition),
                eligibleTeamCount));
        }
        if (mode == GameMode.Ctf
            && rules is CtfCompetitionChallengeRules ctfRules
            && definition is CtfChallengeDefinition ctfDefinition)
        {
            if (ctfDefinition.InteractionKind == CtfInteractionKind.PatchVerification)
            {
                if (ctfRules.MaxFlagAttempts is not null)
                    errors.Add("PatchVerification rules cannot configure MaxFlagAttempts.");
                if (ctfRules.HasFlagTemplate)
                    errors.Add("PatchVerification rules cannot configure FlagTemplate.");
            }
            else if (ctfRules.MaxPatchAttempts is not null)
            {
                errors.Add("FlagSubmission rules cannot configure MaxPatchAttempts.");
            }
        }
        return errors;
    }

    private static IReadOnlyList<string> ValidateAwdpRules(
        AwdpCompetitionChallengeRules rules,
        AwdpCompetitionModeConfiguration competition,
        int eligibleTeamCount)
    {
        var mappedRules = TypedGameModeConfiguration.Awdp(rules);
        var errors = AwdpConfigurationValidator.Validate(
            mappedRules, eligibleTeamCount).ToList();
        errors.AddRange(AwdpConfigurationValidator.Validate(
            AwdpConfigurationResolver.Resolve(
                TypedGameModeConfiguration.Awdp(competition),
                mappedRules,
                AwdpChallengeConfiguration.Empty),
            eligibleTeamCount));
        return errors;
    }
}
