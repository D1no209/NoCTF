using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Tests.Unit;

public sealed class LiveSoloPreparationPolicyTests
{
    [Test]
    [Arguments(RuntimeMutationFailure.NotFound, LiveSoloFailure.NotFound)]
    [Arguments(RuntimeMutationFailure.CapacityExceeded, LiveSoloFailure.ResourceCapacityExceeded)]
    [Arguments(RuntimeMutationFailure.Unsupported, LiveSoloFailure.UnsupportedEnvironment)]
    [Arguments(RuntimeMutationFailure.Conflict, LiveSoloFailure.Conflict)]
    public async Task Actual_failures_take_precedence_over_an_unavailable_isolation_flag(RuntimeMutationFailure cause, LiveSoloFailure expected) =>
        await Assert.That(LiveSoloPreparationPolicy.Failure(new(null, null, false, cause))).IsEqualTo(expected);

    [Test]
    [Arguments(ExecutionIsolationStatus.NotConfigured, LiveSoloFailure.IsolationNotConfigured)]
    [Arguments(ExecutionIsolationStatus.Unverified, LiveSoloFailure.IsolationVerificationFailed)]
    [Arguments(ExecutionIsolationStatus.Unavailable, LiveSoloFailure.DependencyUnavailable)]
    public async Task Configuration_verification_and_dependency_failures_remain_distinct(ExecutionIsolationStatus cause, LiveSoloFailure expected) =>
        await Assert.That(LiveSoloPreparationPolicy.Failure(new(null, null, false, RuntimeMutationFailure.Unsupported, cause))).IsEqualTo(expected);

    [Test]
    public async Task A_static_question_needs_no_environment_or_isolation() =>
        await Assert.That(LiveSoloPreparationPolicy.Failure(new(null, null, true))).IsNull();
}
