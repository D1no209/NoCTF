using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpFixOutcomeMapperTests
{
    [Test]
    [Arguments(AwdpFixOutcome.DefenseSucceeded, GameplayFactResult.Correct, null)]
    [Arguments(AwdpFixOutcome.ExploitSucceeded, GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpExploitSucceeded)]
    [Arguments(AwdpFixOutcome.ServiceAbnormal, GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpServiceAbnormal)]
    [Arguments(AwdpFixOutcome.PatchFailed, GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpPatchFailed)]
    [Arguments(AwdpFixOutcome.PatchTimeout, GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpPatchTimeout)]
    [Arguments(AwdpFixOutcome.PlatformFailed, GameplayFactResult.Rejected, GameplayFactFailureCode.AwdpPlatformFailed)]
    public async Task Typed_outcome_maps_to_one_domain_decision(
        AwdpFixOutcome outcome,
        GameplayFactResult result,
        GameplayFactFailureCode? failureCode)
    {
        var decision = AwdpFixOutcomeMapper.Map(outcome);

        await Assert.That(decision.Result).IsEqualTo(result);
        await Assert.That(decision.FailureCode).IsEqualTo(failureCode);
    }

    [Test]
    public async Task Callback_result_contains_only_current_fact_and_runtime_fences()
    {
        var result = AwdpFixResult.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            AwdpFixOutcome.ServiceAbnormal,
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

        await Assert.That(result.GameplayFactId)
            .IsEqualTo(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }
}
