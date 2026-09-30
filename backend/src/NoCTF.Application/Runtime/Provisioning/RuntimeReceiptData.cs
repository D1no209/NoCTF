using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Provisioning;

public abstract record RuntimeReceiptData(Guid OperationId, RuntimeProvider Provider);

public sealed record ContainerRuntimeReceiptData(
    Guid OperationId, RuntimeProvider Provider, string ProjectName, string Namespace, string PublicHost,
    DateTimeOffset CreatedAt, IReadOnlyList<ContainerServiceStatus> Services,
    string? OwnedNetworkId = null, string? DiscoveryServiceName = null) : RuntimeReceiptData(OperationId, Provider)
{
    public static ContainerRuntimeReceiptData From(ContainerDeploymentReceipt receipt) => new(
        receipt.OperationId, receipt.Provider, receipt.ProjectName, receipt.Namespace, receipt.PublicHost,
        receipt.CreatedAt, receipt.Services, receipt.OwnedNetworkId, receipt.DiscoveryServiceName);
    public ContainerDeploymentReceipt ToReceipt() => new(OperationId, Provider, ProjectName, Namespace, PublicHost,
        CreatedAt, Services, OwnedNetworkId, DiscoveryServiceName);
    public ContainerReceipt ServiceReceipt(string serviceName)
    {
        var service = Services.SingleOrDefault(service => service.Name == serviceName)
            ?? throw new InvalidDataException("The target Runtime service is absent.");
        return NamedContainerRuntime.ServiceReceipt(ToReceipt(), service);
    }
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
            RuntimeInstanceId = runtimeInstanceId, OperationId = receipt.OperationId, Provider = receipt.Provider,
            ProjectName = receipt.ProjectName, Namespace = receipt.Namespace, PublicHost = receipt.PublicHost,
            CreatedAt = receipt.CreatedAt, OwnedNetworkId = receipt.OwnedNetworkId, DiscoveryServiceName = receipt.DiscoveryServiceName,
            Services = receipt.Services.Select(service => new ContainerRuntimeReceiptService
            {
                Id = Guid.CreateVersion7(), Name = service.Name, ResourceId = service.ResourceId, Status = service.Status, InternalHost = service.InternalHost,
                PublishedPorts = service.PublishedPorts.Select(port => new ContainerRuntimeReceiptPort
                { Id = Guid.CreateVersion7(), ContainerPort = port.Key, HostPort = port.Value }).ToList()
            }).ToList()
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
            receipt.OperationId, receipt.Provider, receipt.ProjectName, receipt.Namespace, receipt.PublicHost, receipt.CreatedAt,
            receipt.Services.Select(service => new ContainerServiceStatus(service.Name, service.ResourceId, service.Status,
                service.PublishedPorts.ToDictionary(port => port.ContainerPort, port => port.HostPort), service.InternalHost)).ToArray(),
            receipt.OwnedNetworkId, receipt.DiscoveryServiceName),
        OvaRuntimeReceiptEntity receipt => new OvaRuntimeReceiptData(
            receipt.OperationId, receipt.Provider, receipt.NetworkId, receipt.NetworkCidr,
            receipt.VirtualMachines.Select(machine => new OvaVirtualMachineReceipt(
                machine.VmId, machine.ResourceId, machine.Address)).ToArray(), receipt.CreatedAt),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.GetType().FullName)
    };
}
