using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;

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
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            state,
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

        await Assert.That(result.State).IsEqualTo(state);
        await Assert.That(result.GameplayFactId)
            .IsEqualTo(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    [Test]
    [Arguments(AwdServiceState.Up, AwdServiceState.Up, null)]
    [Arguments(AwdServiceState.Up, AwdServiceState.Down, GameplayFactResult.ServiceDown)]
    [Arguments(AwdServiceState.Down, AwdServiceState.Down, null)]
    [Arguments(AwdServiceState.Down, AwdServiceState.Up, GameplayFactResult.ServiceUp)]
    [Arguments(AwdServiceState.Up, AwdServiceState.CheckerAbnormalExit, null)]
    public async Task Only_service_state_changes_produce_scoring_facts(
        AwdServiceState current,
        AwdServiceState received,
        GameplayFactResult? expected)
    {
        var result = AwdServiceStateTransition.ToGameplayFactResult(current, received);

        await Assert.That(result).IsEqualTo(expected);
    }
}
