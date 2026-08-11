namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationResolver
{
    private const string DefaultPatchEntrypoint = "fix.sh";
    private const int DefaultPatchTimeoutSeconds = 60;
    private const int DefaultReadyTimeoutSeconds = 30;

    public static AwdpEffectiveConfiguration Resolve(
        string competitionJson,
        string legacyChallengeJson) =>
        Resolve(competitionJson, legacyChallengeJson, legacyChallengeJson);

    public static AwdpEffectiveConfiguration Resolve(
        string competitionJson,
        string challengeRulesJson,
        string challengeDefinitionJson) =>
        Resolve(
            AwdpConfigurationParser.ParseCompetition(competitionJson),
            AwdpConfigurationParser.ParseChallenge(challengeRulesJson),
            AwdpConfigurationParser.ParseChallenge(challengeDefinitionJson));

    public static AwdpEffectiveConfiguration Resolve(
        AwdpConfiguration competition,
        AwdpChallengeConfiguration rules,
        AwdpChallengeConfiguration definition) =>
        new(
            competition.RoundDurationSeconds,
            rules.Break ?? competition.Break,
            rules.Fix ?? competition.Fix,
            rules.BreakWrongPenalty ?? competition.BreakWrongPenalty,
            rules.FixFailurePenalty ?? competition.FixFailurePenalty,
            rules.ViolationPenalty ?? competition.ViolationPenalty,
            rules.ServiceDownPenalty ?? competition.ServiceDownPenalty,
            rules.RequireBreakBeforeFix ?? competition.RequireBreakBeforeFix,
            rules.MaxBreakSubmissions ?? competition.MaxBreakSubmissions,
            rules.MaxFixSubmissions ?? competition.MaxFixSubmissions,
            rules.EvaluationDispatchMode ?? competition.EvaluationDispatchMode,
            definition.Runtime,
            definition.PatchEntrypoint ?? DefaultPatchEntrypoint,
            definition.PatchCommand,
            definition.PatchTimeoutSeconds ?? DefaultPatchTimeoutSeconds,
            definition.Checker,
            definition.ReadyTimeoutSeconds ?? DefaultReadyTimeoutSeconds,
            definition.MaximumPatchUploadBytes
                ?? NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.DefaultMaximumArchiveBytes);

    public static AwdpEffectiveConfiguration Resolve(
        AwdpConfiguration competition,
        AwdpChallengeConfiguration challenge) =>
        Resolve(competition, challenge, challenge);
}
