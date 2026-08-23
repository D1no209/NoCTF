namespace NoCTF.Infrastructure.Persistence;

internal sealed class FeatureCriticalSectionTimeoutException(string feature)
    : TimeoutException($"The {feature} critical section was busy for more than two seconds.");

internal sealed class NoopCriticalSectionLease : IDisposable
{
    public static readonly NoopCriticalSectionLease Instance = new();
    public void Dispose() { }
}
