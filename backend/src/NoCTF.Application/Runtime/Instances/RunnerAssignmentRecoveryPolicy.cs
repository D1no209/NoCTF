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
        bool hasReceipt,
        bool recoveryAlreadyRecorded,
        bool assignmentReleasePending)
    {
        if (hasReceipt)
            return state is RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping
                && (!recoveryAlreadyRecorded || assignmentReleasePending)
                ? RunnerAssignmentRecoveryAction.AwaitOwnerCleanup
                : RunnerAssignmentRecoveryAction.Ignore;

        return RunnerAssignmentRecoveryAction.Ignore;
    }
}
