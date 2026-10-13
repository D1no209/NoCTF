using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public sealed class AwdCheckerResultTests
{
    [Test]
    [Arguments(AwdHealth.Ok, GameplayFactResult.ServiceUp)]
    [Arguments(AwdHealth.Mumble, GameplayFactResult.ServiceDown)]
    [Arguments(AwdHealth.Offline, GameplayFactResult.ServiceDown)]
    [Arguments(AwdHealth.Recovering, GameplayFactResult.ServiceDown)]
    public async Task Conclusive_health_has_a_gameplay_result(AwdHealth health, GameplayFactResult expected) =>
        await Assert.That(AwdCheckerOutcomeMapper.ToGameplayFactResult(health)).IsEqualTo(expected);

    [Test]
    public async Task Internal_error_is_not_an_authoritative_service_failure() =>
        await Assert.That(AwdCheckerOutcomeMapper.ToGameplayFactResult(AwdHealth.InternalError)).IsNull();
}