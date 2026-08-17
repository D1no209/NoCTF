using System.Text.Json;
using NoCTF.API.Endpoints.Internal;

namespace NoCTF.Tests.Unit.API;

public sealed class AwdpCheckResultProtocolTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("ExploitSucceeded", AwdpFixResultOutcome.ExploitSucceeded)]
    [Arguments("DefenseSucceeded", AwdpFixResultOutcome.DefenseSucceeded)]
    [Arguments("ServiceAbnormal", AwdpFixResultOutcome.ServiceAbnormal)]
    [Arguments("StillVulnerable", AwdpFixResultOutcome.ExploitSucceeded)]
    [Arguments("Fixed", AwdpFixResultOutcome.DefenseSucceeded)]
    [Arguments("RuleViolation", AwdpFixResultOutcome.ServiceAbnormal)]
    [Arguments("ServiceUnavailable", AwdpFixResultOutcome.ServiceAbnormal)]
    public async Task Awdp_check_result_accepts_new_values_and_maps_legacy_inputs(
        string input,
        AwdpFixResultOutcome expected)
    {
        var request = JsonSerializer.Deserialize<RecordAwdpCheckResultRequest>(
            $$"""{"outcome":"{{input}}"}""",
            Options);

        await Assert.That(request).IsNotNull();
        await Assert.That(request!.Outcome).IsEqualTo(expected);
    }

    [Test]
    public async Task Awdp_check_result_serializes_only_the_three_public_outcomes()
    {
        var json = JsonSerializer.Serialize(
            new RecordAwdpCheckResultRequest
            {
                Outcome = AwdpFixResultOutcome.ServiceAbnormal
            },
            Options);

        await Assert.That(json).Contains("ServiceAbnormal");
        await Assert.That(json).DoesNotContain("ServiceUnavailable");
        await Assert.That(json).DoesNotContain("RuleViolation");
    }

    [Test]
    public async Task Awdp_check_result_rejects_unknown_outcomes()
    {
        var act = () => JsonSerializer.Deserialize<RecordAwdpCheckResultRequest>(
            """{"outcome":"PatchFailed"}""",
            Options);

        await Assert.That(act).Throws<JsonException>();
    }
}
