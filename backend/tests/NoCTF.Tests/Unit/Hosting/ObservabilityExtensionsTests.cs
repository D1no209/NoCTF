using NoCTF.Hosting.Observability;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class ObservabilityExtensionsTests
{
    [Test]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions", "flag")]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/practice-flag", "flag")]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-break-flag-judgement", "flag")]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets", "fix_request")]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets/{runtimeInstanceId}/fix", "fix_upload")]
    [Arguments("POST", "/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start", "runtime_start")]
    [Arguments("POST", "/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}/force-terminate", "runtime_force_terminate")]
    public async Task Mutating_gameplay_routes_are_classified(
        string method,
        string route,
        string expected)
    {
        var classified = ObservabilityExtensions.TryClassifyRuntimeOperation(
            method,
            route,
            out var operation);

        await Assert.That(classified).IsTrue();
        await Assert.That(operation).IsEqualTo(expected);
    }

    [Test]
    [Arguments("GET", "/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime")]
    [Arguments("GET", "/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions")]
    [Arguments("POST", "/admin/challenges/{challengeId}/flags")]
    [Arguments("POST", "/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/flag-access")]
    public async Task Read_and_administration_routes_are_not_gameplay_operations(
        string method,
        string route)
    {
        var classified = ObservabilityExtensions.TryClassifyRuntimeOperation(
            method,
            route,
            out var operation);

        await Assert.That(classified).IsFalse();
        await Assert.That(operation).IsEmpty();
    }
}
