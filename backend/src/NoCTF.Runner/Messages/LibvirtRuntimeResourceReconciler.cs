using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Messages;

public sealed class LibvirtRuntimeResourceReconciler(IOvaRuntime runtime)
    : IRuntimeManagedResourceReconciler
{
    public RuntimeProvider Provider => RuntimeProvider.Libvirt;

    public async Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
        CancellationToken cancellationToken) =>
        (await runtime.ListManagedAsync(cancellationToken))
        .Select(resource => new RuntimeResourceIdentity(
            resource.OperationId,
            resource.Generation))
        .ToArray();

    public Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken) =>
        runtime.DestroyByIdentityAsync(
            new(identity.RuntimeInstanceId, identity.Generation),
            cancellationToken);
}
