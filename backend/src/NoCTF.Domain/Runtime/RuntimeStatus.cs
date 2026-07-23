namespace NoCTF.Domain.Runtime;

public enum RuntimeStatus
{
    Pending,
    Starting,
    Running,
    Stopping,
    Stopped,
    Failed
}

public enum RuntimeProvider
{
    Docker,
    Kubernetes,
    Libvirt
}
