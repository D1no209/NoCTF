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
    [Arguments(AwdServiceState.Up, GameplayFactResult.ServiceUp)]
    [Arguments(AwdServiceState.Down, GameplayFactResult.ServiceDown)]
    [Arguments(AwdServiceState.CheckerAbnormalExit, GameplayFactResult.ServiceDown)]
    [Arguments(AwdServiceState.CheckerTimedOut, GameplayFactResult.ServiceDown)]
    public async Task Every_checker_execution_maps_to_an_authoritative_result(
        AwdServiceState state,
        GameplayFactResult expected)
    {
        var result = AwdCheckerOutcomeMapper.ToGameplayFactResult(state);

        await Assert.That(result).IsEqualTo(expected);
    }
}
