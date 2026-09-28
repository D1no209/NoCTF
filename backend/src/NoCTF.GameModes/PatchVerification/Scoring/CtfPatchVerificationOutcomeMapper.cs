using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.PatchVerification.Scoring;

public sealed record PatchVerificationDecision(
    GameplayFactResult Result,
    GameplayFactFailureCode? FailureCode);

public static class CtfPatchVerificationOutcomeMapper
{
    public static PatchVerificationDecision Map(AwdpFixOutcome outcome) => outcome switch
    {
        AwdpFixOutcome.DefenseSucceeded => new(GameplayFactResult.Correct, null),
        AwdpFixOutcome.ExploitSucceeded => new(
            GameplayFactResult.Wrong,
            GameplayFactFailureCode.PatchStillExploitable),
        AwdpFixOutcome.ServiceAbnormal => new(
            GameplayFactResult.Wrong,
            GameplayFactFailureCode.PatchServiceAbnormal),
        AwdpFixOutcome.PatchFailed or AwdpFixOutcome.PatchTimeout => new(
            GameplayFactResult.Wrong,
            GameplayFactFailureCode.PatchExecutionFailed),
        AwdpFixOutcome.PlatformFailed => new(
            GameplayFactResult.Rejected,
            GameplayFactFailureCode.PatchVerificationPlatformFailed),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}
