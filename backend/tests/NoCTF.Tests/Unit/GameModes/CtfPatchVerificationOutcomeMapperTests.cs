using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.PatchVerification.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CtfPatchVerificationOutcomeMapperTests
{
    [Test]
    [Arguments(AwdpFixOutcome.DefenseSucceeded, GameplayFactResult.Correct, null)]
    [Arguments(AwdpFixOutcome.ExploitSucceeded, GameplayFactResult.Wrong, GameplayFactFailureCode.PatchStillExploitable)]
    [Arguments(AwdpFixOutcome.ServiceAbnormal, GameplayFactResult.Wrong, GameplayFactFailureCode.PatchServiceAbnormal)]
    [Arguments(AwdpFixOutcome.PatchFailed, GameplayFactResult.Wrong, GameplayFactFailureCode.PatchExecutionFailed)]
    [Arguments(AwdpFixOutcome.PatchTimeout, GameplayFactResult.Wrong, GameplayFactFailureCode.PatchExecutionFailed)]
    [Arguments(AwdpFixOutcome.PlatformFailed, GameplayFactResult.Rejected, GameplayFactFailureCode.PatchVerificationPlatformFailed)]
    public async Task Maps_checker_outcomes_to_ctf_result_semantics(
        AwdpFixOutcome outcome,
        GameplayFactResult result,
        GameplayFactFailureCode? failureCode)
    {
        var decision = CtfPatchVerificationOutcomeMapper.Map(outcome);

        await Assert.That(decision.Result).IsEqualTo(result);
        await Assert.That(decision.FailureCode).IsEqualTo(failureCode);
    }
}
