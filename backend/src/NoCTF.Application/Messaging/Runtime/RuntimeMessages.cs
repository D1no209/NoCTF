namespace NoCTF.Application.Messaging;

public sealed record DispatchRuntime(Guid RuntimeInstanceId, long ProcessingVersion);
public sealed record StopRuntime(Guid RuntimeInstanceId, long ProcessingVersion);
