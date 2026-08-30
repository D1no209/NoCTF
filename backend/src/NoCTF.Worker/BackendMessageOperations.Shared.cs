namespace NoCTF.Worker;

internal static partial class BackendMessageOperations
{
    private static readonly TimeSpan RunnerDependencyRetryDelay = TimeSpan.FromSeconds(5);
}
