using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Runtime;

[PersistentHierarchy]
public abstract class RuntimeReceipt
{
    public Guid RuntimeInstanceId { get; set; }
    public Guid OperationId { get; set; }
    public RuntimeProvider Provider { get; set; }
    public List<ContainerRuntimeReceiptPort> PortMappings { get; set; } = [];
    public List<OvaRuntimeReceiptMachine> VirtualMachines { get; set; } = [];
}

[PersistentDiscriminator("container")]
public sealed class ContainerRuntimeReceipt : RuntimeReceipt
{
    [MaxLength(512)] public string ResourceId { get; set; } = string.Empty;
    public RuntimeStatus Status { get; set; }
    [MaxLength(255)] public string? PublicHost { get; set; }
    [MaxLength(255)] public string? InternalHost { get; set; }
    [MaxLength(512)] public string? NetworkId { get; set; }
}

public sealed class ContainerRuntimeReceiptPort
{
    public Guid Id { get; set; }
    public int ContainerPort { get; set; }
    public int HostPort { get; set; }
}

[PersistentDiscriminator("compose")]
public sealed class ComposeRuntimeReceipt : RuntimeReceipt
{
    [MaxLength(255)] public string ProjectName { get; set; } = string.Empty;
    [MaxLength(255)] public string Namespace { get; set; } = string.Empty;
    [MaxLength(255)] public string PublicHost { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

[PersistentDiscriminator("ova")]
public sealed class OvaRuntimeReceiptEntity : RuntimeReceipt
{
    [MaxLength(512)] public string NetworkId { get; set; } = string.Empty;
    [MaxLength(128)] public string NetworkCidr { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OvaRuntimeReceiptMachine
{
    public Guid Id { get; set; }
    [MaxLength(255)] public string VmId { get; set; } = string.Empty;
    [MaxLength(512)] public string ResourceId { get; set; } = string.Empty;
    [MaxLength(255)] public string Address { get; set; } = string.Empty;
}
