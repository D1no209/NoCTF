namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationResolver
{
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
            definition.Runtime ?? competition.Runtime,
            definition.PatchEntrypoint ?? competition.PatchEntrypoint,
            definition.PatchCommand ?? competition.PatchCommand,
            definition.PatchTimeoutSeconds ?? competition.PatchTimeoutSeconds,
            definition.Checker ?? competition.Checker,
            definition.ReadyTimeoutSeconds ?? competition.ReadyTimeoutSeconds);

    public static AwdpEffectiveConfiguration Resolve(
        AwdpConfiguration competition,
        AwdpChallengeConfiguration challenge) =>
        Resolve(competition, challenge, challenge);
}
