using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Domain;

public sealed class ScoringFailureCodeCompatibilityTests
{
    [Test]
    [Arguments(ScoringFailureCode.FlagNotSupported, 0)]
    [Arguments(ScoringFailureCode.AmbiguousFlagMatch, 20)]
    [Arguments(ScoringFailureCode.AwdpViolation, 28)]
    [Arguments(ScoringFailureCode.ForeignTeamFlagDetected, 29)]
    public async Task Persisted_numeric_values_remain_stable(
        ScoringFailureCode failureCode,
        int expected)
    {
        await Assert.That((int)failureCode).IsEqualTo(expected);
    }
}
