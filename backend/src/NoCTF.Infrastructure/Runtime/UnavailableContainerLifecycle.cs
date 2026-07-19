using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Infrastructure.Runtime;

/// <summary>
/// API hosts do not execute privileged container operations. Runtime work is
/// delegated to the separately deployed Runner boundary. This adapter keeps
/// the application graph valid and fails explicitly if a runtime operation is
/// accidentally attempted in the API process.
/// </summary>
public sealed class UnavailableContainerLifecycle : IContainerLifecycle
{
    public Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken) =>
        Task.FromException<ContainerReceipt>(new InvalidOperationException("Container operations must be executed by the Runner service."));

    public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("Container operations must be executed by the Runner service."));

    public Task<ContainerReceipt?> GetAsync(string resourceId, CancellationToken cancellationToken) =>
        Task.FromException<ContainerReceipt?>(new InvalidOperationException("Container operations must be executed by the Runner service."));
}
