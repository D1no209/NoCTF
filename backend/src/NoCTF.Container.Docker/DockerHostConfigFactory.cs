using Docker.DotNet.Models;
using NoCTF.PluginBase;

namespace NoCTF.Container.Docker;

internal static class DockerHostConfigFactory
{
    internal const string HostGatewayLabel = "noctf.host-gateway";

    public static HostConfig Create(ContainerConfig config, bool publishAllPorts, IDictionary<string, IList<PortBinding>>? portBindings = null)
    {
        var limits = config.ResourceLimits ?? new ContainerResourceLimits();
        var policy = config.SecurityPolicy ?? new ContainerSecurityPolicy();
        var capDrop = policy.CapDrop?.ToList() ?? [];
        var capAdd = policy.CapAdd?.ToList() ?? [];
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
            AutoRemove = false,
            ExtraHosts = AllowsHostGateway(config) ? ["host.docker.internal:host-gateway"] : null
        };
    }

    private static bool AllowsHostGateway(ContainerConfig config)
        => config.Labels is not null &&
           config.Labels.TryGetValue(HostGatewayLabel, out var value) &&
           bool.TryParse(value, out var allowed) &&
           allowed;

    public static string? ResolveUser(ContainerConfig config)
    {
        var policy = config.SecurityPolicy ?? new ContainerSecurityPolicy();
        return policy.RunAsNonRoot ? "1000:1000" : null;
    }
}
