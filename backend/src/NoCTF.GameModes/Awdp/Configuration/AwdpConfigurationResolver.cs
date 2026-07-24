namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationResolver
{
    public static AwdpEffectiveConfiguration Resolve(
        string competitionJson,
        string challengeJson) =>
        Resolve(
            AwdpConfigurationParser.ParseCompetition(competitionJson),
            AwdpConfigurationParser.ParseChallenge(challengeJson));

    public static AwdpEffectiveConfiguration Resolve(
        AwdpConfiguration competition,
        AwdpChallengeConfiguration challenge) =>
        new(
            competition.RoundDurationSeconds,
            challenge.Break ?? competition.Break,
            challenge.Fix ?? competition.Fix,
            challenge.BreakWrongPenalty ?? competition.BreakWrongPenalty,
            challenge.FixFailurePenalty ?? competition.FixFailurePenalty,
            challenge.ViolationPenalty ?? competition.ViolationPenalty,
            challenge.ServiceDownPenalty ?? competition.ServiceDownPenalty,
            challenge.RequireBreakBeforeFix ?? competition.RequireBreakBeforeFix,
            challenge.MaxBreakSubmissions ?? competition.MaxBreakSubmissions,
            challenge.MaxFixSubmissions ?? competition.MaxFixSubmissions,
            challenge.EvaluationDispatchMode ?? competition.EvaluationDispatchMode,
            challenge.Runtime ?? competition.Runtime,
            challenge.PatchEntrypoint ?? competition.PatchEntrypoint,
            challenge.PatchCommand ?? competition.PatchCommand,
            challenge.PatchTimeoutSeconds ?? competition.PatchTimeoutSeconds,
            challenge.Checker ?? competition.Checker,
            challenge.TargetPort ?? competition.TargetPort,
            challenge.ReadyTimeoutSeconds ?? competition.ReadyTimeoutSeconds);
}
