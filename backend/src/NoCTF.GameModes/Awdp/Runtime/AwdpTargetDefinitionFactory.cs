using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

public static class AwdpTargetDefinitionFactory
{
    public static ContainerRequest Create(
        Guid operationId,
        ChallengeRuntimeTemplate template,
        int targetPort)
    {
        if (template.RuntimeKind != RuntimeKind.Container
            || template.Provider is not (RuntimeProvider.Docker or RuntimeProvider.Kubernetes))
            throw new InvalidOperationException(
                "AWDP disposable targets require a Docker or Kubernetes Container runtime.");
        if (targetPort is < 1 or > 65535)
            throw new InvalidOperationException("AWDP TargetPort must be between 1 and 65535.");

        var labels = template.Labels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(template.Labels, StringComparer.Ordinal);
        labels["noctf.managed"] = "true";
        labels["noctf.operation-id"] = operationId.ToString("D");
        labels["noctf.runtime-instance-id"] = operationId.ToString("D");
        labels["noctf.purpose"] = "awdp-target";
        return new ContainerRequest(
            operationId,
            template.Provider,
            template.Image,
            template.Command ?? [],
            template.Environment ?? new Dictionary<string, string>(),
            labels,
            new Dictionary<int, int>(),
            template.Limits ?? new ContainerResourceLimits(512 * 1024 * 1024, 500_000_000, 256),
            template.Security ?? new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
            Ttl: template.TtlSeconds is > 0
                ? TimeSpan.FromSeconds(template.TtlSeconds.Value)
                : TimeSpan.FromMinutes(15),
            OperationTimeout: template.OperationTimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(template.OperationTimeoutSeconds.Value)
                : TimeSpan.FromMinutes(2),
            NetworkIsolation: ContainerNetworkIsolation.Isolated,
            InternalPorts: [targetPort]);
    }
}
