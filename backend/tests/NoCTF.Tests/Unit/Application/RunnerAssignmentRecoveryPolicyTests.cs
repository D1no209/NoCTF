using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public sealed class RunnerAssignmentRecoveryPolicyTests
{
    [Test]
    [Arguments(RuntimeState.Provisioning, false, false, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Running, false, false, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Stopping, false, false, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Stopping, true, false, RunnerAssignmentRecoveryAction.Ignore)]
    [Arguments(RuntimeState.Stopping, true, true, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Provisioning, false, true, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Running, false, true, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Stopping, false, true, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Failed, false, false, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Failed, true, false, RunnerAssignmentRecoveryAction.AwaitOwnerCleanup)]
    [Arguments(RuntimeState.Queued, false, false, RunnerAssignmentRecoveryAction.Ignore)]
    [Arguments(RuntimeState.Stopped, false, false, RunnerAssignmentRecoveryAction.Ignore)]
    public async Task Decide_recovers_active_node_assignments(
        RuntimeState state,
        bool recoveryAlreadyRecorded,
        bool assignmentReleasePending,
        RunnerAssignmentRecoveryAction expected)
    {
        var actual = RunnerAssignmentRecoveryPolicy.Decide(
            state,
            recoveryAlreadyRecorded,
            assignmentReleasePending);

        await Assert.That(actual).IsEqualTo(expected);
    }
}
