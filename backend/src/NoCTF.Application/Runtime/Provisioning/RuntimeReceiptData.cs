using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Provisioning;

public abstract record RuntimeReceiptData(Guid OperationId, RuntimeProvider Provider);

public sealed record ContainerRuntimeReceiptData(
    Guid OperationId,
    RuntimeProvider Provider,
    string ResourceId,
    RuntimeStatus Status,
    IReadOnlyDictionary<int, int> PortMappings,
    string? PublicHost,
    string? InternalHost,
    string? NetworkId,
    Guid RuntimeInstanceId) : RuntimeReceiptData(OperationId, Provider)
{
    public static ContainerRuntimeReceiptData From(ContainerReceipt receipt) => new(
        receipt.OperationId,
        receipt.Provider,
        receipt.ResourceId,
        receipt.Status,
        receipt.PortMappings,
        receipt.PublicHost,
        receipt.InternalHost,
        receipt.NetworkId,
        receipt.RuntimeInstanceId ?? receipt.OperationId);

    public ContainerReceipt ToReceipt() => new(
        OperationId, Provider, ResourceId, Status, PortMappings, PublicHost, InternalHost,
        NetworkId, RuntimeInstanceId);
}

public sealed record ComposeRuntimeReceiptData(
    Guid OperationId,
    RuntimeProvider Provider,
    string ProjectName,
    string Namespace,
    string PublicHost,
    DateTimeOffset CreatedAt) : RuntimeReceiptData(OperationId, Provider)
{
    public static ComposeRuntimeReceiptData From(ComposeReceipt receipt) => new(
        receipt.OperationId, receipt.Provider, receipt.ProjectName, receipt.Namespace,
        receipt.PublicHost, receipt.CreatedAt);

    public ComposeReceipt ToReceipt() => new(
        OperationId, Provider, ProjectName, Namespace, PublicHost, CreatedAt);
}

public sealed record OvaRuntimeReceiptData(
    Guid OperationId,
    RuntimeProvider Provider,
    string NetworkId,
    string NetworkCidr,
    IReadOnlyList<OvaVirtualMachineReceipt> VirtualMachines,
    DateTimeOffset CreatedAt) : RuntimeReceiptData(OperationId, Provider)
{
    public static OvaRuntimeReceiptData From(OvaRuntimeReceipt receipt) => new(
        receipt.OperationId, receipt.Provider, receipt.NetworkId, receipt.NetworkCidr,
        receipt.VirtualMachines, receipt.CreatedAt);

    public OvaRuntimeReceipt ToReceipt() => new(
        OperationId, Provider, NetworkId, NetworkCidr, VirtualMachines, CreatedAt);
}

public static class RuntimeReceiptDataMapping
{
    public static RuntimeReceipt ToEntity(this RuntimeReceiptData value, Guid runtimeInstanceId) => value switch
    {
        ContainerRuntimeReceiptData receipt => new ContainerRuntimeReceipt
        {
            RuntimeInstanceId = runtimeInstanceId,
            OperationId = receipt.OperationId,
            Provider = receipt.Provider,
            ResourceId = receipt.ResourceId,
            Status = receipt.Status,
            PublicHost = receipt.PublicHost,
            InternalHost = receipt.InternalHost,
            NetworkId = receipt.NetworkId,
            PortMappings = receipt.PortMappings.OrderBy(item => item.Key).Select(item =>
                new ContainerRuntimeReceiptPort
                {
                    Id = Guid.CreateVersion7(),
                    ContainerPort = item.Key,
                    HostPort = item.Value
                }).ToList()
        },
        ComposeRuntimeReceiptData receipt => new ComposeRuntimeReceipt
        {
            RuntimeInstanceId = runtimeInstanceId,
            OperationId = receipt.OperationId,
            Provider = receipt.Provider,
            ProjectName = receipt.ProjectName,
            Namespace = receipt.Namespace,
            PublicHost = receipt.PublicHost,
            CreatedAt = receipt.CreatedAt
        },
        OvaRuntimeReceiptData receipt => new OvaRuntimeReceiptEntity
        {
            RuntimeInstanceId = runtimeInstanceId,
            OperationId = receipt.OperationId,
            Provider = receipt.Provider,
            NetworkId = receipt.NetworkId,
            NetworkCidr = receipt.NetworkCidr,
            CreatedAt = receipt.CreatedAt,
            VirtualMachines = receipt.VirtualMachines.Select(machine => new OvaRuntimeReceiptMachine
            {
                Id = Guid.CreateVersion7(),
                VmId = machine.VmId,
                ResourceId = machine.ResourceId,
                Address = machine.Address
            }).ToList()
        },
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.GetType().FullName)
    };

    public static RuntimeReceiptData ToData(this RuntimeReceipt value) => value switch
    {
        ContainerRuntimeReceipt receipt => new ContainerRuntimeReceiptData(
            receipt.OperationId, receipt.Provider, receipt.ResourceId, receipt.Status,
            receipt.PortMappings.ToDictionary(item => item.ContainerPort, item => item.HostPort),
            receipt.PublicHost, receipt.InternalHost, receipt.NetworkId, receipt.RuntimeInstanceId),
        ComposeRuntimeReceipt receipt => new ComposeRuntimeReceiptData(
            receipt.OperationId, receipt.Provider, receipt.ProjectName, receipt.Namespace,
            receipt.PublicHost, receipt.CreatedAt),
        OvaRuntimeReceiptEntity receipt => new OvaRuntimeReceiptData(
            receipt.OperationId, receipt.Provider, receipt.NetworkId, receipt.NetworkCidr,
            receipt.VirtualMachines.Select(machine => new OvaVirtualMachineReceipt(
                machine.VmId, machine.ResourceId, machine.Address)).ToArray(), receipt.CreatedAt),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.GetType().FullName)
    };
}
