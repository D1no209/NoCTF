using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.Awdp.Scoring;

public sealed record AwdpFixDecision(
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode);

public static class AwdpFixOutcomeMapper
{
    public static AwdpFixDecision Map(AwdpFixOutcome outcome) => outcome switch
    {
        AwdpFixOutcome.DefenseSucceeded => new(GameplayFactResult.Correct, null),
        AwdpFixOutcome.ExploitSucceeded =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpExploitSucceeded),
        AwdpFixOutcome.ServiceAbnormal =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpServiceAbnormal),
        AwdpFixOutcome.PatchFailed =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpPatchFailed),
        AwdpFixOutcome.PatchTimeout =>
            new(GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpPatchTimeout),
        AwdpFixOutcome.PlatformFailed =>
            new(null, GameplayFactFailureCode.CheckerPlatformError),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}
