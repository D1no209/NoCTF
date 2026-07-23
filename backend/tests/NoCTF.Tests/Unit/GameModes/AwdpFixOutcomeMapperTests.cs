using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awdp.Scoring;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpFixOutcomeMapperTests
{
    [Test]
    [Arguments(AwdpFixOutcome.Fixed, ScoringResult.Correct, null)]
    [Arguments(AwdpFixOutcome.StillVulnerable, ScoringResult.Wrong, ScoringFailureCode.AwdpFixFailed)]
    [Arguments(AwdpFixOutcome.RuleViolation, ScoringResult.Rejected, ScoringFailureCode.AwdpViolation)]
    [Arguments(AwdpFixOutcome.ServiceUnavailable, ScoringResult.Wrong, ScoringFailureCode.AwdpServiceDown)]
    [Arguments(AwdpFixOutcome.PatchFailed, ScoringResult.Wrong, ScoringFailureCode.AwdpPatchFailed)]
    [Arguments(AwdpFixOutcome.PatchTimeout, ScoringResult.Wrong, ScoringFailureCode.AwdpPatchTimeout)]
    [Arguments(AwdpFixOutcome.PlatformFailed, ScoringResult.PlatformFailed, ScoringFailureCode.CheckerPlatformError)]
    public async Task Typed_outcome_maps_to_one_domain_decision(
        AwdpFixOutcome outcome,
        ScoringResult result,
        ScoringFailureCode? failureCode)
    {
        var decision = AwdpFixOutcomeMapper.Map(outcome);

        await Assert.That(decision.Result).IsEqualTo(result);
        await Assert.That(decision.FailureCode).IsEqualTo(failureCode);
    }

    [Test]
    public async Task Result_hash_uses_the_canonical_outcome_name()
    {
        var result = AwdpFixResult.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            generation: 3,
            processingVersion: 4,
            runtimeProcessingVersion: 5,
            AwdpFixOutcome.RuleViolation,
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

        await Assert.That(Convert.ToHexString(result.BodySha256))
            .IsEqualTo("608DD216E93C309A1ABDCD03B8C1EE57657B44DB64F07DD4667D26EE08A1A2B2");
    }
}
