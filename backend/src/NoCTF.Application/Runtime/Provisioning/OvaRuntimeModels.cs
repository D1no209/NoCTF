using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Provisioning;

public sealed record OvaRuntimeRequest(
    Guid OperationId,
    Uri OvaSource,
    string Sha256,
    string NetworkName,
    RuntimeResourceLimits Limits,
    TimeSpan? Ttl,
    TimeSpan OperationTimeout,
    IReadOnlyList<RuntimeUrlBinding>? UrlBindings = null,
    RuntimeUrlBinding? ControlCheckUrlBinding = null);

public sealed record OvaVirtualMachineReceipt(
    string VmId,
    string ResourceId,
    string Address);

public sealed record OvaRuntimeReceipt(
    Guid OperationId,
    RuntimeProvider Provider,
    string NetworkId,
    string NetworkCidr,
    IReadOnlyList<OvaVirtualMachineReceipt> VirtualMachines,
    DateTimeOffset CreatedAt);

public readonly record struct OvaManagedRuntimeResource(Guid OperationId);

public interface IOvaRuntime
{
    Task<OvaRuntimeReceipt> ImportAsync(
        OvaRuntimeRequest request,
        CancellationToken cancellationToken);

    Task DestroyAsync(
        OvaRuntimeReceipt receipt,
        CancellationToken cancellationToken);

    Task DestroyAsync(
        OvaRuntimeReceipt receipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken) =>
        DestroyAsync(receipt, cancellationToken);

    Task<IReadOnlyList<OvaManagedRuntimeResource>> ListManagedAsync(
        CancellationToken cancellationToken);

    Task DestroyByIdentityAsync(
        OvaManagedRuntimeResource identity,
        CancellationToken cancellationToken);

    Task DestroyByIdentityAsync(
        OvaManagedRuntimeResource identity,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken) =>
        DestroyByIdentityAsync(identity, cancellationToken);
}
