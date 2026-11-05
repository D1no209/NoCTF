using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.LiveSolo.Configuration;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;

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
            GameMode.LiveSolo => new LiveSoloCompetitionChallengeRules(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        rules.CompetitionChallengeId = competitionChallengeId;
        return rules;
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
                AwdCompetitionModeConfiguration awdCompetition) =>
                AwdConfigurationValidator.Validate(TypedGameModeConfiguration.Awd(awdRules))
                    .Concat(AwdEffectiveRules.ValidateConfiguration(awdCompetition, awdRules)).ToArray(),
            (AwdpCompetitionChallengeRules awdpRules,
                AwdpCompetitionModeConfiguration awdpCompetition) =>
                ValidateAwdpRules(awdpRules, awdpCompetition, eligibleTeamCount),
            (KohCompetitionChallengeRules kohRules,
                KohCompetitionModeConfiguration) =>
                KohConfigurationValidator.Validate(TypedGameModeConfiguration.Koh(kohRules)),
            (LiveSoloCompetitionChallengeRules, LiveSoloCompetitionModeConfiguration liveSolo) => LiveSoloConfigurationValidator.Validate(liveSolo),
            _ => ["Challenge rules type does not match the competition mode."]
        };

    public IReadOnlyList<string> ValidateDefinition(ChallengeDefinition definition)
    {
        var errors = ChallengeCapabilityValidation.Validate(definition).Select(error => $"{error.Capability}: {error.Code}.").ToList();
        if (errors.Count > 0) return errors;
        var runtime = TypedGameModeConfiguration.Runtime(definition);
        errors.AddRange(ChallengeRuntimeTemplateValidator.Validate(
            runtime,
            allowControlCheckUrlBinding: definition.Control is not null));
        if (definition.Check is not null)
            errors.AddRange(RunnerJobConfigurationValidator.Validate(TypedGameModeConfiguration.Checker(definition), "Check"));
        return errors;
    }

    public IReadOnlyList<string> ValidateDefinitionForStart(
        GameMode mode,
        ChallengeDefinition definition)
    {
        var errors = ValidateDefinition(definition).ToList();
        if (errors.Count > 0) return errors;
        if (mode == GameMode.Koh)
            errors.AddRange(KohConfigurationValidator.ValidateForStart(
                TypedGameModeConfiguration.Koh(definition)));
        return errors;
    }

    public IReadOnlyList<string> ValidateRulesForDefinition(
        GameMode mode,
        CompetitionChallengeRules rules,
        ChallengeDefinition definition,
        CompetitionModeConfiguration competitionConfiguration,
        int eligibleTeamCount,
        NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset? scorePreset = null)
    {
        var errors = (rules is CtfCompetitionChallengeRules ctf && competitionConfiguration is CtfCompetitionModeConfiguration competition
            ? CtfConfigurationValidator.Validate(TypedGameModeConfiguration.Ctf(ctf, scorePreset),
                TypedGameModeConfiguration.Ctf(competition), eligibleTeamCount)
            : Validate(mode, rules, competitionConfiguration, eligibleTeamCount)).ToList();
        errors.AddRange(ValidateDefinition(definition));
        var required = RequiredCapabilities(rules);
        foreach (var capability in required)
            if (!definition.Capabilities.Any(item => item.Kind == capability))
                errors.Add($"Required capability {capability} is missing.");
        if (mode != rules.Mode || mode != competitionConfiguration.Mode)
            errors.Add("Competition and challenge rules must use the same mode.");
        if (rules is IChallengeRuntimeRules runtimeRules)
        {
            var policy = runtimeRules.Runtime;
            if (!Enum.IsDefined(policy.Allocation) || !Enum.IsDefined(policy.FlagSource) || policy.TtlSeconds is <= 0 or > 604800)
                errors.Add("Runtime allocation, Flag issuance or lifetime is invalid.");
            if (policy.Allocation != (mode == GameMode.Koh ? PersistedRuntimeAllocation.Shared : PersistedRuntimeAllocation.PerTeam))
                errors.Add("Runtime allocation is incompatible with this competition mode.");
            var validFlagSource = mode switch
            {
                GameMode.Ctf or GameMode.LiveSolo => policy.FlagSource is PersistedRuntimeFlagSource.Static or PersistedRuntimeFlagSource.PerTeam,
                GameMode.Awd => policy.FlagSource == PersistedRuntimeFlagSource.AwdRotation,
                GameMode.Awdp => policy.FlagSource == PersistedRuntimeFlagSource.PerTeam,
                GameMode.Koh => policy.FlagSource == PersistedRuntimeFlagSource.Static,
                _ => false
            };
            if (!validFlagSource) errors.Add("Flag issuance is incompatible with this competition mode.");
        }
        if (errors.Count > 0) return errors;
        var effectiveRuntime = TypedGameModeConfiguration.Runtime(definition, rules);
        errors.AddRange(ChallengeRuntimeTemplateValidator.Validate(effectiveRuntime, mode == GameMode.Koh));
        if (mode == GameMode.Awdp
            && rules is AwdpCompetitionChallengeRules awdpRules
            && competitionConfiguration is AwdpCompetitionModeConfiguration awdpCompetition)
        {
            errors.AddRange(AwdpConfigurationValidator.Validate(
                AwdpConfigurationResolver.Resolve(
                    awdpCompetition,
                    awdpRules,
                    definition),
                eligibleTeamCount));
        }
        if (mode == GameMode.Ctf) errors.AddRange(CtfConfigurationValidator.Validate(TypedGameModeConfiguration.Ctf(definition) with { Runtime = effectiveRuntime }));
        if (mode == GameMode.Awd) errors.AddRange(AwdConfigurationValidator.Validate(TypedGameModeConfiguration.Awd(definition) with { Runtime = effectiveRuntime }));
        if (mode == GameMode.Koh) errors.AddRange(KohConfigurationValidator.Validate(TypedGameModeConfiguration.Koh(definition) with { Runtime = effectiveRuntime }));
        if (mode == GameMode.LiveSolo) errors.AddRange(LiveSoloConfigurationValidator.ValidateDefinition(definition));
        if (required.Contains(ChallengeCapabilityKind.Fix) && definition.Check is { } checker
            && mode is GameMode.Awdp or GameMode.LiveSolo
            && (!checker.Requirements.Any(item => item.Kind == CheckItemKind.Functionality)
                || !checker.Requirements.Any(item => item.Kind == CheckItemKind.Vulnerability)))
            errors.Add("Fix completion requires functionality and vulnerability checks.");
        return errors;
    }

    public static IReadOnlyList<ChallengeCapabilityKind> RequiredCapabilities(CompetitionChallengeRules rules) => rules switch
    {
        CtfCompetitionChallengeRules => [ChallengeCapabilityKind.Break],
        AwdCompetitionChallengeRules { AllowFixSubmissions: true } => [ChallengeCapabilityKind.Break, ChallengeCapabilityKind.Check, ChallengeCapabilityKind.Fix],
        AwdCompetitionChallengeRules => [ChallengeCapabilityKind.Break, ChallengeCapabilityKind.Check],
        AwdpCompetitionChallengeRules => [ChallengeCapabilityKind.Break, ChallengeCapabilityKind.Fix, ChallengeCapabilityKind.Check],
        KohCompetitionChallengeRules => [ChallengeCapabilityKind.Control],
        LiveSoloCompetitionChallengeRules { CompletionKind: LiveSoloCompletionKind.Break } => [ChallengeCapabilityKind.Break],
        LiveSoloCompetitionChallengeRules { CompletionKind: LiveSoloCompletionKind.FixAndCheck } => [ChallengeCapabilityKind.Fix, ChallengeCapabilityKind.Check],
        _ => throw new ArgumentOutOfRangeException(nameof(rules))
    };

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
