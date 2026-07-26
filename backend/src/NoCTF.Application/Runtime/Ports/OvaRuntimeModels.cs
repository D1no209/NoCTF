namespace NoCTF.Application.Runtime.Ports;

public sealed record OvaRuntimeRequest(
    Guid OperationId,
    int Generation,
    Uri OvaSource,
    string NetworkName,
    ContainerResourceLimits Limits,
    TimeSpan? Ttl,
    TimeSpan OperationTimeout,
    IReadOnlyList<RuntimeUrlBinding>? UrlBindings = null);
