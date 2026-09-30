using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using Wolverine.Attributes;

namespace NoCTF.Runner.Messages;

[NonTransactional]
public sealed class ContainerRuntimeMessageHandler(RuntimeProviderHandler runtime)
{
    public Task<object?> Handle(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken) =>
        runtime.ProvisionContainerAsync(message, cancellationToken);

    public Task<object> Handle(
        StopContainerRuntime message,
        CancellationToken cancellationToken) =>
        runtime.StopContainerAsync(message, cancellationToken);
}
[NonTransactional]
public sealed class OvaRuntimeMessageHandler(RuntimeProviderHandler runtime)
{
    public Task<object?> Handle(
        ProvisionOvaRuntime message,
        CancellationToken cancellationToken) =>
        runtime.ProvisionOvaAsync(message, cancellationToken);

    public Task<object> Handle(
        StopOvaRuntime message,
        CancellationToken cancellationToken) =>
        runtime.StopOvaAsync(message, cancellationToken);
}

[NonTransactional]
public sealed class RuntimeTerminationMessageHandler(RuntimeProviderHandler runtime)
{
    public Task<object> Handle(
        ForceTerminateRuntime message,
        CancellationToken cancellationToken) =>
        runtime.ForceTerminateAsync(message, cancellationToken);
}
