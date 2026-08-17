using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpFixOutcomeMapperTests
{
    [Test]
    [Arguments(AwdpFixOutcome.DefenseSucceeded, GameplayFactResult.Correct, null)]
    [Arguments(AwdpFixOutcome.ExploitSucceeded, GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpExploitSucceeded)]
    [Arguments(AwdpFixOutcome.ServiceAbnormal, GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpServiceAbnormal)]
    [Arguments(AwdpFixOutcome.PatchFailed, GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpPatchFailed)]
    [Arguments(AwdpFixOutcome.PatchTimeout, GameplayFactResult.Wrong, GameplayFactFailureCode.AwdpPatchTimeout)]
    [Arguments(AwdpFixOutcome.PlatformFailed, null, GameplayFactFailureCode.CheckerPlatformError)]
    public async Task Typed_outcome_maps_to_one_domain_decision(
        AwdpFixOutcome outcome,
        GameplayFactResult? result,
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
            generation: 3,
            runtimeProcessingVersion: 5,
            AwdpFixOutcome.ServiceAbnormal,
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

        await Assert.That(result.GameplayFactId)
            .IsEqualTo(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        await Assert.That(result.RuntimeProcessingVersion).IsEqualTo(5);
    }
}
