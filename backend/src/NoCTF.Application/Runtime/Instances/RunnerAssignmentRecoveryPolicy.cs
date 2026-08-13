using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public enum RunnerAssignmentRecoveryAction
{
    Ignore,
    AwaitOwnerCleanup
}

public static class RunnerAssignmentRecoveryPolicy
{
    public static RunnerAssignmentRecoveryAction Decide(
        RuntimeState state,
        bool recoveryAlreadyRecorded,
        bool assignmentReleasePending)
    {
        return state == RuntimeState.Failed
            || state is RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping
                && (!recoveryAlreadyRecorded || assignmentReleasePending)
            ? RunnerAssignmentRecoveryAction.AwaitOwnerCleanup
            : RunnerAssignmentRecoveryAction.Ignore;
    }
}
