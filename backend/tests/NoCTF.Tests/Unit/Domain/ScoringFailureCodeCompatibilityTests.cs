using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Domain;

public sealed class GameplayFactFailureCodeCompatibilityTests
{
    [Test]
    [Arguments(GameplayFactFailureCode.FlagNotSupported, 0)]
    [Arguments(GameplayFactFailureCode.AmbiguousFlagMatch, 20)]
    [Arguments(GameplayFactFailureCode.AwdpViolation, 28)]
    [Arguments(GameplayFactFailureCode.ForeignTeamFlagDetected, 29)]
    public async Task Persisted_numeric_values_remain_stable(
        GameplayFactFailureCode failureCode,
        int expected)
    {
        await Assert.That((int)failureCode).IsEqualTo(expected);
    }
}
