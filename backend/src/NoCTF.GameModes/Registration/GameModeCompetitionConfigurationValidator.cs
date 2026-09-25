using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeCompetitionConfigurationValidator : ICompetitionConfigurationValidator
{
    public IReadOnlyList<string> Validate(
        GameMode mode,
        CompetitionModeConfiguration configuration,
        int eligibleTeamCount,
        IReadOnlyList<CompetitionChallengeRules> challengeRules) => mode switch
        {
            GameMode.Ctf when configuration is CtfCompetitionModeConfiguration ctf =>
                ValidateCtf(ctf, eligibleTeamCount, challengeRules),
            GameMode.Awd when configuration is AwdCompetitionModeConfiguration awd =>
                Awd.Configuration.AwdConfigurationValidator.Validate(
                    TypedGameModeConfiguration.Awd(awd)),
            GameMode.Awdp when configuration is AwdpCompetitionModeConfiguration awdp =>
                ValidateAwdp(awdp, eligibleTeamCount, challengeRules),
            GameMode.Koh when configuration is KohCompetitionModeConfiguration koh =>
                Koh.Configuration.KohConfigurationValidator.Validate(
                    TypedGameModeConfiguration.Koh(koh)),
            _ => ["The competition configuration type does not match its mode."]
        };

    public IReadOnlyList<string> ValidateForStart(
        GameMode mode,
        CompetitionModeConfiguration configuration,
        int eligibleTeamCount,
        IReadOnlyList<CompetitionChallengeRules> challengeRules)
    {
        var errors = Validate(mode, configuration, eligibleTeamCount, challengeRules).ToList();
        if (errors.Count > 0) return errors;
        if (mode == GameMode.Awdp
            && configuration is AwdpCompetitionModeConfiguration awdp)
        {
            foreach (var rules in challengeRules.OfType<AwdpCompetitionChallengeRules>())
            {
                errors.AddRange(Awdp.Configuration.AwdpConfigurationValidator.ValidateForStart(
                    Awdp.Configuration.AwdpConfigurationResolver.Resolve(
                        TypedGameModeConfiguration.Awdp(awdp),
                        TypedGameModeConfiguration.Awdp(rules),
                        Awdp.Configuration.AwdpChallengeConfiguration.Empty)));
            }
        }
        return errors;
    }

    public IReadOnlyList<string> ValidateForStart(
        GameMode mode,
        CompetitionModeConfiguration configuration,
        int eligibleTeamCount,
        IReadOnlyList<ChallengeConfigurationSections> challenges)
    {
        var errors = Validate(
            mode,
            configuration,
            eligibleTeamCount,
            challenges.Select(challenge => challenge.Rules).ToArray()).ToList();
        if (errors.Count > 0) return errors;

        switch (configuration)
        {
            case AwdpCompetitionModeConfiguration awdp:
                foreach (var challenge in challenges)
                {
                    if (challenge.Rules is not AwdpCompetitionChallengeRules rules
                        || challenge.Definition is not AwdpChallengeDefinition definition)
                    {
                        errors.Add("AWDP challenge configuration types do not match the competition mode.");
                        continue;
                    }
                    errors.AddRange(Awdp.Configuration.AwdpConfigurationValidator.ValidateForStart(
                        Awdp.Configuration.AwdpConfigurationResolver.Resolve(
                            awdp, rules, definition)));
                }
                break;
            case CtfCompetitionModeConfiguration:
                foreach (var challenge in challenges)
                {
                    if (challenge.Rules is not CtfCompetitionChallengeRules rules
                        || challenge.Definition is not CtfChallengeDefinition definition)
                    {
                        errors.Add("CTF challenge configuration types do not match the competition mode.");
                        continue;
                    }
                    if (definition.InteractionKind == CtfInteractionKind.PatchVerification)
                    {
                        if (rules.MaxFlagAttempts is not null)
                            errors.Add("PatchVerification rules cannot configure MaxFlagAttempts.");
                        if (rules.HasFlagTemplate)
                            errors.Add("PatchVerification rules cannot configure FlagTemplate.");
                    }
                    else if (rules.MaxPatchAttempts is not null)
                    {
                        errors.Add("FlagSubmission rules cannot configure MaxPatchAttempts.");
                    }
                }
                break;
            case KohCompetitionModeConfiguration:
                foreach (var challenge in challenges)
                {
                    if (challenge.Definition is not KohChallengeDefinition definition)
                    {
                        errors.Add("KoH challenge definition type does not match the competition mode.");
                        continue;
                    }
                    errors.AddRange(Koh.Configuration.KohConfigurationValidator.ValidateForStart(
                        TypedGameModeConfiguration.Koh(definition)));
                }
                break;
        }
        return errors;
    }

    private static IReadOnlyList<string> ValidateCtf(
        CtfCompetitionModeConfiguration configuration,
        int eligibleTeamCount,
        IReadOnlyList<CompetitionChallengeRules> challengeRules)
    {
        var competition = TypedGameModeConfiguration.Ctf(configuration);
        var errors = Ctf.Configuration.CtfConfigurationValidator.Validate(
            competition, eligibleTeamCount).ToList();
        foreach (var rules in challengeRules)
        {
            if (rules is not CtfCompetitionChallengeRules ctf)
            {
                errors.Add("CTF challenge rules type does not match the competition mode.");
                continue;
            }
            errors.AddRange(Ctf.Configuration.CtfConfigurationValidator.Validate(
                TypedGameModeConfiguration.Ctf(ctf), competition, eligibleTeamCount));
        }
        return errors;
    }

    private static IReadOnlyList<string> ValidateAwdp(
        AwdpCompetitionModeConfiguration configuration,
        int eligibleTeamCount,
        IReadOnlyList<CompetitionChallengeRules> challengeRules)
    {
        var competition = TypedGameModeConfiguration.Awdp(configuration);
        var errors = Awdp.Configuration.AwdpConfigurationValidator.Validate(
            competition, eligibleTeamCount).ToList();
        if (errors.Count > 0) return errors;
        foreach (var rules in challengeRules)
        {
            if (rules is not AwdpCompetitionChallengeRules awdp)
            {
                errors.Add("AWDP challenge rules type does not match the competition mode.");
                continue;
            }
            var mapped = TypedGameModeConfiguration.Awdp(awdp);
            errors.AddRange(Awdp.Configuration.AwdpConfigurationValidator.Validate(
                mapped, eligibleTeamCount));
            errors.AddRange(Awdp.Configuration.AwdpConfigurationValidator.Validate(
                Awdp.Configuration.AwdpConfigurationResolver.Resolve(
                    competition,
                    mapped,
                    Awdp.Configuration.AwdpChallengeConfiguration.Empty),
                eligibleTeamCount));
        }
        return errors;
    }
}
