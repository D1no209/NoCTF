using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerAssignmentRecoveryPolicyTests
{
    [Test]
    [Arguments(RuntimeState.Provisioning, false, false, false, RunnerAssignmentRecoveryAction.Ignore)]
    [Arguments(RuntimeState.Stopping, false, false, false, RunnerAssignmentRecoveryAction.Ignore)]
    [Arguments(RuntimeState.Provisioning, true, false, false, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Running, true, false, false, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Stopping, true, false, false, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Stopping, true, true, false, RunnerAssignmentRecoveryAction.Ignore)]
    [Arguments(RuntimeState.Stopping, true, true, true, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Provisioning, false, false, true, RunnerAssignmentRecoveryAction.Ignore)]
    [Arguments(RuntimeState.Stopping, false, false, true, RunnerAssignmentRecoveryAction.Ignore)]
    [Arguments(RuntimeState.Queued, false, false, false, RunnerAssignmentRecoveryAction.Ignore)]
    [Arguments(RuntimeState.Stopped, false, false, false, RunnerAssignmentRecoveryAction.Ignore)]
    public async Task Decide_preserves_node_local_receipt_ownership(
        RuntimeState state,
        bool hasReceipt,
        bool recoveryAlreadyRecorded,
        bool assignmentReleasePending,
        RunnerAssignmentRecoveryAction expected)
    {
        var actual = RunnerAssignmentRecoveryPolicy.Decide(
            state,
            hasReceipt,
            recoveryAlreadyRecorded,
            assignmentReleasePending);

        await Assert.That(actual).IsEqualTo(expected);
    }
}
