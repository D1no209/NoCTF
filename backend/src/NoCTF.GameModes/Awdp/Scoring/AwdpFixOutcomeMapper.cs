using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.Awdp.Scoring;

public sealed record AwdpFixDecision(
    GameplayFactResult Result,
    GameplayFactFailureCode? FailureCode);

public static class AwdpFixOutcomeMapper
{
    public static AwdpFixDecision Map(AwdpFixOutcome outcome) => outcome switch
    {
        AwdpFixOutcome.DefenseSucceeded => new(GameplayFactResult.Correct, null),
        AwdpFixOutcome.ExploitSucceeded =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpExploitSucceeded),
        AwdpFixOutcome.ServiceAbnormal =>
            new(GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpServiceAbnormal),
        AwdpFixOutcome.PatchFailed =>
            new(GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpPatchFailed),
        AwdpFixOutcome.PatchTimeout =>
            new(GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpPatchTimeout),
        AwdpFixOutcome.PlatformFailed =>
            new(GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpPlatformFailed),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}
