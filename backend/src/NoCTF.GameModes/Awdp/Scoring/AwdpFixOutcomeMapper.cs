using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.Awdp.Scoring;

public sealed record AwdpFixDecision(
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode);

public static class AwdpFixOutcomeMapper
{
    public static AwdpFixDecision Map(AwdpFixOutcome outcome) => outcome switch
    {
        AwdpFixOutcome.Fixed => new(GameplayFactResult.Correct, null),
        AwdpFixOutcome.StillVulnerable =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpFixFailed),
        AwdpFixOutcome.RuleViolation =>
            new(GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpViolation),
        AwdpFixOutcome.ServiceUnavailable =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpServiceDown),
        AwdpFixOutcome.PatchFailed =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpPatchFailed),
        AwdpFixOutcome.PatchTimeout =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpPatchTimeout),
        AwdpFixOutcome.PlatformFailed =>
            new(null, GameplayFactFailureCode.CheckerPlatformError),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}
