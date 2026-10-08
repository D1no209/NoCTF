namespace NoCTF.Application.Runtime.Access;

public sealed record RuntimeProxyOptions(
    int MaximumConnectionsPerRuntime = 32,
    int MaximumConnectionMinutes = 30,
    int ConnectTimeoutSeconds = 10,
    int BufferSizeBytes = 65_536,
    long DefaultCaptureLimitBytes = 268_435_456,
    long MaximumCaptureLimitBytes = 4_294_967_296);

public sealed record RuntimeProxyTarget(
    Guid RuntimeInstanceId,
    int BindingIndex,
    string Host,
    int Port,
    Guid? CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    bool TrafficCaptureEnabled,
    long? TrafficCaptureLimitBytes)
{
    public Guid? ExecutionScopeId { get; init; }
}

public interface IRuntimeProxyTargetReader
{
    Task<RuntimeProxyTarget?> FindAsync(
        Guid runtimeInstanceId,
        int bindingIndex,
        CancellationToken cancellationToken);
}

public interface IRuntimeProxyConnectionLease : IAsyncDisposable;

public interface IRuntimeProxyConnectionGate
{
    Task<IRuntimeProxyConnectionLease?> TryAcquireAsync(
        Guid runtimeInstanceId,
        CancellationToken cancellationToken);
}

public enum RuntimeTrafficDirection : short
{
    ClientToRuntime,
    RuntimeToClient
}

public interface IRuntimeTrafficCaptureSession : IAsyncDisposable
{
    ValueTask RecordAsync(
        RuntimeTrafficDirection direction,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}

public interface IRuntimeTrafficCaptureFactory
{
    Task<IRuntimeTrafficCaptureSession?> CreateAsync(
        RuntimeProxyTarget target,
        System.Net.IPEndPoint client,
        System.Net.IPEndPoint destination,
        string connectionId,
        CancellationToken cancellationToken);
}
