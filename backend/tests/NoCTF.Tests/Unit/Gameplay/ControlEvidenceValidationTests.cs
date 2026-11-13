using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Tests.Unit.Gameplay;

public sealed class ControlEvidenceValidationTests
{
    [Test]
    [Arguments(ControlResult.Controlled, "controller-7", null)]
    [Arguments(ControlResult.Uncontrolled, null, null)]
    [Arguments(ControlResult.Controlled, null, CapabilityExecutionFailure.InvalidEvidence)]
    [Arguments(ControlResult.Uncontrolled, "controller-7", CapabilityExecutionFailure.InvalidEvidence)]
    [Arguments(ControlResult.Indeterminate, null, CapabilityExecutionFailure.InvalidEvidence)]
    public async Task Completed_observations_require_consistent_control_identity(ControlResult result, string? identifier,
        CapabilityExecutionFailure? expected)
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new ControlEvidence(CapabilityExecutionState.Completed, result, identifier, null, now);
        await Assert.That(ControlEvidenceValidation.Validate(evidence, now.AddSeconds(-1), now.AddSeconds(10), now))
            .IsEqualTo(expected);
        await Assert.That(evidence.ToString()).DoesNotContain("controller-7");
    }

    [Test]
    public async Task Unexecuted_observations_cannot_report_control()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var result in new[] { ControlResult.Controlled, ControlResult.Uncontrolled })
        {
            var evidence = new ControlEvidence(CapabilityExecutionState.NotExecuted, result,
                result == ControlResult.Controlled ? "controller-7" : null, null, now);
            await Assert.That(ControlEvidenceValidation.Validate(evidence, now.AddSeconds(-1), now.AddSeconds(10), now))
                .IsEqualTo(CapabilityExecutionFailure.InvalidEvidence);
        }
    }

    [Test]
    public async Task Failures_and_invalid_observation_windows_cannot_become_controlled_results()
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new ControlEvidence(CapabilityExecutionState.PlatformFailed, ControlResult.Indeterminate, null,
            CapabilityExecutionFailure.RuntimeUnavailable, now);
        await Assert.That(ControlEvidenceValidation.Validate(evidence, now.AddSeconds(-1), now.AddSeconds(10), now)).IsNull();
        await Assert.That(ControlEvidenceValidation.Validate(evidence with { Result = ControlResult.Controlled, ControllerIdentifier = "team" },
            now.AddSeconds(-1), now.AddSeconds(10), now)).IsEqualTo(CapabilityExecutionFailure.InvalidEvidence);
        await Assert.That(ControlEvidenceValidation.Validate(evidence with { ObservedAt = now.AddSeconds(1) },
            now.AddSeconds(-1), now.AddSeconds(10), now)).IsEqualTo(CapabilityExecutionFailure.InvalidEvidence);
        await Assert.That(ControlEvidenceValidation.Validate(evidence with { ControllerIdentifier = new string('x', 4097) },
            now.AddSeconds(-1), now.AddSeconds(10), now)).IsEqualTo(CapabilityExecutionFailure.InvalidEvidence);
    }
}
