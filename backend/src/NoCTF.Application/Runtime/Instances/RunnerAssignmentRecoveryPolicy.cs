using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Instances;

public enum RunnerAssignmentRecoveryAction
{
    Ignore,
    Redispatch,
    CompleteStop,
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
        if (assignmentReleasePending)
            return RunnerAssignmentRecoveryAction.Ignore;

        if (hasReceipt)
            return recoveryAlreadyRecorded
                ? RunnerAssignmentRecoveryAction.Ignore
                : state is RuntimeState.Provisioning or RuntimeState.Running or RuntimeState.Stopping
                    ? RunnerAssignmentRecoveryAction.AwaitOwnerCleanup
                    : RunnerAssignmentRecoveryAction.Ignore;

        return state switch
        {
            RuntimeState.Provisioning => RunnerAssignmentRecoveryAction.Redispatch,
            RuntimeState.Stopping => RunnerAssignmentRecoveryAction.CompleteStop,
            _ => RunnerAssignmentRecoveryAction.Ignore
        };
    }
}
