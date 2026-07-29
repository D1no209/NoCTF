using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Application;

public sealed class AwdCheckerResultTests
{
    [Test]
    [Arguments(AwdServiceState.Up)]
    [Arguments(AwdServiceState.Down)]
    [Arguments(AwdServiceState.CheckerAbnormalExit)]
    [Arguments(AwdServiceState.CheckerTimedOut)]
    public async Task Callback_preserves_the_typed_status(AwdServiceState state)
    {
        var result = AwdCheckResult.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            generation: 2,
            state,
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

        await Assert.That(result.State).IsEqualTo(state);
    }

    [Test]
    [Arguments(AwdServiceState.Up, AwdServiceState.Up, null)]
    [Arguments(AwdServiceState.Up, AwdServiceState.Down, ScoringResult.Wrong)]
    [Arguments(AwdServiceState.Down, AwdServiceState.Down, null)]
    [Arguments(AwdServiceState.Down, AwdServiceState.Up, ScoringResult.Correct)]
    [Arguments(AwdServiceState.Up, AwdServiceState.CheckerAbnormalExit, null)]
    public async Task Only_service_state_changes_produce_scoring_facts(
        AwdServiceState current,
        AwdServiceState received,
        ScoringResult? expected)
    {
        var result = AwdServiceStateTransition.ToScoringResult(current, received);

        await Assert.That(result).IsEqualTo(expected);
    }
}
