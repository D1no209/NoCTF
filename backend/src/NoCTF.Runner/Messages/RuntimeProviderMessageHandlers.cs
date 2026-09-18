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
public sealed class ComposeRuntimeMessageHandler(RuntimeProviderHandler runtime)
{
    public Task<object?> Handle(
        ProvisionComposeRuntime message,
        CancellationToken cancellationToken) =>
        runtime.ProvisionComposeAsync(message, cancellationToken);

    public Task<object> Handle(
        StopComposeRuntime message,
        CancellationToken cancellationToken) =>
        runtime.StopComposeAsync(message, cancellationToken);
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

internal static class RuntimeProviderHandlerCompatibilityExtensions
{
    public static Task<object?> Handle(
        this RuntimeProviderHandler runtime,
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken) =>
        runtime.ProvisionContainerAsync(message, cancellationToken);

    public static Task<object> Handle(
        this RuntimeProviderHandler runtime,
        StopContainerRuntime message,
        CancellationToken cancellationToken) =>
        runtime.StopContainerAsync(message, cancellationToken);

    public static Task<object?> Handle(
        this RuntimeProviderHandler runtime,
        ProvisionComposeRuntime message,
        CancellationToken cancellationToken) =>
        runtime.ProvisionComposeAsync(message, cancellationToken);

    public static Task<object> Handle(
        this RuntimeProviderHandler runtime,
        StopComposeRuntime message,
        CancellationToken cancellationToken) =>
        runtime.StopComposeAsync(message, cancellationToken);

    public static Task<object?> Handle(
        this RuntimeProviderHandler runtime,
        ProvisionOvaRuntime message,
        CancellationToken cancellationToken) =>
        runtime.ProvisionOvaAsync(message, cancellationToken);

    public static Task<object> Handle(
        this RuntimeProviderHandler runtime,
        StopOvaRuntime message,
        CancellationToken cancellationToken) =>
        runtime.StopOvaAsync(message, cancellationToken);

    public static Task<object> Handle(
        this RuntimeProviderHandler runtime,
        ForceTerminateRuntime message,
        CancellationToken cancellationToken) =>
        runtime.ForceTerminateAsync(message, cancellationToken);
}
