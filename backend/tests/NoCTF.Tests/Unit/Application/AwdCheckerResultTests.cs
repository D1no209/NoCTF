using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Application;

public sealed class AwdCheckerResultTests
{
    [Test]
    [Arguments(AwdServiceState.Up, "55490A4BF3A8E8CB5EED69720293A700FF01EF7DA2BFC749F332F626CB584916")]
    [Arguments(AwdServiceState.Down, "B86D11AF79188E58AF384B421F2799B08BFB151939B4404679D948AAE2720B0B")]
    public async Task Callback_state_uses_the_documented_ascii_body_hash(
        AwdServiceState state,
        string expectedSha256)
    {
        var result = AwdCheckResult.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            generation: 2,
            checkerSequence: 3,
            processingVersion: 4,
            state,
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

        await Assert.That(Convert.ToHexString(result.BodySha256)).IsEqualTo(expectedSha256);
    }

    [Test]
    [Arguments(AwdServiceState.Up, AwdServiceState.Up, null)]
    [Arguments(AwdServiceState.Up, AwdServiceState.Down, ScoringResult.Wrong)]
    [Arguments(AwdServiceState.Down, AwdServiceState.Down, null)]
    [Arguments(AwdServiceState.Down, AwdServiceState.Up, ScoringResult.Correct)]
    public async Task Only_service_state_changes_produce_scoring_facts(
        AwdServiceState current,
        AwdServiceState received,
        ScoringResult? expected)
    {
        var result = AwdServiceStateTransition.ToScoringResult(current, received);

        await Assert.That(result).IsEqualTo(expected);
    }
}
