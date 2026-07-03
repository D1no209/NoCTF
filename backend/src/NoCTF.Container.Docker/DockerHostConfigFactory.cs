using Docker.DotNet.Models;
using NoCTF.PluginBase;

namespace NoCTF.Container.Docker;

internal static class DockerHostConfigFactory
{
    public static HostConfig Create(ContainerConfig config, bool publishAllPorts, IDictionary<string, IList<PortBinding>>? portBindings = null)
    {
        var limits = config.ResourceLimits ?? new ContainerResourceLimits();
        var policy = config.SecurityPolicy ?? new ContainerSecurityPolicy();
        var capDrop = policy.CapDrop?.ToList() ?? ["ALL"];
        var capAdd = policy.CapAdd?.ToList() ?? ["SYS_CHROOT", "SETUID", "SETGID"];
        var securityOpt = policy.NoNewPrivileges ? ["no-new-privileges:true"] : new List<string>();

        return new HostConfig
        {
            PublishAllPorts = publishAllPorts,
            PortBindings = portBindings,
            NetworkMode = config.NetworkName ?? "bridge",
            Memory = limits.MemoryBytes,
            NanoCPUs = limits.NanoCpus,
            PidsLimit = limits.PidsLimit,
            CapAdd = capAdd,
            CapDrop = capDrop,
            SecurityOpt = securityOpt,
            ReadonlyRootfs = policy.ReadonlyRootfs,
            AutoRemove = false
        };
    }
}
