using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

public static class AwdpTargetDefinitionFactory
{
    public static ContainerRequest Create(
        Guid operationId,
        ChallengeRuntimeTemplate template,
        RuntimeProvider provider,
        DateTimeOffset now)
    {
        if (template.Definition is not ContainerRuntimeDefinition definition)
            throw new InvalidOperationException(
                "AWDP disposable targets require a Docker or Kubernetes Container runtime.");
        if (definition.InternalPorts is not { Count: 1 }
            || definition.InternalPorts[0] is < 1 or > 65535)
            throw new InvalidOperationException(
                "AWDP target Runtime must declare exactly one valid InternalPort.");

        var ttl = template.TtlSeconds is > 0
            ? TimeSpan.FromSeconds(template.TtlSeconds.Value)
            : TimeSpan.FromMinutes(15);
        var labels = definition.Labels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(definition.Labels, StringComparer.Ordinal);
        labels["noctf.io/managed"] = "true";
        labels["noctf.io/runtime-instance-id"] = operationId.ToString("D");
        labels["noctf.io/purpose"] = "awdp-target";
        labels["noctf.io/job-kind"] = "awdp-verification";
        var security = definition.Security
            ?? new ContainerSecurityPolicy(false, false, false, [], []);
        security = security with
        {
            CapDrop = security.CapDrop ?? [],
            CapAdd = security.CapAdd ?? []
        };
        return new ContainerRequest(
            operationId,
            provider,
            definition.Image,
            definition.Command ?? [],
            definition.Environment ?? new Dictionary<string, string>(),
            labels,
            new Dictionary<int, int>(),
            template.Limits ?? new RuntimeResourceLimits(512 * 1024 * 1024, 500_000_000, 256),
            security,
            Ttl: ttl,
            OperationTimeout: template.OperationTimeoutSeconds is > 0
                ? TimeSpan.FromSeconds(template.OperationTimeoutSeconds.Value)
                : TimeSpan.FromMinutes(2),
            NetworkIsolation: ContainerNetworkIsolation.Isolated,
            InternalPorts: definition.InternalPorts,
            RuntimeInstanceId: operationId,
            EgressPolicy: definition.EgressPolicy,
            NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);
    }
}
