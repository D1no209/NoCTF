using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;

namespace NoCTF.GameModes.Awdp.Scoring;

public sealed class AwdpCheckExitCodeMapper : IAwdpCheckExitCodeMapper
{
    public AwdpCheckDecision Map(int exitCode, bool timedOut)
    {
        if (timedOut)
            return new(AwdpCheckOutcome.FixServiceError, ScoringResult.Wrong, ScoringFailureCode.AwdpServiceDown);
        return exitCode switch
        {
            0 => new(AwdpCheckOutcome.FixSuccess, ScoringResult.Correct, null),
            1 => new(AwdpCheckOutcome.FixFailed, ScoringResult.Wrong, ScoringFailureCode.AwdpFixFailed),
            2 => new(AwdpCheckOutcome.FixRuleViolation, ScoringResult.Rejected, ScoringFailureCode.AwdpViolation),
            3 => new(AwdpCheckOutcome.FixServiceError, ScoringResult.Wrong, ScoringFailureCode.AwdpServiceDown),
            _ => new(AwdpCheckOutcome.PlatformFailure, ScoringResult.PlatformFailed, ScoringFailureCode.CheckerPlatformError)
        };
    }
}
