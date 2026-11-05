using System.Text.Json;
using NoCTF.API.Endpoints.Internal;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Tests.Unit.API;

public sealed class AwdpCheckResultProtocolTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("Passed", CheckProbeResult.Passed)]
    [Arguments("TargetFailure", CheckProbeResult.TargetFailure)]
    [Arguments("PlatformFailure", CheckProbeResult.PlatformFailure)]
    public async Task Check_evidence_uses_explicit_probe_results(
        string input,
        CheckProbeResult expected)
    {
        var request = JsonSerializer.Deserialize<RecordCheckResultRequest>(
            $$"""{"observedAt":"2026-11-04T00:00:00Z","probes":[{"kind":"Functionality","result":"{{input}}","reason":"None"}]}""",
            Options);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Probes.Single().Result).IsEqualTo(expected);
        await Assert.That(request.Probes.Single().Kind).IsEqualTo(CheckItemKind.Functionality);
    }

    [Test]
    public async Task Check_evidence_serializes_objective_probe_information()
    {
        var json = JsonSerializer.Serialize(
            new RecordCheckResultRequest
            {
                ObservedAt = DateTimeOffset.Parse("2026-11-04T00:00:00Z"),
                Probes = [new() { Kind = CheckItemKind.Vulnerability, Result = CheckProbeResult.TargetFailure, Reason = CheckFailureReason.VulnerabilityPresent }]
            },
            Options);

        await Assert.That(json).Contains("VulnerabilityPresent");
        await Assert.That(json).Contains("TargetFailure");
        await Assert.That(json).DoesNotContain("outcome");
    }

    [Test]
    [Arguments("DefenseSucceeded")]
    [Arguments("ExploitSucceeded")]
    [Arguments("RuleViolation")]
    [Arguments("ServiceUnavailable")]
    [Arguments("PatchFailed")]
    public async Task Check_evidence_rejects_game_decisions_as_probe_results(string outcome)
    {
        var act = () => JsonSerializer.Deserialize<RecordCheckResultRequest>(
            $$"""{"probes":[{"kind":"Functionality","result":"{{outcome}}","reason":"None"}]}""",
            Options);

        await Assert.That(act).Throws<JsonException>();
    }
}
