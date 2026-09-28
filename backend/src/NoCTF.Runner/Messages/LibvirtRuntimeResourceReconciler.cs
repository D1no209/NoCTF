using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Libvirt;

namespace NoCTF.Runner.Messages;

public sealed class LibvirtRuntimeResourceReconciler(
    IOvaRuntime runtime,
    ILibvirtProcessAdapter processes)
    : IRuntimeManagedResourceReconciler, IRuntimeProviderAvailabilityProbe
{
    public RuntimeProvider Provider => RuntimeProvider.Libvirt;

    public async Task CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("virsh", ["uri"], cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Libvirt is unavailable.");
    }

    public async Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
        CancellationToken cancellationToken) =>
        (await runtime.ListManagedAsync(cancellationToken))
        .Select(resource => new RuntimeResourceIdentity(resource.OperationId))
        .ToArray();

    public Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        CancellationToken cancellationToken) =>
        runtime.DestroyByIdentityAsync(
            new(identity.RuntimeInstanceId),
            cancellationToken);

    public Task DestroyByIdentityAsync(
        RuntimeResourceIdentity identity,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken) =>
        runtime.DestroyByIdentityAsync(
            new(identity.RuntimeInstanceId),
            mode,
            policy,
            cancellationToken);
}
