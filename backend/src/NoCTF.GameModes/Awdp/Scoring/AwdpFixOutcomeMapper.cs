using NoCTF.Domain.Submissions;

namespace NoCTF.GameModes.Awdp.Scoring;

public sealed record AwdpFixDecision(
    ScoringResult Result,
    ScoringFailureCode? FailureCode);

public static class AwdpFixOutcomeMapper
{
    public static AwdpFixDecision Map(AwdpFixOutcome outcome) => outcome switch
    {
        AwdpFixOutcome.Fixed => new(ScoringResult.Correct, null),
        AwdpFixOutcome.StillVulnerable =>
            new(ScoringResult.Wrong, ScoringFailureCode.AwdpFixFailed),
        AwdpFixOutcome.RuleViolation =>
            new(ScoringResult.Rejected, ScoringFailureCode.AwdpViolation),
        AwdpFixOutcome.ServiceUnavailable =>
            new(ScoringResult.Wrong, ScoringFailureCode.AwdpServiceDown),
        AwdpFixOutcome.PatchFailed =>
            new(ScoringResult.Wrong, ScoringFailureCode.AwdpPatchFailed),
        AwdpFixOutcome.PatchTimeout =>
            new(ScoringResult.Wrong, ScoringFailureCode.AwdpPatchTimeout),
        AwdpFixOutcome.PlatformFailed =>
            new(ScoringResult.PlatformFailed, ScoringFailureCode.CheckerPlatformError),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}
