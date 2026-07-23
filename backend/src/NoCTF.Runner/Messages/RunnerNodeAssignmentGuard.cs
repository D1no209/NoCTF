using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Runner.Messages;

public static class RunnerNodeAssignmentGuard
{
    public static void Validate(
        IRunnerNodeMessage message,
        string configuredPool,
        string configuredRunnerId)
    {
        if (!string.Equals(message.RunnerPool, configuredPool, StringComparison.Ordinal)
            || !string.Equals(message.RunnerId, configuredRunnerId, StringComparison.Ordinal)
            || RunnerNodeQueueName.FromAssignment(message.RunnerPool, message.RunnerId)
                != RunnerNodeQueueName.FromAssignment(configuredPool, configuredRunnerId))
        {
            throw new InvalidOperationException(
                "Runner work was delivered to a node that does not own the assignment.");
        }
    }
}
