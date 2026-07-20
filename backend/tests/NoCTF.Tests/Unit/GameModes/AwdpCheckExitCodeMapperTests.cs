using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpCheckExitCodeMapperTests
{
    [Test]
    [Arguments(0, AwdpCheckOutcome.FixSuccess, ScoringResult.Correct, null)]
    [Arguments(1, AwdpCheckOutcome.FixFailed, ScoringResult.Wrong, ScoringFailureCode.AwdpFixFailed)]
    [Arguments(2, AwdpCheckOutcome.FixRuleViolation, ScoringResult.Rejected, ScoringFailureCode.AwdpViolation)]
    [Arguments(3, AwdpCheckOutcome.FixServiceError, ScoringResult.Wrong, ScoringFailureCode.AwdpServiceDown)]
    [Arguments(-1, AwdpCheckOutcome.PlatformFailure, ScoringResult.PlatformFailed, ScoringFailureCode.CheckerPlatformError)]
    public async Task Exit_code_maps_to_bounded_check_decision(
        int exitCode,
        AwdpCheckOutcome outcome,
        ScoringResult result,
        ScoringFailureCode? failureCode)
    {
        var decision = new AwdpCheckExitCodeMapper().Map(exitCode, false);

        await Assert.That(decision.Outcome).IsEqualTo(outcome);
        await Assert.That(decision.Result).IsEqualTo(result);
        await Assert.That(decision.FailureCode).IsEqualTo(failureCode);
    }

    [Test]
    public async Task Timeout_maps_to_service_error_independent_of_exit_code()
    {
        var decision = new AwdpCheckExitCodeMapper().Map(-1, true);

        await Assert.That(decision.Outcome).IsEqualTo(AwdpCheckOutcome.FixServiceError);
        await Assert.That(decision.Result).IsEqualTo(ScoringResult.Wrong);
        await Assert.That(decision.FailureCode).IsEqualTo(ScoringFailureCode.AwdpServiceDown);
    }
}
