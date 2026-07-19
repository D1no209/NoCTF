namespace NoCTF.Domain.Submissions;

public static class FixVerificationStateMachine
{
    public static bool CanResetForRetry(FixVerificationStatus status) =>
        status is FixVerificationStatus.Valid
            or FixVerificationStatus.TeamFailure
            or FixVerificationStatus.PlatformFailed;

    public static bool CanTransition(FixVerificationStatus from, FixVerificationStatus to) =>
        (from, to) switch
        {
            (FixVerificationStatus.Created, FixVerificationStatus.Claimed) => true,
            (FixVerificationStatus.Claimed, FixVerificationStatus.Verifying) => true,
            (FixVerificationStatus.Verifying, FixVerificationStatus.Valid) => true,
            (FixVerificationStatus.Verifying, FixVerificationStatus.TeamFailure) => true,
            (FixVerificationStatus.Verifying, FixVerificationStatus.PlatformFailed) => true,
            (FixVerificationStatus.Created, FixVerificationStatus.Expired) => true,
            (FixVerificationStatus.Claimed, FixVerificationStatus.Expired) => true,
            _ => false
        };
}
