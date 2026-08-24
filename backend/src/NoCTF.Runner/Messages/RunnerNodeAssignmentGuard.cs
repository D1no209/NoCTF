using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Runner.Messages;

public static class RunnerNodeAssignmentGuard
{
    public static void Validate(
        IRunnerNodeMessage message,
        string configuredPool,
        string configuredRunnerId)
    {
        _ = configuredPool;
        if (!string.Equals(message.RunnerId, configuredRunnerId, StringComparison.Ordinal)
            || RunnerNodeQueueName.FromRunnerId(message.RunnerId)
                != RunnerNodeQueueName.FromRunnerId(configuredRunnerId))
        {
            throw new InvalidOperationException(
                "Runner work was delivered to a node that does not own the assignment.");
        }
    }
}
